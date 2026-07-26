using System;
using Microsoft.Xna.Framework;
using Keys = Microsoft.Xna.Framework.Input.Keys;

namespace FNA.Gui
{
    /// <summary>
    /// A single-line text input widget. Supports cursor movement, text insertion,
    /// deletion, and selection placeholder. Focus the widget to begin editing.
    /// </summary>
    public class TextBox : Widget
    {
        private string _text = "";
        private int _cursorPos;
        private SdfFont? _font;
        private float _fontSize = 18;
        private string _placeholder = "";

        /// <summary>The current text content.</summary>
        public string Text
        {
            get => _text;
            set
            {
                _text = value ?? "";
                _cursorPos = Math.Clamp(_cursorPos, 0, _text.Length);
                InvalidateMeasure();
                TextChanged?.Invoke(this, _text);
            }
        }

        /// <summary>Placeholder text shown when Text is empty and not focused.</summary>
        public string Placeholder
        {
            get => _placeholder;
            set { _placeholder = value ?? ""; }
        }

        /// <summary>Cursor position (0 = before first char, Length = after last char).</summary>
        public int CursorPosition
        {
            get => _cursorPos;
            set => _cursorPos = Math.Clamp(value, 0, _text.Length);
        }

        /// <summary>SDF font for text rendering. If null, text won't be drawn.</summary>
        public SdfFont? Font
        {
            get => _font;
            set { _font = value; InvalidateMeasure(); }
        }

        /// <summary>Font size for text rendering.</summary>
        public float FontSize
        {
            get => _fontSize;
            set { _fontSize = value; InvalidateMeasure(); }
        }

        /// <summary>Maximum number of characters allowed.</summary>
        public int MaxLength { get; set; } = 256;

        /// <summary>Whether the text box is read-only.</summary>
        public bool IsReadOnly { get; set; }

        /// <summary>Background color.</summary>
        public Color BackgroundColor { get; set; } = new(30, 30, 46, 255);

        /// <summary>Text color.</summary>
        public Color TextColor { get; set; } = Color.White;

        /// <summary>Placeholder text color.</summary>
        public Color PlaceholderColor { get; set; } = Color.Gray;

        /// <summary>Cursor color.</summary>
        public Color CursorColor { get; set; } = Color.White;

        /// <summary>Border color when not focused.</summary>
        public Color BorderColor { get; set; } = new(80, 80, 100, 255);

        /// <summary>Border color when focused.</summary>
        public Color FocusedBorderColor { get; set; } = new(74, 144, 217, 255);

        /// <summary>Fired when the text content changes.</summary>
        public event Action<TextBox, string>? TextChanged;

        /// <summary>Whether this TextBox currently has keyboard focus (independent of visual state).</summary>
        public bool HasFocus { get; private set; }

        /// <summary>Whether this TextBox wants SDL text input (IME/character composition).</summary>
        public override bool WantsTextInput => !IsReadOnly;

        public TextBox()
        {
            IsFocusable = true;
        }

        // ── Layout ─────────────────────────────────────────────────

        protected override Vector2 OnMeasure(Vector2 available)
        {
            float w = !float.IsNaN(Width) ? Width : 200;
            float h = !float.IsNaN(Height) ? Height : MathF.Max(_fontSize, 20) + Padding.Vertical + 8;
            return new Vector2(w, h);
        }

        protected override void OnArrange(Rectangle content) { }

        // ── Input ──────────────────────────────────────────────────

        public override void OnEvent(GuiEvent evt)
        {
            switch (evt.Type)
            {
                case GuiEventType.FocusGained:
                    HasFocus = true;
                    evt.Handled = true;
                    break;

                case GuiEventType.FocusLost:
                    HasFocus = false;
                    evt.Handled = true;
                    break;

                case GuiEventType.Click:
                    // Set cursor position based on click position (rough: place at end)
                    if (!IsReadOnly)
                    {
                        _cursorPos = _text.Length; // simplified — no per-character hit testing yet
                    }
                    evt.Handled = true;
                    break;

                case GuiEventType.KeyDown:
                    if (!IsReadOnly)
                        HandleKeyDown(evt.Key);
                    evt.Handled = true;
                    break;

                case GuiEventType.TextInput:
                    if (!IsReadOnly)
                        HandleTextInput(evt.Text);
                    evt.Handled = true;
                    break;
            }
        }

