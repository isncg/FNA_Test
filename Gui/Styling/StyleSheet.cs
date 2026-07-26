using Microsoft.Xna.Framework;

namespace FNA.Gui
{
    /// <summary>
    /// Defines the visual appearance of a widget across its five states.
    /// Properties are nullable — when null, the widget falls back to its
    /// own hardcoded defaults or the theme default.
    /// </summary>
    public class StyleSheet
    {
        /// <summary>Background fill color per state.</summary>
        public VisualState<Color>? BackgroundColor { get; set; }

        /// <summary>Border/outline color per state.</summary>
        public VisualState<Color>? BorderColor { get; set; }

        /// <summary>Text/foreground color per state.</summary>
        public VisualState<Color>? TextColor { get; set; }

        /// <summary>
        /// Resolve a background color for the given state.
        /// Returns <paramref name="fallback"/> if BackgroundColor is not set.
        /// </summary>
        public Color ResolveBackground(WidgetState state, Color fallback) =>
            BackgroundColor?.GetValue(state) ?? fallback;

        /// <summary>
        /// Resolve a border color for the given state.
        /// Returns <paramref name="fallback"/> if BorderColor is not set.
        /// </summary>
        public Color ResolveBorder(WidgetState state, Color fallback) =>
            BorderColor?.GetValue(state) ?? fallback;

        /// <summary>
        /// Resolve a text/foreground color for the given state.
        /// Returns <paramref name="fallback"/> if TextColor is not set.
        /// </summary>
        public Color ResolveText(WidgetState state, Color fallback) =>
            TextColor?.GetValue(state) ?? fallback;

        /// <summary>
        /// Create a simple StyleSheet with a single background color for all states.
        /// </summary>
        public static StyleSheet Solid(Color bg) => new()
        {
            BackgroundColor = VisualState<Color>.All(bg),
        };

        /// <summary>
        /// Create a button-like StyleSheet with hover/pressed variants.
        /// </summary>
        public static StyleSheet Interactive(Color normal) => new()
        {
            BackgroundColor = VisualState<Color>.FromBase(normal),
            BorderColor = VisualState<Color>.All(Color.Black),
            TextColor = VisualState<Color>.All(Color.White),
        };

        /// <summary>A minimal default style (no overrides — all fallback).</summary>
        public static StyleSheet Default { get; } = new();
    }
}
