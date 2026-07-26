using System;
using System.IO;
using System.Text;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;

namespace MaterialLib;

/// <summary>
/// Loader for Radiance .hdr (RGBE) environment maps.
/// Decodes new-style adaptive RLE and packs pixels into HalfVector4 for HDR GPU sampling.
/// </summary>
public static class HdriLoader
{
    /// <summary>Load a Radiance .hdr file as a HalfVector4 Texture2D.</summary>
    /// <param name="mipMap">Enable auto-generated mipmaps (desirable for IBL prefiltering).</param>
    public static Texture2D Load(GraphicsDevice device, string path, bool mipMap)
    {
        byte[] data = File.ReadAllBytes(path);

        // ── 1. Parse ASCII header ────────────────────────────────────────────
        int pos = 0;
        while (pos < data.Length)
        {
            int nl = IndexOf(data, (byte)'\n', pos);
            if (nl < 0) throw new InvalidDataException("HDR: no header terminator");
            string line = Encoding.ASCII.GetString(data, pos, nl - pos).TrimEnd('\r');
            pos = nl + 1;
            if (line.Length == 0)
                break; // blank line ends header
        }

        // ── 2. Parse resolution line ─────────────────────────────────────────
        int resNl = IndexOf(data, (byte)'\n', pos);
        if (resNl < 0) throw new InvalidDataException("HDR: missing resolution line");
        string resLine = Encoding.ASCII.GetString(data, pos, resNl - pos).TrimEnd('\r');
        pos = resNl + 1;

        int width = 0, height = 0;
        string[] parts = resLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i] == "-Y") height = int.Parse(parts[i + 1]);
            if (parts[i] == "+X") width  = int.Parse(parts[i + 1]);
        }

        if (width <= 0 || height <= 0)
            throw new InvalidDataException($"HDR: could not parse resolution from '{resLine}'");

        Console.WriteLine($"  HDR: {width}×{height} ({Path.GetFileName(path)})");

        // ── 3. Decode pixels ─────────────────────────────────────────────────
        var pixels = new HalfVector4[width * height];
        DecodePixels(data, pos, width, height, pixels);

        // ── 4. Create texture ────────────────────────────────────────────────
        var tex = new Texture2D(device, width, height, mipMap, SurfaceFormat.HalfVector4);
        tex.SetData(pixels);
        return tex;
    }

    // ── RLE decode ────────────────────────────────────────────────────────────

    private static void DecodePixels(byte[] data, int offset, int width, int height,
        HalfVector4[] pixels)
    {
        int pos = offset;
        // Temporary scanline buffers (4 channels × width bytes each)
        byte[] rChan = new byte[width];
        byte[] gChan = new byte[width];
        byte[] bChan = new byte[width];
        byte[] eChan = new byte[width];
        // Work buffer for RGBE→float per scanline
        var scanline = new HalfVector4[width];

        for (int y = 0; y < height; y++)
        {
            if (pos + 4 > data.Length)
                throw new InvalidDataException($"HDR: truncated at scanline {y}");

            // Detect RLE style: new-style starts with 0x02 0x02 + width (BE)
            bool newStyle = data[pos] == 0x02 && data[pos + 1] == 0x02;
            int scanWidth = newStyle
                ? (data[pos + 2] << 8) | data[pos + 3]
                : width;

            if (scanWidth != width)
                throw new InvalidDataException(
                    $"HDR: scanline {y} width mismatch: expected {width}, got {scanWidth}");

            if (newStyle)
            {
                pos += 4;
                DecodeRleChannel(data, ref pos, rChan, width);
                DecodeRleChannel(data, ref pos, gChan, width);
                DecodeRleChannel(data, ref pos, bChan, width);
                DecodeRleChannel(data, ref pos, eChan, width);
            }
            else
            {
                // Old-style: raw RGBE quads, no RLE
                for (int x = 0; x < width; x++)
                {
                    rChan[x] = data[pos++];
                    gChan[x] = data[pos++];
                    bChan[x] = data[pos++];
                    eChan[x] = data[pos++];
                }
            }

            // Convert RGBE → HalfVector4 for this scanline
            RgbeToPixels(rChan, gChan, bChan, eChan, width, scanline);

            // Radiance -Y means "Y axis points downward": scanline 0 = top,
            // same convention as Texture2D (row 0 = top).  No flip needed.
            int destY = y;
            Array.Copy(scanline, 0, pixels, destY * width, width);
        }
    }

    /// <summary>Decode one channel's RLE stream into dest[0..width).</summary>
    private static void DecodeRleChannel(byte[] data, ref int pos, byte[] dest, int width)
    {
        int j = 0;
        while (j < width)
        {
            int code = data[pos++];
            if (code > 128)
            {
                // Run-length: repeat next byte (code - 128) times
                int count = code - 128;
                byte val = data[pos++];
                int end = Math.Min(j + count, width);
                while (j < end)
                    dest[j++] = val;
            }
            else
            {
                // Literal run: copy next `code` bytes
                int count = code;
                int end = Math.Min(j + count, width);
                while (j < end)
                    dest[j++] = data[pos++];
            }
        }
    }

    // ── RGBE-to-float conversion ──────────────────────────────────────────────

    private static void RgbeToPixels(byte[] r, byte[] g, byte[] b, byte[] e,
        int width, HalfVector4[] dest)
    {
        for (int x = 0; x < width; x++)
        {
            int exp = e[x];
            if (exp == 0)
            {
                dest[x] = new HalfVector4(0, 0, 0, 1);
            }
            else
            {
                // Standard Radiance decode: value = (mantissa + 0.5) * 2^(exp-128) / 256
                //  = (mantissa + 0.5) * 2^(exp - 136)
                float scale = MathF.Pow(2.0f, exp - 136.0f);
                dest[x] = new HalfVector4(
                    (r[x] + 0.5f) * scale,
                    (g[x] + 0.5f) * scale,
                    (b[x] + 0.5f) * scale,
                    1.0f);
            }
        }
    }

    // ── Utility ───────────────────────────────────────────────────────────────

    private static int IndexOf(byte[] data, byte value, int start)
    {
        for (int i = start; i < data.Length; i++)
            if (data[i] == value)
                return i;
        return -1;
    }
}
