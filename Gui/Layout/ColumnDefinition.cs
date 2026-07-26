namespace FNA.Gui
{
    /// <summary>Column definition for <see cref="GridLayout"/>.</summary>
    public class ColumnDefinition
    {
        public GridLength Width { get; set; }

        /// <summary>Minimum width in logical pixels (only effective for Star/Auto).</summary>
        public float MinWidth { get; set; }

        /// <summary>Maximum width in logical pixels (only effective for Star/Auto).</summary>
        public float MaxWidth { get; set; } = float.PositiveInfinity;

        public ColumnDefinition() : this(GridLength.Star(1f)) { }

        public ColumnDefinition(GridLength width)
        {
            Width = width;
        }

        public static implicit operator ColumnDefinition(GridLength width) =>
            new(width);

        public static implicit operator ColumnDefinition(float pixels) =>
            new(GridLength.Fixed(pixels));
    }
}
