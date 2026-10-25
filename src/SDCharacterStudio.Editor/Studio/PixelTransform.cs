namespace SDCharacterStudio.Editor.Studio;

/// <summary>Where a floating piece goes: moved, scaled (negative flips) and rotated about a pivot (frame pixels).</summary>
public sealed record PieceTransform(double Dx, double Dy, double ScaleX, double ScaleY, double Angle, double PivotX, double PivotY)
{
    public static PieceTransform Identity(PixelRect bounds) => new(0, 0, 1, 1, 0, bounds.X + bounds.Width / 2.0, bounds.Y + bounds.Height / 2.0);
    public bool IsIdentity => Dx == 0 && Dy == 0 && ScaleX == 1 && ScaleY == 1 && Angle == 0;
    /// <summary>A whole number of quarter turns: rotating needs no smoothing.</summary>
    public bool IsRightAngle => Math.Abs(Math.IEEERemainder(Angle, 90)) < 1e-9;

    /// <summary>A point of the piece (frame coordinates before the transform) where it lands.</summary>
    public (double X, double Y) Apply(double x, double y)
    {
        double lx = (x - PivotX) * ScaleX, ly = (y - PivotY) * ScaleY;
        double r = Angle * Math.PI / 180, cos = Math.Cos(r), sin = Math.Sin(r);
        return (PivotX + Dx + lx * cos - ly * sin, PivotY + Dy + lx * sin + ly * cos);
    }

    /// <summary>A landed point back to the piece (null when a scale is zero).</summary>
    public (double X, double Y)? Invert(double x, double y)
    {
        if (Math.Abs(ScaleX) < 1e-9 || Math.Abs(ScaleY) < 1e-9) return null;
        double lx = x - PivotX - Dx, ly = y - PivotY - Dy;
        double r = -Angle * Math.PI / 180, cos = Math.Cos(r), sin = Math.Sin(r);
        double ux = lx * cos - ly * sin, uy = lx * sin + ly * cos;
        return (PivotX + ux / ScaleX, PivotY + uy / ScaleY);
    }
}

