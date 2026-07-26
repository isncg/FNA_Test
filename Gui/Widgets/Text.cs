using Microsoft.Xna.Framework;

namespace FNA.Gui
{
    /// <summary>
    /// Text widget that renders SDF font glyphs via the dedicated
    /// <see cref="IGuiRenderer.DrawSdfText"/> path. Does NOT use
    /// <see cref="GeometryBuffer"/> — SDF text goes through a
    /// separate shader (SDFText.feb) from image quads.
    /// </summary>
    public class Text : Graphic
    {
        private SdfFont? _font;
        private string _text = "";
        private float _fontSize = 32f;
        private TextOverflow _overflow = TextOverflow.Overflow;

        public SdfFont? Font
        {
            get => _font;
            set
            {
                if (_font != value)
                {
                    _font = value;
                    SetGeometryDirty();
                    InvalidateMeasure();
                }
            }
        }

        public string TextString
        {
            get => _text;
            set
            {
                if (_text != value)
                {
                    _text = value ?? "";
                    SetGeometryDirty();
                    InvalidateMeasure();
                }
            }
        }

        /// <summary>
        /// Font size in logical pixels. Maps to SDF scale factor.
        /// </summary>
        public float FontSize
        {
            get => _fontSize;
            set
            {
                if (_fontSize != value)
                {
                    _fontSize = value;
                    SetGeometryDirty();
                    InvalidateMeasure();
                }
            }
        }

        public TextOverflow Overflow
        {
            get => _overflow;
            set
            {
                if (_overflow != value)
                {
                    _overflow = value;
                    SetGeometryDirty();
                    InvalidateMeasure();
                }
            }
        }

        // ── Layout ─────────────────────────────────────────────────

        protected override Vector2 OnMeasure(Vector2 available)
        {
            if (_font == null || string.IsNullOrEmpty(_text))
                return Vector2.Zero;

            float scale = _fontSize;
            var measured = _font.MeasureString(_text, scale);

            return measured;
        }

        protected override void OnRebuildGeometry(Rectangle content, GeometryBuffer buffer)
        {
            // Text does NOT use GeometryBuffer — it draws via DrawSdfText.
            // OnRebuildGeometry is still called but is a no-op.
            // Geometry rebuild tracking is handled by SetGeometryDirty.
            IncrementRebuildCount();
        }

        // ── Draw ───────────────────────────────────────────────────

        protected override void OnDraw(IGuiRenderer renderer)
        {
            if (_font == null || string.IsNullOrEmpty(_text))
                return;

            var content = ContentBounds;
            if (content.Width <= 0 || content.Height <= 0)
                return;

            float scaleFactor = _fontSize / _font.FontSize;

            // Compute text position based on alignment
            var measured = _font.MeasureString(_text, _fontSize);
            float x = content.X;
            float y = content.Y;

            // Horizontal alignment
            if (HorizontalAlignment == HorizontalAlignment.Center)
                x += (content.Width - measured.X) / 2;
            else if (HorizontalAlignment == HorizontalAlignment.Right)
                x += content.Width - measured.X;

            // Vertical alignment: position baseline
            // Ascender is the distance from baseline to top of tallest glyph
            float textHeight = measured.Y;
            if (VerticalAlignment == VerticalAlignment.Center)
                y += (content.Height - textHeight) / 2;
            else if (VerticalAlignment == VerticalAlignment.Bottom)
                y += content.Height - textHeight;

            // Position at baseline: y is baseline, ascender goes upward
            y += _font.Ascender * scaleFactor;

            renderer.DrawSdfText(_font, _text, new Vector2(x, y), Color, _fontSize);
        }
    }
}
