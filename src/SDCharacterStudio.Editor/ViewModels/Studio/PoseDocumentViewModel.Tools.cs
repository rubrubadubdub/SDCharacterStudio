using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Models;

namespace SDCharacterStudio.Editor.ViewModels;

/// <summary>Where a layer's pixels live and where they sit on the canvas.</summary>
public sealed record PaintTarget(string Path, RgbaImage Image, int CellX, int CellY, int CellWidth, int CellHeight, int OriginX, int OriginY, bool Flip)
{
    /// <summary>Canvas pixel → image pixel, or null outside the layer's cell.</summary>
    public (int X, int Y)? ToImage(int x, int y)
    {
        int lx = x - OriginX, ly = y - OriginY;
        if (Flip) lx = CellWidth - 1 - lx;
        return lx < 0 || ly < 0 || lx >= CellWidth || ly >= CellHeight ? null : (CellX + lx, CellY + ly);
    }
    public PixelRect Cell => new(CellX, CellY, CellWidth, CellHeight);
}

public enum PointerButton { Primary, Secondary }

/// <summary>Pixel tools on the pose canvas (doc 07 §6.2). Coordinates are frame pixels.</summary>
public partial class PoseDocumentViewModel
{
    private PaintTarget? stroke;
    private RgbaImage? strokeBase;
    private uint strokeColor;
    private (int X, int Y)? strokeStart, strokeLast, lastPoint;
    private bool strokeChanged;
    /// <summary>The freehand stroke's points so far (for pixel-perfect corners).</summary>
    private readonly List<(int X, int Y)> strokePath = new();
    private (int X, int Y)? dragFrom;
    private RgbaImage? dragPiece;
    private PixelRect floatingRect;
    private bool dragCopies;
    private static RgbaImage? clipboard;

    public bool IsStroking => stroke != null || strokeStart != null || dragFrom != null || liveErase != null || lift != null || flatGesture != null || partsWorking != null;

    /// <summary>The paint target for a layer: its working copy if a stroke is in progress.</summary>
    public PaintTarget? TargetFor(int layerIndex)
    {
        if (Frame?.Layers.ElementAtOrDefault(layerIndex) is not { } layer) return null;
        string? path; int cellX = 0, cellY = 0;
        switch (layer)
        {
            case PaintLayer paint: path = paint.Image; break;
            case ImageLayer image:
                path = image.Image;
                var size = image.FrameSize ?? Model.FrameSize ?? new[] { Width, Height };
                var sheet = Context.PackImage(path);
                if (sheet == null || size[0] <= 0) return null;
                int columns = Math.Max(1, sheet.Width / size[0]);
                cellX = image.Frame % columns * size[0]; cellY = image.Frame / columns * size[1];
                break;
            default: return null;
        }
        if (path == null || Context.PackImage(path) is not { } pixels) return null;
        int width = layer is ImageLayer { FrameSize: { Length: 2 } fs } ? fs[0] : Width, height = layer is ImageLayer { FrameSize: { Length: 2 } fs2 } ? fs2[1] : Height;
        int originX = layer.Offset[0], originY = layer.Offset[1];
        if (layer.AnchorTo is { From: JArray { Count: 2 } from } anchor && Frame.Anchors.TryGetValue(anchor.Anchor ?? "", out var point) && point is { Length: 2 })
        {
            int fromX = from[0]!.Value<int>(), fromY = from[1]!.Value<int>();
            originX += point[0] - (layer.Flip ? width - 1 - fromX : fromX);
            originY += point[1] - fromY;
        }
        return new PaintTarget(path, pixels, cellX, cellY, width, height, originX, originY, layer.Flip);
    }

    private bool CanPaint(out PaintTarget? target)
    {
        target = null;
        if (SelectedLayer == null || SelectedLayer is LiveLayer or EffectLayer)
        {
            // Drawing needs pixels of its own: add a paint layer above the selection and draw there.
            AddPaintLayer();
        }
        if (IsLayerLocked(LayerIndex)) { Ui.Toast("Layer locked", "Unlock the layer to draw on it.", ToastKind.Info); return false; }
        target = TargetFor(LayerIndex);
        if (target == null) { Ui.Toast("Nothing to draw on", "This layer's image is missing. Pick another layer or add a paint layer.", ToastKind.Warning); return false; }
        return true;
    }

