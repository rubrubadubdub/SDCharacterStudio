using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Editor.Studio;

namespace SDCharacterStudio.Editor.ViewModels;

/// <summary>
/// Selections of any shape (D-49, after Aseprite): the marquee, the lasso and the magic wand, with Shift to add,
/// Alt to subtract and both to intersect; invert, reselect, and Isolate, which dims everything outside the
/// selection (edits already stay inside it). <see cref="Selection"/> is the shape's bounds.
/// </summary>
public partial class PoseDocumentViewModel
{
    /// <summary>The selected pixels (null: nothing selected). The canvas outlines it.</summary>
    [ObservableProperty] private SelectionMask? selectionShape;
    /// <summary>Dim everything outside the selection (Shift+I).</summary>
    [ObservableProperty] private bool isolate;
    [ObservableProperty] private BitmapSource? isolateOverlay;
    /// <summary>The magic wand's tolerance: how different a colour may be and still be picked up (0–64).</summary>
    [ObservableProperty] private int wandTolerance;

    private bool settingShape;
    private SelectionMask? lastSelection;
    private SelectionMask? selectionBase;
    private SelectionMode selectionMode;
    private readonly List<(int X, int Y)> lassoPath = new();
    /// <summary>The Move tool lifted the whole picture (no selection): drop the selection after the drag.</summary>
    private bool moveDropsSelection;
    private SelectionMask? selectionBeforeGesture;
    private PixelRect? selectionBoundsBeforeGesture;
    private SelectionMask? lastSelectionBeforeGesture;
    private bool selectionGestureActive;

    public bool IsLassoTool => Tool == StudioTool.Lasso;
    public bool IsWandTool => Tool == StudioTool.Wand;
    /// <summary>Tools that make selections.</summary>
    public static bool IsSelectionTool(StudioTool tool) => tool is StudioTool.Select or StudioTool.Lasso or StudioTool.Wand;

    partial void OnSelectionChanged(PixelRect? value)
    {
        if (settingShape) return;
        if (value is not { } rect || rect.IsEmpty) { SetShape(null); return; }
        // A selection moved by a drag or a nudge keeps its shape.
        if (SelectionShape is { IsRectangle: false } shape && shape.Bounds.Width == rect.Width && shape.Bounds.Height == rect.Height)
        { SetShape(shape.Offset(rect.X - shape.Bounds.X, rect.Y - shape.Bounds.Y), keepBounds: rect); return; }
        SetShape(SelectionMask.FromRect(Width, Height, rect), keepBounds: rect);
    }

    /// <summary>Make <paramref name="shape"/> the selection (and its bounds <see cref="Selection"/>).</summary>
    private void SetShape(SelectionMask? shape, PixelRect? keepBounds = null)
    {
        if (shape is { IsEmpty: true }) shape = null;
        settingShape = true;
        try
        {
            SelectionShape = shape;
            Selection = shape == null ? null : keepBounds ?? shape.Bounds;
            if (shape != null) lastSelection = shape;
        }
        finally { settingShape = false; }
        RefreshIsolate();
    }

    partial void OnIsolateChanged(bool value) => RefreshIsolate();
    partial void OnWandToleranceChanged(int value) { if (value < 0 || value > 64) WandTolerance = Math.Clamp(value, 0, 64); }

    private void RefreshIsolate()
    {
        if (!Isolate || SelectionShape is not { } shape) { IsolateOverlay = null; return; }
        var overlay = StudioImages.Blank(Width, Height);
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                if (!shape.Contains(x, y)) overlay.Pixels[y * Width + x] = 0xFF101114;
        IsolateOverlay = StudioImages.ToBitmap(overlay);
    }

