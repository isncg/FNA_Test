using System;
using Microsoft.Xna.Framework;

namespace FNA.Gui
{
    /// <summary>
    /// A free-floating, draggable window with an optional title bar and close button.
    /// When <see cref="Modal"/> is true, input events outside the window are intercepted.
    /// Derive from this or use <see cref="Dialog"/> for pre-configured modal behavior.
    /// </summary>
    public class Window : Widget
    {
        private bool _dragging;
        private Vector2 _dragOffset;
        private bool _modal;
        private string _title = "";

        /// <summary>Window position (top-left) in parent coordinates.</summary>
        public float WindowX { get; set; }
        public float WindowY { get; set; }

        /// <summary>Window width and height.</summary>
        public float WindowWidth { get => Width; set => Width = value; }
        public float WindowHeight { get => Height; set => Height = value; }

        /// <summary>Title text displayed in the title bar.</summary>
        public string Title
        {
            get => _title;
            set { _title = value ?? ""; }
        }

        /// <summary>Height of the title bar in pixels.</summary>
        public float TitleBarHeight { get; set; } = 28f;

        /// <summary>
        /// Whether this window is modal. When true, pointer events outside the
        /// window are captured by this window (blocking widgets behind it).
        /// </summary>
        public bool Modal
        {
            get => _modal;
            set => _modal = value;
        }

        /// <summary>Whether the window can be dragged by its title bar.</summary>
        public bool Draggable { get; set; } = true;

        /// <summary>Whether the close button is shown in the title bar.</summary>
        public bool ShowCloseButton { get; set; } = true;

        /// <summary>Background color of the title bar.</summary>
        public Color TitleBarColor { get; set; } = new(60, 60, 90, 255);

        /// <summary>Text color of the title.</summary>
        public Color TitleColor { get; set; } = Color.White;

        /// <summary>Background color of the window content area.</summary>
        public Color WindowBackground { get; set; } = new(45, 45, 63, 255);

        /// <summary>Border color of the window.</summary>
        public Color WindowBorderColor { get; set; } = new(80, 80, 100, 255);

        /// <summary>Fired when the close button is clicked.</summary>
        public event Action<Window>? Closed;

        /// <summary>Fired when the window is closed (alias for <see cref="Closed"/>).</summary>
        public event Action<Window>? CloseClicked
        {
            add => Closed += value;
            remove => Closed -= value;
        }

        public Window()
        {
            WindowX = 100;
            WindowY = 100;
            Width = 400;
            Height = 300;
        }

        /// <summary>Window draws children inside PushClip/PopClip in OnDraw.</summary>
        public override bool HandlesOwnChildDrawing => true;

        // ── Layout ─────────────────────────────────────────────────

        protected override Vector2 OnMeasure(Vector2 available)
        {
            float w = !float.IsNaN(Width) ? Width : 400;
            float h = !float.IsNaN(Height) ? Height : 300;

            // Measure content area for children
            float contentAvailW = w - Padding.Horizontal;
            float contentAvailH = h - TitleBarHeight - Padding.Vertical;
            float maxChildW = 0, maxChildH = 0;

            foreach (var child in Children)
            {
                if (child.Visibility == Visibility.Collapsed)
                    continue;
                child.Measure(new Vector2(contentAvailW, contentAvailH));
                var ds = child.DesiredSize;
                maxChildW = MathF.Max(maxChildW, ds.X);
                maxChildH = MathF.Max(maxChildH, ds.Y);
            }

            return new Vector2(w, h);
        }

        protected override void OnArrange(Rectangle content)
        {
            // Window positions itself at WindowX, WindowY (overrides parent layout)
            // We set Bounds in the Arrange override called by Widget.Arrange.
            // This is handled by the base Widget.Arrange using HorizontalAlignment/VerticalAlignment.
            // For free-floating, we intercept via Arrange.
        }

        /// <summary>Override Arrange to position the window at its WindowX/WindowY.</summary>
        public override void Arrange(Rectangle finalRect)
        {
            // Position the window at its explicit coordinates
            int w = !float.IsNaN(Width) ? (int)Width : 400;
            int h = !float.IsNaN(Height) ? (int)Height : 300;

            var windowRect = new Rectangle((int)WindowX, (int)WindowY, w, h);

            // Use base logic but with our position
            if (Visibility == Visibility.Collapsed)
            {
                // Skip — the real Arrange method handles this via Widget.Arrange
            }

            // Manually set Bounds
            var inner = new Rectangle(
                windowRect.X + (int)Margin.Left,
                windowRect.Y + (int)Margin.Top,
                windowRect.Width - (int)(Margin.Left + Margin.Right),
                windowRect.Height - (int)(Margin.Top + Margin.Bottom));

            int sizeW = HorizontalAlignment == HorizontalAlignment.Stretch && inner.Width > 0
                ? inner.Width : Math.Min((int)DesiredSize.X, inner.Width);
            int sizeH = VerticalAlignment == VerticalAlignment.Stretch && inner.Height > 0
                ? inner.Height : Math.Min((int)DesiredSize.Y, inner.Height);

            if (!float.IsNaN(Width)) sizeW = (int)Width;
            if (!float.IsNaN(Height)) sizeH = (int)Height;

            Bounds = new Rectangle(inner.X, inner.Y, sizeW, sizeH);

            // Arrange content children
            var content = ContentBounds;
            content.Y += (int)TitleBarHeight;
            content.Height -= (int)TitleBarHeight;

            foreach (var child in Children)
            {
                if (child.Visibility == Visibility.Collapsed)
                    continue;
                child.Arrange(content);
            }
        }

