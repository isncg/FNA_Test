using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNA.Test
{
    /// <summary>Procedural texture generation — no external assets needed.</summary>
    public static class TextureGen
    {
        /// <summary>Checkerboard pattern (2 colors, configurable square size).</summary>
        public static Texture2D Checkerboard(GraphicsDevice device, int size, int squareSize,
            Color colorA, Color colorB)
        {
            var data = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int cx = x / squareSize;
                int cy = y / squareSize;
                data[y * size + x] = ((cx + cy) % 2 == 0) ? colorA : colorB;
            }
            var tex = new Texture2D(device, size, size);
            tex.SetData(data);
            return tex;
        }

        /// <summary>1x1 solid white texture.</summary>
        public static Texture2D White(GraphicsDevice device)
        {
            var tex = new Texture2D(device, 1, 1);
            tex.SetData(new[] { Color.White });
            return tex;
        }

        /// <summary>Horizontal alpha gradient: alpha 0 (left) → 1 (right).</summary>
        public static Texture2D AlphaGradient(GraphicsDevice device, int width, int height,
            Color baseColor)
        {
            var data = new Color[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float a = (float)x / (width - 1);
                data[y * width + x] = new Color(
                    (int)(baseColor.R * a),
                    (int)(baseColor.G * a),
                    (int)(baseColor.B * a),
                    (int)(a * 255)
                );
            }
            var tex = new Texture2D(device, width, height);
            tex.SetData(data);
            return tex;
        }

        /// <summary>Radial gradient from center (white) to edge (black).</summary>
        public static Texture2D RadialGradient(GraphicsDevice device, int size)
        {
            var data = new Color[size * size];
            float center = size / 2f;
            float maxDist = center;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - center + 0.5f;
                float dy = y - center + 0.5f;
                float d = (float)Math.Sqrt(dx * dx + dy * dy) / maxDist;
                d = MathHelper.Clamp(1f - d, 0, 1);
                byte v = (byte)(d * 255);
                data[y * size + x] = new Color(v, v, v, 255);
            }
            var tex = new Texture2D(device, size, size);
            tex.SetData(data);
            return tex;
        }

        /// <summary>
        /// 6-face cubemap. Each face is a solid color with a white grid overlay.
        /// Face colors: +X=red, -X=cyan, +Y=green, -Y=magenta, +Z=blue, -Z=yellow.
        /// </summary>
        public static TextureCube CheckerCube(GraphicsDevice device, int size, int gridDiv)
        {
            Color[] faceColors = {
                Color.Red, Color.Cyan,       // +X, -X
                Color.Green, Color.Magenta,  // +Y, -Y
                Color.Blue, Color.Yellow     // +Z, -Z
            };

            var cube = new TextureCube(device, size, false, SurfaceFormat.Color);
            for (int face = 0; face < 6; face++)
            {
                var data = new Color[size * size];
                Color fc = faceColors[face];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    bool grid = (x % (size / gridDiv) < 2) || (y % (size / gridDiv) < 2);
                    data[y * size + x] = grid ? Color.White : fc;
                }
                cube.SetData((CubeMapFace)face, data);
            }
            return cube;
        }
    }
}
