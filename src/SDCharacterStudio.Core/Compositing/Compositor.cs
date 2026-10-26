using System.Globalization;
using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Palette;

namespace SDCharacterStudio.Core.Compositing;

public enum MaskMode { Keep, Remove }
public static class Compositor
{
    /// <summary>Composes bottom-to-top layers without changing resolved images or the pose model.</summary>
    public static RgbaImage Compose(int width, int height, PoseFrame frame, ILayerSourceResolver sources, Func<string, ColorRamp>? resolveColor = null, bool mirror = false, Action<Layer, RgbaImage, int>? transformSource = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(sources);
        // Validate dimensions before allocation, including overflow.
        if (width <= 0 || height <= 0 || (long)width * height > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(width));
        var canvas = new RgbaImage(width, height, new uint[width * height]);
        foreach (var layer in frame.Layers)
        {
            if (!layer.Visible)
                continue;
            var resolved = sources.Resolve(layer);
            var pixels = new RgbaImage(resolved.Image.Width, resolved.Image.Height, (uint[])resolved.Image.Pixels.Clone());
            // Expressions operate on the colored source, and the final mask also clips their pixels.
            // Erase is transparent even on a layer with no explicit slot map.
            ReplaceSlots(pixels, ColorSlotTable.FromMappings(layer.ColorSlots ?? EmptyMappings, resolveColor));
            if (layer is LiveLayer { Recolor: { Count: > 0 } recolor }) Recolor.Apply(pixels, recolor, resolveColor);   // her colours changed, still live
            transformSource?.Invoke(layer, pixels, resolved.SourceFrameIndex);
            if (layer.Mask is not null)
            {
                var mask = sources.GetMask(layer.Mask);
                var path = mask.PerFrame.TryGetValue(resolved.SourceFrameIndex.ToString(CultureInfo.InvariantCulture), out var perFrame)
                    ? perFrame : mask.Image;
                if (path is null)
                    throw new ArgumentException($"Mask '{layer.Mask}' has no image for source frame {resolved.SourceFrameIndex}.", nameof(frame));
                var mode = mask.Mode.ToLowerInvariant() switch
                {
                    "keep" => MaskMode.Keep,
                    "remove" => MaskMode.Remove,
                    _ => throw new ArgumentException($"Unknown mask mode '{mask.Mode}'.", nameof(frame))
                };
                ApplyMask(pixels, sources.ResolveMaskImage(path), mode);
            }
            if (layer is LiveLayer { Map: { } map } live)
            {
                // Map cells may show pixels of her other frames (blue = frame + 1): resolve each once, coloured like this one.
                Dictionary<int, RgbaImage>? others = null;
                RgbaImage? Other(int index)
                {
                    others ??= new Dictionary<int, RgbaImage>();
                    if (others.TryGetValue(index, out var known)) return known;
                    var frameLayer = new LiveLayer { Source = live.Source, Part = "Full", SourceFrame = new SourceFrame { Index = index }, ColorSlots = live.ColorSlots, Recolor = live.Recolor };
                    var other = sources.Resolve(frameLayer);
                    var copy = new RgbaImage(other.Image.Width, other.Image.Height, (uint[])other.Image.Pixels.Clone());
                    ReplaceSlots(copy, ColorSlotTable.FromMappings(live.ColorSlots ?? EmptyMappings, resolveColor));
                    Recolor.Apply(copy, live.Recolor, resolveColor);
                    transformSource?.Invoke(frameLayer, copy, other.SourceFrameIndex);
                    return others[index] = copy;
                }
                pixels = Remap(pixels, sources.ResolveMaskImage(map), Other);
            }
            var (x, y) = Place(frame, layer, resolved);
            BlitAt(canvas, pixels, x, y, layer.Flip, layer.Opacity);
        }
        if (mirror)
            for (var row = 0; row < height; row++)
                Array.Reverse(canvas.Pixels, row * width, width);
        return canvas;
    }

    /// <summary>
    /// Where a layer's resolved source lands on the canvas: the top-left of its (possibly flipped) image, from its offset
    /// and anchor. Compose draws there; the editor maps a click on the canvas back to the source pixel with it.
    /// </summary>
    public static (long X, long Y) Place(PoseFrame frame, Layer layer, ResolvedLayerSource resolved)
    {
        var offset = Point(layer.Offset, "Layer.Offset");
        long x = offset.X, y = offset.Y;
        if (layer.AnchorTo is not null)
        {
            var target = frame.Anchors.FirstOrDefault(pair => string.Equals(pair.Key, layer.AnchorTo.Anchor, StringComparison.OrdinalIgnoreCase));
            if (target.Value is null)
                throw new ArgumentException($"Frame anchor '{layer.AnchorTo.Anchor}' was not found.", nameof(frame));
            var anchor = Point(target.Value, "Frame.Anchors");
            var from = layer.AnchorTo.From;
            var origin = from is JArray array && array.Count == 2 && array.All(v => v.Type == JTokenType.Integer)
                ? new PixelPoint(array[0]!.Value<int>(), array[1]!.Value<int>())
                : from?.Type == JTokenType.String && string.Equals(from.Value<string>(), "rig", StringComparison.OrdinalIgnoreCase)
                    ? resolved.RigAnchor ?? throw new ArgumentException("The resolved source has no rig anchor.", nameof(resolved))
                    : throw new ArgumentException("AnchorTo.From must be 'rig' or an integer [x,y] point.", nameof(frame));
            long sourceX = layer.Flip ? (long)resolved.Image.Width - 1 - origin.X : origin.X;
            x += (long)anchor.X - sourceX;
            y += (long)anchor.Y - origin.Y;
        }
        return (x, y);
    }

