using SDCharacterStudio.Core.Compositing;

namespace SDCharacterStudio.Editor.Studio;

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public bool Contains(int x, int y) => x >= X && y >= Y && x < X + Width && y < Y + Height;
    public static PixelRect FromCorners(int x0, int y0, int x1, int y1) => new(Math.Min(x0, x1), Math.Min(y0, y1), Math.Abs(x1 - x0) + 1, Math.Abs(y1 - y0) + 1);
    public PixelRect Clip(int width, int height)
    {
        int left = Math.Max(0, X), top = Math.Max(0, Y), right = Math.Min(width, X + Width), bottom = Math.Min(height, Y + Height);
        return new(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }
}

/// <summary>Pixel editing on straight-alpha images (doc 07 §6.1, §6.3). Every operation edits the image it is given.</summary>
public static class PixelTools
{
    public static bool InBounds(RgbaImage image, int x, int y) => x >= 0 && y >= 0 && x < image.Width && y < image.Height;
    public static uint Get(RgbaImage image, int x, int y) => InBounds(image, x, y) ? image.Pixels[y * image.Width + x] : 0;
    public static bool Set(RgbaImage image, int x, int y, uint color)
    {
        if (!InBounds(image, x, y)) return false;
        color = color >> 24 == 0 ? 0 : color;
        ref var pixel = ref image.Pixels[y * image.Width + x];
        if (pixel == color) return false;
        pixel = color;
        return true;
    }