/// <summary>
/// Transforming pixel art without new colours (D-49). The piece is a grid of indexes into its pixels, so a live
/// pixel (a map cell naming a sprite pixel) moves as a whole and stays live. Small turns (up to 30°) shear whole rows
/// and columns (Paeth); bigger ones use RotSprite (Xenowhirl): three Scale2x passes, which only copy neighbours by
/// colour equality, then nearest sampling.
/// </summary>
public static class PixelTransform
{
    /// <summary>Scale2x on an index grid: <paramref name="color"/> gives each index's shown colour (-1 is empty).</summary>
    public static int[] Scale2x(int[] grid, int width, int height, Func<int, uint> color)
    {
        var result = new int[width * 2 * height * 2];
        uint C(int x, int y) { x = Math.Clamp(x, 0, width - 1); y = Math.Clamp(y, 0, height - 1); int i = grid[y * width + x]; return i < 0 ? 0 : color(i); }
        int I(int x, int y) => grid[Math.Clamp(y, 0, height - 1) * width + Math.Clamp(x, 0, width - 1)];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int e = grid[y * width + x];
                uint b = C(x, y - 1), d = C(x - 1, y), f = C(x + 1, y), h = C(x, y + 1);
                int e0 = e, e1 = e, e2 = e, e3 = e;
                if (b != h && d != f)
                {
                    if (d == b) e0 = I(x - 1, y);
                    if (b == f) e1 = I(x + 1, y);
                    if (d == h) e2 = I(x - 1, y);
                    if (h == f) e3 = I(x + 1, y);
                }
                int row = y * 2 * width * 2, x2 = x * 2;
                result[row + x2] = e0; result[row + x2 + 1] = e1;
                result[row + width * 2 + x2] = e2; result[row + width * 2 + x2 + 1] = e3;
            }
        return result;
    }

    /// <summary>Turns up to this many degrees use three shears; bigger ones RotSprite.</summary>
    public const double ShearLimit = 30;

    /// <summary>
    /// Rotate the occupied cells of <paramref name="cells"/> (a frame-size index grid, -1 empty) by <paramref name="angle"/>
    /// degrees about pixel (<paramref name="pivotX"/>, <paramref name="pivotY"/>) with three integer shears
    /// (x += round(-tan(a/2)·y); y += round(sin(a)·x); x += round(-tan(a/2)·y)), a bijection on the pixel grid.
    /// </summary>
    public static int[] Shear(int[] cells, int width, int height, double angle, int pivotX, int pivotY)
    {
        double r = angle * Math.PI / 180, alpha = -Math.Tan(r / 2), beta = Math.Sin(r);
        var result = Enumerable.Repeat(-1, width * height).ToArray();
        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i] < 0) continue;
            int x = i % width - pivotX, y = i / width - pivotY;
            x += (int)Math.Round(alpha * y, MidpointRounding.AwayFromZero);
            y += (int)Math.Round(beta * x, MidpointRounding.AwayFromZero);
            x += (int)Math.Round(alpha * y, MidpointRounding.AwayFromZero);
            int tx = x + pivotX, ty = y + pivotY;
            if (tx >= 0 && ty >= 0 && tx < width && ty < height) result[ty * width + tx] = cells[i];
        }
        return result;
    }

    /// <summary>
    /// Where each landed pixel of a frame comes from: for every frame pixel inside the transformed piece, the index of
    /// the piece cell it shows (or -1). <paramref name="piece"/> is the piece's index grid over <paramref name="bounds"/>.
    /// </summary>
    public static int[] Land(int[] piece, PixelRect bounds, PieceTransform transform, int frameWidth, int frameHeight, Func<int, uint> color)
    {
        var landed = Enumerable.Repeat(-1, frameWidth * frameHeight).ToArray();
        if (bounds.IsEmpty) return landed;
        // A small turn of an unscaled piece: three shears (Paeth). Whole rows and columns slide, so every pixel lands
        // exactly once: no strays, no gaps. Crisper than RotSprite at the 5–30° a head or an arm tilts.
        if (!transform.IsRightAngle && Math.Abs(Math.Abs(transform.ScaleX) - 1) < 1e-9 && Math.Abs(Math.Abs(transform.ScaleY) - 1) < 1e-9 && Math.Abs(transform.Angle) <= ShearLimit)
        {
            var flat = Land(piece, bounds, transform with { Angle = 0 }, frameWidth, frameHeight, color);
            var pivot = transform.Apply(transform.PivotX, transform.PivotY);
            return Shear(flat, frameWidth, frameHeight, transform.Angle, (int)Math.Floor(pivot.X), (int)Math.Floor(pivot.Y));
        }
        // Smooth rotation samples an 8x RotSprite grid; quarter turns, flips and scales sample the piece itself.
        int factor = transform.IsRightAngle ? 1 : 8;
        int[] grid = piece; int gw = bounds.Width, gh = bounds.Height;
        for (int pass = 1; pass < factor; pass *= 2) { grid = Scale2x(grid, gw, gh, color); gw *= 2; gh *= 2; }

        var corners = new[] { (bounds.X, bounds.Y), (bounds.X + bounds.Width, bounds.Y), (bounds.X, bounds.Y + bounds.Height), (bounds.X + bounds.Width, bounds.Y + bounds.Height) }
            .Select(c => transform.Apply(c.Item1, c.Item2)).ToArray();
        int left = Math.Max(0, (int)Math.Floor(corners.Min(c => c.X)) - 1), right = Math.Min(frameWidth - 1, (int)Math.Ceiling(corners.Max(c => c.X)) + 1);
        int top = Math.Max(0, (int)Math.Floor(corners.Min(c => c.Y)) - 1), bottom = Math.Min(frameHeight - 1, (int)Math.Ceiling(corners.Max(c => c.Y)) + 1);
        for (int y = top; y <= bottom; y++)
            for (int x = left; x <= right; x++)
            {
                if (transform.Invert(x + 0.5, y + 0.5) is not { } source) continue;
                double u = (source.X - bounds.X) * factor, v = (source.Y - bounds.Y) * factor;
                // A hair inwards keeps exact edges (a flip lands on .0) on the pixel they belong to.
                int gx = (int)Math.Floor(u + 1e-7), gy = (int)Math.Floor(v + 1e-7);
                if (gx < 0 || gy < 0 || gx >= gw || gy >= gh) continue;
                landed[y * frameWidth + x] = grid[gy * gw + gx];
            }
        return landed;
    }
}
