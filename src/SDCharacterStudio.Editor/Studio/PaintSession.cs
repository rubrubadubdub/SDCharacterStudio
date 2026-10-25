using SDCharacterStudio.Core.Compositing;

namespace SDCharacterStudio.Editor.Studio;

/// <summary>
/// One stroke on a pack image (mask, expression…): pixels go into a working copy that previews through
/// <see cref="StudioContext.Overrides"/>, and the caller commits the result as one undo step when the stroke ends.
/// Canvas coordinates map to the image by subtracting the origin.
/// </summary>
public sealed class PaintSession
{
    private readonly StudioContext context;
    private readonly RgbaImage original;
    private (int X, int Y)? last;

    private PaintSession(StudioContext context, string path, RgbaImage image, int originX, int originY, uint color)
    {
        this.context = context; Path = path; original = image; OriginX = originX; OriginY = originY; Color = color;
        Image = StudioImages.Clone(image);
        context.Overrides[path] = Image;
    }

    public static PaintSession Begin(StudioContext context, string path, RgbaImage image, int originX, int originY, uint color) => new(context, path, image, originX, originY, color);

    public string Path { get; }
    public RgbaImage Image { get; }
    public int OriginX { get; }
    public int OriginY { get; }
    public uint Color { get; }
    public bool Changed => !original.Pixels.AsSpan().SequenceEqual(Image.Pixels);

    /// <summary>Draw to a canvas point, joining it to the previous one with a line.</summary>
    public void DrawTo(int x, int y)
    {
        var from = last ?? (x, y);
        foreach (var (px, py) in PixelTools.Line(from.X, from.Y, x, y)) PixelTools.Set(Image, px - OriginX, py - OriginY, Color);
        last = (x, y);
    }

    public void Fill(int x, int y, bool global) => PixelTools.Fill(Image, x - OriginX, y - OriginY, Color, global);

    /// <summary>Stop previewing; the caller writes <see cref="Image"/> if <see cref="Changed"/>.</summary>
    public RgbaImage End() { context.Overrides.Remove(Path); return Image; }

    public void Cancel() => context.Overrides.Remove(Path);
}