    /// <param name="fx">The pointer in fractional frame pixels (handles of the transform box need better than whole pixels); NaN: the pixel's centre.</param>
    public void PointerDown(int x, int y, PointerButton button, bool shift = false, bool ctrl = false, bool alt = false, double fx = double.NaN, double fy = double.NaN)
    {
        if (IsPlaying) StopPlayback();
        if (TryTransformDown(double.IsNaN(fx) ? x + 0.5 : fx, double.IsNaN(fy) ? y + 0.5 : fy)) return;   // the floating piece's box
        if (TryPartsDown(x, y)) return;   // Show parts: the brush paints her body parts
        uint color = button == PointerButton.Primary ? Palette.Primary : Palette.Secondary;
        // Alt with any painting tool is a quick eyedropper (Aseprite); the tool stays.
        if (alt && IsPaintTool(Tool)) { Pick(x, y, button); return; }
        if (TrySelectDown(x, y, shift, ctrl, alt)) return;   // marquee, lasso and wand (Shift adds, Alt takes away)
        if (IsPeeking) return;   // her other frame is only there to select from
        PrepareMove();
        if (TryFlatDown(x, y, ctrl, color)) return;   // one flat picture: erase, move, fill, draw on it (D-48)
        if (TryDirectDown(x, y, ctrl)) return;        // layered frames: erase and move what you see
        var tool = Tool;
        switch (tool)
        {
            case StudioTool.Picker:
                Pick(x, y, button);
                Tool = lastPaintTool ?? StudioTool.Pencil;   // pick, then straight back to painting
                return;
            case StudioTool.Anchor:
                SetAnchor(AnchorName, x, y);
                return;
            case StudioTool.Select or StudioTool.Move:
                if (Selection is { } rect && (rect.Contains(x, y) || Tool == StudioTool.Move) && CanPaint(out var moving))
                {
                    BeginStroke(moving!, 0);
                    floatingRect = rect;
                    dragPiece = PixelTools.Copy(strokeBase!, Translate(rect, moving!).Clip(strokeBase!.Width, strokeBase.Height));
                    dragCopies = ctrl;   // Ctrl+drag copies instead of moving
                    dragFrom = (x, y);
                    return;
                }
                strokeStart = (x, y);
                Selection = new PixelRect(x, y, 1, 1);
                return;
        }
        if (!CanPaint(out var target)) return;
        BeginStroke(target!, tool == StudioTool.Eraser ? 0u : color);
        strokeStart = shift && lastPoint is { } from && tool == StudioTool.Pencil ? from : (x, y);
        switch (tool)
        {
            case StudioTool.Fill:
                if (target!.ToImage(x, y) is { } p) strokeChanged |= FillAt(p.X, p.Y, ctrl);
                EndStroke("Fill");
                return;
            case StudioTool.Pencil or StudioTool.Eraser:
                strokePath.Clear();
                DrawSegment(strokeStart.Value, (x, y));
                break;
            default:
                Preview(x, y, shift);
                break;
        }
        strokeLast = (x, y);
    }

