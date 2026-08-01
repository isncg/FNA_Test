using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SceneRenderer;

/// <summary>
/// Loads material textures with the colour space and mip chain the deferred
/// pipeline expects. <c>Texture2D.FromStream</c> cannot do either: it always
/// produces a single-level <see cref="SurfaceFormat.Color"/> (linear UNORM)
/// texture, which meant sRGB-encoded albedo was fed to a linear pipeline and no
/// texture had mips for the anisotropic sampler to minify with.
///
/// Mirrors Unreal's per-texture sRGB flag: base colour is tagged sRGB so the
/// texture unit decodes it to linear during sampling, while normal maps and
/// packed masks stay linear because they carry data, not colour.
/// </summary>
public static class TextureLoader
{
    public enum Kind
    {
        /// <summary>Colour data stored with the sRGB transfer curve (albedo).</summary>
        SrgbColor,
        /// <summary>Tangent-space normal map, XYZ biased into [0,1].</summary>
        NormalMap,
        /// <summary>Linear scalar data such as the AO/Roughness/Metallic mask.</summary>
        LinearData,
    }

    /// <summary>
    /// Decodes an image file and uploads it with a full mip chain. Returns null
    /// when the file is absent so callers can fall back to a default texture.
    /// </summary>
    public static Texture2D? Load(GraphicsDevice device, string path, Kind kind,
        bool mipMap = true)
    {
        if (!File.Exists(path)) return null;

        byte[] level0;
        int width, height;
        try
        {
            using var stream = File.OpenRead(path);
            level0 = Texture2D.DecodeImageEXT(stream, out width, out height);
            if (level0 == null)
            {
                Console.WriteLine($"  Failed to decode: {Path.GetFileName(path)}");
                return null;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Failed: {ex.Message}");
            return null;
        }

        var format = kind == Kind.SrgbColor
            ? SurfaceFormat.ColorSrgbEXT
            : SurfaceFormat.Color;

        var texture = new Texture2D(device, width, height, mipMap, format);
        texture.SetData(0, null, level0, 0, level0.Length);

        if (!mipMap)
        {
            return texture;
        }

        /* Fill every remaining level: Texture2D allocated LevelCount levels and
         * anything left unwritten would sample as garbage.
         */
        var src = level0;
        int sw = width, sh = height;
        for (int level = 1; level < texture.LevelCount; level += 1)
        {
            int dw = Math.Max(1, sw / 2);
            int dh = Math.Max(1, sh / 2);
            var dst = Downsample(src, sw, sh, dw, dh, kind);
            texture.SetData(level, null, dst, 0, dst.Length);
            src = dst;
            sw = dw;
            sh = dh;
        }

        return texture;
    }

    /// <summary>
    /// Box-filters an RGBA8 image to half size. The averaging happens in the
    /// space that is meaningful for the data: linear light for sRGB colour,
    /// renormalised vectors for normal maps, raw values for scalar masks.
    /// </summary>
    private static byte[] Downsample(byte[] src, int sw, int sh, int dw, int dh, Kind kind)
    {
        var dst = new byte[dw * dh * 4];

        // Rows write disjoint ranges of dst, so this parallelises safely.
        System.Threading.Tasks.Parallel.For(0, dh, y =>
        {
            int y0 = Math.Min(y * 2, sh - 1);
            int y1 = Math.Min(y * 2 + 1, sh - 1);
            for (int x = 0; x < dw; x += 1)
            {
                int x0 = Math.Min(x * 2, sw - 1);
                int x1 = Math.Min(x * 2 + 1, sw - 1);

                int i00 = (y0 * sw + x0) * 4;
                int i01 = (y0 * sw + x1) * 4;
                int i10 = (y1 * sw + x0) * 4;
                int i11 = (y1 * sw + x1) * 4;
                int o = (y * dw + x) * 4;

                if (kind == Kind.SrgbColor)
                {
                    /* Averaging sRGB-encoded values would darken the result;
                     * decode, average in linear light, re-encode.
                     */
                    for (int c = 0; c < 3; c += 1)
                    {
                        float lin = (SrgbToLinear[src[i00 + c]] + SrgbToLinear[src[i01 + c]]
                                   + SrgbToLinear[src[i10 + c]] + SrgbToLinear[src[i11 + c]]) * 0.25f;
                        dst[o + c] = LinearToSrgb(lin);
                    }
                    dst[o + 3] = (byte)((src[i00 + 3] + src[i01 + 3]
                                       + src[i10 + 3] + src[i11 + 3]) / 4);
                }
                else if (kind == Kind.NormalMap)
                {
                    /* Averaging biased normals shortens them; decode to [-1,1],
                     * average, renormalise, re-encode. Unrolled on purpose: a
                     * stackalloc here would accumulate stack per pixel until the
                     * method returns and overflow on a large image.
                     */
                    float nx = (src[i00 + 0] + src[i01 + 0] + src[i10 + 0] + src[i11 + 0])
                             / (4f * 127.5f) - 1f;
                    float ny = (src[i00 + 1] + src[i01 + 1] + src[i10 + 1] + src[i11 + 1])
                             / (4f * 127.5f) - 1f;
                    float nz = (src[i00 + 2] + src[i01 + 2] + src[i10 + 2] + src[i11 + 2])
                             / (4f * 127.5f) - 1f;

                    float len = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                    if (len > 1e-6f)
                    {
                        nx /= len; ny /= len; nz /= len;
                    }
                    else
                    {
                        nx = 0; ny = 0; nz = 1;
                    }
                    dst[o + 0] = ToByte((nx + 1f) * 0.5f);
                    dst[o + 1] = ToByte((ny + 1f) * 0.5f);
                    dst[o + 2] = ToByte((nz + 1f) * 0.5f);
                    dst[o + 3] = 255;
                }
                else
                {
                    for (int c = 0; c < 4; c += 1)
                    {
                        dst[o + c] = (byte)((src[i00 + c] + src[i01 + c]
                                           + src[i10 + c] + src[i11 + c]) / 4);
                    }
                }
            }
        });

        return dst;
    }

    #region sRGB transfer curve

    private static readonly float[] SrgbToLinear = BuildSrgbToLinear();

    private static float[] BuildSrgbToLinear()
    {
        var table = new float[256];
        for (int i = 0; i < 256; i += 1)
        {
            float s = i / 255f;
            table[i] = s <= 0.04045f
                ? s / 12.92f
                : MathF.Pow((s + 0.055f) / 1.055f, 2.4f);
        }
        return table;
    }

    private static byte LinearToSrgb(float lin)
    {
        lin = Math.Clamp(lin, 0f, 1f);
        float s = lin <= 0.0031308f
            ? lin * 12.92f
            : 1.055f * MathF.Pow(lin, 1f / 2.4f) - 0.055f;
        return ToByte(s);
    }

    private static byte ToByte(float v) =>
        (byte)Math.Clamp(MathF.Round(v * 255f), 0f, 255f);

    #endregion
}
