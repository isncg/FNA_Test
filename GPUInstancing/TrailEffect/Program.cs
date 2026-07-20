using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNA.Test;

namespace TrailEffectDemo
{
    /// <summary>
    /// GPU-instanced cube trail effect. A cube orbits the origin while self-rotating.
    /// Each frame records position+rotation into a history buffer. The entire trail
    /// is drawn in a single DrawInstancedPrimitives call, with per-instance color
    /// gradient: red/opaque (head) → blue/transparent (tail).
    /// </summary>
    public class TrailEffectGame : Game
    {
        private GraphicsDeviceManager graphics;
        private Effect effect;
        private IndexBuffer indexBuffer;
        private VertexBuffer geometryBuffer;   // slot 0, freq=0: 36 cube positions
        private VertexBuffer instanceBuffer;    // slot 1, freq=1: N trail instances
        private Texture2D dummyTex;             // 1×1 white texture (unused by PS, for compat)

        // ── Effect parameters ───────────────────────────────────────────
        private EffectParameter viewProjParam;

        // ── Vertex declarations ─────────────────────────────────────────
        private VertexDeclaration geometryDecl;
        private VertexDeclaration instanceDecl;

        // ── Config (ImGui-tweakable) ────────────────────────────────────
        private int maxTrailLength = 256;
        private float orbitSpeed = 1.5f;
        private float orbitRadiusX = 2.0f;
        private float orbitRadiusY = 1.5f;
        private float orbitRadiusZ = 1.5f;
        private float selfRotationSpeed = 3.0f;
        private float cubeScale = 0.5f;
        private float cameraDistance = 6.0f;
        private float cameraHeight = 1.5f;
        private bool pauseOrbit = false;

        // ── State ───────────────────────────────────────────────────────
        private List<TrailRecord> trailRecords = new List<TrailRecord>();
        private float totalTime;

        // ── Instance data struct (GPU-side, Sequential layout) ──────────
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

        // ── CPU trail record ────────────────────────────────────────────
        private struct TrailRecord
        {
            public Vector3 Position;
            public Quaternion Rotation;
        }

        public TrailEffectGame()
        {
            graphics = new GraphicsDeviceManager(this)
            {
                PreferredBackBufferWidth = 800,
                PreferredBackBufferHeight = 600,
                SynchronizeWithVerticalRetrace = false
            };
            Window.Title = "Trail Effect Demo — GPU Instancing | ESC=quit";
            IsMouseVisible = true;
        }

        protected override void LoadContent()
        {
            // ── Load embedded FEB ────────────────────────────────────────
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("TrailEffectDemo.TrailEffect.feb");
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            effect = new Effect(GraphicsDevice, ms.ToArray());

            viewProjParam = effect.Parameters["ViewProj"];

            Console.WriteLine($"[TrailEffect] Effect loaded: {effect.Techniques.Count} techniques, {effect.Parameters.Count} params");

            // ── Vertex declarations ─────────────────────────────────────
            geometryDecl = new VertexDeclaration(
                new VertexElement(0, VertexElementFormat.Vector3,
                    VertexElementUsage.Position, 0)
            );
            instanceDecl = new VertexDeclaration(
                new VertexElement(0,  VertexElementFormat.Vector4,
                    VertexElementUsage.TextureCoordinate, 0),   // Position
                new VertexElement(16, VertexElementFormat.Vector4,
                    VertexElementUsage.TextureCoordinate, 1),   // Rotation quaternion
                new VertexElement(32, VertexElementFormat.Vector4,
                    VertexElementUsage.TextureCoordinate, 2)    // Color
            );

            // ── Geometry buffer: scaled cube ────────────────────────────
            geometryBuffer = new VertexBuffer(GraphicsDevice, geometryDecl,
                36, BufferUsage.WriteOnly);
            RebuildGeometryBuffer();

            // ── Index buffer: sequential 0..35 ──────────────────────────
            indexBuffer = new IndexBuffer(GraphicsDevice, IndexElementSize.ThirtyTwoBits,
                36, BufferUsage.WriteOnly);
            var indices = new uint[36];
            for (uint i = 0; i < 36; i++) indices[i] = i;
            indexBuffer.SetData(indices);

            // ── Instance buffer: initially empty, rebuilt each frame ────
            instanceBuffer = new VertexBuffer(GraphicsDevice, instanceDecl,
                1, BufferUsage.WriteOnly);

            // ── Dummy texture (PS doesn't sample, but we bind to avoid warnings) ──
            dummyTex = TextureGen.White(GraphicsDevice);

            // ── ImGui ───────────────────────────────────────────────────
            ImGuiTestHarness.Init(GraphicsDevice);

            Console.WriteLine($"[TrailEffect] Ready. Max trail length: {maxTrailLength}");
        }

