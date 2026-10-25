using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Editor.Studio;

namespace SDCharacterStudio.Editor.ViewModels;

/// <summary>The transform box on the canvas: its corners, pivot and rotation handle, in frame pixels.</summary>
public sealed record TransformBox((double X, double Y)[] Corners, (double X, double Y) Pivot, (double X, double Y) RotateHandle);

/// <summary>
/// Free transform (D-49, after Aseprite and Animate): the selection's visible pixels float above the picture. Drag
/// inside to move, a corner or edge to scale (from the opposite side; Shift keeps the proportions), the round handle
/// to rotate (Shift: 15° steps) and the pivot to rotate about another point. Flip and quarter turns too. Live pixels
/// stay live: a pixel is moved whole, never mixed (RotSprite for any angle). Enter or a click outside applies it as
/// one undo step; Esc puts everything back.
/// </summary>
public partial class PoseDocumentViewModel
{
    private sealed class Floating
    {
        public required string MapPath { get; init; }
        public required string PaintPath { get; init; }
        public required RgbaImage BaseMap { get; init; }
        public required RgbaImage BasePaint { get; init; }
        public required PixelRect Bounds { get; init; }
        public required int[] Grid { get; init; }
        public required List<(uint Map, uint Paint, uint Shown)> Cells { get; init; }
        public required SelectionMask Before { get; init; }
        public required PieceTransform Transform { get; set; }
        public string Label { get; set; } = "Transform";
        /// <summary>Pasted or copied in (not lifted): applying it is a change even where it is, and it's for this frame only.</summary>
        public bool Pasted { get; init; }
        public string? Handle { get; set; }
        public (double X, double Y) DragFrom { get; set; }
        public PieceTransform DragStart { get; set; } = null!;
    }

    private Floating? floating;
    [ObservableProperty] private TransformBox? transformBox;

    public bool IsTransforming => floating != null;
    public PieceTransform? CurrentTransform => floating?.Transform;

    /// <summary>The rotation in degrees (the options bar's box).</summary>
    public double TransformAngle
    {
        get => floating?.Transform.Angle ?? 0;
        set { if (floating is { } f && Math.Abs(value - f.Transform.Angle) > 1e-9) Retransform(f.Transform with { Angle = Normalize(value) }); }
    }
    /// <summary>The width and height in pixels, as scaled.</summary>
    public int TransformWidth
    {
        get => floating is { } f ? (int)Math.Round(Math.Abs(f.Transform.ScaleX) * f.Bounds.Width) : 0;
        set { if (floating is { } f && value > 0) Retransform(f.Transform with { ScaleX = Math.Sign(f.Transform.ScaleX == 0 ? 1 : f.Transform.ScaleX) * value / (double)f.Bounds.Width }); }
    }
    public int TransformHeight
    {
        get => floating is { } f ? (int)Math.Round(Math.Abs(f.Transform.ScaleY) * f.Bounds.Height) : 0;
        set { if (floating is { } f && value > 0) Retransform(f.Transform with { ScaleY = Math.Sign(f.Transform.ScaleY == 0 ? 1 : f.Transform.ScaleY) * value / (double)f.Bounds.Height }); }
    }

    private static double Normalize(double angle) { angle %= 360; if (angle > 180) angle -= 360; if (angle <= -180) angle += 360; return angle; }

