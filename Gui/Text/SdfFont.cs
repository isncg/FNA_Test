using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNA.Gui
{
    /// <summary>Single glyph metrics in atlas space.</summary>
    public struct GlyphInfo
    {
        /// <summary>Pixel X position in the atlas (left edge).</summary>
        public int X;
        /// <summary>Pixel Y position in the atlas (top edge, PNG convention).</summary>
        public int Y;
        /// <summary>Width in atlas pixels.</summary>
        public int W;
        /// <summary>Height in atlas pixels.</summary>
        public int H;

        /// <summary>Horizontal offset from origin to left of glyph cell, in pixels.</summary>
        public float OffsetX;
        /// <summary>Vertical offset from baseline up to top of glyph cell, in pixels.</summary>
        public float OffsetY;
        /// <summary>Advance width, in pixels.</summary>
        public float Advance;

        /// <summary>Normalized UV: left edge.</summary>
        public float U0;
        /// <summary>Normalized UV: top edge.</summary>
        public float V0;
        /// <summary>Normalized UV: right edge.</summary>
        public float U1;
        /// <summary>Normalized UV: bottom edge.</summary>
        public float V1;
    }

    /// <summary>
    /// SDF font loaded from msdf-atlas-gen output (PNG atlas + JSON metrics).
    /// Implements <see cref="IFontProvider"/> for use in the GUI text system.
    /// </summary>
    public class SdfFont : IFontProvider, IDisposable
    {
        public Texture2D Atlas { get; private set; } = null!;
        public Dictionary<int, GlyphInfo> Glyphs { get; private set; } = null!;
        public float FontSize { get; private set; }
        public float LineHeight { get; private set; }
        public float Ascender { get; private set; }
        public float Descender { get; private set; }
        public int AtlasWidth { get; private set; }
        public int AtlasHeight { get; private set; }

        /// <summary>
        /// Load an SDF font from PNG bytes + msdf-atlas-gen JSON metrics string.
        /// </summary>
        public static SdfFont Load(GraphicsDevice device,
            byte[] atlasPngBytes, string metricsJson)
        {
            using var doc = JsonDocument.Parse(metricsJson);
            var root = doc.RootElement;

            var atlas = root.GetProperty("atlas");
            int atlasW = atlas.GetProperty("width").GetInt32();
            int atlasH = atlas.GetProperty("height").GetInt32();
            float size = atlas.GetProperty("size").GetSingle();

            var metrics = root.GetProperty("metrics");
            float lineHeight = metrics.GetProperty("lineHeight").GetSingle();
            float ascender = metrics.GetProperty("ascender").GetSingle();
            float descender = metrics.GetProperty("descender").GetSingle();

            using var pngStream = new MemoryStream(atlasPngBytes);
            var atlasTex = Texture2D.FromStream(device, pngStream);

            var glyphs = new Dictionary<int, GlyphInfo>();
            var glyphsArr = root.GetProperty("glyphs");
            float pixelScale = size;

            foreach (var g in glyphsArr.EnumerateArray())
            {
                int unicode = g.GetProperty("unicode").GetInt32();
                float advance = g.GetProperty("advance").GetSingle();

                if (!g.TryGetProperty("planeBounds", out var plane))
                {
                    glyphs[unicode] = new GlyphInfo { Advance = advance * pixelScale };
                    continue;
                }
                if (!g.TryGetProperty("atlasBounds", out var ab))
                    continue;

                float pl = plane.GetProperty("left").GetSingle();
                float pb = plane.GetProperty("bottom").GetSingle();
                float pr = plane.GetProperty("right").GetSingle();
                float pt = plane.GetProperty("top").GetSingle();

                float al = ab.GetProperty("left").GetSingle();
                float ab_bottom = ab.GetProperty("bottom").GetSingle();
                float ar = ab.GetProperty("right").GetSingle();
                float ab_top = ab.GetProperty("top").GetSingle();

                // msdf-atlas-gen uses bottom-origin for atlas coordinates.
                // Convert to PNG top-origin:
                int cellX = (int)Math.Floor(al);
                int cellY = (int)Math.Floor(atlasH - ab_top);
                int cellW = (int)Math.Ceiling(ar - al);
                int cellH = (int)Math.Ceiling(ab_top - ab_bottom);

                float ox = pl * pixelScale;
                float oy = pt * pixelScale;
                float adv = advance * pixelScale;

                glyphs[unicode] = new GlyphInfo
                {
                    X = cellX, Y = cellY, W = cellW, H = cellH,
                    OffsetX = ox, OffsetY = oy, Advance = adv,
                    U0 = al / atlasW,
                    V0 = (atlasH - ab_top) / atlasH,
                    U1 = ar / atlasW,
                    V1 = (atlasH - ab_bottom) / atlasH,
                };
            }

            return new SdfFont
            {
                Atlas = atlasTex,
                Glyphs = glyphs,
                FontSize = size,
                LineHeight = lineHeight * pixelScale,
                Ascender = ascender * pixelScale,
                Descender = descender * pixelScale,
                AtlasWidth = atlasW,
                AtlasHeight = atlasH,
            };
        }

        /// <summary>
        /// Load from simplified metrics format (flat glyph dict with pre-computed UVs).
        /// Compatible with the en_test_metrics.json format used by the SDFFontTest.
        /// </summary>
        public static SdfFont LoadSimple(GraphicsDevice device,
            byte[] atlasPngBytes, string metricsJson)
        {
            using var doc = JsonDocument.Parse(metricsJson);
            var root = doc.RootElement;

            int atlasW = root.GetProperty("atlasWidth").GetInt32();
            int atlasH = root.GetProperty("atlasHeight").GetInt32();
            float fontSize = root.GetProperty("fontSize").GetSingle();
            float lineHeight = root.GetProperty("lineHeight").GetSingle();
            float ascender = root.GetProperty("ascender").GetSingle();
            float descender = root.GetProperty("descender").GetSingle();

            using var pngStream = new MemoryStream(atlasPngBytes);
            var atlasTex = Texture2D.FromStream(device, pngStream);

            var glyphs = new Dictionary<int, GlyphInfo>();
            var glyphsObj = root.GetProperty("glyphs");

            foreach (var prop in glyphsObj.EnumerateObject())
            {
                int unicode = int.Parse(prop.Name);
                var g = prop.Value;

                glyphs[unicode] = new GlyphInfo
                {
                    X = g.GetProperty("x").GetInt32(),
                    Y = g.GetProperty("y").GetInt32(),
                    W = g.GetProperty("w").GetInt32(),
                    H = g.GetProperty("h").GetInt32(),
                    OffsetX = g.GetProperty("ox").GetSingle(),
                    OffsetY = g.GetProperty("oy").GetSingle(),
                    Advance = g.GetProperty("adv").GetSingle(),
                    U0 = g.GetProperty("u0").GetSingle(),
                    V0 = g.GetProperty("v0").GetSingle(),
                    U1 = g.GetProperty("u1").GetSingle(),
                    V1 = g.GetProperty("v1").GetSingle(),
                };
            }

            return new SdfFont
            {
                Atlas = atlasTex,
                Glyphs = glyphs,
                FontSize = fontSize,
                LineHeight = lineHeight,
                Ascender = ascender,
                Descender = descender,
                AtlasWidth = atlasW,
                AtlasHeight = atlasH,
            };
        }

        /// <summary>Load from embedded resources in the calling assembly.</summary>
        public static SdfFont LoadEmbedded(GraphicsDevice device,
            string pngResourceName, string metricsResourceName)
        {
            var asm = Assembly.GetCallingAssembly();
            var pngBytes = LoadEmbeddedBytes(asm, pngResourceName);
            var metricsJson = LoadEmbeddedString(asm, metricsResourceName);
            return Load(device, pngBytes, metricsJson);
        }

        public Vector2 MeasureString(string text, float scale)
        {
            float scaleFactor = scale / FontSize;
            float curX = 0;
            float maxX = 0;
            int lines = 1;

            foreach (char c in text)
            {
                if (c == '\n') { maxX = Math.Max(maxX, curX); curX = 0; lines++; continue; }
                if (c == '\r') continue;

                if (Glyphs.TryGetValue(c, out var g))
                    curX += g.Advance * scaleFactor;
            }

            maxX = Math.Max(maxX, curX);
            return new Vector2(maxX, lines * LineHeight * scaleFactor);
        }

        public static byte[] LoadEmbeddedBytes(Assembly asm, string resourceName)
        {
            using var stream = asm.GetManifestResourceStream(resourceName)
                ?? throw new FileNotFoundException(
                    $"Resource '{resourceName}' not found. " +
                    $"Available: {string.Join(", ", asm.GetManifestResourceNames())}");
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }

        public static string LoadEmbeddedString(Assembly asm, string resourceName)
            => System.Text.Encoding.UTF8.GetString(LoadEmbeddedBytes(asm, resourceName));

        public void Dispose() => Atlas?.Dispose();
    }
}
