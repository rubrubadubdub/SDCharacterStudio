using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Palette;

namespace SDCharacterStudio.Core.Compositing;

public static class ExpressionOverlay
{
    /// <summary>
    /// Applies an expression to a mutable head-source clone, before its mask and placement transforms.
    /// The overlay, frame and color mappings are never modified. FaceBox supplies the origin, not a clip.
    /// </summary>
    public static void Apply(RgbaImage head, RgbaImage overlay, int[] faceBox, ExpressionFrame frame,
        IReadOnlyDictionary<string, string>? colorSlots = null, Func<string, ColorRamp>? resolveColor = null)
    {
        ArgumentNullException.ThrowIfNull(head);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(frame);
        if (faceBox is not { Length: 4 } || faceBox[2] <= 0 || faceBox[3] <= 0)
            throw new ArgumentException("FaceBox must contain [x,y,width,height] with positive dimensions.", nameof(faceBox));
        if (frame.Offset is not { Length: 2 })
            throw new ArgumentException("Expression frame offset must contain two integers.", nameof(frame));

        long x = (long)faceBox[0] + frame.Offset[0], y = (long)faceBox[1] + frame.Offset[1];
        var left = Math.Max(0L, x);
        var right = Math.Min(head.Width, x + overlay.Width);
        var top = Math.Max(0L, y);
        var bottom = Math.Min(head.Height, y + overlay.Height);
        if (left >= right || top >= bottom)
            return;

        var pixels = new RgbaImage(overlay.Width, overlay.Height, (uint[])overlay.Pixels.Clone());
        var slots = ColorSlotTable.FromMappings(colorSlots ?? EmptyMappings, resolveColor);
        for (var dy = top; dy < bottom; dy++)
            for (var dx = left; dx < right; dx++)
                if (pixels.Pixels[(int)(dy - y) * pixels.Width + (int)(dx - x)] == ColorSlots.Erase)
                    head.Pixels[(int)dy * head.Width + (int)dx] = 0;
        Compositor.ReplaceSlots(pixels, slots);
        // A source intersecting the head canvas has an origin that fits in an int.
        Compositor.Blit(head, pixels, (int)x, (int)y);
    }

    private static readonly Dictionary<string, string> EmptyMappings = new();
}
