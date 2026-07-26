using Microsoft.Xna.Framework;

namespace FNA.Gui
{
    /// <summary>
    /// Abstraction for font rendering backends.
    /// Default implementation: <see cref="SdfFont"/> (SDF distance-field rendering).
    /// </summary>
    public interface IFontProvider
    {
        /// <summary>Measure the pixel dimensions of a text string at the given scale.</summary>
        Vector2 MeasureString(string text, float scale);

        /// <summary>Line height in pixels at scale=1.0.</summary>
        float LineHeight { get; }

        /// <summary>Ascender (distance above baseline) in pixels at scale=1.0.</summary>
        float Ascender { get; }

        /// <summary>Descender (distance below baseline) in pixels at scale=1.0.</summary>
        float Descender { get; }
    }
}
