using SDCharacterStudio.Core.Compositing;

namespace SDCharacterStudio.Core.Bridge;

/// <summary>Copies source rectangles using native point-filtered, clamped-edge texture sampling.</summary>
public static class TextureCrop
{
    public const int MaximumPixels = 16 * 1024 * 1024;

    public static RgbaImage PointClamp(RgbaImage source, int x, int y, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (width <= 0 || height <= 0 || (long)width * height > MaximumPixels)
            throw new ArgumentOutOfRangeException(nameof(width), "Crop dimensions must be positive and contain at most 16 million pixels.");
        var pixels = new uint[width * height];
        for (int row = 0; row < height; row++)
        {
            int sourceRow = (int)Math.Clamp((long)y + row, 0, source.Height - 1) * source.Width;
            for (int column = 0; column < width; column++)
            {
                int sourceColumn = (int)Math.Clamp((long)x + column, 0, source.Width - 1);
                pixels[row * width + column] = source.Pixels[sourceRow + sourceColumn];
            }
        }
        return new RgbaImage(width, height, pixels);
    }
}
