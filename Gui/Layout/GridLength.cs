using System;

namespace FNA.Gui
{
    /// <summary>Grid track sizing mode.</summary>
    public enum GridUnitType
    {
        /// <summary>Fixed pixel size.</summary>
        Fixed,
        /// <summary>Size to fit content.</summary>
        Auto,
        /// <summary>Proportional distribution of remaining space.</summary>
        Star,
    }

    /// <summary>
    /// Represents the length of a grid row or column — Fixed (pixels),
    /// Auto (content-sized), or Star (proportional).
    /// </summary>
    public struct GridLength : IEquatable<GridLength>
    {
        public GridUnitType Type { get; }
        public float Value { get; }

        public GridLength(float value, GridUnitType type)
        {
            Type = type;
            Value = type == GridUnitType.Auto ? 0 : value;
        }

        public bool IsFixed => Type == GridUnitType.Fixed;
        public bool IsAuto => Type == GridUnitType.Auto;
        public bool IsStar => Type == GridUnitType.Star;

        public static GridLength Fixed(float pixels) =>
            new(pixels, GridUnitType.Fixed);

        public static GridLength Auto =>
            new(0, GridUnitType.Auto);

        public static GridLength Star(float weight = 1f) =>
            new(weight, GridUnitType.Star);

        // Implicit conversions for convenience
        public static implicit operator GridLength(float pixels) =>
            Fixed(pixels);

        public override bool Equals(object? obj) =>
            obj is GridLength gl && Equals(gl);

        public bool Equals(GridLength other) =>
            Type == other.Type && Value == other.Value;

        public override int GetHashCode() =>
            HashCode.Combine(Type, Value);

        public static bool operator ==(GridLength a, GridLength b) =>
            a.Equals(b);

        public static bool operator !=(GridLength a, GridLength b) =>
            !a.Equals(b);

        public override string ToString() => Type switch
        {
            GridUnitType.Fixed => $"{Value}px",
            GridUnitType.Auto => "Auto",
            GridUnitType.Star => $"*{Value}",
            _ => "?",
        };
    }
}
