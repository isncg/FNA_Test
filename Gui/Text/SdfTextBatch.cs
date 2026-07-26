using System;
using System.IO;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNA.Gui
{
    /// <summary>
    /// Batches SDF text glyphs into a single draw call using dynamic vertex/index buffers.
    /// Owned by <see cref="SpriteBatchGuiRenderer"/> and flushed during End().
    /// </summary>
    public class SdfTextBatch : IDisposable
    {
        private readonly GraphicsDevice _device;
        private readonly Effect _effect;

        private EffectParameter _matrixParam = null!;
        private EffectParameter _smoothingParam = null!;
        private EffectParameter _outlineColorParam = null!;
        private EffectParameter _outlineWidthParam = null!;
        private EffectParameter _weightParam = null!;
        private EffectParameter _textureParam = null!;

        private VertexBuffer _vertexBuffer = null!;
        private IndexBuffer _indexBuffer = null!;
        private int _maxGlyphs;

        private VertexPositionColorTexture[] _vertices = null!;
        private int[] _indices = null!;
        private int _glyphCount;

        private const int InitialMaxGlyphs = 256;

        public float Smoothing { get; set; } = 0.05f;
        public Color OutlineColor { get; set; } = Color.Black;
        public float OutlineWidth { get; set; } = 0.0f;
        public float Weight { get; set; } = 0.0f;

        public SdfTextBatch(GraphicsDevice device)
        {
            _device = device;

            // Load embedded FEB effect from the Gui assembly
            var asm = Assembly.GetExecutingAssembly();
            string febName = null!;
            foreach (var name in asm.GetManifestResourceNames())
            {
                if (name.EndsWith(".SDFText.feb", StringComparison.OrdinalIgnoreCase))
                {
                    febName = name;
                    break;
                }
            }

            if (febName == null)
                throw new FileNotFoundException(
                    $"SDFText.feb not found in embedded resources. " +
                    $"Available: {string.Join(", ", asm.GetManifestResourceNames())}");

            using var stream = asm.GetManifestResourceStream(febName)!;
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            _effect = new Effect(device, ms.ToArray());

            _matrixParam = _effect.Parameters["MatrixTransform"]!;
            _smoothingParam = _effect.Parameters["Smoothing"]!;
            _outlineColorParam = _effect.Parameters["OutlineColor"]!;
            _outlineWidthParam = _effect.Parameters["OutlineWidth"]!;
            _weightParam = _effect.Parameters["Weight"]!;
            _textureParam = _effect.Parameters["SDFTexture"]!;

            _maxGlyphs = InitialMaxGlyphs;
            CreateBuffers();
        }

        private void CreateBuffers()
        {
            _vertexBuffer?.Dispose();
            _indexBuffer?.Dispose();

            _vertexBuffer = new VertexBuffer(_device,
                typeof(VertexPositionColorTexture),
                _maxGlyphs * 4, BufferUsage.WriteOnly);
            _indexBuffer = new IndexBuffer(_device,
                IndexElementSize.ThirtyTwoBits,
                _maxGlyphs * 6, BufferUsage.WriteOnly);

            _vertices = new VertexPositionColorTexture[_maxGlyphs * 4];
            _indices = new int[_maxGlyphs * 6];
        }

        private void EnsureCapacity(int glyphCount)
        {
            if (glyphCount <= _maxGlyphs) return;
            while (_maxGlyphs < glyphCount)
                _maxGlyphs *= 2;
            CreateBuffers();
        }

        public void DrawString(SdfFont font, string text,
            Vector2 position, Color color, float scale)
        {
            float scaleFactor = scale / font.FontSize;
            float curX = position.X;
            float curY = position.Y;

            EnsureCapacity(_glyphCount + text.Length);

            foreach (char c in text)
            {
                if (c == '\n')
                {
                    curX = position.X;
                    curY += font.LineHeight * scaleFactor;
                    continue;
                }
                if (c == '\r') continue;

                if (!font.Glyphs.TryGetValue(c, out var g))
                    continue;

                float sx = curX + g.OffsetX * scaleFactor;
                float sy = curY - g.OffsetY * scaleFactor;
                float sw = g.W * scaleFactor;
                float sh = g.H * scaleFactor;

                int vi = _glyphCount * 4;

                _vertices[vi + 0] = new VertexPositionColorTexture(
                    new Vector3(sx, sy, 0), color, new Vector2(g.U0, g.V0));
                _vertices[vi + 1] = new VertexPositionColorTexture(
                    new Vector3(sx + sw, sy, 0), color, new Vector2(g.U1, g.V0));
                _vertices[vi + 2] = new VertexPositionColorTexture(
                    new Vector3(sx, sy + sh, 0), color, new Vector2(g.U0, g.V1));
                _vertices[vi + 3] = new VertexPositionColorTexture(
                    new Vector3(sx + sw, sy + sh, 0), color, new Vector2(g.U1, g.V1));

                int ii = _glyphCount * 6;
                int bi = _glyphCount * 4;
                _indices[ii + 0] = bi + 0;
                _indices[ii + 1] = bi + 1;
                _indices[ii + 2] = bi + 2;
                _indices[ii + 3] = bi + 2;
                _indices[ii + 4] = bi + 1;
                _indices[ii + 5] = bi + 3;

                _glyphCount++;
                curX += g.Advance * scaleFactor;
            }
        }

        /// <summary>Flush all batched text to the GPU and reset the batch.</summary>
        public void End(Matrix projection, Texture2D atlasTexture)
        {
            if (_glyphCount == 0) return;

            _vertexBuffer.SetData(_vertices, 0, _glyphCount * 4);
            _indexBuffer.SetData(_indices, 0, _glyphCount * 6);

            _matrixParam.SetValue(projection);
            _smoothingParam.SetValue(Smoothing);
            _outlineColorParam.SetValue(OutlineColor.ToVector4());
            _outlineWidthParam.SetValue(OutlineWidth);
            _weightParam.SetValue(Weight);
            _textureParam.SetValue(atlasTexture);

            _effect.CurrentTechnique!.Passes[0].Apply();

            _device.SetVertexBuffer(_vertexBuffer);
            _device.Indices = _indexBuffer;

            _device.DrawIndexedPrimitives(
                PrimitiveType.TriangleList,
                baseVertex: 0,
                minVertexIndex: 0,
                numVertices: _glyphCount * 4,
                startIndex: 0,
                primitiveCount: _glyphCount * 2);

            _glyphCount = 0;
        }

        public void Dispose()
        {
            _effect?.Dispose();
            _vertexBuffer?.Dispose();
            _indexBuffer?.Dispose();
        }
    }
}
