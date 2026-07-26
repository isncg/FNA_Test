using System;

namespace FNA.Gui
{
    /// <summary>
    /// Left, top, right, bottom margin or padding values in logical pixels.
    /// </summary>
    public struct Thickness : IEquatable<Thickness>
    {
        public float Left;
        public float Top;
        public float Right;
        public float Bottom;

        public Thickness(float uniform) : this(uniform, uniform, uniform, uniform) { }

        public Thickness(float left, float top, float right, float bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public float Horizontal => Left + Right;
        public float Vertical => Top + Bottom;

        public static readonly Thickness Zero = default;

        public override bool Equals(object? obj) => obj is Thickness t && Equals(t);
        public bool Equals(Thickness other) =>
            Left == other.Left && Top == other.Top &&
            Right == other.Right && Bottom == other.Bottom;
        public override int GetHashCode() =>
            HashCode.Combine(Left, Top, Right, Bottom);

        public static bool operator ==(Thickness a, Thickness b) => a.Equals(b);
        public static bool operator !=(Thickness a, Thickness b) => !a.Equals(b);

        public override string ToString() =>
            $"(L:{Left}, T:{Top}, R:{Right}, B:{Bottom})";
    }
}
