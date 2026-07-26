using System;
using System.Collections.Generic;

namespace FNA.Gui.Serialization
{
    /// <summary>
    /// Widget type registry for XAML-lite loading.
    /// Uses explicit factory registration (no reflection) for AOT/trimming safety.
    /// </summary>
    public static class TypeRegistry
    {
        private static readonly Dictionary<string, Func<Widget>> _factories = new();

        /// <summary>
        /// Register a widget type for XAML-lite. Call once per widget type at startup.
        /// </summary>
        /// <param name="name">XML element name (e.g., "Button", "StackLayout").</param>
        /// <param name="factory">Factory function that creates a new instance.</param>
        public static void Register(string name, Func<Widget> factory)
        {
            _factories[name] = factory;
        }

        /// <summary>
        /// Create a new widget instance by its registered name.
        /// Returns null if the type is not registered (caller should report error).
        /// </summary>
        public static Widget? Create(string name)
        {
            if (_factories.TryGetValue(name, out var factory))
                return factory();
            return null;
        }

        /// <summary>
        /// Register all built-in widget types. Call once at startup.
        /// </summary>
        public static void RegisterDefaults()
        {
            // Core
            Register("Panel", () => new Panel());
            Register("Screen", () => new Panel()); // <Screen> maps to Panel root

            // Layout
            Register("StackLayout", () => new StackLayout());
            Register("GridLayout", () => new GridLayout());
            Register("DockLayout", () => new DockLayout());

            // Interactive widgets
            Register("Button", () => new Button());
            Register("CheckBox", () => new CheckBox());
            Register("Slider", () => new Slider());
            Register("TextBox", () => new TextBox());

            // Display
            Register("Text", () => new Text());
            Register("Image", () => new Image());

            // Advanced
            Register("ScrollView", () => new ScrollView());
            Register("Window", () => new Window());
            Register("Dialog", () => new Dialog());
        }
    }
}
