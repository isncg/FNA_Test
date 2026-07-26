using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNA.Gui
{
    /// <summary>
    /// Renderer abstraction for the GUI system.
    /// Decouples widget drawing from the specific rendering backend.
    /// Sole implementation: <see cref="SpriteBatchGuiRenderer"/>.
    /// </summary>
    public interface IGuiRenderer
    {
        /// <summary>Begin a render frame with the given view-projection transform.</summary>
        void Begin(Matrix transform);

        /// <summary>Push a scissor clip rectangle (intersects with current clip).</summary>
        void PushClip(Rectangle rect);

        /// <summary>Pop the last pushed clip rectangle.</summary>
        void PopClip();

        /// <summary>Draw a solid-color filled rectangle.</summary>
        void DrawRect(Rectangle rect, Color color);

        /// <summary>Draw a textured rectangle with optional source region.</summary>
        void DrawTexture(Texture2D texture, Rectangle destination, Rectangle? source, Color tint);

        /// <summary>Draw a 9-slice image scaled to the destination rectangle.</summary>
        void DrawNineSlice(NineSlice slice, Rectangle destination, Color tint);

        /// <summary>Submit a geometry buffer of quads for rendering.</summary>
        void DrawGeometry(GeometryBuffer geometry, Color tint);

        /// <summary>
        /// Submit SDF text for batched rendering. Accumulated across widgets
        /// and flushed in End() via the dedicated SDF effect pass.
        /// </summary>
        void DrawSdfText(SdfFont font, string text, Vector2 position, Color color, float scale);

        /// <summary>End the render frame and flush all batches.</summary>
        void End();
    }
}
