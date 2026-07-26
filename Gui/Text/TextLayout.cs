using Microsoft.Xna.Framework;

namespace FNA.Gui
{
    /// <summary>
    /// How text overflow is handled when content bounds are smaller than the text.
    /// </summary>
    public enum TextOverflow
    {
        /// <summary>Text renders beyond bounds (clipped by parent clip if any).</summary>
        Overflow,
        /// <summary>Text is cut at the content boundary.</summary>
        Truncate,
        /// <summary>Last visible line ends with an ellipsis character.</summary>
        Ellipsis,
    }

    /// <summary>
    /// Rich text segment with a color override.
    /// </summary>
    public struct TextSegment
    {
        public string Text;
        public Color? Color;

        public TextSegment(string text, Color? color = null)
        {
            Text = text;
            Color = color;
        }
    }

    /// <summary>
    /// Computed text layout ready for rendering.
    /// Tracks line breaks, glyph positions, and per-segment colors.
    /// </summary>
    public class TextLayout
    {
        /// <summary>The font used for this layout.</summary>
        public IFontProvider Font { get; }

        /// <summary>The plain text string after resolving rich-text markup.</summary>
        public string PlainText { get; }

        /// <summary>Display scale factor.</summary>
        public float Scale { get; }

        /// <summary>Number of lines after word-wrap.</summary>
        public int LineCount { get; private set; }

        /// <summary>Bounding size of the laid-out text.</summary>
        public Vector2 Size { get; private set; }

        /// <summary>Overflow handling mode.</summary>
        public TextOverflow Overflow { get; set; } = TextOverflow.Overflow;

        public TextLayout(IFontProvider font, string text, float scale = 1.0f)
        {
            Font = font;
            PlainText = text;
            Scale = scale;
            LineCount = 1;

            // Simple initial measurement — word-wrap and rich text are Phase 1 proper
            var measured = font.MeasureString(text, scale);
            Size = measured;

            // Count lines
            foreach (char c in text)
                if (c == '\n') LineCount++;
        }

        /// <summary>
        /// Update the layout with a constrained width for word-wrap.
        /// Returns the number of lines after wrapping.
        /// </summary>
        public void Update(float maxWidth)
        {
            // TODO (Phase 1): full word-wrap, ellipsis, and rich-text parsing
            var measured = Font.MeasureString(PlainText, Scale);
            LineCount = 1;
            foreach (char c in PlainText)
                if (c == '\n') LineCount++;
            Size = new Vector2(
                System.Math.Min(measured.X, maxWidth > 0 ? maxWidth : measured.X),
                measured.Y);
        }
    }
}
