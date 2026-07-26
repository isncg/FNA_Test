using System;
using Microsoft.Xna.Framework;

namespace FNA.Gui
{
    /// <summary>
    /// A toggleable check box widget with a label.
    /// Fires <see cref="CheckedChanged"/> when toggled.
    /// </summary>
    public class CheckBox : Widget
    {
        private bool _isChecked;
        private string _text = "";

        /// <summary>Whether the check box is in the checked state.</summary>
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked != value)
                {
                    _isChecked = value;
                    CheckedChanged?.Invoke(this, _isChecked);
                }
            }
        }

        /// <summary>Label text displayed next to the check box.</summary>
        public string Text
        {
            get => _text;
            set { _text = value ?? ""; InvalidateMeasure(); }
        }

        /// <summary>Fired when <see cref="IsChecked"/> changes.</summary>
        public event Action<CheckBox, bool>? CheckedChanged;

        public CheckBox()
        {
            IsFocusable = true;
        }

        protected override Vector2 OnMeasure(Vector2 available)
        {
            // Box (16x16) + gap + label
            float boxSize = 16 + Padding.Vertical;
            float w = boxSize + 8; // gap
            if (!string.IsNullOrEmpty(_text))
                w += 100; // rough text estimate
            return new Vector2(w, boxSize);
        }

        protected override void OnArrange(Rectangle content) { }

        public override void OnEvent(GuiEvent evt)
        {
            if (!Enabled) return;

            switch (evt.Type)
            {
                case GuiEventType.Click:
                    IsChecked = !IsChecked;
                    evt.Handled = true;
                    break;

                case GuiEventType.KeyDown:
                    if (evt.Key == Microsoft.Xna.Framework.Input.Keys.Enter ||
                        evt.Key == Microsoft.Xna.Framework.Input.Keys.Space)
                    {
                        IsChecked = !IsChecked;
                        evt.Handled = true;
                    }
                    break;
            }
        }

        protected override void OnDraw(IGuiRenderer renderer)
        {
            int boxX = Bounds.X + (int)Padding.Left;
            int boxY = Bounds.Y + (Bounds.Height - 16) / 2;

            // Check box background
            var boxColor = ResolveBackground(State switch
            {
                WidgetState.Disabled => Color.Gray,
                WidgetState.Hover => Color.LightGray,
                WidgetState.Pressed => Color.DarkGray,
                _ => Color.White,
            });
            renderer.DrawRect(new Rectangle(boxX, boxY, 16, 16), boxColor);

            // Check box border
            var borderColor = ResolveBorder(IsFocusable && State == WidgetState.Focused
                ? Color.Yellow : Color.Black);
            var r = new Rectangle(boxX, boxY, 16, 16);
            renderer.DrawRect(new Rectangle(r.X, r.Y, r.Width, 1), borderColor);
            renderer.DrawRect(new Rectangle(r.X, r.Y + r.Height - 1, r.Width, 1), borderColor);
            renderer.DrawRect(new Rectangle(r.X, r.Y, 1, r.Height), borderColor);
            renderer.DrawRect(new Rectangle(r.X + r.Width - 1, r.Y, 1, r.Height), borderColor);

            // Check mark
            if (_isChecked)
            {
                int inset = 3;
                renderer.DrawRect(new Rectangle(
                    boxX + inset, boxY + inset,
                    16 - inset * 2, 16 - inset * 2), Color.Black);
            }

            // Label text (drawn via renderer if font available — placeholder)
            // TODO: Add font rendering for label in Phase 4+
        }
    }
}