    public void PointerMove(int x, int y, bool shift = false, double fx = double.NaN, double fy = double.NaN)
    {
        HoverText = x >= 0 && y >= 0 && x < Width && y < Height ? $"{x}, {y}" + Describe(x, y) : null;
        if (TryTransformMove(double.IsNaN(fx) ? x + 0.5 : fx, double.IsNaN(fy) ? y + 0.5 : fy, shift)) return;
        if (TryPartsMove(x, y)) return;
        if (TrySelectMove(x, y)) return;
        if (TryFlatMove(x, y)) return;
        if (TryDirectMove(x, y)) return;
        if (dragFrom is { } from && stroke != null && dragPiece != null)
        {
            strokeBase!.Pixels.CopyTo(stroke.Image.Pixels, 0);
            if (!dragCopies) PixelTools.Clear(stroke.Image, Translate(floatingRect, stroke).Clip(stroke.Image.Width, stroke.Image.Height));
            var moved = floatingRect with { X = floatingRect.X + x - from.X, Y = floatingRect.Y + y - from.Y };
            var at = Translate(moved, stroke);
            PixelTools.Paste(stroke.Image, dragPiece, at.X, at.Y);
            Selection = moved;
            strokeChanged = true;
            Render();
            return;
        }
        if (stroke == null) return;
        if (Tool is StudioTool.Pencil or StudioTool.Eraser) { DrawSegment(strokeLast ?? (x, y), (x, y)); strokeLast = (x, y); }
        else if (Tool is StudioTool.Line or StudioTool.Rectangle or StudioTool.Ellipse) Preview(x, y, shift);
    }

    public void PointerUp(int x, int y)
    {
        if (TryTransformUp()) return;
        if (TryPartsUp()) return;
        if (TryFlatUp()) { FinishMove(); return; }
        if (TryDirectUp()) { FinishMove(); return; }
        if (TrySelectUp()) return;
        if (dragFrom != null) { dragFrom = null; dragPiece = null; EndStroke("Move pixels"); FinishMove(); return; }
        if (stroke == null) return;
        lastPoint = (x, y);
        EndStroke(Tool switch { StudioTool.Eraser => "Erase", StudioTool.Line => "Draw line", StudioTool.Rectangle => "Draw rectangle", StudioTool.Ellipse => "Draw ellipse", _ => "Draw" });
    }

    /// <summary>Abandon an in-progress stroke (Esc, or the canvas lost the mouse).</summary>
    public void CancelStroke()
    {
        if (stroke != null) Context.Overrides.Remove(stroke.Path);
        bool dropSelection = moveDropsSelection;
        CancelSelectionGesture();
        if (dragFrom != null) Selection = floatingRect;
        stroke = null; strokeBase = null; dragFrom = null; dragPiece = null; strokeStart = null; strokeLast = null;
        strokePath.Clear();
        strokeChanged = false;
        if (partsWorking != null) { partsWorking = null; RefreshPartsOverlay(); }
        CancelDirect();
        CancelFlat();
        CancelTransform();
        if (dropSelection) SetShape(null);
        moveDropsSelection = false;
        Render();
    }

    private void BeginStroke(PaintTarget target, uint color)
    {
        var working = StudioImages.Clone(target.Image);
        strokeBase = StudioImages.Clone(target.Image);
        stroke = target with { Image = working };
        strokeColor = color;
        strokeChanged = false;
        Context.Overrides[target.Path] = working;
    }

    private void EndStroke(string label)
    {
        if (stroke is not { } done) return;
        var before = strokeBase;
        stroke = null; strokeStart = null; strokeLast = null; strokeBase = null; strokePath.Clear();
        try
        {
            // Drawing on a picture with several frames picked draws the same pixels on all of them.
            bool all = HasSeveralPicked && before != null && Flat is { } flat && string.Equals(flat.Paint.Image, done.Path, StringComparison.OrdinalIgnoreCase);
            if (strokeChanged) Save(all ? OnPicked(label) : label, _ => { }, lib =>
            {
                lib.WriteImage(done.Path, done.Image);
                if (all) CopyChangesToOtherPicked(lib, null, (before!, done.Image));
            }, rebuildFields: false);
            if (strokeChanged && strokeColor >> 24 != 0) Palette.Remember(strokeColor);
        }
        finally
        {
            Context.Overrides.Remove(done.Path);
            Context.InvalidatePack();
        }
        Render();
        RenderThumbnails();
    }

