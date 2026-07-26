namespace FNA.Gui
{
    /// <summary>
    /// Visual/interaction states a widget can be in.
    /// Evaluated each frame from Enabled + pointer + focus flags.
    /// </summary>
    public enum WidgetState
    {
        Normal,
        Hover,
        Pressed,
        Disabled,
        Focused,
    }
}
