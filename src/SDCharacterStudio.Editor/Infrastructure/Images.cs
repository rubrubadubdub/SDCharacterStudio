using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SDCharacterStudio.Editor.Infrastructure;

/// <summary>Frozen, cached bitmaps for export PNGs. Frozen images can be built on any thread and shown on the UI thread.</summary>
public static class Images
{
    private static readonly ConcurrentDictionary<string, BitmapSource?> Files = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, BitmapSource?> Crops = new(StringComparer.OrdinalIgnoreCase);

    public static BitmapSource? Load(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        return Files.GetOrAdd(path, p =>
        {
            try
            {
                if (!File.Exists(p)) return null;
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                image.UriSource = new Uri(p);
                image.EndInit();
                // 96 dpi so one image pixel is one device-independent unit; pixel art then scales by integers.
                BitmapSource result = image.DpiX is 96 or 0 ? image : ToDpi96(image);
                result.Freeze();
                return result;
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException) { return null; }
        });
    }

    public static BitmapSource? Crop(string? path, int x, int y, int width, int height)
    {
        if (string.IsNullOrEmpty(path) || width <= 0 || height <= 0) return null;
        return Crops.GetOrAdd($"{path}|{x},{y},{width},{height}", _ =>
        {
            var source = Load(path);
            if (source == null || x < 0 || y < 0 || x + width > source.PixelWidth || y + height > source.PixelHeight) return null;
            var crop = new CroppedBitmap(source, new Int32Rect(x, y, width, height));
            crop.Freeze();
            return crop;
        });
    }

    /// <summary>Frame <paramref name="index"/> of a sheet laid out left to right, top to bottom (doc 05 §3.1).</summary>
    public static BitmapSource? Frame(string? path, int frameWidth, int frameHeight, int index)
    {
        var source = Load(path);
        if (source == null || frameWidth <= 0 || frameHeight <= 0) return null;
        int columns = Math.Max(1, source.PixelWidth / frameWidth);
        return Crop(path, index % columns * frameWidth, index / columns * frameHeight, frameWidth, frameHeight);
    }

    public static void Clear() { Files.Clear(); Crops.Clear(); }

    private static BitmapSource ToDpi96(BitmapSource source)
    {
        int stride = source.PixelWidth * 4;
        var pixels = new byte[stride * source.PixelHeight];
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        converted.CopyPixels(pixels, stride, 0);
        return BitmapSource.Create(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
    }
}
