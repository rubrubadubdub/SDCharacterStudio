namespace SDCharacterStudio.Core.Compositing;

/// <summary>Pixels are straight (non-premultiplied) alpha, packed as 0xAARRGGBB.</summary>
public sealed class RgbaImage
{
    public int Width { get; }
    public int Height { get; }
    public uint[] Pixels { get; }
    /// <summary>Wraps the supplied array. Compose clones resolved sources before editing them.</summary>
    public RgbaImage(int width, int height, uint[] pixels)
    {
        if (width <= 0 || height <= 0 || (long)width * height > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(width), "Image dimensions must be positive and fit in one array.");
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Length != (long)width * height)
            throw new ArgumentException("Pixel count must equal width times height.", nameof(pixels));
        Width = width;
        Height = height;
        Pixels = pixels;
    }
}
