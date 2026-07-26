using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNA.Gui
{
    /// <summary>Image display mode.</summary>
    public enum ImageType
    {
        /// <summary>Single quad stretched to content bounds.</summary>
        Simple,
        /// <summary>9-slice / nine-patch: corners stay fixed, edges and center stretch.</summary>
        Sliced,
        /// <summary>Tile the texture repeatedly to fill the content bounds.</summary>
        Tiled,
        /// <summary>Radial or linear fill (for progress bars, cooldown indicators).</summary>
        Filled,
    }

    /// <summary>
    /// Displays a texture or 9-slice image. Emits geometry via the
    /// <see cref="Graphic"/> base class for unified rendering.
    /// </summary>
    public class Image : Graphic
    {
        private Texture2D? _texture;
        private Rectangle? _sourceRect;
        private ImageType _imageType = ImageType.Simple;
        private Thickness _border; // 9-slice border (used when ImageType.Sliced)
        private float _fillAmount = 1.0f;

        public Texture2D? Texture
        {
            get => _texture;
            set { if (_texture != value) { _texture = value; SetGeometryDirty(); InvalidateMeasure(); } }
        }

        public Rectangle? SourceRect
        {
            get => _sourceRect;
            set { if (_sourceRect != value) { _sourceRect = value; SetGeometryDirty(); InvalidateMeasure(); } }
        }

        public ImageType ImageType
        {
            get => _imageType;
            set { if (_imageType != value) { _imageType = value; SetGeometryDirty(); InvalidateMeasure(); } }
        }

        /// <summary>9-slice border margins (only used when ImageType == Sliced).</summary>
        public Thickness Border
        {
            get => _border;
            set { if (_border != value) { _border = value; SetGeometryDirty(); InvalidateMeasure(); } }
        }

        /// <summary>Fill amount (0.0–1.0, only used when ImageType == Filled).</summary>
        public float FillAmount
        {
            get => _fillAmount;
            set { if (_fillAmount != value) { _fillAmount = MathHelper.Clamp(value, 0, 1); SetGeometryDirty(); } }
        }

        protected override Vector2 OnMeasure(Vector2 available)
        {
            if (_texture == null)
                return Vector2.Zero;

            var src = _sourceRect ?? new Rectangle(0, 0, _texture.Width, _texture.Height);
            return new Vector2(src.Width, src.Height);
        }

        protected override void OnRebuildGeometry(Rectangle content, GeometryBuffer buffer)
        {
            IncrementRebuildCount();

            if (_texture == null)
                return;

            switch (_imageType)
            {
                case ImageType.Simple:
                    BuildSimple(content, buffer);
                    break;
                case ImageType.Sliced:
                    BuildSliced(content, buffer);
                    break;
                case ImageType.Tiled:
                    BuildTiled(content, buffer);
                    break;
                case ImageType.Filled:
                    BuildFilled(content, buffer);
                    break;
            }
        }

        private void BuildSimple(Rectangle content, GeometryBuffer buffer)
        {
            var src = _sourceRect ?? new Rectangle(0, 0, _texture!.Width, _texture.Height);
            float u0 = (float)src.X / _texture!.Width;
            float v0 = (float)src.Y / _texture.Height;
            float u1 = (float)(src.X + src.Width) / _texture.Width;
            float v1 = (float)(src.Y + src.Height) / _texture.Height;

            buffer.AddQuad(
                new Vector2(content.X, content.Y),
                new Vector2(content.Width, content.Height),
                new Vector2(u0, v0), new Vector2(u1, v1),
                Color.White, _texture);
        }

        private void BuildSliced(Rectangle content, GeometryBuffer buffer)
        {
            var tex = _texture!;
            var src = _sourceRect ?? new Rectangle(0, 0, tex.Width, tex.Height);
            float bl = _border.Left, bt = _border.Top, br = _border.Right, bb = _border.Bottom;

            // Scale borders if content is smaller than total border
            float cw = content.Width, ch = content.Height;
            float totalBW = bl + br, totalBH = bt + bb;
            float scaleW = cw < totalBW ? cw / totalBW : 1f;
            float scaleH = ch < totalBH ? ch / totalBH : 1f;
            float cl = bl * scaleW, ct = bt * scaleH, cr = br * scaleW, cb = bb * scaleH;

            float sx = src.X, sy = src.Y, sw = src.Width, sh = src.Height;
            float dx = content.X, dy = content.Y, dw = cw, dh = ch;

            // Source UVs for the 3x3 grid
            float[] su = { 0, bl / sw, (sw - br) / sw, 1 };
            float[] sv = { 0, bt / sh, (sh - bb) / sh, 1 };

            // Destination positions
            float[] dxArr = { dx, dx + cl, dx + dw - cr, dx + dw };
            float[] dyArr = { dy, dy + ct, dy + dh - cb, dy + dh };

            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 3; col++)
                {
                    float qx = dxArr[col];
                    float qy = dyArr[row];
                    float qw = dxArr[col + 1] - dxArr[col];
                    float qh = dyArr[row + 1] - dyArr[row];

                    if (qw <= 0 || qh <= 0) continue;

                    float u0 = sx / tex.Width + su[col] * sw / tex.Width;
                    float v0 = sy / tex.Height + sv[row] * sh / tex.Height;
                    float u1 = sx / tex.Width + su[col + 1] * sw / tex.Width;
                    float v1 = sy / tex.Height + sv[row + 1] * sh / tex.Height;

                    buffer.AddQuad(
                        new Vector2(qx, qy),
                        new Vector2(qw, qh),
                        new Vector2(u0, v0), new Vector2(u1, v1),
                        Color.White, tex);
                }
            }
        }

        private void BuildTiled(Rectangle content, GeometryBuffer buffer)
        {
            var tex = _texture!;
            var src = _sourceRect ?? new Rectangle(0, 0, tex.Width, tex.Height);
            int tileW = src.Width, tileH = src.Height;
            if (tileW <= 0 || tileH <= 0) return;

            float u0 = (float)src.X / tex.Width;
            float v0 = (float)src.Y / tex.Height;
            float u1 = (float)(src.X + src.Width) / tex.Width;
            float v1 = (float)(src.Y + src.Height) / tex.Height;

            for (float y = content.Y; y < content.Y + content.Height; y += tileH)
            {
                for (float x = content.X; x < content.X + content.Width; x += tileW)
                {
                    float w = MathHelper.Min(tileW, content.X + content.Width - x);
                    float h = MathHelper.Min(tileH, content.Y + content.Height - y);
                    if (w <= 0 || h <= 0) continue;

                    float tu1 = u0 + (u1 - u0) * (w / tileW);
                    float tv1 = v0 + (v1 - v0) * (h / tileH);

                    buffer.AddQuad(
                        new Vector2(x, y),
                        new Vector2(w, h),
                        new Vector2(u0, v0), new Vector2(tu1, tv1),
                        Color.White, tex);
                }
            }
        }

        private void BuildFilled(Rectangle content, GeometryBuffer buffer)
        {
            var tex = _texture!;
            var src = _sourceRect ?? new Rectangle(0, 0, tex.Width, tex.Height);
            float fillW = content.Width * _fillAmount;

            if (fillW <= 0) return;

            float u0 = (float)src.X / tex.Width;
            float v0 = (float)src.Y / tex.Height;
            float u1 = u0 + ((float)(src.X + src.Width) / tex.Width - u0) * _fillAmount;
            float v1 = (float)(src.Y + src.Height) / tex.Height;

            buffer.AddQuad(
                new Vector2(content.X, content.Y),
                new Vector2(fillW, content.Height),
                new Vector2(u0, v0), new Vector2(u1, v1),
                Color.White, tex);
        }
    }
}
