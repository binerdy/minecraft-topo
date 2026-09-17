using System.Buffers.Binary;
using System.IO.Compression;

namespace MinecraftTopo.Core.Imaging;

/// <summary>A decoded image as 8-bit RGBA.</summary>
public sealed class RgbaImage
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }

    public RgbaImage(int width, int height, byte[] pixels)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public (byte R, byte G, byte B, byte A) this[int x, int y]
    {
        get
        {
            int i = (y * Width + x) * 4;
            return (Pixels[i], Pixels[i + 1], Pixels[i + 2], Pixels[i + 3]);
        }
    }
}

/// <summary>
/// Minimal PNG decoder (no dependencies): 8-bit, non-interlaced, colour types greyscale, RGB,
/// palette, greyscale+alpha and RGBA. Enough for map server output.
/// </summary>
public static class PngReader
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static RgbaImage Decode(ReadOnlySpan<byte> png)
    {
        if (png.Length < 8 || !png[..8].SequenceEqual(Signature)) throw new InvalidDataException("Not a PNG file.");

        int width = 0, height = 0, bitDepth = 0, colourType = 0, interlace = 0;
        byte[]? palette = null;
        byte[]? trns = null;
        using var idat = new MemoryStream();

        int pos = 8;
        while (pos + 8 <= png.Length)
        {
            int len = BinaryPrimitives.ReadInt32BigEndian(png.Slice(pos, 4));
            string type = System.Text.Encoding.ASCII.GetString(png.Slice(pos + 4, 4));
            if (len < 0 || pos + 12 + len > png.Length) throw new InvalidDataException("Corrupt PNG chunk.");
            var data = png.Slice(pos + 8, len);
            switch (type)
            {
                case "IHDR":
                    width = BinaryPrimitives.ReadInt32BigEndian(data);
                    height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                    bitDepth = data[8];
                    colourType = data[9];
                    interlace = data[12];
                    break;
                case "PLTE":
                    palette = data.ToArray();
                    break;
                case "tRNS":
                    trns = data.ToArray();
                    break;
                case "IDAT":
                    idat.Write(data);
                    break;
                case "IEND":
                    pos = png.Length;
                    continue;
            }
            pos += 12 + len;
        }

        if (width <= 0 || height <= 0) throw new InvalidDataException("PNG has no IHDR.");
        if (bitDepth != 8) throw new NotSupportedException($"PNG bit depth {bitDepth} is not supported (only 8).");
        if (interlace != 0) throw new NotSupportedException("Interlaced PNGs are not supported.");
        int channels = colourType switch
        {
            0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4,
            _ => throw new NotSupportedException($"PNG colour type {colourType} is not supported."),
        };

        // Inflate.
        idat.Position = 0;
        int stride = width * channels;
        var raw = new byte[(stride + 1) * height];
        using (var z = new ZLibStream(idat, CompressionMode.Decompress))
        {
            z.ReadExactly(raw);
        }

        // Unfilter in place into a tightly packed buffer.
        var data2 = new byte[stride * height];
        for (int y = 0; y < height; y++)
        {
            byte filter = raw[y * (stride + 1)];
            int src = y * (stride + 1) + 1;
            int dst = y * stride;
            for (int x = 0; x < stride; x++)
            {
                int a = x >= channels ? data2[dst + x - channels] : 0;
                int b = y > 0 ? data2[dst - stride + x] : 0;
                int c = x >= channels && y > 0 ? data2[dst - stride + x - channels] : 0;
                int v = raw[src + x];
                v += filter switch
                {
                    0 => 0,
                    1 => a,
                    2 => b,
                    3 => (a + b) >> 1,
                    4 => Paeth(a, b, c),
                    _ => throw new InvalidDataException($"Unknown PNG filter {filter}."),
                };
                data2[dst + x] = (byte)v;
            }
        }

        // Expand to RGBA.
        var rgba = new byte[width * height * 4];
        for (int i = 0, o = 0; i < width * height; i++, o += 4)
        {
            int s = i * channels;
            switch (colourType)
            {
                case 0:
                    rgba[o] = rgba[o + 1] = rgba[o + 2] = data2[s];
                    rgba[o + 3] = trns is { Length: >= 2 } && data2[s] == trns[1] ? (byte)0 : (byte)255;
                    break;
                case 2:
                    rgba[o] = data2[s]; rgba[o + 1] = data2[s + 1]; rgba[o + 2] = data2[s + 2];
                    rgba[o + 3] = trns is { Length: >= 6 } && data2[s] == trns[1] && data2[s + 1] == trns[3] && data2[s + 2] == trns[5] ? (byte)0 : (byte)255;
                    break;
                case 3:
                    {
                        int idx = data2[s];
                        if (palette is null || idx * 3 + 2 >= palette.Length) throw new InvalidDataException("PNG palette index out of range.");
                        rgba[o] = palette[idx * 3]; rgba[o + 1] = palette[idx * 3 + 1]; rgba[o + 2] = palette[idx * 3 + 2];
                        rgba[o + 3] = trns is not null && idx < trns.Length ? trns[idx] : (byte)255;
                        break;
                    }
                case 4:
                    rgba[o] = rgba[o + 1] = rgba[o + 2] = data2[s];
                    rgba[o + 3] = data2[s + 1];
                    break;
                case 6:
                    rgba[o] = data2[s]; rgba[o + 1] = data2[s + 1]; rgba[o + 2] = data2[s + 2]; rgba[o + 3] = data2[s + 3];
                    break;
            }
        }
        return new RgbaImage(width, height, rgba);
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
