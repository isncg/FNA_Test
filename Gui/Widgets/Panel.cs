using Microsoft.Xna.Framework;

namespace FNA.Gui
{
    /// <summary>
    /// A simple container widget — the building block for all GUI layouts.
    /// Can have an optional background color or 9-slice skin.
    /// </summary>
    public class Panel : Widget
    {
        private Color? _backgroundColor;
        private NineSlice? _backgroundSkin;

        public Color? BackgroundColor
        {
            get => _backgroundColor;
            set { _backgroundColor = value; }
        }

        public NineSlice? BackgroundSkin
        {
            get => _backgroundSkin;
            set { _backgroundSkin = value; }
        }

        /// <summary>
        /// Optional border color. When set, a 1-pixel outline is drawn
        /// around the panel bounds for visual debugging.
        /// </summary>
        public Color? BorderColor { get; set; }

        /// <summary>
        /// Whether this panel clips its children to its content bounds.
        /// </summary>
        public bool ClipChildren { get; set; }

        /// <summary>
        /// Panel handles its own child drawing only when ClipChildren is true
        /// (children are drawn inside PushClip/PopClip in OnDraw).
        /// </summary>
        public override bool HandlesOwnChildDrawing => ClipChildren;

        public Panel()
        {
            ClipChildren = false;
        }

        protected override Vector2 OnMeasure(Vector2 available)
        {
            // Panel with no children sizes to zero (or explicit Width/Height)
            if (Children.Count == 0)
                return Vector2.Zero;

            float maxW = 0, maxH = 0;
            foreach (var child in Children)
            {
                if (child.Visibility == Visibility.Collapsed)
                    continue;
                child.Measure(available);
                var ds = child.DesiredSize;
                maxW = MathHelper.Max(maxW, ds.X);
                maxH = MathHelper.Max(maxH, ds.Y);
            }
            return new Vector2(maxW, maxH);
        }

        protected override void OnArrange(Rectangle content)
        {
            foreach (var child in Children)
            {
                if (child.Visibility == Visibility.Collapsed)
                    continue;

                // By default, give each child the full content area
                // (containers like StackLayout override this)
                child.Arrange(content);
            }
        }

        protected override void OnDraw(IGuiRenderer renderer)
        {
            // Draw background
            if (_backgroundColor.HasValue && _backgroundSkin == null)
            {
                renderer.DrawRect(Bounds, ResolveBackground(_backgroundColor.Value));
            }
            else if (_backgroundColor == null && _backgroundSkin == null)
            {
                // Check if style/theme provides a background
                var styled = Style?.BackgroundColor;
                var themeStyle = Theme?.GetStyle(GetType().Name);
                if (styled != null || themeStyle?.BackgroundColor != null)
                {
                    renderer.DrawRect(Bounds, ResolveBackground(Color.Transparent));
                }
            }
            else if (_backgroundSkin != null)
            {
                renderer.DrawNineSlice(_backgroundSkin, Bounds, Color.White);
            }

            // Draw border (1px outline, 4 lines)
            if (BorderColor.HasValue)
            {
                var c = ResolveBorder(BorderColor.Value);
                var b = Bounds;
                // Top
                renderer.DrawRect(new Rectangle(b.X, b.Y, b.Width, 1), c);
                // Bottom
                renderer.DrawRect(new Rectangle(b.X, b.Y + b.Height - 1, b.Width, 1), c);
                // Left
                renderer.DrawRect(new Rectangle(b.X, b.Y, 1, b.Height), c);
                // Right
                renderer.DrawRect(new Rectangle(b.X + b.Width - 1, b.Y, 1, b.Height), c);
            }

            // Draw children
            if (ClipChildren)
            {
                renderer.PushClip(ContentBounds);
                foreach (var child in Children)
                    child.Draw(renderer);
                renderer.PopClip();
            }
            else
            {
                foreach (var child in Children)
                    child.Draw(renderer);
            }
        }
    }
}
