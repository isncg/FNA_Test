using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace FNA.Gui.Serialization
{
    /// <summary>
    /// Registry of type converters for XAML-lite attribute parsing.
    /// Converts string attribute values to CLR types without reflection.
    /// </summary>
    public static class TypeConverterRegistry
    {
        private static readonly Dictionary<Type, Func<string, object?>> _converters = new();

        static TypeConverterRegistry()
        {
            // Primitives
            Register(typeof(float), s => float.Parse(s));
            Register(typeof(int), s => int.Parse(s));
            Register(typeof(double), s => double.Parse(s));
            Register(typeof(bool), s => bool.Parse(s));
            Register(typeof(string), s => s);

            // FNA/XNA types
            Register(typeof(Color), s => ParseColor(s));
            Register(typeof(Vector2), s => ParseVector2(s));
            Register(typeof(Thickness), s => ParseThickness(s));

            // GUI enums
            Register(typeof(Orientation), s => Enum.Parse<Orientation>(s));
            Register(typeof(Visibility), s => Enum.Parse<Visibility>(s));
            Register(typeof(HorizontalAlignment), s => Enum.Parse<HorizontalAlignment>(s));
            Register(typeof(VerticalAlignment), s => Enum.Parse<VerticalAlignment>(s));
            Register(typeof(Dock), s => Enum.Parse<Dock>(s));
            Register(typeof(ImageType), s => Enum.Parse<ImageType>(s));
            Register(typeof(EasingType), s => Enum.Parse<EasingType>(s));
        }

        /// <summary>Register a converter for a given target type.</summary>
        public static void Register(Type type, Func<string, object?> converter)
        {
            _converters[type] = converter;
        }

        /// <summary>Convert a string value to the target type.</summary>
        public static object? Convert(Type targetType, string value)
        {
            if (_converters.TryGetValue(targetType, out var converter))
                return converter(value);

            // Fallback: try Enum.Parse for unknown enums
            if (targetType.IsEnum)
                return Enum.Parse(targetType, value);

            throw new InvalidOperationException(
                $"No type converter registered for {targetType.Name}. Value: '{value}'");
        }

        /// <summary>Convert and cast to T.</summary>
        public static T Convert<T>(string value)
        {
            var result = Convert(typeof(T), value);
            if (result == null)
                throw new InvalidOperationException(
                    $"Failed to convert '{value}' to {typeof(T).Name}: got null");
            return (T)result;
        }

        // ── Built-in converters ─────────────────────────────────────

        private static Color ParseColor(string s)
        {
            s = s.Trim();

            // Named colors (subset)
            if (TryParseNamedColor(s, out var named))
                return named;

            // Hex: #RRGGBB or #RRGGBBAA
            if (s.StartsWith("#"))
            {
                s = s.TrimStart('#');
                uint val = uint.Parse(s, System.Globalization.NumberStyles.HexNumber);
                if (s.Length == 6)
                    return new Color(
                        (byte)((val >> 16) & 0xFF),
                        (byte)((val >> 8) & 0xFF),
                        (byte)(val & 0xFF),
                        255);
                if (s.Length == 8)
                    return new Color(
                        (byte)((val >> 24) & 0xFF),
                        (byte)((val >> 16) & 0xFF),
                        (byte)((val >> 8) & 0xFF),
                        (byte)(val & 0xFF));
            }

            // R,G,B or R,G,B,A
            var parts = s.Split(',');
            if (parts.Length == 3)
                return new Color(
                    byte.Parse(parts[0].Trim()),
                    byte.Parse(parts[1].Trim()),
                    byte.Parse(parts[2].Trim()),
                    255);
            if (parts.Length == 4)
                return new Color(
                    byte.Parse(parts[0].Trim()),
                    byte.Parse(parts[1].Trim()),
                    byte.Parse(parts[2].Trim()),
                    byte.Parse(parts[3].Trim()));

            throw new FormatException($"Cannot parse Color from '{s}'");
        }

        private static bool TryParseNamedColor(string s, out Color c)
        {
            c = s.ToLowerInvariant() switch
            {
                "red" => Color.Red,
                "green" => Color.Green,
                "blue" => Color.Blue,
                "white" => Color.White,
                "black" => Color.Black,
                "transparent" => Color.Transparent,
                "gray" => Color.Gray,
                "darkgray" => Color.DarkGray,
                "lightgray" => Color.LightGray,
                "yellow" => Color.Yellow,
                "orange" => Color.Orange,
                "purple" => Color.Purple,
                "cyan" => Color.Cyan,
                "magenta" => Color.Magenta,
                "lime" => Color.Lime,
                "cornflowerblue" => Color.CornflowerBlue,
                "darkblue" => new Color(0, 0, 139, 255),
                "darkgreen" => new Color(0, 100, 0, 255),
                _ => default,
            };
            if (c == default && s != "transparent" && s != "black")
                return false;
            return true;
        }

        private static Vector2 ParseVector2(string s)
        {
            var parts = s.Split(',');
            if (parts.Length == 2)
                return new Vector2(
                    float.Parse(parts[0].Trim()),
                    float.Parse(parts[1].Trim()));
            throw new FormatException($"Cannot parse Vector2 from '{s}'");
        }

        private static Thickness ParseThickness(string s)
        {
            var parts = s.Split(',');
            if (parts.Length == 1)
            {
                float v = float.Parse(parts[0].Trim());
                return new Thickness(v);
            }
            if (parts.Length == 4)
            {
                return new Thickness(
                    float.Parse(parts[0].Trim()),
                    float.Parse(parts[1].Trim()),
                    float.Parse(parts[2].Trim()),
                    float.Parse(parts[3].Trim()));
            }
            throw new FormatException(
                $"Cannot parse Thickness from '{s}'. Expected single value or L,T,R,B");
        }
    }
}
