using System;
using System.IO;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SDFFontTest
{
    /// <summary>
    /// Batches SDF text glyphs into a single draw call using dynamic
    /// vertex and index buffers. Similar to SpriteBatch's internal text
    /// rendering but for SDF fonts with support for outline effects.
    ///
    /// Usage:
    ///   renderer.DrawString(font, "Hello", position, Color.White, 1.0f);
    ///   renderer.DrawString(font, "World", position2, Color.Red, 0.5f);
    ///   renderer.End(projectionMatrix);
    /// </summary>
    public class SDFTextRenderer : IDisposable
    {
        private GraphicsDevice _device;
        private Effect _effect;

        // Cached effect parameters
        private EffectParameter _matrixParam;
        private EffectParameter _smoothingParam;
        private EffectParameter _outlineColorParam;
        private EffectParameter _outlineWidthParam;
        private EffectParameter _weightParam;
        private EffectParameter _textureParam;

        // Dynamic geometry
        private VertexBuffer _vertexBuffer;
        private IndexBuffer _indexBuffer;
        private int _maxGlyphs;

        // CPU-side vertex/index accumulation
        private VertexPositionColorTexture[] _vertices;
        private int[] _indices;
        private int _glyphCount;

        private const int InitialMaxGlyphs = 256;

        /// <summary>
        /// Edge softness in SDF units. Fixed for crisp anti-aliasing.
        /// </summary>
        public float Smoothing { get; set; } = 0.05f;

        /// <summary>Outline color (alpha controls visibility).</summary>
        public Color OutlineColor { get; set; } = Color.Black;

        /// <summary>
        /// Outline width in SDF units. 0 = no outline.
        /// Positive values expand outward.
        /// </summary>
        public float OutlineWidth { get; set; } = 0.0f;

        /// <summary>
        /// Font weight offset. 0 = normal weight.
        /// Positive values (+0.05 to +0.10) = bolder, negative values = lighter.
        /// </summary>
        public float Weight { get; set; } = 0.0f;

        public SDFTextRenderer(GraphicsDevice device)
        {
            _device = device;

            // Load embedded FEB effect
            var asm = Assembly.GetExecutingAssembly();
            string febName = null;
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

            using var stream = asm.GetManifestResourceStream(febName);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            _effect = new Effect(device, ms.ToArray());

            // Cache parameters
            _matrixParam = _effect.Parameters["MatrixTransform"];
            _smoothingParam = _effect.Parameters["Smoothing"];
            _outlineColorParam = _effect.Parameters["OutlineColor"];
            _outlineWidthParam = _effect.Parameters["OutlineWidth"];
            _weightParam = _effect.Parameters["Weight"];
            _textureParam = _effect.Parameters["SDFTexture"];

            // Create initial buffers
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
            if (glyphCount <= _maxGlyphs)
                return;

            // Grow by doubling
            while (_maxGlyphs < glyphCount)
                _maxGlyphs *= 2;

            CreateBuffers();
        }

        /// <summary>
        /// Add a string to the batch. Does not draw — call End() to flush.
        /// Multiple DrawString calls may be batched together before End().
        /// </summary>
        /// <param name="font">The SDF font to use.</param>
        /// <param name="text">Text string to render.</param>
        /// <param name="position">
        /// Screen-space position of the first character's BASELINE origin.
        /// Y increases downward (standard screen coordinates).
        /// </param>
        /// <param name="color">Text color (premultiplied by the shader).</param>
        /// <param name="scale">
        /// Scale factor relative to the font's reference size.
        /// 1.0 = reference size, 0.5 = half size, 2.0 = double size.
        /// </param>
        public void DrawString(SDFFont font, string text,
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
                if (c == '\r')
                    continue;

                if (!font.Glyphs.TryGetValue(c, out var g))
                    continue;

                // Compute screen-space quad.
                float sx = curX + g.OffsetX * scaleFactor;
                float sy = curY - g.OffsetY * scaleFactor;
                float sw = g.W * scaleFactor;
                float sh = g.H * scaleFactor;

                int vi = _glyphCount * 4;

                _vertices[vi + 0] = new VertexPositionColorTexture(
                    new Vector3(sx, sy, 0),
                    color,
                    new Vector2(g.U0, g.V0));

                _vertices[vi + 1] = new VertexPositionColorTexture(
                    new Vector3(sx + sw, sy, 0),
                    color,
                    new Vector2(g.U1, g.V0));

                _vertices[vi + 2] = new VertexPositionColorTexture(
                    new Vector3(sx, sy + sh, 0),
                    color,
                    new Vector2(g.U0, g.V1));

                _vertices[vi + 3] = new VertexPositionColorTexture(
                    new Vector3(sx + sw, sy + sh, 0),
                    color,
                    new Vector2(g.U1, g.V1));

                // Two triangles per glyph quad (counter-clockwise winding)
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

        /// <summary>
        /// Flush all batched DrawString calls to the GPU.
        /// </summary>
        /// <param name="projection">
        /// Combined world-view-projection matrix (usually orthographic).
        /// </param>
        /// <param name="atlasTexture">
        /// The SDF atlas texture to bind. Must be non-null.
        /// </param>
        public void End(Matrix projection, Texture2D atlasTexture)
        {
            if (_glyphCount == 0)
                return;

            // Upload vertex and index data
            _vertexBuffer.SetData(_vertices, 0, _glyphCount * 4);
            _indexBuffer.SetData(_indices, 0, _glyphCount * 6);

            // Set effect parameters
            _matrixParam.SetValue(projection);
            _smoothingParam.SetValue(Smoothing);
            _outlineColorParam.SetValue(OutlineColor.ToVector4());
            _outlineWidthParam.SetValue(OutlineWidth);
            _weightParam.SetValue(Weight);
            _textureParam.SetValue(atlasTexture);

            // Apply effect (commits parameters, binds textures)
            _effect.CurrentTechnique.Passes[0].Apply();

            // Bind geometry
            _device.SetVertexBuffer(_vertexBuffer);
            _device.Indices = _indexBuffer;

            // Draw
            _device.DrawIndexedPrimitives(
                PrimitiveType.TriangleList,
                baseVertex: 0,
                minVertexIndex: 0,
                numVertices: _glyphCount * 4,
                startIndex: 0,
                primitiveCount: _glyphCount * 2
            );

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
