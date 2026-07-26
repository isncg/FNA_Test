using System;
using Microsoft.Xna.Framework;

namespace FNA.Gui
{
    /// <summary>Dock position within a <see cref="DockLayout"/>.</summary>
    public enum Dock
    {
        Left,
        Top,
        Right,
        Bottom,
    }

    /// <summary>
    /// Arranges children by docking them to edges in declaration order.
    /// First child is docked first, claiming the specified edge.
    /// If <see cref="LastChildFill"/> is true (default), the last child
    /// fills the remaining space.
    /// </summary>
    public class DockLayout : Widget
    {
        /// <summary>
        /// If true, the last child fills all remaining space instead of
        /// being docked to its declared edge.
        /// </summary>
        public bool LastChildFill { get; set; } = true;

        // ── Attached Property ────────────────────────────────────────

        private const string PropDock = "DockLayout.Dock";

        public static Dock GetDock(Widget w) =>
            w.GetAttached<Dock>(PropDock);

        public static void SetDock(Widget w, Dock value) =>
            w.SetAttached(PropDock, value);

        // ── Measure ──────────────────────────────────────────────────

        protected override Vector2 OnMeasure(Vector2 available)
        {
            float remainingW = available.X;
            float remainingH = available.Y;
            float totalW = 0, totalH = 0;

            int count = 0;
            foreach (var child in Children)
            {
                if (child.Visibility == Visibility.Collapsed) continue;
                count++;
            }

            int index = 0;
            foreach (var child in Children)
            {
                if (child.Visibility == Visibility.Collapsed) continue;
                index++;

                bool isLast = (index == count);
                bool fill = isLast && LastChildFill;

                if (fill)
                {
                    child.Measure(new Vector2(remainingW, remainingH));
                }
                else
                {
                    child.Measure(new Vector2(remainingW, remainingH));
                }

                var ds = child.DesiredSize;
                var dock = GetDock(child);

                switch (dock)
                {
                    case Dock.Left:
                        totalW += ds.X;
                        totalH = MathF.Max(totalH, ds.Y);
                        remainingW = MathF.Max(0, remainingW - ds.X);
                        break;
                    case Dock.Right:
                        totalW += ds.X;
                        totalH = MathF.Max(totalH, ds.Y);
                        remainingW = MathF.Max(0, remainingW - ds.X);
                        break;
                    case Dock.Top:
                        totalW = MathF.Max(totalW, ds.X);
                        totalH += ds.Y;
                        remainingH = MathF.Max(0, remainingH - ds.Y);
                        break;
                    case Dock.Bottom:
                        totalW = MathF.Max(totalW, ds.X);
                        totalH += ds.Y;
                        remainingH = MathF.Max(0, remainingH - ds.Y);
                        break;
                }
            }

            return new Vector2(totalW, totalH);
        }

        // ── Arrange ───────────────────────────────────────────────────

        protected override void OnArrange(Rectangle content)
        {
            var remaining = content;

            int count = 0;
            foreach (var child in Children)
            {
                if (child.Visibility == Visibility.Collapsed) continue;
                count++;
            }

            int index = 0;
            foreach (var child in Children)
            {
                if (child.Visibility == Visibility.Collapsed) continue;
                index++;

                bool isLast = (index == count);
                bool fill = isLast && LastChildFill;

                if (fill)
                {
                    child.Arrange(remaining);
                    return;
                }

                var ds = child.DesiredSize;
                var dock = GetDock(child);

                Rectangle childRect;
                switch (dock)
                {
                    case Dock.Left:
                    {
                        int w = (int)ds.X;
                        childRect = new Rectangle(remaining.X, remaining.Y,
                            w, remaining.Height);
                        remaining = new Rectangle(remaining.X + w, remaining.Y,
                            Math.Max(0, remaining.Width - w), remaining.Height);
                        break;
                    }
                    case Dock.Right:
                    {
                        int w = (int)ds.X;
                        childRect = new Rectangle(
                            remaining.X + remaining.Width - w, remaining.Y,
                            w, remaining.Height);
                        remaining = new Rectangle(remaining.X, remaining.Y,
                            Math.Max(0, remaining.Width - w), remaining.Height);
                        break;
                    }
                    case Dock.Top:
                    {
                        int h = (int)ds.Y;
                        childRect = new Rectangle(remaining.X, remaining.Y,
                            remaining.Width, h);
                        remaining = new Rectangle(remaining.X, remaining.Y + h,
                            remaining.Width, Math.Max(0, remaining.Height - h));
                        break;
                    }
                    case Dock.Bottom:
                    {
                        int h = (int)ds.Y;
                        childRect = new Rectangle(
                            remaining.X, remaining.Y + remaining.Height - h,
                            remaining.Width, h);
                        remaining = new Rectangle(remaining.X, remaining.Y,
                            remaining.Width, Math.Max(0, remaining.Height - h));
                        break;
                    }
                    default:
                        childRect = remaining;
                        break;
                }

                child.Arrange(childRect);
            }
        }
    }
}
