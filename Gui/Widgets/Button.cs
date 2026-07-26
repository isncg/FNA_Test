using System;
using Microsoft.Xna.Framework;

namespace FNA.Gui
{
    /// <summary>
    /// A clickable button widget. Fires a <see cref="Click"/> event when
    /// the pointer is pressed and released on the same button.
    /// </summary>
    public class Button : Widget
    {
        private string _text = "";
        private SdfFont? _font;
        private float _fontSize = 24;

        public string Text
        {
            get => _text;
            set { _text = value ?? ""; InvalidateMeasure(); }
        }

        public SdfFont? Font
        {
            get => _font;
            set { _font = value; InvalidateMeasure(); }
        }

        public float FontSize
        {
            get => _fontSize;
            set { _fontSize = value; InvalidateMeasure(); }
        }

        /// <summary>Background color for each state.</summary>
        public Color NormalColor { get; set; } = Color.Gray;
        public Color HoverColor { get; set; } = Color.LightGray;
        public Color PressedColor { get; set; } = Color.DarkGray;
        public Color DisabledColor { get; set; } = new Color(80, 80, 80, 255);

        /// <summary>Fired when the button is clicked (pointer down then up on same widget).</summary>
        public event Action<Button>? Click;

        /// <summary>Number of times this button has been clicked (for test assertions).</summary>
        public int ClickCount { get; private set; }

        public Button()
        {
            IsFocusable = true;
        }

        protected override Vector2 OnMeasure(Vector2 available)
        {
            if (_font == null || string.IsNullOrEmpty(_text))
                return new Vector2(80, 40); // Default button size

            var textSize = _font.MeasureString(_text, _fontSize);
            return new Vector2(
                textSize.X + Padding.Horizontal + 24,  // horizontal padding
                MathF.Max(textSize.Y, _fontSize) + Padding.Vertical + 12);
        }

        protected override void OnArrange(Rectangle content)
        {
            // No children to arrange by default
        }

        public override void OnEvent(GuiEvent evt)
        {
            switch (evt.Type)
            {
                case GuiEventType.PointerDown:
                    evt.Handled = true;
                    break;

                case GuiEventType.PointerUp:
                    evt.Handled = true;
                    break;

                case GuiEventType.Click:
                    if (Enabled)
                    {
                        ClickCount++;
                        Click?.Invoke(this);
                    }
                    evt.Handled = true;
                    break;

                case GuiEventType.KeyDown:
                    if (Enabled && (evt.Key == Microsoft.Xna.Framework.Input.Keys.Enter ||
                                    evt.Key == Microsoft.Xna.Framework.Input.Keys.Space))
                    {
                        ClickCount++;
                        Click?.Invoke(this);
                        evt.Handled = true;
                    }
                    break;
            }
        }

        protected override void OnDraw(IGuiRenderer renderer)
        {
            var bgColor = ResolveBackground(State switch
            {
                WidgetState.Disabled => DisabledColor,
                WidgetState.Pressed => PressedColor,
                WidgetState.Hover => HoverColor,
                WidgetState.Focused => HoverColor,
                _ => NormalColor,
            });

            renderer.DrawRect(Bounds, bgColor);

            // Draw 1px border
            var borderColor = ResolveBorder(IsFocusable && State == WidgetState.Focused
                ? Color.Yellow : Color.Black);
            var b = Bounds;
            renderer.DrawRect(new Rectangle(b.X, b.Y, b.Width, 1), borderColor);
            renderer.DrawRect(new Rectangle(b.X, b.Y + b.Height - 1, b.Width, 1), borderColor);
            renderer.DrawRect(new Rectangle(b.X, b.Y, 1, b.Height), borderColor);
            renderer.DrawRect(new Rectangle(b.X + b.Width - 1, b.Y, 1, b.Height), borderColor);

            // Draw centered text
            if (_font != null && !string.IsNullOrEmpty(_text))
            {
                float scale = _fontSize / _font.FontSize;
                var size = _font.MeasureString(_text, _fontSize);
                float x = Bounds.X + (Bounds.Width - size.X) / 2;
                float y = Bounds.Y + (Bounds.Height - size.Y) / 2 + _font.Ascender * scale;
                renderer.DrawSdfText(_font, _text,
                    new Vector2(x, y),
                    ResolveText(Enabled ? Color.White : Color.Gray),
                    _fontSize);
            }
        }
    }
}