    /// <summary>Bresenham points from (x0,y0) to (x1,y1), both ends included.</summary>
    public static IEnumerable<(int X, int Y)> Line(int x0, int y0, int x1, int y1)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1, dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1, error = dx + dy;
        while (true)
        {
            yield return (x0, y0);
            if (x0 == x1 && y0 == y1) yield break;
            int e2 = 2 * error;
            if (e2 >= dy) { error += dy; x0 += sx; }
            if (e2 <= dx) { error += dx; y0 += sy; }
        }
    }

    /// <summary>The end point snapped to horizontal, vertical or 45° from the start (Shift with the line tool).</summary>
    public static (int X, int Y) Snap45(int x0, int y0, int x1, int y1)
    {
        int dx = x1 - x0, dy = y1 - y0;
        if (Math.Abs(dx) > 2 * Math.Abs(dy)) return (x1, y0);
        if (Math.Abs(dy) > 2 * Math.Abs(dx)) return (x0, y1);
        int d = Math.Max(Math.Abs(dx), Math.Abs(dy));
        return (x0 + Math.Sign(dx) * d, y0 + Math.Sign(dy) * d);
    }

    public static int DrawLine(RgbaImage image, int x0, int y0, int x1, int y1, uint color) => Line(x0, y0, x1, y1).Count(p => Set(image, p.X, p.Y, color));

    public static int DrawRectangle(RgbaImage image, PixelRect rect, uint color, bool filled)
    {
        int changed = 0;
        for (int y = rect.Y; y < rect.Y + rect.Height; y++)
            for (int x = rect.X; x < rect.X + rect.Width; x++)
                if (filled || y == rect.Y || y == rect.Y + rect.Height - 1 || x == rect.X || x == rect.X + rect.Width - 1)
                    changed += Set(image, x, y, color) ? 1 : 0;
        return changed;
    }

    /// <summary>Contiguous (4-neighbour) fill of the colour under (x,y); <paramref name="global"/> replaces that colour everywhere.</summary>
    public static int Fill(RgbaImage image, int x, int y, uint color, bool global)
    {
        if (!InBounds(image, x, y)) return 0;
        uint target = Get(image, x, y);
        color = color >> 24 == 0 ? 0 : color;
        if (target == color) return 0;
        if (global)
        {
            int count = 0;
            for (int i = 0; i < image.Pixels.Length; i++) if (image.Pixels[i] == target) { image.Pixels[i] = color; count++; }
            return count;
        }
        return Flood(image.Width, image.Height, x, y, (px, py) => Get(image, px, py) == target).Count(p => Set(image, p.X, p.Y, color));
    }

    /// <summary>Pixels connected to (x,y) that pass <paramref name="accept"/> (4-neighbourhood).</summary>
    public static List<(int X, int Y)> Flood(int width, int height, int x, int y, Func<int, int, bool> accept)
    {
        var result = new List<(int, int)>();
        if (x < 0 || y < 0 || x >= width || y >= height || !accept(x, y)) return result;
        var seen = new bool[width * height];
        var stack = new Stack<(int X, int Y)>();
        stack.Push((x, y)); seen[y * width + x] = true;
        while (stack.Count > 0)
        {
            var (px, py) = stack.Pop();
            result.Add((px, py));
            foreach (var (nx, ny) in new[] { (px + 1, py), (px - 1, py), (px, py + 1), (px, py - 1) })
                if (nx >= 0 && ny >= 0 && nx < width && ny < height && !seen[ny * width + nx] && accept(nx, ny))
                { seen[ny * width + nx] = true; stack.Push((nx, ny)); }
        }
        return result;
    }

    /// <summary>Largest per-channel difference (alpha included); the wand's tolerance compares against this (0–255).</summary>
    public static int Distance(uint a, uint b)
    {
        if (a >> 24 == 0 && b >> 24 == 0) return 0;
        int d = 0;
        for (int shift = 0; shift <= 24; shift += 8) d = Math.Max(d, Math.Abs((int)((a >> shift) & 255) - (int)((b >> shift) & 255)));
        return d;
    }

    /// <summary>Wand selection on a source image: contiguous (or global) pixels within tolerance of the clicked colour.</summary>
    public static IEnumerable<(int X, int Y)> Wand(RgbaImage source, int x, int y, int tolerance, bool global)
    {
        if (!InBounds(source, x, y)) return Array.Empty<(int, int)>();
        uint picked = Get(source, x, y);
        bool Accept(int px, int py) => Distance(Get(source, px, py), picked) <= tolerance;
        if (!global) return Flood(source.Width, source.Height, x, y, Accept);
        var all = new List<(int, int)>();
        for (int py = 0; py < source.Height; py++)
            for (int px = 0; px < source.Width; px++)
                if (Accept(px, py)) all.Add((px, py));
        return all;
    }

    // ---------------- masks: opaque = kept (doc 05 §5) ----------------

    public const uint MaskOn = 0xFFFFFFFF;

    public static bool IsKept(RgbaImage mask, int x, int y) => Get(mask, x, y) >> 24 != 0;

    public static RgbaImage AboveRow(int width, int height, int row)
    {
        var mask = StudioImages.Blank(width, height);
        for (int y = 0; y <= Math.Min(row, height - 1); y++)
            for (int x = 0; x < width; x++) mask.Pixels[y * width + x] = MaskOn;
        return mask;
    }

    /// <summary>Grow (+1) or shrink (−1) a mask by one pixel, 4-neighbourhood.</summary>
    public static RgbaImage GrowShrink(RgbaImage mask, bool grow)
    {
        var result = StudioImages.Clone(mask);
        for (int y = 0; y < mask.Height; y++)
            for (int x = 0; x < mask.Width; x++)
            {
                bool on = IsKept(mask, x, y);
                bool Neighbour(int nx, int ny) => InBounds(mask, nx, ny) ? IsKept(mask, nx, ny) : !grow;
                if (grow && !on && (Neighbour(x + 1, y) || Neighbour(x - 1, y) || Neighbour(x, y + 1) || Neighbour(x, y - 1))) result.Pixels[y * mask.Width + x] = MaskOn;
                if (!grow && on && !(Neighbour(x + 1, y) && Neighbour(x - 1, y) && Neighbour(x, y + 1) && Neighbour(x, y - 1))) result.Pixels[y * mask.Width + x] = 0;
            }
        return result;
    }

    public static RgbaImage Invert(RgbaImage mask)
    {
        var result = StudioImages.Clone(mask);
        for (int i = 0; i < result.Pixels.Length; i++) result.Pixels[i] = mask.Pixels[i] >> 24 == 0 ? MaskOn : 0;
        return result;
    }

    // ---------------- selections ----------------

    public static RgbaImage Copy(RgbaImage image, PixelRect rect) => StudioImages.Crop(image, rect.X, rect.Y, rect.Width, rect.Height);

    public static void Clear(RgbaImage image, PixelRect rect)
    {
        var area = rect.Clip(image.Width, image.Height);
        for (int y = area.Y; y < area.Y + area.Height; y++)
            for (int x = area.X; x < area.X + area.Width; x++) image.Pixels[y * image.Width + x] = 0;
    }

    /// <summary>Paste with transparent pixels ignored (moving a selection keeps what's underneath).</summary>
    public static void Paste(RgbaImage image, RgbaImage piece, int x, int y)
    {
        for (int row = 0; row < piece.Height; row++)
            for (int column = 0; column < piece.Width; column++)
            {
                uint pixel = piece.Pixels[row * piece.Width + column];
                if (pixel >> 24 != 0) Set(image, x + column, y + row, pixel);
            }
    }

    /// <summary>Move the pixels inside <paramref name="rect"/> by (dx,dy).</summary>
    public static void Move(RgbaImage image, PixelRect rect, int dx, int dy)
    {
        var piece = Copy(image, rect);
        Clear(image, rect);
        Paste(image, piece, rect.X + dx, rect.Y + dy);
    }

    /// <summary>Mirror the pixels inside <paramref name="rect"/> horizontally (Ctrl+M).</summary>
    public static void Mirror(RgbaImage image, PixelRect rect)
    {
        var area = rect.Clip(image.Width, image.Height);
        for (int y = area.Y; y < area.Y + area.Height; y++)
            Array.Reverse(image.Pixels, y * image.Width + area.X, area.Width);
    }

    /// <summary>Flip the pixels inside <paramref name="rect"/> vertically.</summary>
    public static void FlipVertical(RgbaImage image, PixelRect rect)
    {
        var area = rect.Clip(image.Width, image.Height);
        for (int top = area.Y, bottom = area.Y + area.Height - 1; top < bottom; top++, bottom--)
            for (int x = area.X; x < area.X + area.Width; x++)
                (image.Pixels[top * image.Width + x], image.Pixels[bottom * image.Width + x]) = (image.Pixels[bottom * image.Width + x], image.Pixels[top * image.Width + x]);
    }

    // ---------------- brushes and shapes ----------------

    /// <summary>
    /// The pixels a brush of <paramref name="size"/> covers around (x,y): a square up to 2 px, a disc from 3 px
    /// (Aseprite's round brush). Even sizes lean right and down.
    /// </summary>
    public static IEnumerable<(int X, int Y)> Brush(int x, int y, int size)
    {
        size = Math.Clamp(size, 1, 16);
        int start = -(size - 1) / 2;
        double radius = size / 2.0, centre = (size - 1) / 2.0;
        for (int dy = 0; dy < size; dy++)
            for (int dx = 0; dx < size; dx++)
                if (size <= 2 || Math.Pow(dx - centre, 2) + Math.Pow(dy - centre, 2) <= radius * radius + 0.25)
                    yield return (x + start + dx, y + start + dy);
    }

    /// <summary>
    /// The outline of the ellipse inside the rectangle from (x0,y0) to (x1,y1), corners included (Zingl's algorithm,
    /// as pixel editors draw it). A filled ellipse is every pixel between the outline's ends on each row.
    /// </summary>
    public static IEnumerable<(int X, int Y)> Ellipse(int x0, int y0, int x1, int y1, bool filled)
    {
        var outline = new HashSet<(int X, int Y)>();
        long a = Math.Abs(x1 - x0), b = Math.Abs(y1 - y0), b1 = b & 1;
        long dx = 4 * (1 - a) * b * b, dy = 4 * (b1 + 1) * a * a, err = dx + dy + b1 * a * a, e2;
        if (x0 > x1) { x0 = x1; x1 += (int)a; }
        if (y0 > y1) y0 = y1;
        y0 += (int)((b + 1) / 2); y1 = y0 - (int)b1;
        a *= 8 * a; b1 = 8 * b * b;
        do
        {
            outline.Add((x1, y0)); outline.Add((x0, y0)); outline.Add((x0, y1)); outline.Add((x1, y1));
            e2 = 2 * err;
            if (e2 <= dy) { y0++; y1--; err += dy += a; }
            if (e2 >= dx || 2 * err > dy) { x0++; x1--; err += dx += b1; }
        } while (x0 <= x1);
        while (y0 - y1 < b)
        {
            outline.Add((x0 - 1, y0)); outline.Add((x1 + 1, y0++)); outline.Add((x0 - 1, y1)); outline.Add((x1 + 1, y1--));
        }
        if (!filled) return outline;
        var all = new HashSet<(int X, int Y)>(outline);
        foreach (var row in outline.GroupBy(p => p.Y))
            for (int x = row.Min(p => p.X); x <= row.Max(p => p.X); x++) all.Add((x, row.Key));
        return all;
    }

    // ---------------- onion skin ----------------

    /// <summary>Straight-alpha "over": <paramref name="top"/> at <paramref name="opacity"/> onto <paramref name="under"/>, tinted towards <paramref name="tint"/> by <paramref name="tintAmount"/>.</summary>
    public static void Over(RgbaImage under, RgbaImage top, double opacity, uint tint = 0, double tintAmount = 0)
    {
        int count = Math.Min(under.Pixels.Length, top.Pixels.Length);
        for (int i = 0; i < count; i++)
        {
            uint src = top.Pixels[i];
            double sa = (src >> 24) / 255.0 * opacity;
            if (sa <= 0) continue;
            double r = (src >> 16) & 255, g = (src >> 8) & 255, bl = src & 255;
            if (tintAmount > 0) { r += (((tint >> 16) & 255) - r) * tintAmount; g += (((tint >> 8) & 255) - g) * tintAmount; bl += ((tint & 255) - bl) * tintAmount; }
            uint dst = under.Pixels[i];
            double da = (dst >> 24) / 255.0, oa = sa + da * (1 - sa);
            double Mix(double s, double d) => (s * sa + d * da * (1 - sa)) / oa;
            under.Pixels[i] = (uint)Math.Round(oa * 255) << 24 | (uint)Math.Round(Mix(r, (dst >> 16) & 255)) << 16 | (uint)Math.Round(Mix(g, (dst >> 8) & 255)) << 8 | (uint)Math.Round(Mix(bl, dst & 255));
        }
    }

    /// <summary>The smallest rectangle around the opaque pixels, or empty.</summary>
    public static PixelRect Bounds(RgbaImage image)
    {
        int left = image.Width, top = image.Height, right = -1, bottom = -1;
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
                if (image.Pixels[y * image.Width + x] >> 24 != 0) { left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
        return right < 0 ? default : new PixelRect(left, top, right - left + 1, bottom - top + 1);
    }
}
