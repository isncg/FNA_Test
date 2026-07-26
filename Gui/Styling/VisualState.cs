using System;
using Microsoft.Xna.Framework;

namespace FNA.Gui
{
    /// <summary>
    /// A visual property whose value varies by widget state.
    /// Each state (Normal/Hover/Pressed/Disabled/Focused) can have its own value.
    /// </summary>
    public class VisualState<T> where T : struct
    {
        /// <summary>Value shown when State == Normal.</summary>
        public T Normal { get; set; }

        /// <summary>Value shown when State == Hover.</summary>
        public T Hover { get; set; }

        /// <summary>Value shown when State == Pressed.</summary>
        public T Pressed { get; set; }

        /// <summary>Value shown when State == Disabled.</summary>
        public T Disabled { get; set; }

        /// <summary>Value shown when State == Focused.</summary>
        public T Focused { get; set; }

        /// <summary>Resolve the value for a given widget state.</summary>
        public T GetValue(WidgetState state) => state switch
        {
            WidgetState.Hover => Hover,
            WidgetState.Pressed => Pressed,
            WidgetState.Disabled => Disabled,
            WidgetState.Focused => Focused,
            _ => Normal,
        };

        /// <summary>Create a VisualState where all states share the same value.</summary>
        public static VisualState<T> All(T value) => new()
        {
            Normal = value,
            Hover = value,
            Pressed = value,
            Disabled = value,
            Focused = value,
        };

        /// <summary>
        /// Create with Normal as the base, Hover a brighter variant,
        /// Pressed a darker variant, and Disabled a dimmed variant.
        /// Applies a scale factor to each RGB channel.
        /// </summary>
        public static VisualState<Color> FromBase(Color normal, float hoverScale = 1.15f,
            float pressedScale = 0.85f, float disabledScale = 0.5f)
        {
            return new VisualState<Color>
            {
                Normal = normal,
                Hover = ScaleColor(normal, hoverScale),
                Pressed = ScaleColor(normal, pressedScale),
                Disabled = ScaleColor(normal, disabledScale),
                Focused = normal, // focused keeps normal bg
            };
        }

        private static Color ScaleColor(Color c, float factor)
        {
            return new Color(
                (int)Math.Clamp(c.R * factor, 0, 255),
                (int)Math.Clamp(c.G * factor, 0, 255),
                (int)Math.Clamp(c.B * factor, 0, 255),
                c.A);
        }
    }
}