    /// <summary>Lift the selection's visible pixels (all of them with nothing selected) into a floating piece. False when there's nothing to float.</summary>
    public bool BeginTransform(string label = "Transform", bool copy = false)
    {
        if (floating != null) return true;
        if (IsStroking) return false;
        if (Flat is not { } flat || FlatImages() is not { } images)
        {
            Ui.Toast("One picture only", "Transforms work on a pose that is one picture. Use Make it one picture first, or turn Advanced off for new poses.", ToastKind.Info);
            return false;
        }
        var shape = SelectionShape ?? SelectionMask.FromRect(Width, Height, new PixelRect(0, 0, Width, Height));
        var shown = Context.RenderFrame(Model, FrameIndex).Image;
        var cells = new List<(uint Map, uint Paint, uint Shown)>();
        var lifted = new List<(int X, int Y)>();
        var baseMap = StudioImages.Clone(images.Map); var basePaint = StudioImages.Clone(images.Paint);
        foreach (var (x, y) in shape.Cells())
        {
            int i = y * Width + x;
            if (shown.Pixels[i] >> 24 == 0) continue;   // only what you see floats
            cells.Add((images.Map.Pixels[i], images.Paint.Pixels[i], shown.Pixels[i]));
            lifted.Add((x, y));
            if (!copy) { baseMap.Pixels[i] = 0; basePaint.Pixels[i] = 0; }
        }
        if (cells.Count == 0) { Ui.Toast("Nothing to transform", "Select part of the picture first.", ToastKind.Info); return false; }
        var bounds = SelectionMask.FromCells(Width, Height, lifted).Bounds;
        var grid = Enumerable.Repeat(-1, bounds.Width * bounds.Height).ToArray();
        for (int k = 0; k < lifted.Count; k++) grid[(lifted[k].Y - bounds.Y) * bounds.Width + lifted[k].X - bounds.X] = k;
        if (!(IsSelectionTool(Tool) || Tool == StudioTool.Move)) Tool = StudioTool.Select;   // before floating: changing tools applies a transform
        var around = shape.Bounds;   // turns and flips happen about the selection's centre
        floating = new Floating
        {
            MapPath = flat.Live.Map!, PaintPath = flat.Paint.Image!, BaseMap = baseMap, BasePaint = basePaint, Bounds = bounds, Grid = grid, Cells = cells,
            Before = shape, Transform = new PieceTransform(0, 0, 1, 1, 0, around.X + around.Width / 2.0, around.Y + around.Height / 2.0), Label = label,
        };
        ApplyFloating();
        NotifyTransform();
        return true;
    }

    private void Retransform(PieceTransform transform)
    {
        if (floating is not { } f) return;
        f.Transform = transform;
        ApplyFloating();
        NotifyTransform();
    }

    /// <summary>Show the floating piece where it is now (working images, not yet saved).</summary>
    private void ApplyFloating()
    {
        if (floating is not { } f) return;
        var landed = PixelTransform.Land(f.Grid, f.Bounds, f.Transform, Width, Height, i => f.Cells[i].Shown);
        var map = StudioImages.Clone(f.BaseMap); var paint = StudioImages.Clone(f.BasePaint);
        var cells = new List<(int X, int Y)>();
        for (int i = 0; i < landed.Length; i++)
        {
            if (landed[i] < 0) continue;
            var cell = f.Cells[landed[i]];
            map.Pixels[i] = cell.Map; paint.Pixels[i] = cell.Paint;
            cells.Add((i % Width, i / Width));
        }
        Working(f.MapPath, map); Working(f.PaintPath, paint);
        SetShape(cells.Count > 0 ? SelectionMask.FromCells(Width, Height, cells) : null);
        Render();
    }

    private void NotifyTransform()
    {
        if (floating is { } f)
        {
            var t = f.Transform; var b = f.Bounds;
            var corners = new[] { t.Apply(b.X, b.Y), t.Apply(b.X + b.Width, b.Y), t.Apply(b.X + b.Width, b.Y + b.Height), t.Apply(b.X, b.Y + b.Height) };
            var top = ((corners[0].X + corners[1].X) / 2, (corners[0].Y + corners[1].Y) / 2);
            var centre = t.Apply(t.PivotX, t.PivotY);
            // The rotation handle sits outside the top edge, away from the piece.
            double nx = top.Item1 - (corners[0].X + corners[2].X) / 2, ny = top.Item2 - (corners[0].Y + corners[2].Y) / 2, length = Math.Max(1e-6, Math.Sqrt(nx * nx + ny * ny));
            double reach = 18 / Math.Max(1, Zoom);
            TransformBox = new TransformBox(corners, centre, (top.Item1 + nx / length * reach, top.Item2 + ny / length * reach));
        }
        else TransformBox = null;
        OnPropertyChanged(nameof(IsTransforming)); OnPropertyChanged(nameof(TransformAngle)); OnPropertyChanged(nameof(TransformWidth)); OnPropertyChanged(nameof(TransformHeight));
        OnPropertyChanged(nameof(CurrentTransform)); OnPropertyChanged(nameof(ToolHint)); OnPropertyChanged(nameof(ShowsSelectionActions));
    }