        private void RebuildGeometryBuffer()
        {
            var rawPositions = GeometryGen.CubePositions();
            var scaled = new Vector3[36];
            for (int i = 0; i < 36; i++)
                scaled[i] = rawPositions[i] * cubeScale;
            geometryBuffer.SetData(scaled);
        }

        private void RebuildInstanceBuffer()
        {
            int count = trailRecords.Count;
            if (count == 0) return;

            instanceBuffer.Dispose();

            var data = new TrailInstanceData[count];
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / Math.Max(count - 1, 1);
                // Red → Blue gradient
                Vector4 color = new Vector4(
                    1.0f - t,   // R: 1 → 0
                    0.0f,       // G: always 0
                    t,          // B: 0 → 1
                    1.0f - t    // A: 1 → 0
                );
                data[i] = new TrailInstanceData(
                    trailRecords[i].Position,
                    trailRecords[i].Rotation,
                    color
                );
            }

            instanceBuffer = new VertexBuffer(GraphicsDevice, instanceDecl,
                count, BufferUsage.WriteOnly);
            instanceBuffer.SetData(data);
        }

        protected override void Update(GameTime gameTime)
        {
            var kb = Keyboard.GetState();
            if (kb.IsKeyDown(Keys.Escape)) Exit();

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            if (!pauseOrbit)
                totalTime += dt;

            // ── Compute orbit position (3D Lissajous path) ───────────────
            float t = totalTime * orbitSpeed;
            var pos = new Vector3(
                orbitRadiusX * (float)Math.Cos(t),
                orbitRadiusY * (float)Math.Sin(t * 0.7f),
                orbitRadiusZ * (float)Math.Sin(t * 0.5f)
            );

            // ── Compute self-rotation ────────────────────────────────────
            var rotAxis = Vector3.Normalize(new Vector3(1.0f, 0.5f, 0.3f));
            float rotAngle = totalTime * selfRotationSpeed;
            var rot = Quaternion.CreateFromAxisAngle(rotAxis, rotAngle);

            // ── Record trail ────────────────────────────────────────────
            trailRecords.Insert(0, new TrailRecord { Position = pos, Rotation = rot });
            while (trailRecords.Count > maxTrailLength)
                trailRecords.RemoveAt(trailRecords.Count - 1);

            // ── Headless test ───────────────────────────────────────────
            TestHarness.Tick(this, 3, () =>
            {
                var px = TestHarness.ReadBackbuffer(GraphicsDevice);
                int fails = 0;
                // Trail cubes should be visible on the dark background
                Color bg = new Color(10, 10, 26);
                fails += TestHarness.AssertCoverage(px, bg, 0.005f, "trail-coverage");
                TestHarness.Report("TrailEffect", fails);
            });
        }

