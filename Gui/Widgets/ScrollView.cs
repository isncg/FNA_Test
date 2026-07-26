using System;
using Microsoft.Xna.Framework;

namespace FNA.Gui
{
    /// <summary>
    /// A scrollable container that clips its content and provides
    /// scroll offset control via mouse wheel, drag, or programmatic access.
    /// </summary>
    public class ScrollView : Widget
    {
        private Vector2 _scrollOffset;
        private Vector2 _lastDragPosition;
        private float _scrollBarWidth = 8f;
        private bool _showVerticalBar = true;
        private bool _showHorizontalBar = true;

        /// <summary>Current scroll offset (positive = content scrolled down/right).</summary>
        public Vector2 ScrollOffset
        {
            get => _scrollOffset;
            set
            {
                _scrollOffset = ClampOffset(value);
                InvalidateArrange();
            }
        }

        /// <summary>Total scrollable content size (set during Measure).</summary>
        public Vector2 ContentSize { get; private set; }

        /// <summary>Visible viewport size (ContentBounds).</summary>
        public Vector2 ViewportSize => new(ContentBounds.Width, ContentBounds.Height);

        /// <summary>Width of scroll bars in pixels.</summary>
        public float ScrollBarWidth
        {
            get => _scrollBarWidth;
            set { _scrollBarWidth = value; InvalidateMeasure(); }
        }

        /// <summary>Whether to show the vertical scroll bar.</summary>
        public bool ShowVerticalBar
        {
            get => _showVerticalBar;
            set { _showVerticalBar = value; InvalidateMeasure(); }
        }

        /// <summary>Whether to show the horizontal scroll bar.</summary>
        public bool ShowHorizontalBar
        {
            get => _showHorizontalBar;
            set { _showHorizontalBar = value; InvalidateMeasure(); }
        }

        /// <summary>How many pixels to scroll per wheel tick.</summary>
        public float WheelScrollAmount { get; set; } = 40f;

        /// <summary>Smooth scroll duration (0 = instant). Phase 5 tween integration.</summary>
        public float SmoothScrollDuration { get; set; } = 0f;

        /// <summary>Color of the scroll bar track.</summary>
        public Color ScrollTrackColor { get; set; } = new(40, 40, 50, 255);

        /// <summary>Color of the scroll bar thumb.</summary>
        public Color ScrollThumbColor { get; set; } = new(100, 100, 120, 255);

        public ScrollView()
        {
            IsFocusable = true; // to receive scroll events
        }

        /// <summary>ScrollView draws children inside PushClip/PopClip in OnDraw.</summary>
        public override bool HandlesOwnChildDrawing => true;

        // ── Measure / Arrange ──────────────────────────────────────

        protected override Vector2 OnMeasure(Vector2 available)
        {
            float maxW = 0, maxH = 0;

            foreach (var child in Children)
            {
                if (child.Visibility == Visibility.Collapsed)
                    continue;

                // Content gets infinite space on scroll axes
                float childAvailW = float.PositiveInfinity;
                float childAvailH = float.PositiveInfinity;

                child.Measure(new Vector2(childAvailW, childAvailH));
                var ds = child.DesiredSize;
                maxW = MathF.Max(maxW, ds.X);
                maxH = MathF.Max(maxH, ds.Y);
            }

            ContentSize = new Vector2(maxW, maxH);

            // Viewport adds scrollbar space if needed
            float viewW = maxW;
            float viewH = maxH;
            if (_showVerticalBar) viewW += _scrollBarWidth;
            if (_showHorizontalBar) viewH += _scrollBarWidth;

            return new Vector2(viewW, viewH);
        }

        protected override void OnArrange(Rectangle content)
        {
            // Clamp offset after viewport size is known
            _scrollOffset = ClampOffset(_scrollOffset);

            var scrolled = new Rectangle(
                content.X - (int)_scrollOffset.X,
                content.Y - (int)_scrollOffset.Y,
                Math.Max((int)ContentSize.X, content.Width),
                Math.Max((int)ContentSize.Y, content.Height));

            foreach (var child in Children)
            {
                if (child.Visibility == Visibility.Collapsed)
                    continue;
                child.Arrange(scrolled);
            }
        }

        // ── Input ──────────────────────────────────────────────────

