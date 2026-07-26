namespace FNA.Gui
{
    /// <summary>Row definition for <see cref="GridLayout"/>.</summary>
    public class RowDefinition
    {
        public GridLength Height { get; set; }

        /// <summary>Minimum height in logical pixels (only effective for Star/Auto).</summary>
        public float MinHeight { get; set; }

        /// <summary>Maximum height in logical pixels (only effective for Star/Auto).</summary>
        public float MaxHeight { get; set; } = float.PositiveInfinity;

        public RowDefinition() : this(GridLength.Star(1f)) { }

        public RowDefinition(GridLength height)
        {
            Height = height;
        }

        public static implicit operator RowDefinition(GridLength height) =>
            new(height);

        public static implicit operator RowDefinition(float pixels) =>
            new(GridLength.Fixed(pixels));
    }
}