        protected override void Draw(GameTime gameTime)
        {
            ImGuiTestHarness.NewFrame(GraphicsDevice);

            GraphicsDevice.Clear(new Color(10, 10, 26)); // dark blue-ish background

            if (trailRecords.Count == 0) return;

            // ── Camera ──────────────────────────────────────────────────
            var camPos = new Vector3(0, cameraHeight, cameraDistance);
            var view = Matrix.CreateLookAt(camPos, Vector3.Zero, Vector3.Up);
            var proj = Matrix.CreatePerspectiveFieldOfView(
                MathHelper.PiOver4,
                GraphicsDevice.Viewport.AspectRatio,
                0.1f, 100f
            );
            viewProjParam.SetValue(view * proj);

            // ── Render state ────────────────────────────────────────────
            GraphicsDevice.BlendState = BlendState.NonPremultiplied;
            GraphicsDevice.DepthStencilState = DepthStencilState.Default;
            GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;

            // ── Rebuild instance buffer with current trail data ─────────
            RebuildInstanceBuffer();

            // ── Bind dummy texture (PS doesn't sample, but slot 0 should be valid) ──
            GraphicsDevice.Textures[0] = dummyTex;

            // ── Apply effect ────────────────────────────────────────────
            effect.CurrentTechnique.Passes[0].Apply();

            // ── Bind geometry + instance buffers ────────────────────────
            GraphicsDevice.SetVertexBuffers(
                new VertexBufferBinding(geometryBuffer, 0, 0),   // slot 0: per-vertex
                new VertexBufferBinding(instanceBuffer, 0, 1)    // slot 1: per-instance
            );
            GraphicsDevice.Indices = indexBuffer;

            // ── Draw instanced ──────────────────────────────────────────
            GraphicsDevice.DrawInstancedPrimitives(
                PrimitiveType.TriangleList,
                baseVertex: 0,
                minVertexIndex: 0,
                numVertices: 36,
                startIndex: 0,
                primitiveCount: 12,
                instanceCount: trailRecords.Count
            );

            // ── ImGui panel ─────────────────────────────────────────────
            if (!TestHarness.Headless)
                DrawImGui();
        }

        private void DrawImGui()
        {
            ImGuiBindings.BeginPanel("Trail Effect");

            ImGuiBindings.ImGui_Checkbox("Pause Orbit", ref pauseOrbit);

            int[] trailLengths = { 32, 64, 128, 256, 512, 1024 };
            string[] trailNames = { "32", "64", "128", "256", "512", "1024" };
            int ti = Array.IndexOf(trailLengths, maxTrailLength);
            if (ti < 0) ti = 3;
            if (ImGuiBindings.Combo("Trail Length", ref ti, trailNames))
            {
                maxTrailLength = trailLengths[ti];
            }

            bool geomRebuild = false;
            geomRebuild |= ImGuiBindings.ImGui_SliderFloat("Cube Scale", ref cubeScale, 0.05f, 1.0f);
            if (geomRebuild) RebuildGeometryBuffer();

            ImGuiBindings.ImGui_SliderFloat("Orbit Speed", ref orbitSpeed, 0.1f, 5.0f);
            ImGuiBindings.ImGui_SliderFloat("Orbit Radius X", ref orbitRadiusX, 0.5f, 4.0f);
            ImGuiBindings.ImGui_SliderFloat("Orbit Radius Y", ref orbitRadiusY, 0.5f, 4.0f);
            ImGuiBindings.ImGui_SliderFloat("Orbit Radius Z", ref orbitRadiusZ, 0.5f, 4.0f);
            ImGuiBindings.ImGui_SliderFloat("Self-Rot Speed", ref selfRotationSpeed, 0.1f, 10.0f);
            ImGuiBindings.ImGui_SliderFloat("Cam Distance", ref cameraDistance, 2.0f, 15.0f);
            ImGuiBindings.ImGui_SliderFloat("Cam Height", ref cameraHeight, 0.0f, 5.0f);

            ImGuiBindings.ImGui_Text($"Instances: {trailRecords.Count}");
            ImGuiBindings.ImGui_Text($"FPS: {(int)(1.0 / Math.Max(0.001, TargetElapsedTime.TotalSeconds))}");
            ImGuiBindings.EndPanel();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                effect?.Dispose();
                geometryBuffer?.Dispose();
                instanceBuffer?.Dispose();
                indexBuffer?.Dispose();
                dummyTex?.Dispose();
            }
            base.Dispose(disposing);
        }

        static void Main(string[] args)
        {
            TestHarness.ParseArgs(args);
            using var g = new TrailEffectGame();
            g.Run();
        }
    }
}