    /// <summary>A freehand stretch: a dab of the brush at each point, pixel-perfect corners for a 1-px brush.</summary>
    private void DrawSegment((int X, int Y) from, (int X, int Y) to)
    {
        if (stroke == null || strokeBase == null) return;
        foreach (var point in PixelTools.Line(from.X, from.Y, to.X, to.Y))
        {
            if (strokePath.Count > 0 && strokePath[^1] == point) continue;
            strokePath.Add(point);
            foreach (var (px, py) in BrushAt(point.X, point.Y))
                if (stroke.ToImage(px, py) is { } p && InsideSelection(px, py)) strokeChanged |= PixelTools.Set(stroke.Image, p.X, p.Y, strokeColor);
            if (PixelPerfect && BrushSize == 1 && PixelPerfectCorner(strokePath) is { } corner)
                foreach (var (px, py) in BrushAt(corner.X, corner.Y))
                    if (stroke.ToImage(px, py) is { } p) PixelTools.Set(stroke.Image, p.X, p.Y, PixelTools.Get(strokeBase, p.X, p.Y));
        }
        Render();
    }

    private void Preview(int x, int y, bool shift)
    {
        if (stroke == null || strokeStart is not { } start || strokeBase == null) return;
        strokeBase.Pixels.CopyTo(stroke.Image.Pixels, 0);
        if (Tool == StudioTool.Line)
        {
            (int X, int Y) end = shift ? PixelTools.Snap45(start.X, start.Y, x, y) : (x, y);
            foreach (var (px, py) in PixelTools.Line(start.X, start.Y, end.X, end.Y))
                if (stroke.ToImage(px, py) is { } p && InsideSelection(px, py)) PixelTools.Set(stroke.Image, p.X, p.Y, strokeColor);
        }
        else if (Tool == StudioTool.Ellipse)
        {
            if (shift) { int side = Math.Max(Math.Abs(x - start.X), Math.Abs(y - start.Y)); x = start.X + Math.Sign(x - start.X) * side; y = start.Y + Math.Sign(y - start.Y) * side; }
            foreach (var (px, py) in PixelTools.Ellipse(start.X, start.Y, x, y, FilledShapes))
                if (stroke.ToImage(px, py) is { } p && InsideSelection(px, py)) PixelTools.Set(stroke.Image, p.X, p.Y, strokeColor);
        }
        else
        {
            if (shift) { int side = Math.Max(Math.Abs(x - start.X), Math.Abs(y - start.Y)); x = start.X + Math.Sign(x - start.X) * side; y = start.Y + Math.Sign(y - start.Y) * side; }
            var rect = PixelRect.FromCorners(start.X, start.Y, x, y);
            for (int py = rect.Y; py < rect.Y + rect.Height; py++)
                for (int px = rect.X; px < rect.X + rect.Width; px++)
                {
                    bool edge = px == rect.X || py == rect.Y || px == rect.X + rect.Width - 1 || py == rect.Y + rect.Height - 1;
                    if ((FilledShapes || edge) && stroke.ToImage(px, py) is { } p && InsideSelection(px, py)) PixelTools.Set(stroke.Image, p.X, p.Y, strokeColor);
                }
        }
        strokeChanged = !strokeBase.Pixels.AsSpan().SequenceEqual(stroke.Image.Pixels);
        Render();
    }

    /// <summary>Flood fill within the layer's cell (Ctrl: every pixel of that colour in the cell).</summary>
    private bool FillAt(int x, int y, bool global)
    {
        if (stroke is not { } target) return false;
        int localX = x - target.CellX, localY = y - target.CellY;
        uint picked = PixelTools.Get(target.Image, x, y);
        if (picked == strokeColor) return false;
        bool Same(int px, int py) => PixelTools.Get(target.Image, target.CellX + px, target.CellY + py) == picked
            && InsideSelection(target.OriginX + (target.Flip ? target.CellWidth - 1 - px : px), target.OriginY + py);
        if (!Same(localX, localY)) return false;
        var cells = global
            ? Enumerable.Range(0, target.CellWidth * target.CellHeight).Select(i => (X: i % target.CellWidth, Y: i / target.CellWidth)).Where(p => Same(p.X, p.Y)).ToList()
            : PixelTools.Flood(target.CellWidth, target.CellHeight, localX, localY, Same);
        bool changed = false;
        foreach (var (px, py) in cells) changed |= PixelTools.Set(target.Image, target.CellX + px, target.CellY + py, strokeColor);
        return changed;
    }