        // ── Hit Testing ────────────────────────────────────────────

        public override bool HitTest(Vector2 point)
        {
            if (_modal)
            {
                // Modal windows capture all pointer input
                return true;
            }
            return Bounds.Contains((int)point.X, (int)point.Y);
        }

        public override Widget? HitTestTree(Vector2 point)
        {
            if (_modal && !Bounds.Contains((int)point.X, (int)point.Y))
            {
                // Clicks outside modal window → hit the window itself (blocks input below)
                return this;
            }

            if (!HitTest(point))
                return null;

            // Check children in reverse order (topmost first)
            for (int i = Children.Count - 1; i >= 0; i--)
            {
                var hit = Children[i].HitTestTree(point);
                if (hit != null)
                    return hit;
            }
            return this;
        }

        // ── Input ──────────────────────────────────────────────────

        public override void OnEvent(GuiEvent evt)
        {
            switch (evt.Type)
            {
                case GuiEventType.PointerDown:
                    if (Draggable && IsOnTitleBar(evt.Position))
                    {
                        _dragging = true;
                        _dragOffset = new Vector2(
                            evt.Position.X - WindowX,
                            evt.Position.Y - WindowY);
                        evt.Handled = true;
                    }

                    // Check close button
                    if (ShowCloseButton && IsOnCloseButton(evt.Position))
                    {
                        Close();
                        evt.Handled = true;
                    }
                    break;

                case GuiEventType.Drag:
                    if (_dragging)
                    {
                        WindowX = evt.Position.X - _dragOffset.X;
                        WindowY = evt.Position.Y - _dragOffset.Y;
                        InvalidateArrange();
                        evt.Handled = true;
                    }
                    break;

                case GuiEventType.PointerUp:
                    _dragging = false;
                    break;

                case GuiEventType.Click:
                    if (ShowCloseButton && IsOnCloseButton(evt.Position))
                    {
                        Close();
                        evt.Handled = true;
                    }
                    break;
            }
        }

        private bool IsOnTitleBar(Vector2 point)
        {
            return point.X >= Bounds.X && point.X <= Bounds.Right &&
                   point.Y >= Bounds.Y && point.Y <= Bounds.Y + TitleBarHeight;
        }

        private bool IsOnCloseButton(Vector2 point)
        {
            int btnX = Bounds.Right - 28;
            int btnY = Bounds.Y;
            return point.X >= btnX && point.X <= btnX + 28 &&
                   point.Y >= btnY && point.Y <= btnY + (int)TitleBarHeight;
        }

        // ── Draw ───────────────────────────────────────────────────

        protected override void OnDraw(IGuiRenderer renderer)
        {
            var b = Bounds;

            // Window background
            renderer.DrawRect(b, WindowBackground);

            // Title bar
            var titleRect = new Rectangle(b.X, b.Y, b.Width, (int)TitleBarHeight);
            renderer.DrawRect(titleRect, TitleBarColor);

            // Window border
            var border = WindowBorderColor;
            renderer.DrawRect(new Rectangle(b.X, b.Y, b.Width, 1), border);
            renderer.DrawRect(new Rectangle(b.X, b.Y + b.Height - 1, b.Width, 1), border);
            renderer.DrawRect(new Rectangle(b.X, b.Y, 1, b.Height), border);
            renderer.DrawRect(new Rectangle(b.X + b.Width - 1, b.Y, 1, b.Height), border);

            // Title text (if font available, drawn via SdfTextBatch — placeholder)
            // Actual text drawing requires font; callers can override or add a Text child

            // Close button
            if (ShowCloseButton)
            {
                int btnX = b.Right - 24;
                int btnY = b.Y + 4;
                int btnSize = (int)TitleBarHeight - 8;
                var closeRect = new Rectangle(btnX, btnY, btnSize, btnSize);
                renderer.DrawRect(closeRect, new Color(180, 60, 60, 255));
                // X mark
                renderer.DrawRect(new Rectangle(btnX + 4, btnY + 4, btnSize - 8, 1), Color.White);
                renderer.DrawRect(new Rectangle(btnX + 4, btnY + btnSize - 5, btnSize - 8, 1), Color.White);
            }

            // Clip children to content area
            var content = ContentBounds;
            content.Y += (int)TitleBarHeight;
            content.Height -= (int)TitleBarHeight;
            renderer.PushClip(content);

            foreach (var child in Children)
            {
                if (child.Visibility == Visibility.Collapsed)
                    continue;
                child.Draw(renderer);
            }

            renderer.PopClip();
        }

        // ── Actions ────────────────────────────────────────────────

        /// <summary>Close the window and fire the <see cref="Closed"/> event.</summary>
        public void Close()
        {
            Closed?.Invoke(this);
            // Parent removes this window
            Parent?.RemoveChild(this);
        }
    }

    /// <summary>
    /// A pre-configured modal dialog window.
    /// Modal = true by default, typically smaller than a full window.
    /// </summary>
    public class Dialog : Window
    {
        public Dialog()
        {
            Modal = true;
            WindowWidth = 350;
            WindowHeight = 200;
            WindowX = 225;
            WindowY = 200;
        }
    }
}
