using System;
using Microsoft.Xna.Framework;

namespace FNA.Gui
{
    /// <summary>Stack orientation.</summary>
    public enum Orientation
    {
        Horizontal,
        Vertical,
    }

    /// <summary>
    /// Arranges children sequentially along a single axis.
    /// Main axis is content-driven (children get +∞ available);
    /// cross axis is passed through (children can Stretch to fill).
    /// </summary>
    public class StackLayout : Widget
    {
        public Orientation Orientation { get; set; } = Orientation.Vertical;

        /// <summary>Gap between adjacent children on the main axis, in logical pixels.</summary>
        public float Spacing { get; set; }

        protected override Vector2 OnMeasure(Vector2 available)
        {
            int visibleCount = 0;
            float totalMain = 0;
            float maxCross = 0;

            foreach (var child in Children)
            {
                if (child.Visibility == Visibility.Collapsed)
                    continue;
                visibleCount++;

                Vector2 childAvailable = Orientation switch
                {
                    Orientation.Horizontal => new Vector2(
                        float.PositiveInfinity, available.Y),
                    Orientation.Vertical => new Vector2(
                        available.X, float.PositiveInfinity),
                    _ => available,
                };

                child.Measure(childAvailable);
                var ds = child.DesiredSize;

                if (Orientation == Orientation.Horizontal)
                {
                    totalMain += ds.X;
                    maxCross = MathF.Max(maxCross, ds.Y);
                }
                else
                {
                    totalMain += ds.Y;
                    maxCross = MathF.Max(maxCross, ds.X);
                }
            }

            if (visibleCount > 1)
                totalMain += Spacing * (visibleCount - 1);

            return Orientation == Orientation.Horizontal
                ? new Vector2(totalMain, maxCross)
                : new Vector2(maxCross, totalMain);
        }

        protected override void OnArrange(Rectangle content)
        {
            float offset = Orientation == Orientation.Horizontal
                ? content.X : content.Y;

            foreach (var child in Children)
            {
                if (child.Visibility == Visibility.Collapsed)
                    continue;

                var ds = child.DesiredSize;

                Rectangle childRect = Orientation switch
                {
                    Orientation.Horizontal => new Rectangle(
                        (int)offset, content.Y,
                        (int)ds.X, content.Height),

                    Orientation.Vertical => new Rectangle(
                        content.X, (int)offset,
                        content.Width, (int)ds.Y),

                    _ => content,
                };

                child.Arrange(childRect);

                if (Orientation == Orientation.Horizontal)
                    offset += ds.X + Spacing;
                else
                    offset += ds.Y + Spacing;
            }
        }
    }
}