    private bool InsideSelection(int x, int y) => SelectionShape?.Contains(x, y) ?? (Selection is not { } rect || rect.Contains(x, y));

    /// <summary>A canvas rectangle in a target's image coordinates (unflipped targets only; flipped ones use the cell).</summary>
    private static PixelRect Translate(PixelRect rect, PaintTarget target) =>
        target.Flip ? new PixelRect(target.CellX + target.CellWidth - (rect.X - target.OriginX) - rect.Width, target.CellY + rect.Y - target.OriginY, rect.Width, rect.Height)
            : new PixelRect(target.CellX + rect.X - target.OriginX, target.CellY + rect.Y - target.OriginY, rect.Width, rect.Height);

    private string Describe(int x, int y)
    {
        if (Context.PackImage(TargetFor(LayerIndex)?.Path) is { } image && TargetFor(LayerIndex)?.ToImage(x, y) is { } p)
        {
            uint raw = PixelTools.Get(image, p.X, p.Y);
            return raw >> 24 == 0 ? "" : " · " + Palette.NameOf(raw);
        }
        return "";
    }

    private void Pick(int x, int y, PointerButton button)
    {
        uint color;
        if (PickFromLayer && TargetFor(LayerIndex) is { } target && target.ToImage(x, y) is { } p) color = PixelTools.Get(target.Image, p.X, p.Y);
        else
        {
            // The composite with raw slot colours, so picking a slot colour keeps it a slot.
            var raw = PackWriter.Clone(Model);
            foreach (var layer in raw.Frames.SelectMany(f => f.Layers).Where(l => l is not LiveLayer)) layer.ColorSlots = null;
            color = PixelTools.Get(Context.RenderFrame(raw, FrameIndex).Image, x, y);
            // Her own pixels come back as her slot colour, so what you paint with it follows outfits too.
            color = Palette.SlotFor(color) ?? color;
        }
        if (button == PointerButton.Primary) Palette.Primary = color; else Palette.Secondary = color;
    }

