using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNA.Gui
{
    /// <summary>
    /// GUI renderer backed by FNA SpriteBatch + SDF text batch.
    /// Manages clip stack via GraphicsDevice.ScissorRectangle, flushing
    /// SpriteBatch on clip changes. Text goes through a dedicated
    /// <see cref="SdfTextBatch"/> for SDF effect rendering.
    /// </summary>
    public class SpriteBatchGuiRenderer : IGuiRenderer, IDisposable
    {
        private readonly GraphicsDevice _device;
        private readonly SpriteBatch _spriteBatch;
        private readonly SdfTextBatch _textBatch;
        private readonly Texture2D _whiteTexture;

        // Clip stack: each entry is the active scissor rectangle in physical pixels
        private readonly Stack<Rectangle> _clipStack = new();
        private RasterizerState _scissorOn = null!;
        private RasterizerState _scissorOff = null!;

        private bool _inBatch;
        private Matrix _transform;
        private Rectangle _currentScissor;

        // ── SDF text effect properties (proxy to SdfTextBatch) ───

        /// <summary>Edge softness in SDF units (default 0.05).</summary>
        public float TextSmoothing
        {
            get => _textBatch.Smoothing;
            set => _textBatch.Smoothing = value;
        }

        /// <summary>Outline color (alpha controls visibility).</summary>
        public Color TextOutlineColor
        {
            get => _textBatch.OutlineColor;
            set => _textBatch.OutlineColor = value;
        }

        /// <summary>Outline width in SDF units. 0 = no outline.</summary>
        public float TextOutlineWidth
        {
            get => _textBatch.OutlineWidth;
            set => _textBatch.OutlineWidth = value;
        }

        /// <summary>Font weight offset. Positive = bolder, negative = lighter.</summary>
        public float TextWeight
        {
            get => _textBatch.Weight;
            set => _textBatch.Weight = value;
        }

        public SpriteBatchGuiRenderer(GraphicsDevice device)
        {
            _device = device;
            _spriteBatch = new SpriteBatch(device);
            _textBatch = new SdfTextBatch(device);
            _whiteTexture = CreateWhiteTexture(device);

            _scissorOn = new RasterizerState
            {
                ScissorTestEnable = true,
                CullMode = CullMode.None,
                FillMode = FillMode.Solid,
            };

            _scissorOff = new RasterizerState
            {
                ScissorTestEnable = false,
                CullMode = CullMode.None,
                FillMode = FillMode.Solid,
            };
        }

        private static Texture2D CreateWhiteTexture(GraphicsDevice device)
        {
            var tex = new Texture2D(device, 1, 1);
            tex.SetData(new[] { Color.White });
            return tex;
        }

        // ── Begin / End ────────────────────────────────────────────

        public void Begin(Matrix transform)
        {
            _transform = transform;
            _clipStack.Clear();

            // Default clip = viewport bounds
            var vp = _device.Viewport;
            _currentScissor = new Rectangle(vp.X, vp.Y, vp.Width, vp.Height);
            _clipStack.Push(_currentScissor);

            ApplyScissor();
            _spriteBatch.Begin(
                SpriteSortMode.Deferred,
                BlendState.AlphaBlend,
                SamplerState.LinearClamp,
                DepthStencilState.None,
                _scissorOn,
                null,
                _transform);
            _inBatch = true;
        }

        public void End()
        {
            // Flush remaining sprite batch and text batch
            if (_inBatch)
            {
                _spriteBatch.End();
                _inBatch = false;
            }

            // Text batch: flush with orthographic projection + SDF font atlas
            if (_currentFont != null)
            {
                var vp = _device.Viewport;
                var projection = Matrix.CreateOrthographicOffCenter(
                    0, vp.Width, vp.Height, 0, 0, -1);
                var finalMatrix = projection * _transform;
                _textBatch.End(finalMatrix, _currentFont.Atlas);
                _currentFont = null;
            }
        }

        // ── Clip Stack ─────────────────────────────────────────────

        public void PushClip(Rectangle rect)
        {
            // Intersect with current clip
            var intersected = Intersect(_currentScissor, rect);
            _clipStack.Push(intersected);

            if (intersected != _currentScissor)
            {
                _currentScissor = intersected;
                RestartBatch();
            }
        }

        public void PopClip()
        {
            if (_clipStack.Count <= 1) return; // don't pop root

            _clipStack.Pop();
            var newScissor = _clipStack.Peek();

            if (newScissor != _currentScissor)
            {
                _currentScissor = newScissor;
                RestartBatch();
            }
        }

        private static Rectangle Intersect(Rectangle a, Rectangle b)
        {
            int left = Math.Max(a.X, b.X);
            int top = Math.Max(a.Y, b.Y);
            int right = Math.Min(a.X + a.Width, b.X + b.Width);
            int bottom = Math.Min(a.Y + a.Height, b.Y + b.Height);

            if (right <= left || bottom <= top)
                return new Rectangle(left, top, 0, 0);

            return new Rectangle(left, top, right - left, bottom - top);
        }

        private void RestartBatch()
        {
            if (_inBatch)
            {
                _spriteBatch.End();
                _inBatch = false;
            }

            ApplyScissor();

            _spriteBatch.Begin(
                SpriteSortMode.Deferred,
                BlendState.AlphaBlend,
                SamplerState.LinearClamp,
                DepthStencilState.None,
                _scissorOn,
                null,
                _transform);
            _inBatch = true;
        }

        private void ApplyScissor()
        {
            _device.ScissorRectangle = _currentScissor;
        }

        // ── Draw Methods ───────────────────────────────────────────

        public void DrawRect(Rectangle rect, Color color)
        {
            EnsureBatch();
            _spriteBatch.Draw(_whiteTexture, rect, color);
        }

        public void DrawTexture(Texture2D texture, Rectangle destination,
            Rectangle? source, Color tint)
        {
            EnsureBatch();
            _spriteBatch.Draw(texture, destination, source, tint);
        }

        public void DrawNineSlice(NineSlice slice, Rectangle destination, Color tint)
        {
            EnsureBatch();

            var tex = slice.Texture;
            var border = slice.Border;
            var srcRect = slice.SourceRect ?? new Rectangle(0, 0, tex.Width, tex.Height);

            // Source regions for the 9 slices
            float srcX = srcRect.X, srcY = srcRect.Y;
            float srcW = srcRect.Width, srcH = srcRect.Height;
            float bl = border.Left, bt = border.Top, br = border.Right, bb = border.Bottom;

            // ── Source rectangles (9 regions) ──
            // Top row
            var srcTL = new Rectangle((int)srcX, (int)srcY, (int)bl, (int)bt);
            var srcT = new Rectangle((int)(srcX + bl), (int)srcY, (int)(srcW - bl - br), (int)bt);
            var srcTR = new Rectangle((int)(srcX + srcW - br), (int)srcY, (int)br, (int)bt);
            // Middle row
            var srcL = new Rectangle((int)srcX, (int)(srcY + bt), (int)bl, (int)(srcH - bt - bb));
            var srcC = new Rectangle((int)(srcX + bl), (int)(srcY + bt), (int)(srcW - bl - br), (int)(srcH - bt - bb));
            var srcR = new Rectangle((int)(srcX + srcW - br), (int)(srcY + bt), (int)br, (int)(srcH - bt - bb));
            // Bottom row
            var srcBL = new Rectangle((int)srcX, (int)(srcY + srcH - bb), (int)bl, (int)bb);
            var srcB = new Rectangle((int)(srcX + bl), (int)(srcY + srcH - bb), (int)(srcW - bl - br), (int)bb);
            var srcBR = new Rectangle((int)(srcX + srcW - br), (int)(srcY + srcH - bb), (int)br, (int)bb);

            // ── Destination rectangles ──
            int dx = destination.X, dy = destination.Y;
            int dw = destination.Width, dh = destination.Height;

            // Clamp border sizes so corners don't overlap
            float scaleW = dw < bl + br ? dw / (bl + br) : 1f;
            float scaleH = dh < bt + bb ? dh / (bt + bb) : 1f;
            float cl = bl * scaleW, ct = bt * scaleH;
            float cr = br * scaleW, cb = bb * scaleH;
            int icl = (int)cl, ict = (int)ct, icr = (int)cr, icb = (int)cb;

            // Top row
            _spriteBatch.Draw(tex, new Rectangle(dx, dy, icl, ict), srcTL, tint);
            _spriteBatch.Draw(tex, new Rectangle(dx + icl, dy, dw - icl - icr, ict), srcT, tint);
            _spriteBatch.Draw(tex, new Rectangle(dx + dw - icr, dy, icr, ict), srcTR, tint);
            // Middle row
            _spriteBatch.Draw(tex, new Rectangle(dx, dy + ict, icl, dh - ict - icb), srcL, tint);
            _spriteBatch.Draw(tex, new Rectangle(dx + icl, dy + ict, dw - icl - icr, dh - ict - icb), srcC, tint);
            _spriteBatch.Draw(tex, new Rectangle(dx + dw - icr, dy + ict, icr, dh - ict - icb), srcR, tint);
            // Bottom row
            _spriteBatch.Draw(tex, new Rectangle(dx, dy + dh - icb, icl, icb), srcBL, tint);
            _spriteBatch.Draw(tex, new Rectangle(dx + icl, dy + dh - icb, dw - icl - icr, icb), srcB, tint);
            _spriteBatch.Draw(tex, new Rectangle(dx + dw - icr, dy + dh - icb, icr, icb), srcBR, tint);
        }

        public void DrawGeometry(GeometryBuffer geometry, Color tint)
        {
            EnsureBatch();

            var quads = geometry.AsSpan();
            for (int i = 0; i < quads.Length; i++)
            {
                var quad = quads[i];
                var finalColor = new Color(
                    (quad.Color.R * tint.R) / 255,
                    (quad.Color.G * tint.G) / 255,
                    (quad.Color.B * tint.B) / 255,
                    (quad.Color.A * tint.A) / 255);

                var tex = quad.Texture ?? _whiteTexture;
                var dst = new Rectangle(
                    (int)quad.Position.X, (int)quad.Position.Y,
                    (int)quad.Size.X, (int)quad.Size.Y);
                _spriteBatch.Draw(tex, dst, null, finalColor);
            }
        }

        /// <summary>
        /// Submit SDF text for batched rendering. Accumulated across widgets
        /// and flushed in End() via the dedicated SDF effect pass.
        /// </summary>
        public void DrawSdfText(SdfFont font, string text,
            Vector2 position, Color color, float scale)
        {
            _textBatch.DrawString(font, text, position, color, scale);
            _currentFont = font;
        }

        private SdfFont? _currentFont;

        private void EnsureBatch()
        {
            if (!_inBatch)
            {
                _spriteBatch.Begin(
                    SpriteSortMode.Deferred,
                    BlendState.AlphaBlend,
                    SamplerState.LinearClamp,
                    DepthStencilState.None,
                    _scissorOn,
                    null,
                    _transform);
                _inBatch = true;
            }
        }

        // ── Texture Factory ─────────────────────────────────────────

        /// <summary>Get the 1x1 white texture for solid fills.</summary>
        public Texture2D WhiteTexture => _whiteTexture;

        /// <summary>
        /// Create a procedural 9-slice skin texture (for testing / default skins).
        /// </summary>
        public static Texture2D CreateCheckerSkin(GraphicsDevice device,
            int size, int border, Color fill, Color edge)
        {
            var tex = new Texture2D(device, size, size);
            var data = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    data[y * size + x] = fill;
            // Draw border edges
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    if (x < border || x >= size - border ||
                        y < border || y >= size - border)
                        data[y * size + x] = edge;
            tex.SetData(data);
            return tex;
        }

        public void Dispose()
        {
            _textBatch?.Dispose();
            _whiteTexture?.Dispose();
            _spriteBatch?.Dispose();
        }
    }
}