    /// <summary>The selection tools' press. Returns false when the press is a drag of what the selection shows instead.</summary>
    private bool TrySelectDown(int x, int y, bool shift, bool ctrl, bool alt)
    {
        if (!IsSelectionTool(Tool)) return false;
        bool modifying = shift || alt;
        // A plain press inside the selection drags what it shows (the flat picture or the layers handle that).
        if (!modifying && Selection is { } rect && rect.Contains(x, y) && (SelectionShape?.Contains(x, y) ?? true) && Tool != StudioTool.Wand) return false;
        // Advanced puppet parts (D-48 stage 2): pressing on one drags it.
        if (!modifying && !ctrl && Tool == StudioTool.Select && !IsFlat && PartAt(x, y) != null) return false;
        selectionMode = SelectionMask.ModeFor(shift, alt);
        selectionBeforeGesture = SelectionShape;
        selectionBoundsBeforeGesture = Selection;
        lastSelectionBeforeGesture = lastSelection;
        selectionBase = selectionMode == SelectionMode.Replace ? null : SelectionShape;
        switch (Tool)
        {
            case StudioTool.Wand:
                selectionGestureActive = false;
                var picture = Context.RenderFrame(ForRender(), FrameIndex).Image;
                var picked = SelectionMask.FromCells(Width, Height, PixelTools.Wand(picture, x, y, WandTolerance, global: ctrl));
                SetShape(Combine(picked));
                selectionBase = null;
                return true;
            case StudioTool.Lasso:
                selectionGestureActive = true;
                lassoPath.Clear();
                lassoPath.Add((x, y));
                strokeStart = (x, y);
                SetShape(Combine(SelectionMask.FromLasso(Width, Height, lassoPath)));
                return true;
            default:
                selectionGestureActive = true;
                strokeStart = (x, y);
                SetShape(Combine(SelectionMask.FromRect(Width, Height, new PixelRect(x, y, 1, 1))));
                return true;
        }
    }

    private SelectionMask Combine(SelectionMask made) => selectionBase?.Combine(made, selectionMode) ?? made;

    private bool TrySelectMove(int x, int y)
    {
        if (!IsSelectionTool(Tool) || strokeStart is not { } start || stroke != null || flatGesture != null || lift != null) return false;
        if (Tool == StudioTool.Lasso)
        {
            if (lassoPath[^1] != (x, y)) lassoPath.Add((x, y));
            SetShape(Combine(SelectionMask.FromLasso(Width, Height, lassoPath)));
        }
        else SetShape(Combine(SelectionMask.FromRect(Width, Height, PixelRect.FromCorners(start.X, start.Y, x, y))));
        return true;
    }

    private bool TrySelectUp()
    {
        if (!IsSelectionTool(Tool) || strokeStart is not { } start || stroke != null) return false;
        strokeStart = null;
        // A plain click (no drag) with the marquee drops the selection, like every editor.
        if (Tool == StudioTool.Select && selectionMode == SelectionMode.Replace && Selection is { Width: 1, Height: 1 } && Selection.Value.X == start.X && Selection.Value.Y == start.Y) SetShape(null);
        lassoPath.Clear();
        selectionBase = null;
        selectionGestureActive = false;
        return true;
    }

    private void CancelSelectionGesture()
    {
        if (!selectionGestureActive) return;
        SelectionShape = selectionBeforeGesture;
        Selection = selectionBoundsBeforeGesture;
        lastSelection = lastSelectionBeforeGesture;
        strokeStart = null;
        lassoPath.Clear();
        selectionBase = null;
        selectionGestureActive = false;
        moveDropsSelection = false;
        RefreshIsolate();
    }

    [RelayCommand]
    private void InvertSelection() => SetShape((SelectionShape ?? SelectionMask.Empty(Width, Height)).Invert());

    [RelayCommand]
    private void Reselect() { if (lastSelection is { } last && last.Width == Width && last.Height == Height) SetShape(last); }

    [RelayCommand]
    private void ToggleIsolate()
    {
        if (!Isolate && SelectionShape == null) { Ui.Toast("Select something first", "Isolate dims everything outside the selection so you can work on just that part.", ToastKind.Info); return; }
        Isolate = !Isolate;
    }

    /// <summary>The Move tool with nothing selected moves the whole picture: select it for the drag.</summary>
    private void PrepareMove()
    {
        moveDropsSelection = false;
        if (Tool != StudioTool.Move || SelectionShape != null) return;
        SetShape(SelectionMask.FromRect(Width, Height, new PixelRect(0, 0, Width, Height)));
        moveDropsSelection = true;
    }

    private void FinishMove()
    {
        if (!moveDropsSelection) return;
        moveDropsSelection = false;
        SetShape(null);
    }
}