    /// <summary>
    /// A live layer's pixel map: output pixel (x, y) is the source pixel at the map pixel's (red, green), or transparent
    /// where the map is transparent or points outside the source. The output has the map's size.
    /// </summary>
    /// <summary>
    /// Rearrange a live source by a map (D-48): each opaque map cell shows source pixel (red, green). Blue 0 is the layer's
    /// own frame; blue k shows a pixel of the source's frame k − 1 (D-49, pixels copied from her other frames), when
    /// <paramref name="otherFrame"/> can resolve it.
    /// </summary>
    public static RgbaImage Remap(RgbaImage source, RgbaImage map, Func<int, RgbaImage?>? otherFrame = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(map);
        var output = new RgbaImage(map.Width, map.Height, new uint[map.Pixels.Length]);
        for (var i = 0; i < map.Pixels.Length; i++)
        {
            uint cell = map.Pixels[i];
            if (cell >> 24 == 0) continue;
            int x = (int)((cell >> 16) & 0xFF), y = (int)((cell >> 8) & 0xFF), frame = (int)(cell & 0xFF);
            var from = frame == 0 ? source : otherFrame?.Invoke(frame - 1);
            if (from != null && x < from.Width && y < from.Height) output.Pixels[i] = from.Pixels[y * from.Width + x];
        }
        return output;
    }

    /// <summary>The map cell that shows source pixel (x, y): of the layer's own frame, or of sheet frame <paramref name="frame"/> (0–254).</summary>
    public static uint MapCell(int x, int y, int? frame = null) => 0xFF000000u | (uint)(x & 0xFF) << 16 | (uint)(y & 0xFF) << 8 | (uint)(frame is { } f ? (f + 1) & 0xFF : 0);

    /// <summary>A map cell's source: pixel (X, Y) of the layer's own frame (Frame null) or of sheet frame Frame.</summary>
    public static (int X, int Y, int? Frame) MapSource(uint cell) => ((int)((cell >> 16) & 0xFF), (int)((cell >> 8) & 0xFF), (cell & 0xFF) == 0 ? null : (int)(cell & 0xFF) - 1);

    /// <summary>Mutates layer only; any mask alpha greater than zero counts as selected.</summary>
    public static void ApplyMask(RgbaImage layer, RgbaImage mask, MaskMode mode)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(mask);
        if (layer.Width != mask.Width || layer.Height != mask.Height)
            throw new ArgumentException("Mask dimensions must match the source frame.", nameof(mask));
        if (mode != MaskMode.Keep && mode != MaskMode.Remove)
            throw new ArgumentOutOfRangeException(nameof(mode));
        for (var i = 0; i < layer.Pixels.Length; i++)
            if ((mask.Pixels[i] >> 24 != 0) != (mode == MaskMode.Keep))
                layer.Pixels[i] = 0;
    }

    /// <summary>Replaces exact fully opaque reserved colors in place; unmatched colors remain unchanged.</summary>
    public static void ReplaceSlots(RgbaImage layer, ColorSlotTable slots)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(slots);
        for (var i = 0; i < layer.Pixels.Length; i++)
        {
            var pixel = layer.Pixels[i];
            if (pixel == ColorSlots.Erase)
                layer.Pixels[i] = 0;
            else if (ColorSlots.Reserved.TryGetValue(pixel, out var reserved) && slots.TryGet(reserved.Slot, out var ramp))
                layer.Pixels[i] = (reserved.Shade switch { 0 => ramp.Dark, 1 => ramp.Mid, _ => ramp.Light }) | 0xFF000000;
        }
    }

    /// <summary>Draws source over destination using straight alpha and nearest integer channel rounding.</summary>
    public static void Blit(RgbaImage destination, RgbaImage source, int x, int y, bool flip = false, double opacity = 1)
        => BlitAt(destination, source, x, y, flip, opacity);

    private static void BlitAt(RgbaImage destination, RgbaImage source, long x, long y, bool flip, double opacity)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(source);
        if (!double.IsFinite(opacity) || opacity < 0 || opacity > 1)
            throw new ArgumentOutOfRangeException(nameof(opacity), "Opacity must be finite and between zero and one.");
        if (opacity == 0)
            return;
        // Clip using widened arithmetic, so extreme offsets cannot wrap onto the canvas.
        var left = Math.Max(0L, x);
        var right = Math.Min(destination.Width, x + source.Width);
        var top = Math.Max(0L, y);
        var bottom = Math.Min(destination.Height, y + source.Height);
        for (var dy = top; dy < bottom; dy++)
            for (var dx = left; dx < right; dx++)
            {
                var sx = (int)(dx - x);
                if (flip)
                    sx = source.Width - 1 - sx;
                var src = source.Pixels[(int)(dy - y) * source.Width + sx];
                var sourceAlpha = (src >> 24) / 255.0 * opacity;
                if (sourceAlpha == 0)
                    continue;
                var index = (int)dy * destination.Width + (int)dx;
                var dst = destination.Pixels[index];
                var remainingAlpha = (dst >> 24) / 255.0 * (1 - sourceAlpha);
                var outputAlpha = sourceAlpha + remainingAlpha;
                uint Channel(int shift) => Round((((src >> shift) & 255) * sourceAlpha + ((dst >> shift) & 255) * remainingAlpha) / outputAlpha);
                destination.Pixels[index] = Round(outputAlpha * 255) << 24 | Channel(16) << 16 | Channel(8) << 8 | Channel(0);
            }
    }

    private static uint Round(double value) => (uint)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, 255);
    private static PixelPoint Point(int[]? point, string name)
        => point is { Length: 2 } ? new(point[0], point[1]) : throw new ArgumentException($"{name} must contain two integers.");
    private static readonly Dictionary<string, string> EmptyMappings = new();
}
