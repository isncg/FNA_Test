using Microsoft.Xna.Framework;

namespace FNA.Gui
{
    /// <summary>
    /// Abstract base for visual elements that emit geometry (Image, Text).
    /// Follows the "geometric emitter" pattern: subclasses rebuild a
    /// <see cref="GeometryBuffer"/> when content changes, and it is
    /// drawn via <see cref="IGuiRenderer.DrawGeometry"/>.
    /// </summary>
    public abstract class Graphic : Widget
    {
        private Color _color = Color.White;
        private bool _geometryDirty = true;
        private GeometryBuffer? _cachedGeometry;
        private Vector2 _cachedContentSize;

        /// <summary>
        /// Tint color applied at draw time. Does NOT trigger geometry rebuild.
        /// </summary>
        public Color Color
        {
            get => _color;
            set
            {
                if (_color != value)
                {
                    _color = value;
                    // Color-only change: no geometry rebuild needed (tint at draw time)
                }
            }
        }

        /// <summary>Mark geometry as needing rebuild (size/content changed).</summary>
        public void SetGeometryDirty()
        {
            if (_geometryDirty) return;
            _geometryDirty = true;
        }

        /// <summary>
        /// Subclasses override to provide natural content size.
        /// </summary>
        protected abstract override Vector2 OnMeasure(Vector2 available);

        /// <summary>
        /// Subclasses override to rebuild geometry for the given content rectangle.
        /// </summary>
        protected abstract void OnRebuildGeometry(Rectangle content, GeometryBuffer buffer);

        protected override void OnArrange(Rectangle content)
        {
            // If content size changed, mark geometry dirty
            var newSize = new Vector2(content.Width, content.Height);
            if (newSize != _cachedContentSize)
            {
                _cachedContentSize = newSize;
                SetGeometryDirty();
            }
        }

        protected override void OnDraw(IGuiRenderer renderer)
        {
            var content = ContentBounds;
            if (content.Width <= 0 || content.Height <= 0)
                return;

            // Rebuild geometry if dirty
            if (_geometryDirty)
            {
                if (_cachedGeometry == null)
                    _cachedGeometry = GeometryBuffer.Rent();
                else
                    _cachedGeometry.Clear();

                OnRebuildGeometry(content, _cachedGeometry);
                _geometryDirty = false;
            }

            // Draw
            if (_cachedGeometry != null && _cachedGeometry.Count > 0)
            {
                renderer.DrawGeometry(_cachedGeometry, _color);
            }
        }

        /// <summary>
        /// Geometry rebuild count for testing (verifies dirty-tracking works).
        /// </summary>
        public int GeometryRebuildCount { get; private set; }

        /// <summary>Increment the rebuild counter (call in OnRebuildGeometry for test assertions).</summary>
        protected void IncrementRebuildCount() => GeometryRebuildCount++;
    }
}
