using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.Xna.Framework;

namespace FNA.Gui
{
    /// <summary>
    /// Base class for all GUI widgets.
    /// Implements the retained-mode widget tree with layout, input, and rendering hooks.
    /// </summary>
    public abstract class Widget
    {
        // ── Tree ───────────────────────────────────────────────────

        private Widget? _parent;
        private readonly List<Widget> _children = new();

        public Widget? Parent => _parent;
        public IReadOnlyList<Widget> Children { get; }

        /// <summary>
        /// Optional name for this widget, used with <see cref="FindByName{T}(string)"/>
        /// and XAML-lite x:Name resolution.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Recursively search for a named widget in this subtree.
        /// Returns null if not found or the type doesn't match.
        /// </summary>
        public T? FindByName<T>(string name) where T : Widget
        {
            if (Name == name && this is T match)
                return match;

            foreach (var child in _children)
            {
                var result = child.FindByName<T>(name);
                if (result != null)
                    return result;
            }

            return null;
        }

        /// <summary>Add a child widget. Removes from previous parent if any.</summary>
        public void AddChild(Widget child)
        {
            if (child._parent != null)
                child._parent.RemoveChild(child);

            child._parent = this;
            child.Theme = _theme; // inherit theme from parent
            _children.Add(child);
            InvalidateMeasure();
        }

        /// <summary>Remove a child widget.</summary>
        public void RemoveChild(Widget child)
        {
            if (_children.Remove(child))
            {
                child._parent = null;
                InvalidateMeasure();
            }
        }

        /// <summary>Remove all children.</summary>
        public void ClearChildren()
        {
            foreach (var child in _children)
                child._parent = null;
            _children.Clear();
            InvalidateMeasure();
        }

        // ── Layout Properties ──────────────────────────────────────

        private float _width = float.NaN;
        private float _height = float.NaN;
        private float _minWidth;
        private float _minHeight;
        private float _maxWidth = float.PositiveInfinity;
        private float _maxHeight = float.PositiveInfinity;
        private Thickness _margin;
        private Thickness _padding;
        private HorizontalAlignment _hAlign = HorizontalAlignment.Stretch;
        private VerticalAlignment _vAlign = VerticalAlignment.Stretch;
        private Visibility _visibility = Visibility.Visible;

        /// <summary>Explicit width in logical pixels. NaN = Auto (determined by content).</summary>
        public float Width { get => _width; set { if (_width != value) { _width = value; InvalidateMeasure(); } } }

        /// <summary>Explicit height in logical pixels. NaN = Auto (determined by content).</summary>
        public float Height { get => _height; set { if (_height != value) { _height = value; InvalidateMeasure(); } } }

        public float MinWidth { get => _minWidth; set { if (_minWidth != value) { _minWidth = value; InvalidateMeasure(); } } }
        public float MinHeight { get => _minHeight; set { if (_minHeight != value) { _minHeight = value; InvalidateMeasure(); } } }
        public float MaxWidth { get => _maxWidth; set { if (_maxWidth != value) { _maxWidth = value; InvalidateMeasure(); } } }
        public float MaxHeight { get => _maxHeight; set { if (_maxHeight != value) { _maxHeight = value; InvalidateMeasure(); } } }

        public Thickness Margin { get => _margin; set { if (_margin != value) { _margin = value; InvalidateMeasure(); } } }
        public Thickness Padding { get => _padding; set { if (_padding != value) { _padding = value; InvalidateMeasure(); } } }

        public HorizontalAlignment HorizontalAlignment { get => _hAlign; set { if (_hAlign != value) { _hAlign = value; InvalidateArrange(); } } }
        public VerticalAlignment VerticalAlignment { get => _vAlign; set { if (_vAlign != value) { _vAlign = value; InvalidateArrange(); } } }

        public Visibility Visibility
        {
            get => _visibility;
            set
            {
                if (_visibility != value)
                {
                    _visibility = value;
                    InvalidateMeasure();
                }
            }
        }

        // ── Layout Results ─────────────────────────────────────────

        /// <summary>The final arranged rectangle in parent-local logical coordinates.</summary>
        public Rectangle Bounds { get; internal set; }

        /// <summary>Cached desired size from the most recent Measure pass (includes Margin).</summary>
        public Vector2 DesiredSize { get; private set; }