        private void HandleKeyDown(Keys key)
        {
            switch (key)
            {
                case Keys.Back:
                    if (_cursorPos > 0)
                    {
                        _text = _text.Remove(_cursorPos - 1, 1);
                        _cursorPos--;
                        NotifyTextChanged();
                    }
                    break;

                case Keys.Delete:
                    if (_cursorPos < _text.Length)
                    {
                        _text = _text.Remove(_cursorPos, 1);
                        NotifyTextChanged();
                    }
                    break;

                case Keys.Left:
                    if (_cursorPos > 0)
                        _cursorPos--;
                    break;

                case Keys.Right:
                    if (_cursorPos < _text.Length)
                        _cursorPos++;
                    break;

                case Keys.Home:
                    _cursorPos = 0;
                    break;

                case Keys.End:
                    _cursorPos = _text.Length;
                    break;
            }
        }

        private void HandleTextInput(string input)
        {
            if (string.IsNullOrEmpty(input))
                return;

            foreach (char c in input)
            {
                // Skip all control characters — editing keys (Backspace, Delete,
                // arrows, Home, End) are handled via the KeyDown path in HandleKeyDown.
                // When StartTextInput is active, FNA fires BOTH KeyDown AND TextInput
                // for editing keys; handling them here would double-process.
                if (char.IsControl(c))
                    continue;

                if (_text.Length >= MaxLength)
                    break;

                _text = _text.Insert(_cursorPos, c.ToString());
                _cursorPos++;
            }

            NotifyTextChanged();
        }

        private void NotifyTextChanged()
        {
            InvalidateMeasure();
            TextChanged?.Invoke(this, _text);
        }

        // ── Draw ───────────────────────────────────────────────────

        protected override void OnDraw(IGuiRenderer renderer)
        {
            var b = Bounds;

            // Background
            renderer.DrawRect(b, ResolveBackground(BackgroundColor));

            // Border (highlighted when this TextBox has keyboard focus)
            var border = HasFocus ? FocusedBorderColor : BorderColor;
            renderer.DrawRect(new Rectangle(b.X, b.Y, b.Width, 1), border);
            renderer.DrawRect(new Rectangle(b.X, b.Y + b.Height - 1, b.Width, 1), border);
            renderer.DrawRect(new Rectangle(b.X, b.Y, 1, b.Height), border);
            renderer.DrawRect(new Rectangle(b.X + b.Width - 1, b.Y, 1, b.Height), border);

            // Text rendering
            if (_font != null)
            {
                float scale = _fontSize / _font.FontSize;
                float textY = b.Y + (b.Height - _fontSize) / 2 + _font.Ascender * scale;
                float textX = b.X + Padding.Left + 4;

                if (!string.IsNullOrEmpty(_text))
                {
                    renderer.DrawSdfText(_font, _text,
                        new Vector2(textX, textY),
                        ResolveText(TextColor),
                        _fontSize);
                }
                else if (!string.IsNullOrEmpty(_placeholder) && !HasFocus)
                {
                    renderer.DrawSdfText(_font, _placeholder,
                        new Vector2(textX, textY),
                        PlaceholderColor,
                        _fontSize);
                }

                // Text caret (visible only when this TextBox has keyboard focus)
                if (HasFocus && !IsReadOnly)
                {
                    // Measure text up to cursor position for cursor X
                    string textBeforeCursor = _cursorPos > 0 ? _text[.._cursorPos] : "";
                    float cursorX = textX;
                    if (!string.IsNullOrEmpty(textBeforeCursor))
                    {
                        var measureBefore = _font.MeasureString(textBeforeCursor, _fontSize);
                        cursorX += measureBefore.X;
                    }

                    int cursorH = (int)_fontSize;
                    int cursorY = b.Y + (b.Height - cursorH) / 2;
                    renderer.DrawRect(new Rectangle(
                        (int)cursorX, cursorY, 2, cursorH),
                        CursorColor);
                }
            }
        }
    }
}
