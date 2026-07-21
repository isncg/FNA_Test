using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNA.Test
{
    /// <summary>CPU-side trail history entry.</summary>
    public struct TrailRecord
    {
        public Vector3 Position;
        public Quaternion Rotation;
    }

    /// <summary>
    /// GPU-instanced trail renderer. Records position+rotation each frame and
    /// draws the entire trail in a single DrawInstancedPrimitives call with a
    /// configurable color gradient: head color → tail color.
    ///
    /// Usage:
    ///   1. Load the TrailEffect.feb Effect, pass it to the constructor
    ///   2. Provide mesh geometry as Vector3[] in local space (triangle list)
    ///   3. Call AddRecord(position, rotation) each frame
    ///   4. Call Draw(view, projection) each frame
    ///
    /// The shader applies per-instance quaternion rotation + translation.
    /// Render states (BlendState.NonPremultiplied, DepthStencilState.Default,
    /// RasterizerState.CullCounterClockwise, Textures[0]=white) are set inside
    /// Draw(). Caller should save/restore if different states are needed afterward.
    /// </summary>
    public class Trail : IDisposable
    {
        // ── GPU resources (owned by Trail) ──────────────────────────────
        private readonly GraphicsDevice _device;
        private readonly Effect _effect;
        private readonly EffectParameter _viewProjParam;

        private VertexBuffer _geometryBuffer;   // slot 0, freq=0: Vector3 positions
        private VertexBuffer _instanceBuffer;    // slot 1, freq=1: TrailInstanceData[]
        private IndexBuffer _indexBuffer;        // sequential 0..N-1
        private Texture2D _dummyTex;             // 1×1 white (PS doesn't sample, compat)
        private readonly VertexDeclaration _geometryDecl;
        private readonly VertexDeclaration _instanceDecl;

        // ── State ──────────────────────────────────────────────────────
        private readonly List<TrailRecord> _records = new List<TrailRecord>();
        private int _maxTrailLength;
        private int _vertexCount;
        private int _primitiveCount;
        private Color _headColor;
        private Color _tailColor;

        // ── GPU instance data struct (Sequential layout) ────────────────
        [StructLayout(LayoutKind.Sequential)]
        private struct TrailInstanceData
        {
            public Vector4 Pos;    // world position (xyz), w unused
            public Vector4 Rot;    // rotation quaternion (xyzw)
            public Vector4 Color;  // rgba

            public TrailInstanceData(Vector3 pos, Quaternion rot, Vector4 color)
            {
                Pos = new Vector4(pos, 0);
                Rot = new Vector4(rot.X, rot.Y, rot.Z, rot.W);
                Color = color;
            }
        }

        // ── Public API ─────────────────────────────────────────────────

        /// <summary>Color of the newest (head) trail instance.</summary>
        public Color HeadColor
        {
            get => _headColor;
            set => _headColor = value;
        }

        /// <summary>Color of the oldest (tail) trail instance.</summary>
        public Color TailColor
        {
            get => _tailColor;
            set => _tailColor = value;
        }

        /// <summary>Current number of recorded trail frames.</summary>
        public int RecordCount => _records.Count;

        /// <summary>Maximum number of trail instances (instance buffer capacity).</summary>
        public int MaxTrailLength => _maxTrailLength;

        /// <summary>Number of vertices in the current geometry.</summary>
        public int VertexCount => _vertexCount;

        /// <summary>Number of triangles in the current geometry.</summary>
        public int PrimitiveCount => _primitiveCount;

        /// <summary>
        /// Create a trail renderer.
        /// </summary>
        /// <param name="effect">Pre-loaded Effect (from TrailEffect.feb).
        ///   Must have a "ViewProj" matrix parameter at register c0 and a
        ///   technique with a pass that matches the TrailEffect shader contract.</param>
        /// <param name="device">Graphics device for buffer allocation.</param>
        /// <param name="geometry">Mesh vertices in local space forming a triangle list.
        ///   Each group of 3 vertices = 1 triangle.</param>
        /// <param name="maxTrailLength">Maximum number of trail instances.</param>
        /// <param name="headColor">Color of the most recent instance (default: red).</param>
        /// <param name="tailColor">Color of the oldest instance (default: blue, alpha=0).</param>
        public Trail(Effect effect, GraphicsDevice device,
            Vector3[] geometry,
            int maxTrailLength = 256,
            Color? headColor = null,
            Color? tailColor = null)
        {
            _effect = effect ?? throw new ArgumentNullException(nameof(effect));
            _device = device ?? throw new ArgumentNullException(nameof(device));

            if (geometry == null || geometry.Length == 0)
                throw new ArgumentException("Geometry must not be null or empty.", nameof(geometry));
            if (geometry.Length % 3 != 0)
                throw new ArgumentException(
                    $"Geometry vertex count ({geometry.Length}) must be a multiple of 3 for triangle list.", nameof(geometry));
            if (maxTrailLength <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxTrailLength));

            _maxTrailLength = maxTrailLength;
            _headColor = headColor ?? Color.Red;
            _tailColor = tailColor ?? new Color(0, 0, 255, 0);

            _viewProjParam = effect.Parameters["ViewProj"];

            // Vertex declarations (fixed layout matching TrailEffect_vs.hlsl)
            _geometryDecl = new VertexDeclaration(
                new VertexElement(0, VertexElementFormat.Vector3,
                    VertexElementUsage.Position, 0)
            );
            _instanceDecl = new VertexDeclaration(
                new VertexElement(0,  VertexElementFormat.Vector4,
                    VertexElementUsage.TextureCoordinate, 0),   // Position
                new VertexElement(16, VertexElementFormat.Vector4,
                    VertexElementUsage.TextureCoordinate, 1),   // Rotation quaternion
                new VertexElement(32, VertexElementFormat.Vector4,
                    VertexElementUsage.TextureCoordinate, 2)    // Color
            );

            // Geometry + index buffers
            CreateGeometryBuffers(geometry);

            // Instance buffer (empty initially, rebuilt per frame)
            _instanceBuffer = new VertexBuffer(device, _instanceDecl,
                maxTrailLength, BufferUsage.WriteOnly);

            // Dummy texture (PS doesn't sample, but slot 0 must be valid)
            _dummyTex = TextureGen.White(device);
        }

        /// <summary>
        /// Replace the trail mesh geometry at runtime.
        /// Disposes old geometry/index buffers and creates new ones.
        /// </summary>
        /// <param name="geometry">New mesh vertices in local space (triangle list).</param>
        public void SetGeometry(Vector3[] geometry)
        {
            if (geometry == null || geometry.Length == 0)
                throw new ArgumentException("Geometry must not be null or empty.", nameof(geometry));
            if (geometry.Length % 3 != 0)
                throw new ArgumentException(
                    $"Geometry vertex count ({geometry.Length}) must be a multiple of 3 for triangle list.", nameof(geometry));

            _geometryBuffer?.Dispose();
            _indexBuffer?.Dispose();

            CreateGeometryBuffers(geometry);
        }

        /// <summary>
        /// Record a trail frame. Call once per frame before Draw().
        /// The newest record is at the head (index 0), oldest at the tail.
        /// </summary>
        public void AddRecord(Vector3 position, Quaternion rotation)
        {
            _records.Insert(0, new TrailRecord { Position = position, Rotation = rotation });
            while (_records.Count > _maxTrailLength)
                _records.RemoveAt(_records.Count - 1);
        }

        /// <summary>Clear all recorded trail history (e.g. on teleport).</summary>
        public void ClearRecords()
        {
            _records.Clear();
        }

        /// <summary>
        /// Render the trail.
        ///
        /// Mutates device state: BlendState (NonPremultiplied), DepthStencilState
        /// (Default), RasterizerState (CullCounterClockwise), and Textures[0] (1×1 white).
        /// Save/restore if the caller needs different state afterward.
        ///
        /// No-op if no records have been added.
        /// </summary>
        public void Draw(Matrix view, Matrix projection)
        {
            int count = _records.Count;
            if (count == 0) return;

            // ── Effect parameter ────────────────────────────────────────
            _viewProjParam.SetValue(view * projection);

            // ── Rebuild instance buffer (color gradient + GPU upload) ───
            RebuildInstanceBuffer(count);

            // ── Render state ────────────────────────────────────────────
            _device.BlendState = BlendState.NonPremultiplied;
            _device.DepthStencilState = DepthStencilState.Default;
            _device.RasterizerState = RasterizerState.CullCounterClockwise;

            // ── Bind dummy texture ──────────────────────────────────────
            _device.Textures[0] = _dummyTex;

            // ── Apply effect ────────────────────────────────────────────
            _effect.CurrentTechnique.Passes[0].Apply();

            // ── Bind geometry + instance buffers ────────────────────────
            _device.SetVertexBuffers(
                new VertexBufferBinding(_geometryBuffer, 0, 0),   // slot 0: per-vertex (freq=0)
                new VertexBufferBinding(_instanceBuffer, 0, 1)    // slot 1: per-instance (freq=1)
            );
            _device.Indices = _indexBuffer;

            // ── Draw instanced ──────────────────────────────────────────
            _device.DrawInstancedPrimitives(
                PrimitiveType.TriangleList,
                baseVertex: 0,
                minVertexIndex: 0,
                numVertices: _vertexCount,
                startIndex: 0,
                primitiveCount: _primitiveCount,
                instanceCount: count
            );
        }

        /// <summary>
        /// Resize the instance buffer capacity. Trims excess records if
        /// the new capacity is smaller than the current record count.
        /// </summary>
        public void Resize(int newMaxTrailLength)
        {
            if (newMaxTrailLength <= 0)
                throw new ArgumentOutOfRangeException(nameof(newMaxTrailLength));

            _maxTrailLength = newMaxTrailLength;

            _instanceBuffer?.Dispose();
            _instanceBuffer = new VertexBuffer(_device, _instanceDecl,
                newMaxTrailLength, BufferUsage.WriteOnly);

            while (_records.Count > _maxTrailLength)
                _records.RemoveAt(_records.Count - 1);
        }

        /// <summary>
        /// Dispose GPU resources owned by this Trail.
        /// Does NOT dispose the Effect (caller-owned).
        /// </summary>
        public void Dispose()
        {
            _geometryBuffer?.Dispose();
            _instanceBuffer?.Dispose();
            _indexBuffer?.Dispose();
            _dummyTex?.Dispose();
        }

        // ── Private helpers ──────────────────────────────────────────

        private void CreateGeometryBuffers(Vector3[] geometry)
        {
            _vertexCount = geometry.Length;
            _primitiveCount = _vertexCount / 3;

            // Geometry buffer: per-vertex positions in local space
            _geometryBuffer = new VertexBuffer(_device, _geometryDecl,
                _vertexCount, BufferUsage.WriteOnly);
            _geometryBuffer.SetData(geometry);

            // Index buffer: sequential 0..N-1 (geometry is already triangulated)
            var indices = new uint[_vertexCount];
            for (uint i = 0; i < _vertexCount; i++)
                indices[i] = i;

            _indexBuffer = new IndexBuffer(_device, IndexElementSize.ThirtyTwoBits,
                _vertexCount, BufferUsage.WriteOnly);
            _indexBuffer.SetData(indices);
        }

        private void RebuildInstanceBuffer(int count)
        {
            var data = new TrailInstanceData[count];
            float n = Math.Max(count - 1, 1);

            Vector4 head = _headColor.ToVector4();
            Vector4 tail = _tailColor.ToVector4();

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / n;
                Vector4 color = new Vector4(
                    head.X + (tail.X - head.X) * t,
                    head.Y + (tail.Y - head.Y) * t,
                    head.Z + (tail.Z - head.Z) * t,
                    head.W + (tail.W - head.W) * t
                );
                data[i] = new TrailInstanceData(
                    _records[i].Position,
                    _records[i].Rotation,
                    color
                );
            }

            _instanceBuffer.SetData(data, 0, count);
        }
    }
}