        /// <summary>Content region = Bounds minus Padding.</summary>
        public Rectangle ContentBounds
        {
            get
            {
                var b = Bounds;
                b.X += (int)_padding.Left;
                b.Y += (int)_padding.Top;
                b.Width -= (int)(_padding.Left + _padding.Right);
                b.Height -= (int)(_padding.Top + _padding.Bottom);
                return b;
            }
        }

        // ── State ──────────────────────────────────────────────────

        private bool _enabled = true;
        public bool Enabled
        {
            get => _enabled;
            set { if (_enabled != value) { _enabled = value; UpdateState(); } }
        }

        private bool _isHovered;
        private bool _isPressed;
        private bool _isFocused;

        public WidgetState State { get; private set; } = WidgetState.Normal;

        // ── Styling ──────────────────────────────────────────────────

        /// <summary>
        /// Optional per-widget style sheet. When set, overrides the theme default.
        /// Use <see cref="ResolveBackground"/> etc. in OnDraw to query styled values.
        /// </summary>
        public StyleSheet? Style { get; set; }

        /// <summary>
        /// The theme this widget uses. Setting this propagates to all children.
        /// </summary>
        public Theme? Theme
        {
            get => _theme;
            set
            {
                _theme = value;
                foreach (var child in _children)
                    child.Theme = value;
            }
        }
        private Theme? _theme;

        /// <summary>Resolve the background color for the current state from style/theme.</summary>
        public Color ResolveBackground(Color fallback)
        {
            // Per-widget style override takes priority
            if (Style?.BackgroundColor != null)
                return Style.BackgroundColor.GetValue(State);

            // Then theme style for this widget type
            var themeStyle = Theme?.GetStyle(GetType().Name);
            if (themeStyle?.BackgroundColor != null)
                return themeStyle.BackgroundColor.GetValue(State);

            return fallback;
        }

        /// <summary>Resolve the border color for the current state from style/theme.</summary>
        public Color ResolveBorder(Color fallback)
        {
            if (Style?.BorderColor != null)
                return Style.BorderColor.GetValue(State);

            var themeStyle = Theme?.GetStyle(GetType().Name);
            if (themeStyle?.BorderColor != null)
                return themeStyle.BorderColor.GetValue(State);

            return fallback;
        }

        /// <summary>Resolve the text color for the current state from style/theme.</summary>
        public Color ResolveText(Color fallback)
        {
            if (Style?.TextColor != null)
                return Style.TextColor.GetValue(State);

            var themeStyle = Theme?.GetStyle(GetType().Name);
            if (themeStyle?.TextColor != null)
                return themeStyle.TextColor.GetValue(State);

            return fallback;
        }

        private void UpdateState()
        {
            var newState = !_enabled ? WidgetState.Disabled
                : _isPressed ? WidgetState.Pressed
                : _isHovered ? WidgetState.Hover
                : _isFocused ? WidgetState.Focused
                : WidgetState.Normal;

            if (State != newState)
            {
                State = newState;
                OnStateChanged(State);
            }
        }

        /// <summary>Called when the widget's visual state changes.</summary>
        protected virtual void OnStateChanged(WidgetState newState) { }

        // ── Internal State Setters (called by InputRouter) ────────────

        /// <summary>Set the hovered state. Called by InputRouter.</summary>
        internal void SetHoveredInternal(bool value)
        {
            if (_isHovered != value)
            {
                _isHovered = value;
                UpdateState();
            }
        }

        /// <summary>Set the pressed state. Called by InputRouter.</summary>
        internal void SetPressedInternal(bool value)
        {
            if (_isPressed != value)
            {
                _isPressed = value;
                UpdateState();
            }
        }

        /// <summary>Set the focused state. Called by InputRouter.</summary>
        internal void SetFocusedInternal(bool value)
        {
            if (_isFocused != value)
            {
                _isFocused = value;
                UpdateState();
            }
        }

        // ── Dirty Flags ────────────────────────────────────────────

        public bool MeasureDirty { get; private set; } = true;
        public bool ArrangeDirty { get; private set; } = true;

        private Vector2 _lastMeasureInput;

        /// <summary>Invalidate this widget's measure, propagating upward.</summary>
        public virtual void InvalidateMeasure()
        {
            if (MeasureDirty) return;
            MeasureDirty = true;
            ArrangeDirty = true;
            _parent?.InvalidateMeasure();
        }