    public void SetAnchor(string name, int x, int y)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        x = Math.Clamp(x, 0, Width - 1); y = Math.Clamp(y, 0, Height - 1);
        var frames = ApplyToAllFrames ? Enumerable.Range(0, Model.Frames.Count) : new[] { FrameIndex };
        Save($"Place {name}", pose => { foreach (int i in frames) pose.Frames[i].Anchors[name] = new[] { x, y }; });
    }

    // ---------------- selection commands ----------------

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void SelectAll() { Tool = StudioTool.Select; Selection = new PixelRect(0, 0, Width, Height); }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void Deselect() => Selection = null;

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void CopySelection()
    {
        if (IsPeeking) { CopyFromPeek(); return; }
        if (FlatCopy()) return;
        if (TargetFor(LayerIndex) is not { } target) return;
        var rect = (Selection ?? new PixelRect(0, 0, Width, Height)).Clip(Width, Height);
        if (rect.IsEmpty) return;
        clipboard = StudioImages.Blank(rect.Width, rect.Height);
        for (int y = 0; y < rect.Height; y++)
            for (int x = 0; x < rect.Width; x++)
                if (InsideSelection(rect.X + x, rect.Y + y) && target.ToImage(rect.X + x, rect.Y + y) is { } p)
                    PixelTools.Set(clipboard, x, y, PixelTools.Get(target.Image, p.X, p.Y));
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void CutSelection() { CopySelection(); DeleteSelection(); }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void PasteSelection()
    {
        if (IsFlat) { FlatPaste(); return; }
        if (clipboard == null || !CanPaint(out var target)) return;
        var rect = Selection ?? new PixelRect(0, 0, clipboard.Width, clipboard.Height);
        var image = StudioImages.Clone(target!.Image);
        for (int y = 0; y < clipboard.Height; y++)
            for (int x = 0; x < clipboard.Width; x++)
                if (PixelTools.Get(clipboard, x, y) is var color && color >> 24 != 0 && target.ToImage(rect.X + x, rect.Y + y) is { } p)
                    PixelTools.Set(image, p.X, p.Y, color);
        Save("Paste", _ => { }, lib => lib.WriteImage(target.Path, image), rebuildFields: false);
        Selection = rect with { Width = clipboard.Width, Height = clipboard.Height };
        Tool = StudioTool.Select;
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void DeleteSelection()
    {
        if (IsTransforming) CancelTransform();
        var shape = SelectionShape;
        void Clear(RgbaImage image, PixelRect rect)
        {
            for (int y = rect.Y; y < rect.Y + rect.Height; y++)
                for (int x = rect.X; x < rect.X + rect.Width; x++)
                    if (shape?.Contains(x, y) ?? true) image.Pixels[y * image.Width + x] = 0;
        }
        if (!FlatEdit("Clear pixels", Clear)) EditPixels("Clear pixels", (image, rect) => PixelTools.Clear(image, rect));
    }

    /// <summary>A shaped selection (lasso, wand) or a floating piece flips through the transform; a box flips its pixels in place.</summary>
    private bool Shaped => IsTransforming || SelectionShape is { IsRectangle: false };

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void MirrorSelection()
    {
        if (IsFlat && Shaped) { FlipFloatingHorizontally(); return; }
        if (!FlatEdit("Mirror pixels", (image, rect) => PixelTools.Mirror(image, rect)))
        {
            var shape = SelectionShape;
            var bounds = Selection;
            if (EditPixels("Mirror pixels", (image, rect) => PixelTools.Mirror(image, rect)) && shape != null && bounds is { } area)
                SetShape(SelectionMask.FromCells(Width, Height, shape.Cells().Select(p => (2 * area.X + area.Width - 1 - p.X, p.Y))));
        }
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void FlipSelectionVertical()
    {
        if (IsFlat && Shaped) { FlipFloatingVertically(); return; }
        if (!FlatEdit("Flip pixels", (image, rect) => PixelTools.FlipVertical(image, rect)))
        {
            var shape = SelectionShape;
            var bounds = Selection;
            if (EditPixels("Flip pixels", (image, rect) => PixelTools.FlipVertical(image, rect)) && shape != null && bounds is { } area)
                SetShape(SelectionMask.FromCells(Width, Height, shape.Cells().Select(p => (p.X, 2 * area.Y + area.Height - 1 - p.Y))));
        }
    }

    public void NudgeSelection(int dx, int dy)
    {
        if (NudgeFloating(dx, dy)) return;
        if (Selection is not { } rect) return;
        if (IsFlat) { FlatNudge(dx, dy); return; }
        if (!EditPixels("Nudge pixels", (image, r) => PixelTools.Move(image, r, dx, dy))) return;
        Selection = rect with { X = rect.X + dx, Y = rect.Y + dy };
    }

    private bool EditPixels(string label, Action<RgbaImage, PixelRect> edit)
    {
        if (IsLayerLocked(LayerIndex) || TargetFor(LayerIndex) is not { } target) return false;
        var image = StudioImages.Clone(target.Image);
        var rect = (Selection ?? new PixelRect(0, 0, Width, Height)).Clip(Width, Height);
        if (rect.IsEmpty) return false;
        // Work in canvas coordinates so offsets and flips behave like the brush. Only selected pixels are lifted;
        // transparent corners of a moved or flipped piece leave the unselected picture underneath intact.
        var selected = StudioImages.Blank(Width, Height);
        for (int y = rect.Y; y < rect.Y + rect.Height; y++)
            for (int x = rect.X; x < rect.X + rect.Width; x++)
                if (InsideSelection(x, y) && target.ToImage(x, y) is { } p)
                {
                    PixelTools.Set(selected, x, y, PixelTools.Get(image, p.X, p.Y));
                    PixelTools.Set(image, p.X, p.Y, 0);
                }
        edit(selected, rect);
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                if (PixelTools.Get(selected, x, y) is var color && color >> 24 != 0 && target.ToImage(x, y) is { } p)
                    PixelTools.Set(image, p.X, p.Y, color);
        if (image.Pixels.AsSpan().SequenceEqual(target.Image.Pixels)) return true;
        Save(label, _ => { }, lib => lib.WriteImage(target.Path, image), rebuildFields: false);
        Context.InvalidatePack();
        Render(); RenderThumbnails();
        return true;
    }

    /// <summary>Single-key tools (doc 07 §6.2). Returns true when handled.</summary>
    public bool HandleKey(string key, bool ctrl, bool shift)
    {
        if (IsPeeking)
            switch (key)
            {
                case "Escape": EndPeek(); return true;
                case "C" when ctrl: CopyFromPeek(); return true;
                case "Return" or "Enter": CopyFromPeek(); return true;
            }
        if (IsTransforming)
            switch (key)
            {
                case "Return" or "Enter": ApplyTransform(); return true;
                case "Escape": CancelTransform(); return true;
            }
        if (ctrl && shift)
            switch (key)
            {
                case "D": Reselect(); return true;
                case "I": InvertSelection(); return true;
                case "R": RotateLeft(); return true;
            }
        if (ctrl)
            switch (key)
            {
                case "T": Transform(); return true;
                case "A": SelectAll(); return true;
                case "C": CopySelection(); return true;
                case "X": CutSelection(); return true;
                case "V": PasteSelection(); return true;
                case "D": Deselect(); return true;
                default: return false;
            }
        int step = shift ? 4 : 1;
        if (shift)
            switch (key)
            {
                case "H": MirrorSelection(); return true;
                case "V": FlipSelectionVertical(); return true;
                case "U": Tool = StudioTool.Ellipse; return true;
                case "R": RotateRight(); return true;
                case "I": ToggleIsolate(); return true;
                case "OemQuestion" or "Oem2": ShowShortcuts(); return true;
            }
        switch (key)
        {
            case "B": Tool = StudioTool.Pencil; return true;
            case "E": Tool = StudioTool.Eraser; return true;
            case "G": Tool = StudioTool.Fill; return true;
            case "L": Tool = StudioTool.Line; return true;
            case "U": Tool = StudioTool.Rectangle; return true;
            case "I": Tool = StudioTool.Picker; return true;
            case "M": Tool = StudioTool.Select; return true;
            case "V": Tool = StudioTool.Move; return true;
            case "Q": Tool = StudioTool.Lasso; return true;
            case "W": Tool = StudioTool.Wand; return true;
            case "N" when Advanced: Tool = StudioTool.Anchor; return true;
            case "X": Palette.SwapCommand.Execute(null); return true;
            case "H": MirrorSelection(); return true;
            case "O" or "F3": ShowOnion = !ShowOnion; return true;
            case "F1": ShowShortcuts(); return true;
            case "OemOpenBrackets" or "Oem4": SmallerBrush(); return true;
            case "OemCloseBrackets" or "Oem6": BiggerBrush(); return true;
            case "Space" or "Return" or "Enter": TogglePlay(); return true;
            case "Home": if (Model.Frames.Count > 0) FrameIndex = 0; return true;
            case "End": if (Model.Frames.Count > 0) FrameIndex = Model.Frames.Count - 1; return true;
            case "OemComma" or "Left" when Selection == null || key == "OemComma": PreviousFrame(); return true;
            case "OemPeriod" or "Right" when Selection == null || key == "OemPeriod": NextFrame(); return true;
            case "Left": NudgeSelection(-step, 0); return true;
            case "Right": NudgeSelection(step, 0); return true;
            case "Up" when Selection != null: NudgeSelection(0, -step); return true;
            case "Down" when Selection != null: NudgeSelection(0, step); return true;
            case "Delete": if (Selection != null) DeleteSelection(); else DeleteLayer(); return true;
            case "Escape": if (IsStroking) CancelStroke(); else Selection = null; return true;
            default: return false;
        }
    }
}
