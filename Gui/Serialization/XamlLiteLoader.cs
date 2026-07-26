using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace FNA.Gui.Serialization
{
    /// <summary>
    /// XAML-lite XML loader. Reads a subset of XAML-like XML and builds
    /// a widget tree using the registered type factories and type converters.
    ///
    /// MVP supports: elements as widget types, attributes as properties,
    /// nested elements as children, x:Name for FindByName lookup,
    /// and Owner.Property attached-property syntax.
    /// </summary>
    public class XamlLiteLoader
    {
        private readonly Dictionary<string, Widget> _nameMap = new();

        /// <summary>
        /// The name→widget map populated during loading. Callers can use this
        /// for FindByName lookups, or call InstallNames(root) to attach to the tree.
        /// </summary>
        public IReadOnlyDictionary<string, Widget> NameMap => _nameMap;

        /// <summary>
        /// Load a widget tree from an XElement.
        /// </summary>
        public Widget Load(XElement element)
        {
            _nameMap.Clear();
            return CreateElement(element);
        }

        /// <summary>
        /// Load a widget tree from an XML string.
        /// </summary>
        public Widget LoadXml(string xml)
        {
            var doc = XDocument.Parse(xml);
            return Load(doc.Root!);
        }

        /// <summary>
        /// Find a named widget from the last loaded tree.
        /// </summary>
        public T? FindByName<T>(string name) where T : Widget
        {
            if (_nameMap.TryGetValue(name, out var widget))
                return widget as T;
            return null;
        }

        // ── Property setter cache (reflection-based; swappable for AOT) ──

        private static readonly Dictionary<(Type, string), PropertyInfo?> _propCache = new();

        private static PropertyInfo? GetCachedProperty(Type type, string propName)
        {
            var key = (type, propName);
            if (_propCache.TryGetValue(key, out var cached))
                return cached;

            // Walk hierarchy for settable public properties
            var prop = type.GetProperty(propName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);

            // Ignore read-only properties and indexers
            if (prop != null && (!prop.CanWrite || prop.GetIndexParameters().Length > 0))
                prop = null;

            _propCache[key] = prop;
            return prop;
        }

        // ── Element creation ──────────────────────────────────────────

        private Widget CreateElement(XElement el)
        {
            string typeName = el.Name.LocalName;
            var widget = TypeRegistry.Create(typeName);
            if (widget == null)
                throw new InvalidOperationException(
                    $"Unknown widget type '{typeName}'. Register it with TypeRegistry first.");

            ApplyAttributes(widget, el);
            ApplyChildren(widget, el);

            return widget;
        }

        private void ApplyAttributes(Widget widget, XElement el)
        {
            foreach (var attr in el.Attributes())
            {
                string attrName = attr.Name.LocalName;
                string attrValue = attr.Value;

                // x:Name special handling
                if (attrName == "Name" && attr.Name.NamespaceName == "http://schemas.fna-gui/xaml")
                {
                    widget.Name = attrValue;
                    _nameMap[attrValue] = widget;
                    continue;
                }

                // Simplified x:Name (without xmlns)
                if (attrName.StartsWith("x:") || (attrName == "Name" && el.Attribute("{http://schemas.fna-gui/xaml}Name") == null))
                {
                    if (attrName == "x:Name" || attrName == "Name")
                    {
                        widget.Name = attrValue;
                        _nameMap[attrValue] = widget;
                    }
                    continue;
                }

                // Attached property (e.g., Grid.Row, DockPanel.Dock)
                if (attrName.Contains('.'))
                {
                    ApplyAttachedProperty(widget, attrName, attrValue);
                    continue;
                }

                // Regular property
                SetWidgetProperty(widget, attrName, attrValue);
            }
        }

        private void ApplyChildren(Widget parent, XElement el)
        {
            foreach (var childEl in el.Elements())
            {
                var child = CreateElement(childEl);
                parent.AddChild(child);
            }
        }

        // ── Property setting ──────────────────────────────────────────

        private static void SetWidgetProperty(Widget widget, string propName, string value)
        {
            var prop = GetCachedProperty(widget.GetType(), propName);

            if (prop == null)
            {
                // Ignore unknown properties silently (XAML-lite is permissive)
                return;
            }

            try
            {
                // Handle Nullable<T> by unwrapping
                var targetType = prop.PropertyType;
                if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Nullable<>))
                    targetType = Nullable.GetUnderlyingType(targetType)!;

                var converted = TypeConverterRegistry.Convert(targetType, value);
                prop.SetValue(widget, converted);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to set property '{propName}' on {widget.GetType().Name} " +
                    $"with value '{value}': {ex.Message}", ex);
            }
        }

        private static void ApplyAttachedProperty(Widget widget, string attrName, string value)
        {
            // Parse "Owner.Property" → owner type name, property name
            int dotIdx = attrName.IndexOf('.');
            if (dotIdx <= 0 || dotIdx >= attrName.Length - 1)
                return;

            string ownerName = attrName.Substring(0, dotIdx);
            string propName = attrName.Substring(dotIdx + 1);

            // Resolve owner type (look in FNA.Gui namespace)
            var ownerType = ResolveAttachedOwnerType(ownerName);
            if (ownerType == null)
                return;

            var prop = ownerType.GetProperty(propName,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (prop == null || !prop.CanWrite)
                return;

            try
            {
                var converted = TypeConverterRegistry.Convert(prop.PropertyType, value);
                prop.SetValue(null, converted);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to set attached property '{attrName}' with value '{value}': {ex.Message}", ex);
            }
        }

        private static Type? ResolveAttachedOwnerType(string name)
        {
            // Try common attached-property owner types
            return name switch
            {
                "GridLayout" or "Grid" => typeof(GridLayout),
                "DockLayout" or "DockPanel" => typeof(DockLayout),
                "StackLayout" => typeof(StackLayout),
                _ => Type.GetType($"FNA.Gui.{name}, Gui", throwOnError: false),
            };
        }
    }
}