        /// <summary>Invalidate this widget's arrange only (no size change).</summary>
        public virtual void InvalidateArrange()
        {
            if (ArrangeDirty) return;
            ArrangeDirty = true;
        }

        // ── Measure / Arrange ──────────────────────────────────────

        /// <summary>
        /// Measure pass: compute desired size from available space.
        /// </summary>
        public Vector2 Measure(Vector2 available)
        {
            if (_visibility == Visibility.Collapsed)
            {
                DesiredSize = Vector2.Zero;
                MeasureDirty = false;
                ArrangeDirty = false;
                return DesiredSize;
            }

            // Cache hit: same input and not dirty
            if (!MeasureDirty && available == _lastMeasureInput)
                return DesiredSize;

            // Subtract margin and padding from available
            var inner = new Vector2(
                available.X - _margin.Left - _margin.Right - _padding.Left - _padding.Right,
                available.Y - _margin.Top - _margin.Bottom - _padding.Top - _padding.Bottom);

            // Clamp to explicit dimensions
            if (!float.IsNaN(_width)) inner.X = _width;
            if (!float.IsNaN(_height)) inner.Y = _height;

            // Clamp to min/max
            inner.X = Math.Clamp(inner.X, _minWidth, _maxWidth);
            inner.Y = Math.Clamp(inner.Y, _minHeight, _maxHeight);

            // Delegate to subclass
            var contentSize = OnMeasure(inner);

            // Clamp content size to explicit and min/max
            float desiredW = !float.IsNaN(_width) ? _width
                : Math.Clamp(contentSize.X, _minWidth, _maxWidth);
            float desiredH = !float.IsNaN(_height) ? _height
                : Math.Clamp(contentSize.Y, _minHeight, _maxHeight);

            // Add padding and margin
            DesiredSize = new Vector2(
                desiredW + _padding.Left + _padding.Right + _margin.Left + _margin.Right,
                desiredH + _padding.Top + _padding.Bottom + _margin.Top + _margin.Bottom);

            _lastMeasureInput = available;
            MeasureDirty = false;
            return DesiredSize;
        }

        /// <summary>
        /// Measure the content area. Override in subclasses.
        /// </summary>
        protected abstract Vector2 OnMeasure(Vector2 available);

        /// <summary>
        /// Arrange pass: position the widget within its parent-allocated rectangle.
        /// </summary>
        public virtual void Arrange(Rectangle finalRect)
        {
            if (_visibility == Visibility.Collapsed)
            {
                Bounds = Rectangle.Empty;
                ArrangeDirty = false;
                return;
            }

            // Subtract margin
            var inner = new Rectangle(
                finalRect.X + (int)_margin.Left,
                finalRect.Y + (int)_margin.Top,
                finalRect.Width - (int)(_margin.Left + _margin.Right),
                finalRect.Height - (int)(_margin.Top + _margin.Bottom));

            // Compute final size based on alignment
            var desired = DesiredSize;
            desired.X -= _margin.Left + _margin.Right;
            desired.Y -= _margin.Top + _margin.Bottom;

            int sizeW = _hAlign == HorizontalAlignment.Stretch && inner.Width > 0
                ? inner.Width : (int)Math.Min(desired.X, inner.Width);
            int sizeH = _vAlign == VerticalAlignment.Stretch && inner.Height > 0
                ? inner.Height : (int)Math.Min(desired.Y, inner.Height);

            // Clamp to explicit and min/max
            if (!float.IsNaN(_width)) sizeW = (int)_width;
            if (!float.IsNaN(_height)) sizeH = (int)_height;
            sizeW = (int)Math.Clamp(sizeW, _minWidth, Math.Min(_maxWidth, inner.Width));
            sizeH = (int)Math.Clamp(sizeH, _minHeight, Math.Min(_maxHeight, inner.Height));

            // Position within inner rectangle based on alignment
            int posX = _hAlign switch
            {
                HorizontalAlignment.Left => inner.X,
                HorizontalAlignment.Right => inner.X + inner.Width - sizeW,
                HorizontalAlignment.Center => inner.X + (inner.Width - sizeW) / 2,
                _ => inner.X, // Stretch or default
            };

            int posY = _vAlign switch
            {
                VerticalAlignment.Top => inner.Y,
                VerticalAlignment.Bottom => inner.Y + inner.Height - sizeH,
                VerticalAlignment.Center => inner.Y + (inner.Height - sizeH) / 2,
                _ => inner.Y,
            };

            Bounds = new Rectangle(posX, posY, sizeW, sizeH);

            // Arrange children
            var content = ContentBounds;
            OnArrange(content);

            ArrangeDirty = false;
        }

