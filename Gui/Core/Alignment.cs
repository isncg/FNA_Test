namespace FNA.Gui
{
    public enum HorizontalAlignment
    {
        Left,
        Center,
        Right,
        Stretch,
    }

    public enum VerticalAlignment
    {
        Top,
        Center,
        Bottom,
        Stretch,
    }

    public enum Visibility
    {
        /// <summary>Normal rendering and layout participation.</summary>
        Visible,
        /// <summary>Hidden from rendering but still occupies layout space.</summary>
        Hidden,
        /// <summary>Collapsed: neither rendered nor occupies layout space.</summary>
        Collapsed,
    }
}
