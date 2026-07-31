using System;
using System.IO;
using System.Text;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;

namespace SceneRenderer;

/// <summary>
/// Loader for Radiance .hdr (RGBE) environment maps.
/// (Copied from MaterialLib.)
/// </summary>
public static class HdriLoader
{
    public static Texture2D Load(GraphicsDevice device, string path, bool mipMap)
    {
        byte[] data = File.ReadAllBytes(path);

        int pos = 0;
        while (pos < data.Length)
        {
            int nl = IndexOf(data, (byte)'\n', pos);
            if (nl < 0) throw new InvalidDataException("HDR: no header terminator");
            string line = Encoding.ASCII.GetString(data, pos, nl - pos).TrimEnd('\r');
            pos = nl + 1;
            if (line.Length == 0) break;
        }

        int resNl = IndexOf(data, (byte)'\n', pos);
        if (resNl < 0) throw new InvalidDataException("HDR: missing resolution line");
        string resLine = Encoding.ASCII.GetString(data, pos, resNl - pos).TrimEnd('\r');
        pos = resNl + 1;

        int width = 0, height = 0;
        string[] parts = resLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i] == "-Y") height = int.Parse(parts[i + 1]);
            if (parts[i] == "+X") width = int.Parse(parts[i + 1]);
        }

        var pixels = new HalfVector4[width * height];
        DecodePixels(data, pos, width, height, pixels);

        var tex = new Texture2D(device, width, height, mipMap, SurfaceFormat.HalfVector4);
        tex.SetData(pixels);
        return tex;
    }

    private static void DecodePixels(byte[] data, int offset, int width, int height,
        HalfVector4[] pixels)
    {
        int pos = offset;
        byte[] rChan = new byte[width];
        byte[] gChan = new byte[width];
        byte[] bChan = new byte[width];
        byte[] eChan = new byte[width];
        var scanline = new HalfVector4[width];

        for (int y = 0; y < height; y++)
        {
            if (pos + 4 > data.Length) break;

            bool newStyle = data[pos] == 0x02 && data[pos + 1] == 0x02;
            int scanWidth = newStyle ? (data[pos + 2] << 8) | data[pos + 3] : width;
            if (scanWidth != width)
                throw new InvalidDataException($"HDR: scanline {y} width mismatch");

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
                for (int x = 0; x < width; x++)
                {
                    rChan[x] = data[pos++]; gChan[x] = data[pos++];
                    bChan[x] = data[pos++]; eChan[x] = data[pos++];
                }
            }

            RgbeToPixels(rChan, gChan, bChan, eChan, width, scanline);
            Array.Copy(scanline, 0, pixels, y * width, width);
        }
    }

    private static void DecodeRleChannel(byte[] data, ref int pos, byte[] dest, int width)
    {
        int j = 0;
        while (j < width)
        {
            int code = data[pos++];
            if (code > 128)
            {
                int count = code - 128;
                byte val = data[pos++];
                int end = Math.Min(j + count, width);
                while (j < end) dest[j++] = val;
            }
            else
            {
                int count = code;
                int end = Math.Min(j + count, width);
                while (j < end) dest[j++] = data[pos++];
            }
        }
    }

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
                float scale = MathF.Pow(2.0f, exp - 136.0f);
                dest[x] = new HalfVector4(
                    (r[x] + 0.5f) * scale,
                    (g[x] + 0.5f) * scale,
                    (b[x] + 0.5f) * scale, 1.0f);
            }
        }
    }

    private static int IndexOf(byte[] data, byte value, int start)
    {
        for (int i = start; i < data.Length; i++)
            if (data[i] == value) return i;
        return -1;
    }
}