        /// <summary>
        /// Arrange children within the content area. Override in subclasses.
        /// </summary>
        protected abstract void OnArrange(Rectangle content);

        // ── Draw ────────────────────────────────────────────────────

        /// <summary>Draw the widget and its children.</summary>
        public virtual void Draw(IGuiRenderer renderer)
        {
            if (_visibility != Visibility.Visible)
                return;

            OnDraw(renderer);

            if (!HandlesOwnChildDrawing)
            {
                foreach (var child in _children)
                    child.Draw(renderer);
            }
        }

        /// <summary>Draw this widget's own content. Override in subclasses.</summary>
        protected virtual void OnDraw(IGuiRenderer renderer) { }

        // ── Input ───────────────────────────────────────────────────

        public virtual bool HitTest(Vector2 point)
        {
            if (_visibility != Visibility.Visible)
                return false;
            return Bounds.Contains((int)point.X, (int)point.Y);
        }

        public virtual Widget? HitTestTree(Vector2 point)
        {
            if (!HitTest(point))
                return null;

            // Depth-first: check children in reverse order (topmost first)
            for (int i = _children.Count - 1; i >= 0; i--)
            {
                var hit = _children[i].HitTestTree(point);
                if (hit != null)
                    return hit;
            }
            return this;
        }

        // ── Events ──────────────────────────────────────────────────

        /// <summary>
        /// Whether this widget can receive keyboard focus.
        /// Interactive widgets (Button, Slider, TextBox) set this to true.
        /// </summary>
        public bool IsFocusable { get; set; }

        /// <summary>
        /// Whether this widget accepts SDL text input (IME / character composition).
        /// When the focused widget returns true, the platform should call
        /// <c>TextInputEXT.StartTextInput()</c> and route characters to
        /// <c>GuiSystem.InjectTextInput()</c>.
        /// </summary>
        public virtual bool WantsTextInput => false;

        /// <summary>
        /// Whether this widget draws its own children inside <see cref="OnDraw"/>.
        /// When true, <see cref="Draw"/> skips its default child iteration —
        /// the widget is responsible for calling <c>child.Draw(renderer)</c> itself.
        /// Used by containers that wrap children in PushClip/PopClip
        /// (ScrollView, Window, Panel with ClipChildren).
        /// </summary>
        public virtual bool HandlesOwnChildDrawing => false;

        /// <summary>
        /// Handle a routed GUI event. Override in subclasses to respond
        /// to input. Set <c>evt.Handled = true</c> to stop bubbling.
        /// </summary>
        public virtual void OnEvent(GuiEvent evt) { }

        // ── Root Measure/Arrange ────────────────────────────────────

        /// <summary>Run Measure and Arrange on the full tree. Call once per frame on root.</summary>
        public void Layout(Vector2 screenSize)
        {
            Measure(screenSize);
            Arrange(new Rectangle(0, 0, (int)screenSize.X, (int)screenSize.Y));
        }

        // ── Attached Properties ─────────────────────────────────────

        /// <summary>
        /// Lightweight attached property store used by layout containers
        /// (Grid.Row, Grid.Column, DockPanel.Dock, etc.). Created lazily.
        /// </summary>
        internal Dictionary<string, object>? _attachedProps;

        internal T? GetAttached<T>(string key)
        {
            if (_attachedProps != null && _attachedProps.TryGetValue(key, out var v))
                return (T)v;
            return default;
        }

        internal void SetAttached<T>(string key, T value)
        {
            _attachedProps ??= new Dictionary<string, object>();
            _attachedProps[key] = value!;
        }

        internal bool RemoveAttached(string key)
        {
            return _attachedProps?.Remove(key) ?? false;
        }

        // ── Constructor ─────────────────────────────────────────────

        protected Widget()
        {
            Children = new ReadOnlyCollection<Widget>(_children);
        }
    }
}
