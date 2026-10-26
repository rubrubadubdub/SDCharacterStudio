using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Editor.Studio;

namespace SDCharacterStudio.Editor.ViewModels;

/// <summary>
/// Tool options (D-49, after Aseprite and Pixelorama): brush size, pixel-perfect strokes, symmetry, the Alt eyedropper
/// that hands back to the tool you were using, and onion skin frames, opacity and tint. All remembered.
/// </summary>
public partial class PoseDocumentViewModel
{
    public const int MaxBrush = 8, MaxOnion = 3;

    [ObservableProperty] private int brushSize = 1;
    [ObservableProperty] private bool pixelPerfect = true;
    [ObservableProperty] private bool symmetry;
    [ObservableProperty] private int onionBefore = 1;
    [ObservableProperty] private int onionAfter = 1;
    [ObservableProperty] private bool onionTint = true;
    [ObservableProperty] private bool onionWrap;
    [ObservableProperty] private bool isShortcutsOpen;
    /// <summary>The painting tool the eyedropper hands back to after a pick.</summary>
    private StudioTool? lastPaintTool = StudioTool.Pencil;

    /// <summary>The remembered options, set without saving them straight back (hence the fields).</summary>
#pragma warning disable MVVMTK0034
    private void LoadOptions()
    {
        var s = Studio.Settings;
        brushSize = Math.Clamp(s.BrushSize, 1, MaxBrush); pixelPerfect = s.PixelPerfect; symmetry = s.Symmetry; filledShapes = s.FilledShapes; paintInside = s.PaintInside;
        onionBefore = Math.Clamp(s.OnionBefore, 0, MaxOnion); onionAfter = Math.Clamp(s.OnionAfter, 0, MaxOnion); onionTint = s.OnionTint; onionWrap = s.OnionWrap;
    }
#pragma warning restore MVVMTK0034

    partial void OnBrushSizeChanged(int value)
    {
        if (value < 1 || value > MaxBrush) { BrushSize = Math.Clamp(value, 1, MaxBrush); return; }
        Studio.Settings.BrushSize = value; Studio.Settings.Save();
    }
    partial void OnPixelPerfectChanged(bool value) { Studio.Settings.PixelPerfect = value; Studio.Settings.Save(); }
    partial void OnSymmetryChanged(bool value) { Studio.Settings.Symmetry = value; Studio.Settings.Save(); }
    partial void OnFilledShapesChanged(bool value) { Studio.Settings.FilledShapes = value; Studio.Settings.Save(); }
    partial void OnOnionBeforeChanged(int value) { Studio.Settings.OnionBefore = Math.Clamp(value, 0, MaxOnion); Studio.Settings.Save(); Render(); }
    partial void OnOnionAfterChanged(int value) { Studio.Settings.OnionAfter = Math.Clamp(value, 0, MaxOnion); Studio.Settings.Save(); Render(); }
    partial void OnOnionTintChanged(bool value) { Studio.Settings.OnionTint = value; Studio.Settings.Save(); Render(); }
    partial void OnOnionWrapChanged(bool value) { Studio.Settings.OnionWrap = value; Studio.Settings.Save(); Render(); }

    public static bool IsPaintTool(StudioTool tool) => tool is StudioTool.Pencil or StudioTool.Eraser or StudioTool.Fill or StudioTool.Line or StudioTool.Rectangle or StudioTool.Ellipse or StudioTool.Shade;
    /// <summary>Tools that draw with a brush (its size and symmetry apply).</summary>
    public bool UsesBrush => Tool is StudioTool.Pencil or StudioTool.Eraser or StudioTool.Shade;
    public bool UsesShapes => Tool is StudioTool.Rectangle or StudioTool.Ellipse;

    /// <summary>The selection's actions show in the options bar for every selection tool and Move.</summary>
    public bool IsSelectTool => IsSelectionTool(Tool) || Tool == StudioTool.Move;
    public bool ShowsSelectionActions => IsSelectTool && !IsTransforming;
    public IReadOnlyList<ShortcutGroup> Shortcuts => PoseShortcuts.All;
    /// <summary>The tool's name and key, for the options bar.</summary>
    public string ToolName => Tool switch
    {
        StudioTool.Select => "Select (M)", StudioTool.Pencil => "Pencil (B)", StudioTool.Eraser => "Eraser (E)", StudioTool.Fill => "Fill (G)",
        StudioTool.Picker => "Eyedropper (I)", StudioTool.Line => "Line (L)", StudioTool.Rectangle => "Rectangle (U)", StudioTool.Ellipse => "Ellipse (Shift+U)",
        StudioTool.Anchor => "Anchor (N)", StudioTool.Move => "Move (V)", StudioTool.Lasso => "Lasso (Q)", StudioTool.Wand => "Magic wand (W)", StudioTool.Shade => "Shade (D)", _ => Tool.ToString(),
    };

    [RelayCommand] private void BiggerBrush() => BrushSize = Math.Min(MaxBrush, BrushSize + 1);
    [RelayCommand] private void SmallerBrush() => BrushSize = Math.Max(1, BrushSize - 1);
    [RelayCommand] private void ShowShortcuts() => IsShortcutsOpen = !IsShortcutsOpen;

    /// <summary>The pixels one dab of the brush covers at (x,y), mirrored across the frame's centre with symmetry on.</summary>
    public IEnumerable<(int X, int Y)> BrushAt(int x, int y)
    {
        var points = PixelTools.Brush(x, y, BrushSize).ToList();
        if (!Symmetry) return points;
        return points.Concat(points.Select(p => (Width - 1 - p.X, p.Y))).Distinct();
    }

    /// <summary>
    /// Pixel-perfect strokes (Aseprite): after adding a point, an L-shaped corner's middle pixel is put back, so a
    /// 1-px stroke stays one pixel wide on diagonals. Returns the corner to restore, if any.
    /// </summary>
    private static (int X, int Y)? PixelPerfectCorner(List<(int X, int Y)> path)
    {
        if (path.Count < 3) return null;
        var (a, b, c) = (path[^3], path[^2], path[^1]);
        if (Math.Abs(a.X - c.X) != 1 || Math.Abs(a.Y - c.Y) != 1) return null;
        if (!((b.X == a.X || b.Y == a.Y) && (b.X == c.X || b.Y == c.Y))) return null;
        path.RemoveAt(path.Count - 2);
        return b;
    }

    /// <summary>Onion skin: earlier frames tinted red, later ones blue, nearer frames stronger (Aseprite, Animate).</summary>
    private (RgbaImage? Before, RgbaImage? After) OnionImages(Core.Models.Pose pose, int index)
    {
        int count = Model.Frames.Count;
        RgbaImage? Build(int frames, int step, uint tint)
        {
            if (frames <= 0 || count < 2) return null;
            RgbaImage? result = null;
            for (int k = frames; k >= 1; k--)
            {
                int at = index + step * k;
                if (OnionWrap) at = ((at % count) + count) % count;
                if (at < 0 || at >= count || at == index) continue;
                result ??= StudioImages.Blank(Width, Height);
                double strength = 1 - (k - 1) / (double)(frames + 1);
                PixelTools.Over(result, Context.RenderFrame(pose, at).Image, strength, tint, OnionTint ? 0.55 : 0);
            }
            return result;
        }
        return (Build(OnionBefore, -1, 0xFFE0453A), Build(OnionAfter, 1, 0xFF3A8FE0));
    }
}
