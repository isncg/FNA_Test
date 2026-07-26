using System;
using Microsoft.Xna.Framework;

namespace FNA.Gui
{
    /// <summary>
    /// A horizontal slider widget. Drag the thumb to change <see cref="Value"/>
    /// between <see cref="Min"/> and <see cref="Max"/>.
    /// Fires <see cref="ValueChanged"/> on each change.
    /// </summary>
    public class Slider : Widget
    {
        private float _value;
        private float _min;
        private float _max = 100f;
        private bool _isDragging;

        /// <summary>Current value (clamped to [Min, Max]).</summary>
        public float Value
        {
            get => _value;
            set
            {
                float clamped = Math.Clamp(value, _min, _max);
                if (_value != clamped)
                {
                    _value = clamped;
                    ValueChanged?.Invoke(this, _value);
                }
            }
        }

        /// <summary>Minimum value.</summary>
        public float Min
        {
            get => _min;
            set { _min = value; if (_value < _min) Value = _min; }
        }

        /// <summary>Maximum value.</summary>
        public float Max
        {
            get => _max;
            set { _max = value; if (_value > _max) Value = _max; }
        }

        /// <summary>Fired when <see cref="Value"/> changes.</summary>
        public event Action<Slider, float>? ValueChanged;

        public Slider()
        {
            IsFocusable = true;
        }

        protected override Vector2 OnMeasure(Vector2 available)
        {
            return new Vector2(
                !float.IsNaN((float)Width) ? Width : 200,
                24 + Padding.Vertical);
        }

        protected override void OnArrange(Rectangle content) { }

        public override void OnEvent(GuiEvent evt)
        {
            if (!Enabled) return;

            switch (evt.Type)
            {
                case GuiEventType.PointerDown:
                    _isDragging = true;
                    UpdateValueFromPosition(evt.Position.X);
                    evt.Handled = true;
                    break;

                case GuiEventType.PointerUp:
                    _isDragging = false;
                    evt.Handled = true;
                    break;

                case GuiEventType.Drag:
                    if (_isDragging)
                    {
                        UpdateValueFromPosition(evt.Position.X);
                        evt.Handled = true;
                    }
                    break;

                case GuiEventType.PointerLeave:
                    // Don't stop dragging on leave — user might drag outside bounds
                    break;

                case GuiEventType.KeyDown:
                    float step = (_max - _min) / 100f;
                    if (evt.Key == Microsoft.Xna.Framework.Input.Keys.Right)
                    {
                        Value += step;
                        evt.Handled = true;
                    }
                    else if (evt.Key == Microsoft.Xna.Framework.Input.Keys.Left)
                    {
                        Value -= step;
                        evt.Handled = true;
                    }
                    break;
            }
        }

        private void UpdateValueFromPosition(float pointerX)
        {
            float trackLeft = Bounds.X + Padding.Left + 8;  // thumb radius
            float trackRight = Bounds.X + Bounds.Width - Padding.Right - 8;
            float t = Math.Clamp((pointerX - trackLeft) / (trackRight - trackLeft), 0, 1);
            Value = _min + t * (_max - _min);
        }

        protected override void OnDraw(IGuiRenderer renderer)
        {
            var b = Bounds;
            int trackY = b.Y + b.Height / 2 - 2;

            // Track background
            renderer.DrawRect(new Rectangle(
                b.X + (int)Padding.Left, trackY,
                b.Width - (int)Padding.Horizontal, 4),
                ResolveBackground(Color.DarkGray));

            // Track fill (left of thumb)
            float t = (_value - _min) / (_max - _min);
            int fillW = (int)((b.Width - Padding.Horizontal - 16) * t);
            renderer.DrawRect(new Rectangle(
                b.X + (int)Padding.Left + 8, trackY,
                fillW, 4),
                Color.CornflowerBlue);

            // Thumb
            int thumbX = b.X + (int)Padding.Left + 8 + fillW - 6;
            int thumbY = b.Y + b.Height / 2 - 8;
            var thumbColor = ResolveBackground(State switch
            {
                WidgetState.Disabled => Color.Gray,
                WidgetState.Pressed => Color.DarkGray,
                WidgetState.Hover => Color.LightGray,
                _ => Color.White,
            });
            renderer.DrawRect(new Rectangle(thumbX, thumbY, 12, 16), thumbColor);

            // Thumb border
            var borderColor = IsFocusable && State == WidgetState.Focused
                ? Color.Yellow : Color.Black;
            var thumb = new Rectangle(thumbX, thumbY, 12, 16);
            renderer.DrawRect(new Rectangle(thumb.X, thumb.Y, thumb.Width, 1), borderColor);
            renderer.DrawRect(new Rectangle(thumb.X, thumb.Y + thumb.Height - 1, thumb.Width, 1), borderColor);
            renderer.DrawRect(new Rectangle(thumb.X, thumb.Y, 1, thumb.Height), borderColor);
            renderer.DrawRect(new Rectangle(thumb.X + thumb.Width - 1, thumb.Y, 1, thumb.Height), borderColor);
        }
    }
}
