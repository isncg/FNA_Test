using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNA.Gui
{
    /// <summary>
    /// A single textured, colored quad for the unified geometry buffer.
    /// All fields are value types for pooling and zero-GC operation.
    /// </summary>
    public struct GraphicQuad
    {
        /// <summary>Top-left position in logical pixels.</summary>
        public Vector2 Position;

        /// <summary>Width and height in logical pixels.</summary>
        public Vector2 Size;

        /// <summary>UV top-left.</summary>
        public Vector2 UV0;

        /// <summary>UV bottom-right.</summary>
        public Vector2 UV1;

        /// <summary>Per-quad color (multiplied with tint at draw time).</summary>
        public Color Color;

        /// <summary>The texture this quad samples from, or null for solid color.</summary>
        public Texture2D? Texture;

        public GraphicQuad(
            Vector2 position, Vector2 size,
            Vector2 uv0, Vector2 uv1,
            Color color, Texture2D? texture = null)
        {
            Position = position;
            Size = size;
            UV0 = uv0;
            UV1 = uv1;
            Color = color;
            Texture = texture;
        }

        /// <summary>Create a solid-color quad (uses 1x1 white texture at render time).</summary>
        public static GraphicQuad Solid(Vector2 position, Vector2 size, Color color) =>
            new(position, size, Vector2.Zero, Vector2.One, color, null);

        /// <summary>Create a textured quad with full UV range.</summary>
        public static GraphicQuad Textured(
            Vector2 position, Vector2 size, Texture2D texture, Color color) =>
            new(position, size, Vector2.Zero, Vector2.One, color, texture);

        /// <summary>Create a textured quad with sub-region UV coordinates.</summary>
        public static GraphicQuad TexturedRegion(
            Vector2 position, Vector2 size,
            Texture2D texture,
            Rectangle source, Color color) =>
            new(
                position, size,
                new Vector2((float)source.X / texture.Width, (float)source.Y / texture.Height),
                new Vector2((float)(source.X + source.Width) / texture.Width,
                            (float)(source.Y + source.Height) / texture.Height),
                color, texture);
    }
}
