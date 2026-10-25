using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SDCharacterStudio.Core.Bridge;
using SDCharacterStudio.Core.Compositing;

namespace SDCharacterStudio.Editor.Studio;

/// <summary>
/// Pixel images for the Character Studio. Everything is straight-alpha <see cref="RgbaImage"/> decoded with Core's exact
/// PNG decoder, so reserved slot colours (doc 05 §4.1) survive bit for bit; WPF only displays the results.
/// </summary>
public static class StudioImages
{
    private static readonly Dictionary<string, (DateTime Stamp, RgbaImage Image)> files = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A PNG on disk (Bridge export), cached until it changes. Null when missing or unreadable.</summary>
    public static RgbaImage? LoadFile(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        var stamp = File.GetLastWriteTimeUtc(path);
        lock (files)
            if (files.TryGetValue(path, out var cached) && cached.Stamp == stamp) return cached.Image;
        try
        {
            var image = PngDecoder.Decode(File.ReadAllBytes(path));
            lock (files) files[path] = (stamp, image);
            return image;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { return null; }
    }

    public static void Clear() { lock (files) files.Clear(); }

    public static RgbaImage Decode(byte[] bytes) => PngDecoder.Decode(bytes);
    public static byte[] Encode(RgbaImage image) => PngEncoder.Encode(image);
    public static RgbaImage Blank(int width, int height) => new(width, height, new uint[width * height]);
    public static RgbaImage Clone(RgbaImage image) => new(image.Width, image.Height, (uint[])image.Pixels.Clone());

    public static RgbaImage Crop(RgbaImage source, int x, int y, int width, int height)
    {
        var result = Blank(width, height);
        for (int row = 0; row < height; row++)
            for (int column = 0; column < width; column++)
            {
                int sx = x + column, sy = y + row;
                if (sx >= 0 && sy >= 0 && sx < source.Width && sy < source.Height) result.Pixels[row * width + column] = source.Pixels[sy * source.Width + sx];
            }
        return result;
    }

    /// <summary>Frame <paramref name="index"/> of a sheet laid out left to right, top to bottom (doc 05 §3.1).</summary>
    public static RgbaImage? Frame(RgbaImage sheet, int width, int height, int index)
    {
        if (width <= 0 || height <= 0 || sheet.Width < width) return null;
        int columns = sheet.Width / width;
        int x = index % columns * width, y = index / columns * height;
        return y + height > sheet.Height || index < 0 ? null : Crop(sheet, x, y, width, height);
    }

    public static RgbaImage FlipHorizontal(RgbaImage image)
    {
        var result = Clone(image);
        for (int row = 0; row < image.Height; row++) Array.Reverse(result.Pixels, row * image.Width, image.Width);
        return result;
    }

    /// <summary>A clearly-wrong stand-in for art that couldn't be resolved: magenta and black checks.</summary>
    public static RgbaImage Missing(int width, int height)
    {
        var image = Blank(Math.Max(1, width), Math.Max(1, height));
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
                image.Pixels[y * image.Width + x] = ((x / 2 + y / 2) & 1) == 0 ? 0xA0FF00FFu : 0xA0000000u;
        return image;
    }

    public static bool IsEmpty(RgbaImage image) => image.Pixels.All(p => p >> 24 == 0);

    /// <summary>A frozen WPF bitmap of the pixels (Bgra32 is straight alpha, matching RgbaImage's packing).</summary>
    public static BitmapSource ToBitmap(RgbaImage image)
    {
        var bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, image.Pixels, image.Width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Scale up by an integer factor (thumbnails, nearest neighbour).</summary>
    public static RgbaImage Scale(RgbaImage image, int factor)
    {
        if (factor <= 1) return image;
        var result = Blank(image.Width * factor, image.Height * factor);
        for (int y = 0; y < result.Height; y++)
            for (int x = 0; x < result.Width; x++)
                result.Pixels[y * result.Width + x] = image.Pixels[y / factor * image.Width + x / factor];
        return result;
    }

    public static uint WithAlpha(uint color, byte alpha) => alpha == 0 ? 0 : (uint)alpha << 24 | (color & 0x00FFFFFF);
    public static Color ToColor(uint argb) => Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
    public static uint FromColor(Color color) => color.A == 0 ? 0 : (uint)color.A << 24 | (uint)color.R << 16 | (uint)color.G << 8 | color.B;
    public static string Hex(uint argb) => $"#{argb & 0xFFFFFF:X6}";
}