        public override void OnEvent(GuiEvent evt)
        {
            switch (evt.Type)
            {
                case GuiEventType.PointerDown:
                    _lastDragPosition = evt.Position;
                    break;

                case GuiEventType.Drag:
                {
                    // Natural drag-to-scroll: content follows pointer movement
                    Vector2 dragDelta = _lastDragPosition - evt.Position;
                    _scrollOffset = ClampOffset(_scrollOffset + dragDelta);
                    _lastDragPosition = evt.Position;
                    InvalidateArrange();
                    evt.Handled = true;
                    break;
                }

                case GuiEventType.Scroll:
                    // Vertical scroll by default, horizontal with Shift
                    float delta = evt.ScrollDelta * WheelScrollAmount;
                    _scrollOffset.Y -= delta;
                    _scrollOffset = ClampOffset(_scrollOffset);
                    InvalidateArrange();
                    evt.Handled = true;
                    break;

                case GuiEventType.KeyDown:
                    float keyStep = WheelScrollAmount;
                    if (evt.Key == Microsoft.Xna.Framework.Input.Keys.Down)
                    {
                        _scrollOffset.Y += keyStep;
                        _scrollOffset = ClampOffset(_scrollOffset);
                        InvalidateArrange();
                        evt.Handled = true;
                    }
                    else if (evt.Key == Microsoft.Xna.Framework.Input.Keys.Up)
                    {
                        _scrollOffset.Y -= keyStep;
                        _scrollOffset = ClampOffset(_scrollOffset);
                        InvalidateArrange();
                        evt.Handled = true;
                    }
                    else if (evt.Key == Microsoft.Xna.Framework.Input.Keys.Right)
                    {
                        _scrollOffset.X += keyStep;
                        _scrollOffset = ClampOffset(_scrollOffset);
                        InvalidateArrange();
                        evt.Handled = true;
                    }
                    else if (evt.Key == Microsoft.Xna.Framework.Input.Keys.Left)
                    {
                        _scrollOffset.X -= keyStep;
                        _scrollOffset = ClampOffset(_scrollOffset);
                        InvalidateArrange();
                        evt.Handled = true;
                    }
                    break;
            }
        }

        // ── Draw ───────────────────────────────────────────────────

        protected override void OnDraw(IGuiRenderer renderer)
        {
            var cb = ContentBounds;

            // Clip to content bounds
            renderer.PushClip(cb);

            // Draw children
            foreach (var child in Children)
            {
                if (child.Visibility == Visibility.Collapsed)
                    continue;
                child.Draw(renderer);
            }

            renderer.PopClip();

            // Draw scroll bars outside clip (on top of content edge)
            DrawScrollBars(renderer, cb);
        }

        private void DrawScrollBars(IGuiRenderer renderer, Rectangle viewport)
        {
            float contentH = ContentSize.Y;
            float contentW = ContentSize.X;
            float viewH = viewport.Height;
            float viewW = viewport.Width;

            // Vertical scroll bar
            if (_showVerticalBar && contentH > viewH)
            {
                int barX = viewport.Right - (int)_scrollBarWidth;
                int barH = viewport.Height;

                // Track
                renderer.DrawRect(new Rectangle(barX, viewport.Y, (int)_scrollBarWidth, barH),
                    ScrollTrackColor);

                // Thumb
                float thumbH = MathF.Max(viewH / contentH * barH, 20f);
                float scrollFrac = _scrollOffset.Y / (contentH - viewH);
                int thumbY = viewport.Y + (int)(scrollFrac * (barH - thumbH));
                renderer.DrawRect(new Rectangle(barX, thumbY, (int)_scrollBarWidth, (int)thumbH),
                    ScrollThumbColor);
            }

            // Horizontal scroll bar
            if (_showHorizontalBar && contentW > viewW)
            {
                int barY = viewport.Bottom - (int)_scrollBarWidth;
                int barW = viewport.Width;

                // Track
                renderer.DrawRect(new Rectangle(viewport.X, barY, barW, (int)_scrollBarWidth),
                    ScrollTrackColor);

                // Thumb
                float thumbW = MathF.Max(viewW / contentW * barW, 20f);
                float scrollFrac = _scrollOffset.X / (contentW - viewW);
                int thumbX = viewport.X + (int)(scrollFrac * (barW - thumbW));
                renderer.DrawRect(new Rectangle(thumbX, barY, (int)thumbW, (int)_scrollBarWidth),
                    ScrollThumbColor);
            }
        }

        // ── Helpers ────────────────────────────────────────────────

        private Vector2 ClampOffset(Vector2 offset)
        {
            float viewW = ContentBounds.Width;
            float viewH = ContentBounds.Height;
            float maxX = MathF.Max(0, ContentSize.X - viewW);
            float maxY = MathF.Max(0, ContentSize.Y - viewH);
            return new Vector2(
                Math.Clamp(offset.X, 0, maxX),
                Math.Clamp(offset.Y, 0, maxY));
        }

        /// <summary>Scroll to the top.</summary>
        public void ScrollToTop()
        {
            _scrollOffset.Y = 0;
            InvalidateArrange();
        }

        /// <summary>Scroll to the bottom.</summary>
        public void ScrollToBottom()
        {
            _scrollOffset.Y = MathF.Max(0, ContentSize.Y - ViewportSize.Y);
            InvalidateArrange();
        }
    }
}