    [RelayCommand]
    public void ApplyTransform()
    {
        if (floating is not { } f) return;
        floating = null;
        var map = Context.PackImage(f.MapPath)!; var paint = Context.PackImage(f.PaintPath)!;
        try
        {
            if (!f.Transform.IsIdentity || f.Pasted)
                Save(f.Label, _ => { }, lib =>
                {
                    lib.WriteImage(f.MapPath, map); lib.WriteImage(f.PaintPath, paint);
                    if (!f.Pasted) TransformOtherPicked(lib, f.Before, f.Transform, copy: false);   // the other picked frames too
                }, rebuildFields: false);
        }
        finally { EndPreview(); }
        NotifyTransform();
        RenderThumbnails();
    }

    [RelayCommand]
    public void CancelTransform()
    {
        if (floating is not { } f) return;
        floating = null;
        EndPreview();
        SetShape(f.Before);
        NotifyTransform();
    }

    [RelayCommand] private void Transform() { if (!IsTransforming) BeginTransform(); }
    [RelayCommand] private void FlipFloatingHorizontally() => QuickTransform("Mirror pixels", t => t with { ScaleX = -t.ScaleX });
    [RelayCommand] private void FlipFloatingVertically() => QuickTransform("Flip pixels", t => t with { ScaleY = -t.ScaleY });
    [RelayCommand] private void RotateRight() => QuickTransform("Rotate pixels", t => t with { Angle = Normalize(t.Angle + 90) });
    [RelayCommand] private void RotateLeft() => QuickTransform("Rotate pixels", t => t with { Angle = Normalize(t.Angle - 90) });

    /// <summary>A flip or quarter turn: of the floating piece, or at once of the selection (one undo step).</summary>
    private void QuickTransform(string label, Func<PieceTransform, PieceTransform> change)
    {
        if (floating is { } f) { Retransform(change(f.Transform)); return; }
        if (!BeginTransform(label)) return;
        Retransform(change(floating!.Transform));
        ApplyTransform();
    }

    // ---------------- dragging the box ----------------

    /// <summary>How close to a handle counts as on it, in frame pixels (about 7 screen pixels).</summary>
    private double HandleReach => 7 / Math.Max(1, Zoom);

