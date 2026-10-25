using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using SDCharacterStudio.Core.Compositing;

namespace SDCharacterStudio.Core.Bridge;

/// <summary>
/// Decodes any standard PNG (all colour types and bit depths, palettes, tRNS, Adam7 interlacing) into straight-alpha
/// <see cref="RgbaImage"/> pixels, byte for byte, without colour management. Reserved slot colours (doc 05 §4.1) must
/// survive exactly, so editors never use a decoder that applies gamma or colour profiles.
/// </summary>
public static class PngDecoder
{
    private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    public static bool IsPng(ReadOnlySpan<byte> data) => data.Length >= 8 && data[..8].SequenceEqual(Signature);

    public static RgbaImage Decode(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!IsPng(data)) throw new InvalidDataException("Not a PNG file.");
        int width = 0, height = 0, depth = 0, colorType = -1, interlace = 0;
        byte[]? palette = null, transparency = null;
        using var idat = new MemoryStream();
        int offset = 8;
        while (offset + 8 <= data.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset));
            string type = Encoding.ASCII.GetString(data, offset + 4, 4);
            if (length < 0 || offset + 12L + length > data.Length) throw new InvalidDataException("Truncated PNG chunk.");
            var body = data.AsSpan(offset + 8, length);
            switch (type)
            {
                case "IHDR":
                    width = BinaryPrimitives.ReadInt32BigEndian(body); height = BinaryPrimitives.ReadInt32BigEndian(body[4..]);
                    depth = body[8]; colorType = body[9]; interlace = body[12];
                    if (body[10] != 0 || body[11] != 0) throw new InvalidDataException("Unsupported PNG compression or filter method.");
                    break;
                case "PLTE": palette = body.ToArray(); break;
                case "tRNS": transparency = body.ToArray(); break;
                case "IDAT": idat.Write(body); break;
                case "IEND": offset = data.Length; continue;
            }
            offset += 12 + length;
        }
        if (width <= 0 || height <= 0 || (long)width * height > int.MaxValue / 4) throw new InvalidDataException("Invalid PNG dimensions.");
        int channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => throw new InvalidDataException($"Unsupported PNG colour type {colorType}.") };
        bool validDepth = colorType switch { 0 => depth is 1 or 2 or 4 or 8 or 16, 3 => depth is 1 or 2 or 4 or 8, _ => depth is 8 or 16 };
        if (!validDepth) throw new InvalidDataException($"Unsupported PNG bit depth {depth} for colour type {colorType}.");
        if (colorType == 3 && palette == null) throw new InvalidDataException("Palette PNG without PLTE.");

        byte[] raw;
        idat.Position = 0;
        using (var zlib = new ZLibStream(idat, CompressionMode.Decompress))
        using (var inflated = new MemoryStream())
        {
            zlib.CopyTo(inflated);
            raw = inflated.ToArray();
        }

        var pixels = new uint[width * height];
        int bitsPerPixel = channels * depth;
        int bytesPerPixel = Math.Max(1, bitsPerPixel / 8);
        int position = 0;
        var passes = interlace == 1
            ? new[] { (0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2) }
            : new[] { (0, 0, 1, 1) };
        foreach (var (x0, y0, dx, dy) in passes)
        {
            int passWidth = (width - x0 + dx - 1) / dx, passHeight = (height - y0 + dy - 1) / dy;
            if (passWidth <= 0 || passHeight <= 0) continue;
            int stride = (passWidth * bitsPerPixel + 7) / 8;
            var previous = new byte[stride];
            var current = new byte[stride];
            for (int row = 0; row < passHeight; row++)
            {
                if (position + 1 + stride > raw.Length) throw new InvalidDataException("Truncated PNG image data.");
                byte filter = raw[position];
                Array.Copy(raw, position + 1, current, 0, stride);
                position += 1 + stride;
                Unfilter(filter, current, previous, bytesPerPixel);
                for (int column = 0; column < passWidth; column++)
                    pixels[(y0 + row * dy) * width + x0 + column * dx] = Pixel(current, column, colorType, depth, palette, transparency);
                (previous, current) = (current, previous);
            }
        }
        return new RgbaImage(width, height, pixels);
    }

    private static void Unfilter(byte filter, byte[] line, byte[] previous, int bpp)
    {
        for (int i = 0; i < line.Length; i++)
        {
            int left = i >= bpp ? line[i - bpp] : 0, up = previous[i], upLeft = i >= bpp ? previous[i - bpp] : 0;
            line[i] = filter switch
            {
                0 => line[i],
                1 => (byte)(line[i] + left),
                2 => (byte)(line[i] + up),
                3 => (byte)(line[i] + ((left + up) >> 1)),
                4 => (byte)(line[i] + Paeth(left, up, upLeft)),
                _ => throw new InvalidDataException($"Unknown PNG filter {filter}.")
            };
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static uint Pixel(byte[] line, int x, int colorType, int depth, byte[]? palette, byte[]? trns)
    {
        int Sample(int channel, int channels)
        {
            if (depth == 8) return line[x * channels + channel];
            if (depth == 16) return line[(x * channels + channel) * 2] << 8 | line[(x * channels + channel) * 2 + 1];
            int bit = x * depth;   // sub-byte depths only occur with one channel
            return (line[bit >> 3] >> (8 - depth - (bit & 7))) & ((1 << depth) - 1);
        }
        int To8(int value) => depth switch { 16 => value >> 8, 8 => value, 4 => value * 17, 2 => value * 85, 1 => value * 255, _ => value };
        uint Pack(int a, int r, int g, int b) => a == 0 ? 0u : (uint)a << 24 | (uint)r << 16 | (uint)g << 8 | (uint)b;
        switch (colorType)
        {
            case 0:
            {
                int v = Sample(0, 1);
                bool transparent = trns is { Length: >= 2 } && v == (trns[0] << 8 | trns[1]);
                int g = To8(v);
                return Pack(transparent ? 0 : 255, g, g, g);
            }
            case 2:
            {
                int r = Sample(0, 3), g = Sample(1, 3), b = Sample(2, 3);
                bool transparent = trns is { Length: >= 6 } && r == (trns[0] << 8 | trns[1]) && g == (trns[2] << 8 | trns[3]) && b == (trns[4] << 8 | trns[5]);
                return Pack(transparent ? 0 : 255, To8(r), To8(g), To8(b));
            }
            case 3:
            {
                int index = Sample(0, 1);
                if (index * 3 + 2 >= palette!.Length) throw new InvalidDataException("PNG palette index out of range.");
                int alpha = trns != null && index < trns.Length ? trns[index] : 255;
                return Pack(alpha, palette[index * 3], palette[index * 3 + 1], palette[index * 3 + 2]);
            }
            case 4:
            {
                int g = To8(Sample(0, 2));
                return Pack(To8(Sample(1, 2)), g, g, g);
            }
            default:
                return Pack(To8(Sample(3, 4)), To8(Sample(0, 4)), To8(Sample(1, 4)), To8(Sample(2, 4)));
        }
    }
}
