using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace FNA.Gui
{
    /// <summary>
    /// Global theme that provides a color palette and default style sheets
    /// for each widget type. A single Theme instance is typically set on
    /// <see cref="GuiSystem.Theme"/> and shared by all widgets.
    /// </summary>
    public class Theme
    {
        // ── Color Palette ────────────────────────────────────────────

        /// <summary>Named palette colors accessible by key.</summary>
        public Dictionary<string, Color> Palette { get; } = new();

        /// <summary>Get a palette color by key. Returns White if not found.</summary>
        public Color GetColor(string key) =>
            Palette.TryGetValue(key, out var c) ? c : Color.White;

        // Convenience palette accessors
        public Color PrimaryColor => GetColor("primary");
        public Color AccentColor => GetColor("accent");
        public Color SurfaceColor => GetColor("surface");
        public Color TextPrimary => GetColor("textPrimary");
        public Color TextSecondary => GetColor("textSecondary");
        public Color BorderLight => GetColor("borderLight");

        // ── Widget Type Styles ────────────────────────────────────────

        /// <summary>
        /// Default style sheets keyed by widget type name (e.g. "Button", "Panel").
        /// If a widget type has no entry, <see cref="StyleSheet.Default"/> is used.
        /// </summary>
        public Dictionary<string, StyleSheet> Styles { get; } = new();

        /// <summary>Get the default style for a widget type.</summary>
        public StyleSheet GetStyle(string widgetType) =>
            Styles.TryGetValue(widgetType, out var s) ? s : StyleSheet.Default;

        // ── Factory Methods ───────────────────────────────────────────

        /// <summary>Create a dark theme suitable for game UIs.</summary>
        public static Theme CreateDark()
        {
            var theme = new Theme();

            // Palette
            theme.Palette["primary"] = new Color(74, 144, 217, 255);   // #4A90D9
            theme.Palette["accent"] = new Color(230, 126, 34, 255);    // #E67E22
            theme.Palette["success"] = new Color(39, 174, 96, 255);    // #27AE60
            theme.Palette["warning"] = new Color(243, 156, 18, 255);   // #F39C12
            theme.Palette["error"] = new Color(231, 76, 60, 255);      // #E74C3C

            theme.Palette["surface"] = new Color(45, 45, 63, 255);     // #2D2D3F
            theme.Palette["surfaceHigh"] = new Color(60, 60, 80, 255); // #3C3C50
            theme.Palette["bgDark"] = new Color(30, 30, 46, 255);      // #1E1E2E

            theme.Palette["textPrimary"] = new Color(240, 240, 240, 255);
            theme.Palette["textSecondary"] = new Color(160, 160, 176, 255);
            theme.Palette["textDisabled"] = new Color(96, 96, 112, 255);

            theme.Palette["borderLight"] = new Color(80, 80, 100, 255);
            theme.Palette["borderDark"] = new Color(40, 40, 56, 255);

            // Widget styles
            var primary = theme.PrimaryColor;
            var surface = theme.SurfaceColor;
            var surfaceHigh = theme.GetColor("surfaceHigh");

            theme.Styles["Button"] = new StyleSheet
            {
                BackgroundColor = VisualState<Color>.FromBase(primary),
                BorderColor = VisualState<Color>.All(theme.GetColor("borderLight")),
                TextColor = VisualState<Color>.All(theme.TextPrimary),
            };

            theme.Styles["Panel"] = new StyleSheet
            {
                BackgroundColor = VisualState<Color>.All(surface),
                BorderColor = VisualState<Color>.All(theme.GetColor("borderDark")),
            };

            theme.Styles["CheckBox"] = new StyleSheet
            {
                BackgroundColor = new VisualState<Color>
                {
                    Normal = surface,
                    Hover = surfaceHigh,
                    Pressed = primary,
                    Disabled = new Color(60, 60, 60, 255),
                    Focused = surfaceHigh,
                },
                BorderColor = VisualState<Color>.All(Color.Black),
                TextColor = VisualState<Color>.All(Color.White),
            };

            theme.Styles["Slider"] = new StyleSheet
            {
                BackgroundColor = VisualState<Color>.All(Color.DarkGray),
                BorderColor = VisualState<Color>.All(Color.Black),
            };

            theme.Styles["Text"] = new StyleSheet
            {
                TextColor = VisualState<Color>.All(theme.TextPrimary),
            };

            return theme;
        }

        /// <summary>Create a light theme.</summary>
        public static Theme CreateLight()
        {
            var theme = new Theme();

            theme.Palette["primary"] = new Color(66, 133, 244, 255);   // #4285F4
            theme.Palette["accent"] = new Color(255, 152, 0, 255);     // #FF9800
            theme.Palette["success"] = new Color(52, 168, 83, 255);
            theme.Palette["warning"] = new Color(251, 188, 4, 255);
            theme.Palette["error"] = new Color(234, 67, 53, 255);

            theme.Palette["surface"] = new Color(245, 245, 245, 255);
            theme.Palette["surfaceHigh"] = new Color(224, 224, 224, 255);
            theme.Palette["bgDark"] = new Color(255, 255, 255, 255);

            theme.Palette["textPrimary"] = new Color(32, 33, 36, 255);
            theme.Palette["textSecondary"] = new Color(95, 99, 104, 255);
            theme.Palette["textDisabled"] = new Color(154, 160, 166, 255);

            theme.Palette["borderLight"] = new Color(218, 220, 224, 255);
            theme.Palette["borderDark"] = new Color(128, 134, 139, 255);

            var primary = theme.PrimaryColor;
            theme.Styles["Button"] = StyleSheet.Interactive(primary);
            theme.Styles["Panel"] = StyleSheet.Solid(theme.SurfaceColor);

            return theme;
        }

        /// <summary>The default theme used when no theme is set on GuiSystem.</summary>
        public static Theme Default { get; } = CreateDark();
    }
}