    private static double Distance((double X, double Y) a, (double X, double Y) b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>The handle under the pointer: "rotate", "pivot", a corner "c0".."c3", an edge "e0".."e3", "move" inside, or null outside.</summary>
    public string? HandleAt(double fx, double fy)
    {
        if (floating is not { } f || TransformBox is not { } box) return null;
        if (Distance(box.RotateHandle, (fx, fy)) <= HandleReach * 1.4) return "rotate";
        for (int i = 0; i < 4; i++) if (Distance(box.Corners[i], (fx, fy)) <= HandleReach) return "c" + i;
        for (int i = 0; i < 4; i++)
        {
            var a = box.Corners[i]; var b = box.Corners[(i + 1) % 4];
            if (Distance(((a.X + b.X) / 2, (a.Y + b.Y) / 2), (fx, fy)) <= HandleReach) return "e" + i;
        }
        if (Distance(box.Pivot, (fx, fy)) <= HandleReach) return "pivot";
        if (f.Transform.Invert(fx, fy) is { } p && p.X >= f.Bounds.X && p.Y >= f.Bounds.Y && p.X <= f.Bounds.X + f.Bounds.Width && p.Y <= f.Bounds.Y + f.Bounds.Height) return "move";
        return null;
    }

    private bool TryTransformDown(double fx, double fy)
    {
        if (floating is not { } f) return false;
        var handle = HandleAt(fx, fy);
        if (handle == null) { ApplyTransform(); return true; }   // a click outside applies it
        f.Handle = handle; f.DragFrom = (fx, fy); f.DragStart = f.Transform;
        return true;
    }

    private bool TryTransformMove(double fx, double fy, bool shift)
    {
        if (floating is not { Handle: { } handle } f) return false;
        var s = f.DragStart; var b = f.Bounds;
        double dx = fx - f.DragFrom.X, dy = fy - f.DragFrom.Y;
        PieceTransform next;
        switch (handle)
        {
            case "move":
                next = s with { Dx = s.Dx + Math.Round(dx), Dy = s.Dy + Math.Round(dy) };
                break;
            case "rotate":
                var centre = s.Apply(s.PivotX, s.PivotY);
                double from = Math.Atan2(f.DragFrom.Y - centre.Y, f.DragFrom.X - centre.X), to = Math.Atan2(fy - centre.Y, fx - centre.X);
                double angle = s.Angle + (to - from) * 180 / Math.PI;
                if (shift) angle = Math.Round(angle / 15) * 15;
                next = s with { Angle = Normalize(angle) };
                break;
            case "pivot":
                // Move the pivot but keep the piece where it is: solve for the shift that lands it in place.
                var landed = s.Apply(s.PivotX, s.PivotY);
                var newPivot = s.Invert(landed.X + dx, landed.Y + dy) ?? (s.PivotX, s.PivotY);
                var moved = s with { PivotX = newPivot.X, PivotY = newPivot.Y, Dx = 0, Dy = 0 };
                var drift = moved.Apply(b.X, b.Y); var want = s.Apply(b.X, b.Y);
                next = moved with { Dx = want.X - drift.X, Dy = want.Y - drift.Y };
                break;
            default:
                next = Scale(f, handle, fx, fy, shift);
                break;
        }
        f.Transform = next;
        ApplyFloating();
        NotifyTransform();
        return true;
    }

    /// <summary>Scale by a corner or edge, from the opposite side (it stays put); sizes snap to whole pixels.</summary>
    private static PieceTransform Scale(Floating f, string handle, double fx, double fy, bool uniform)
    {
        var s = f.DragStart; var b = f.Bounds;
        var piece = new[] { (b.X, b.Y), (b.X + b.Width, b.Y), (b.X + b.Width, b.Y + b.Height), (b.X, b.Y + b.Height) };
        int index = handle[1] - '0';
        bool corner = handle[0] == 'c';
        // The dragged point and the fixed one, in the piece's own (untransformed) coordinates.
        (double X, double Y) dragged, anchor;
        if (corner) { dragged = piece[index]; anchor = piece[(index + 2) % 4]; }
        else
        {
            var a = piece[index]; var c = piece[(index + 1) % 4];
            dragged = ((a.Item1 + c.Item1) / 2.0, (a.Item2 + c.Item2) / 2.0);
            var oa = piece[(index + 2) % 4]; var oc = piece[(index + 3) % 4];
            anchor = ((oa.Item1 + oc.Item1) / 2.0, (oa.Item2 + oc.Item2) / 2.0);
        }
        // The pointer, relative to the fixed point, in the piece's unrotated axes.
        var fixedAt = s.Apply(anchor.X, anchor.Y);
        double r = -s.Angle * Math.PI / 180, cos = Math.Cos(r), sin = Math.Sin(r);
        double qx = (fx - fixedAt.X) * cos - (fy - fixedAt.Y) * sin, qy = (fx - fixedAt.X) * sin + (fy - fixedAt.Y) * cos;
        double spanX = dragged.X - anchor.X, spanY = dragged.Y - anchor.Y;
        double sx = s.ScaleX, sy = s.ScaleY;
        if (Math.Abs(spanX) > 1e-9) sx = qx / spanX;
        if (Math.Abs(spanY) > 1e-9) sy = qy / spanY;
        if (uniform && corner) { double k = Math.Max(Math.Abs(sx), Math.Abs(sy)); sx = Math.Sign(sx == 0 ? 1 : sx) * k; sy = Math.Sign(sy == 0 ? 1 : sy) * k; }
        static double Snap(double scale, int size) { double pixels = Math.Round(Math.Abs(scale) * size); return Math.Sign(scale == 0 ? 1 : scale) * Math.Max(1, pixels) / size; }
        sx = Snap(sx, b.Width); sy = Snap(sy, b.Height);
        var scaled = s with { ScaleX = sx, ScaleY = sy };
        // Keep the fixed point where it was.
        var drift = scaled.Apply(anchor.X, anchor.Y);
        return scaled with { Dx = scaled.Dx + Math.Round(fixedAt.X - drift.X, 6), Dy = scaled.Dy + Math.Round(fixedAt.Y - drift.Y, 6) };
    }

    private bool TryTransformUp()
    {
        if (floating is not { Handle: not null } f) return false;
        f.Handle = null;
        return true;
    }

    private bool NudgeFloating(int dx, int dy)
    {
        if (floating is not { } f) return false;
        Retransform(f.Transform with { Dx = f.Transform.Dx + dx, Dy = f.Transform.Dy + dy });
        return true;
    }
}
