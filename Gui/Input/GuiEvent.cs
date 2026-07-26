using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace FNA.Gui
{
    /// <summary>GUI event types dispatched through the widget tree.</summary>
    public enum GuiEventType
    {
        // Pointer events
        PointerEnter,
        PointerLeave,
        PointerDown,
        PointerUp,
        Click,
        Drag,
        Scroll,

        // Keyboard events
        KeyDown,
        KeyUp,
        TextInput,

        // Focus events
        FocusGained,
        FocusLost,
    }

    /// <summary>Mouse button identifiers.</summary>
    public enum MouseButton
    {
        Left,
        Middle,
        Right,
    }

    /// <summary>Navigation direction for gamepad D-pad / arrow keys.</summary>
    public enum Direction
    {
        Up,
        Down,
        Left,
        Right,
    }

    /// <summary>
    /// A routed GUI event — dispatched through the widget tree via
    /// capture (root→target) then bubble (target→root).
    /// Set <see cref="Handled"/> to true to stop propagation.
    /// </summary>
    public class GuiEvent
    {
        public GuiEventType Type { get; }
        public Widget? Target { get; internal set; }
        public Vector2 Position { get; }
        public MouseButton Button { get; }
        public Keys Key { get; }
        public float ScrollDelta { get; }
        public string? Text { get; }
        public bool Handled { get; set; }

        public GuiEvent(GuiEventType type, Vector2 position,
            MouseButton button = MouseButton.Left,
            Keys key = Keys.None, float scrollDelta = 0, string? text = null)
        {
            Type = type;
            Position = position;
            Button = button;
            Key = key;
            ScrollDelta = scrollDelta;
            Text = text;
        }

        public override string ToString() =>
            $"{Type} at ({Position.X:F0},{Position.Y:F0}) target={Target} handled={Handled}";
    }
}
