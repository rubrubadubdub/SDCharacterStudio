namespace SDCharacterStudio.Editor.Studio;

/// <summary>How a new selection combines with the one there (Aseprite: Shift adds, Alt subtracts, both intersect).</summary>
public enum SelectionMode { Replace, Add, Subtract, Intersect }

/// <summary>
/// A selection of any shape (D-49): which pixels of a frame are selected. Marquee, lasso and wand make one;
/// they add, subtract, intersect and invert. Immutable: every operation returns a new mask.
/// </summary>
public sealed class SelectionMask
{
    private readonly bool[] cells;

    private SelectionMask(int width, int height, bool[] cells)
    {
        Width = width; Height = height; this.cells = cells;
        int left = width, top = height, right = -1, bottom = -1, count = 0;
        for (int i = 0; i < cells.Length; i++)
        {
            if (!cells[i]) continue;
            int x = i % width, y = i / width;
            left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y);
            count++;
        }
        Count = count;
        Bounds = right < 0 ? default : new PixelRect(left, top, right - left + 1, bottom - top + 1);
    }

    public int Width { get; }
    public int Height { get; }
    public int Count { get; }
    public PixelRect Bounds { get; }
    public bool IsEmpty => Count == 0;
    /// <summary>Every pixel of its bounds is selected (a plain box).</summary>
    public bool IsRectangle => Count == Bounds.Width * Bounds.Height;
    public bool Contains(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height && cells[y * Width + x];

    public IEnumerable<(int X, int Y)> Cells()
    {
        for (int i = 0; i < cells.Length; i++) if (cells[i]) yield return (i % Width, i / Width);
    }

    public static SelectionMask Empty(int width, int height) => new(width, height, new bool[width * height]);

    public static SelectionMask FromRect(int width, int height, PixelRect rect)
    {
        var area = rect.Clip(width, height);
        var cells = new bool[width * height];
        for (int y = area.Y; y < area.Y + area.Height; y++)
            for (int x = area.X; x < area.X + area.Width; x++) cells[y * width + x] = true;
        return new(width, height, cells);
    }

    public static SelectionMask FromCells(int width, int height, IEnumerable<(int X, int Y)> points)
    {
        var cells = new bool[width * height];
        foreach (var (x, y) in points) if (x >= 0 && y >= 0 && x < width && y < height) cells[y * width + x] = true;
        return new(width, height, cells);
    }

    /// <summary>
    /// A lasso: the pixels whose centres are inside the closed path through <paramref name="points"/> (pixel
    /// coordinates), plus the pixels the path runs over, so a thin stroke still selects what it touched.
    /// </summary>
    public static SelectionMask FromLasso(int width, int height, IReadOnlyList<(int X, int Y)> points)
    {
        var cells = new bool[width * height];
        if (points.Count == 0) return new(width, height, cells);
        for (int i = 0; i < points.Count; i++)
        {
            var a = points[i]; var b = points[(i + 1) % points.Count];
            foreach (var (x, y) in PixelTools.Line(a.X, a.Y, b.X, b.Y)) if (x >= 0 && y >= 0 && x < width && y < height) cells[y * width + x] = true;
        }
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                // Even-odd rule on the pixel centre.
                double px = x + 0.5, py = y + 0.5;
                bool inside = false;
                for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
                {
                    double xi = points[i].X + 0.5, yi = points[i].Y + 0.5, xj = points[j].X + 0.5, yj = points[j].Y + 0.5;
                    if ((yi > py) != (yj > py) && px < (xj - xi) * (py - yi) / (yj - yi) + xi) inside = !inside;
                }
                if (inside) cells[y * width + x] = true;
            }
        return new(width, height, cells);
    }

    public SelectionMask Combine(SelectionMask other, SelectionMode mode)
    {
        if (mode == SelectionMode.Replace || other.Width != Width || other.Height != Height) return other;
        var result = new bool[cells.Length];
        for (int i = 0; i < cells.Length; i++)
            result[i] = mode switch
            {
                SelectionMode.Add => cells[i] || other.cells[i],
                SelectionMode.Subtract => cells[i] && !other.cells[i],
                _ => cells[i] && other.cells[i],
            };
        return new(Width, Height, result);
    }

    public SelectionMask Invert() => new(Width, Height, cells.Select(c => !c).ToArray());

    /// <summary>The same shape moved by (dx,dy); what moves off the frame is dropped.</summary>
    public SelectionMask Offset(int dx, int dy) => FromCells(Width, Height, Cells().Select(c => (c.X + dx, c.Y + dy)));

    public static SelectionMode ModeFor(bool shift, bool alt) => shift && alt ? SelectionMode.Intersect : shift ? SelectionMode.Add : alt ? SelectionMode.Subtract : SelectionMode.Replace;
}
