using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNA.Gui
{
    /// <summary>
    /// Describes a 9-slice (nine-patch) scalable image.
    /// The Border values define the left, top, right, bottom pixel margins
    /// that remain fixed during scaling. Corners stay original size, edges
    /// stretch along one axis, the center stretches along both axes.
    /// </summary>
    public class NineSlice
    {
        /// <summary>The source texture.</summary>
        public Texture2D Texture { get; }

        /// <summary>The fixed border widths (left, top, right, bottom) in texture pixels.</summary>
        public Thickness Border { get; }

        /// <summary>Optional source rectangle within the texture. If null, uses the whole texture.</summary>
        public Rectangle? SourceRect { get; }

        public NineSlice(Texture2D texture, Thickness border, Rectangle? sourceRect = null)
        {
            Texture = texture;
            Border = border;
            SourceRect = sourceRect;
        }
    }
}
