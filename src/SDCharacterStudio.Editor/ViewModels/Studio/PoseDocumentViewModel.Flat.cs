using CommunityToolkit.Mvvm.Input;
using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Project;
using SDCharacterStudio.Editor.Studio;

namespace SDCharacterStudio.Editor.ViewModels;

/// <summary>
/// The flat pose picture (D-48, the owner's choice): a frame is one picture, made of a live layer with a pixel map
/// (each cell shows a pixel of the character's real sprite, so it follows sprite mods) and a paint layer (your own
/// pixels) on top. Erasing, drawing, moving, mirroring and pasting change those two images; no layers are ever added.
/// Only visible cells move or paste, so a selection's transparent corners never wipe out what's underneath.
/// </summary>
public partial class PoseDocumentViewModel
{
    public sealed record FlatFrame(LiveLayer Live, PaintLayer Paint);

    /// <summary>A frame that is one picture: a live layer with a map, then a paint layer (effects may follow).</summary>
    public static FlatFrame? FlatOf(PoseFrame? frame)
    {
        if (frame is not { Layers: { Count: >= 2 } layers } || layers[0] is not LiveLayer { Map: not null } live || layers[1] is not PaintLayer { Image: not null } paint) return null;
        return layers.Skip(2).All(l => l is EffectLayer) ? new FlatFrame(live, paint) : null;
    }

    public FlatFrame? Flat => FlatOf(Frame);
    public bool IsFlat => Flat != null;
    /// <summary>A character frame made of layers: the simple editor offers to make it one picture.</summary>
    public bool CanFlatten => !IsFlat && Model.Kind == "character" && Frame?.Layers.Any(l => l is LiveLayer { Part: "Full" }) == true;

    private FlatGesture? flatGesture;

    private sealed class FlatGesture
    {
        public required string Kind { get; init; }   // "erase" or "move"
        public required string MapPath { get; init; }
        public required string PaintPath { get; init; }
        public required RgbaImage BaseMap { get; init; }
        public required RgbaImage BasePaint { get; init; }
        public RgbaImage Map { get; set; } = null!;
        public RgbaImage Paint { get; set; } = null!;
        public (int X, int Y) From { get; init; }
        /// <summary>The selection when the gesture began, and how far the drag has moved it (for the other picked frames).</summary>
        public SelectionMask? Shape { get; init; }
        public bool Copy { get; init; }
        public (int X, int Y) Delta { get; set; }
        public (int X, int Y) Last { get; set; }
        public PixelRect Selection { get; init; }
        public List<(int X, int Y, uint Map, uint Paint)> Cells { get; } = new();
        /// <summary>The eraser's points so far (pixel-perfect corners).</summary>
        public List<(int X, int Y)> Path { get; } = new();
        public bool Changed { get; set; }
    }

    private (RgbaImage Map, RgbaImage Paint)? FlatImages()
    {
        if (Flat is not { } flat || Context.PackImage(flat.Live.Map) is not { } map || Context.PackImage(flat.Paint.Image) is not { } paint) return null;
        return map.Width == Width && map.Height == Height && paint.Width == Width && paint.Height == Height ? (map, paint) : null;
    }

    /// <summary>The picture as shown: which cells are visible (for lifting, copying and erasing what you see).</summary>
    private bool[] VisibleCells()
    {
        var image = Context.RenderFrame(Model, FrameIndex).Image;
        return image.Pixels.Select(p => p >> 24 != 0).ToArray();
    }

    private bool TryFlatDown(int x, int y, bool ctrl, uint color = 0)
    {
        if (Flat is not { } flat || FlatImages() is not { } images) return false;
        switch (Tool)
        {
            case StudioTool.Eraser:
                flatGesture = Begin("erase", (x, y), default);
                EraseFlatAt(x, y);
                Render();
                return true;
            case StudioTool.Select or StudioTool.Move when Selection is { IsEmpty: false } selection && (Tool == StudioTool.Move || selection.Contains(x, y)):
                var gesture = Begin("move", (x, y), selection, ctrl);
                var visible = VisibleCells();
                for (int cy = selection.Y; cy < selection.Y + selection.Height; cy++)
                for (int cx = selection.X; cx < selection.X + selection.Width; cx++)
                {
                    int i = cy * Width + cx;
                    if (cx < 0 || cy < 0 || cx >= Width || cy >= Height || !visible[i] || !InsideSelection(cx, cy)) continue;
                    gesture.Cells.Add((cx, cy, images.Map.Pixels[i], images.Paint.Pixels[i]));
                    if (!ctrl) { gesture.BaseMap.Pixels[i] = 0; gesture.BasePaint.Pixels[i] = 0; }   // Ctrl+drag copies
                }
                if (gesture.Cells.Count == 0) { flatGesture = null; EndPreview(); Ui.Toast("Nothing to move", "Select some of the character first.", ToastKind.Info); return true; }
                flatGesture = gesture;
                ApplyFlatMove(gesture, 0, 0);
                return true;
            case StudioTool.Fill:
                FlatFill(flat, images, x, y, ctrl, color);
                return true;
            case StudioTool.Pencil or StudioTool.Line or StudioTool.Rectangle:
                LayerIndex = 1;   // your own pixels go on the paint image, over the live picture
                return false;
            default:
                return false;
        }

        FlatGesture Begin(string kind, (int X, int Y) at, PixelRect selection, bool copy = false)
        {
            var g = new FlatGesture
            {
                Kind = kind, MapPath = flat.Live.Map!, PaintPath = flat.Paint.Image!, From = at, Last = at, Selection = selection,
                Shape = SelectionShape ?? SelectionMask.FromRect(Width, Height, selection), Copy = copy,
                BaseMap = StudioImages.Clone(images.Map), BasePaint = StudioImages.Clone(images.Paint),
            };
            g.Map = StudioImages.Clone(images.Map); g.Paint = StudioImages.Clone(images.Paint);
            Working(g.MapPath, g.Map); Working(g.PaintPath, g.Paint);
            return g;
        }
    }

    /// <summary>Fill the area of the picture that has the clicked colour (Ctrl: that colour everywhere), as you see it.</summary>
    private void FlatFill(FlatFrame flat, (RgbaImage Map, RgbaImage Paint) images, int x, int y, bool global, uint color)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return;
        var shown = Context.RenderFrame(Model, FrameIndex).Image;
        uint target = shown.Pixels[y * Width + x];
        bool Same(int px, int py) => shown.Pixels[py * Width + px] == target && (Selection is not { } s || s.Contains(px, py));
        var cells = global ? Enumerable.Range(0, Width * Height).Select(i => (i % Width, i / Width)).Where(p => Same(p.Item1, p.Item2)).ToList()
            : PixelTools.Flood(Width, Height, x, y, Same);
        if (cells.Count == 0 || target == color) return;
        var map = StudioImages.Clone(images.Map); var paint = StudioImages.Clone(images.Paint);
        foreach (var (px, py) in cells)
        {
            int i = py * Width + px;
            paint.Pixels[i] = color;
            if (color >> 24 == 0) map.Pixels[i] = 0;   // filling with transparent erases
        }
        Save("Fill", _ => { }, lib => { lib.WriteImage(flat.Live.Map!, map); lib.WriteImage(flat.Paint.Image!, paint); }, rebuildFields: false);
        if (color >> 24 != 0) Palette.Remember(color);
        Context.InvalidatePack(); Render(); RenderThumbnails();
    }

    private bool TryFlatMove(int x, int y)
    {
        if (flatGesture is not { } g) return false;
        if (g.Kind == "erase")
        {
            foreach (var (px, py) in PixelTools.Line(g.Last.X, g.Last.Y, x, y)) EraseFlatAt(px, py);
            g.Last = (x, y);
        }
        else
        {
            ApplyFlatMove(g, x - g.From.X, y - g.From.Y);
            g.Delta = (x - g.From.X, y - g.From.Y);
            Selection = g.Selection with { X = g.Selection.X + x - g.From.X, Y = g.Selection.Y + y - g.From.Y };
        }
        Render();
        return true;
    }

    private bool TryFlatUp()
    {
        if (flatGesture is not { } g) return false;
        flatGesture = null;
        try
        {
            if (g.Changed)
                Save(g.Kind == "erase" ? "Erase" : "Move pixels", _ => { }, lib =>
                {
                    lib.WriteImage(g.MapPath, g.Map); lib.WriteImage(g.PaintPath, g.Paint);
                    // The other picked frames move the same way.
                    if (g.Kind == "move" && g.Shape != null && g.Delta != (0, 0)) TransformOtherPicked(lib, g.Shape, new PieceTransform(g.Delta.X, g.Delta.Y, 1, 1, 0, 0, 0), g.Copy);
                }, rebuildFields: false);
        }
        finally { EndPreview(); }
        RenderThumbnails();
        return true;
    }

    /// <summary>One dab of the eraser (brush size, symmetry, inside the selection); pixel-perfect puts corners back.</summary>
    private void EraseFlatAt(int x, int y)
    {
        if (flatGesture is not { } g) return;
        if (g.Path.Count > 0 && g.Path[^1] == (x, y)) return;
        g.Path.Add((x, y));
        foreach (var (px, py) in BrushAt(x, y)) EraseFlatCell(g, px, py);
        if (PixelPerfect && BrushSize == 1 && PixelPerfectCorner(g.Path) is { } corner)
            foreach (var (px, py) in BrushAt(corner.X, corner.Y))
                if (px >= 0 && py >= 0 && px < Width && py < Height)
                {
                    int i = py * Width + px;
                    g.Map.Pixels[i] = g.BaseMap.Pixels[i]; g.Paint.Pixels[i] = g.BasePaint.Pixels[i];
                }
    }

    private void EraseFlatCell(FlatGesture g, int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height || !InsideSelection(x, y)) return;
        int i = y * Width + x;
        if (g.Map.Pixels[i] == 0 && g.Paint.Pixels[i] == 0) return;
        g.Map.Pixels[i] = 0; g.Paint.Pixels[i] = 0;
        g.Changed = true;
    }

    /// <summary>The lifted cells dropped at an offset over the base (each visible cell replaces what's there).</summary>
    private void ApplyFlatMove(FlatGesture g, int dx, int dy)
    {
        g.BaseMap.Pixels.CopyTo(g.Map.Pixels, 0);
        g.BasePaint.Pixels.CopyTo(g.Paint.Pixels, 0);
        foreach (var (x, y, map, paint) in g.Cells)
        {
            int tx = x + dx, ty = y + dy;
            if (tx < 0 || ty < 0 || tx >= Width || ty >= Height) continue;
            g.Map.Pixels[ty * Width + tx] = map; g.Paint.Pixels[ty * Width + tx] = paint;
        }
        g.Changed = true;
    }

    /// <summary>Change the selection (or the whole picture) in both images, as one undo step.</summary>
    private bool FlatEdit(string label, Action<RgbaImage, PixelRect> edit)
    {
        if (Flat is not { } flat || FlatImages() is not { } images) return false;
        var rect = (Selection ?? new PixelRect(0, 0, Width, Height)).Clip(Width, Height);
        if (rect.IsEmpty) return true;
        var map = StudioImages.Clone(images.Map); var paint = StudioImages.Clone(images.Paint);
        edit(map, rect); edit(paint, rect);
        Save(label, _ => { }, lib => { lib.WriteImage(flat.Live.Map!, map); lib.WriteImage(flat.Paint.Image!, paint); EditOtherPicked(lib, rect, edit); }, rebuildFields: false);
        Context.InvalidatePack();
        Render(); RenderThumbnails();
        return true;
    }

    /// <summary>Arrow keys: move what the selection shows by a pixel (visible cells only, like a drag).</summary>
    private bool FlatNudge(int dx, int dy)
    {
        if (Selection is not { } selection || !TryFlatDown(selection.X, selection.Y, false) || flatGesture is not { Kind: "move" } g) { CancelFlat(); return IsFlat; }
        ApplyFlatMove(g, dx, dy);
        Selection = selection with { X = selection.X + dx, Y = selection.Y + dy };
        flatGesture = null;
        try
        {
            Save("Nudge pixels", _ => { }, lib =>
            {
                lib.WriteImage(g.MapPath, g.Map); lib.WriteImage(g.PaintPath, g.Paint);
                if (g.Shape != null) TransformOtherPicked(lib, g.Shape, new PieceTransform(dx, dy, 1, 1, 0, 0, 0), copy: false);
            }, rebuildFields: false);
        }
        finally { EndPreview(); }
        RenderThumbnails();
        return true;
    }

    private void CancelFlat()
    {
        if (flatGesture == null) return;
        flatGesture = null;
        EndPreview();
    }

    // ---------------- making a frame one picture ----------------

    /// <summary>
    /// Make every character frame one flat picture. Each pixel comes from the topmost layer showing it: the character's
    /// own live sprite becomes a map cell (it stays live); anything else becomes your own pixels. Edit masks the pose no
    /// longer uses are removed. One undo step.
    /// </summary>
    [RelayCommand]
    public void MakeOnePicture()
    {
        var results = new List<(int Frame, Layer Live, RgbaImage Map, RgbaImage Paint, List<Layer> Effects)>();
        for (int index = 0; index < Model.Frames.Count; index++)
        {
            var frame = Model.Frames[index];
            if (FlatOf(frame) != null) continue;
            if (frame.Layers.FirstOrDefault(l => l is LiveLayer { Part: "Full" }) is not LiveLayer original) continue;
            var map = new RgbaImage(Width, Height, new uint[Width * Height]);
            var paint = new RgbaImage(Width, Height, new uint[Width * Height]);
            var layers = frame.Layers.Where(l => l.Visible && l is not EffectLayer).Reverse()
                .Select(l => (Layer: l, Image: Context.RenderLayer(Model, index, l).Image, Place: SameSource(l, original) ? Context.Placement(Model, index, l) : null,
                    Map: l is LiveLayer { Map: { } m, Offset: [0, 0] } && SameSource(l, original) ? Context.PackImage(m) : null)).ToList();
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                int i = y * Width + x;
                uint? coverage = null;   // a live cell that's transparent now (a sprite mod may fill it later)
                foreach (var (layer, image, place, cells) in layers)
                {
                    bool shown = PixelTools.Get(image, x, y) >> 24 != 0;
                    if (cells != null && PixelTools.Get(cells, x, y) is var mapped && mapped >> 24 != 0)   // already a picture: keep its cell
                    {
                        if (shown) { map.Pixels[i] = mapped; coverage = null; break; }
                        coverage ??= mapped;
                        continue;
                    }
                    if (place?.ToSource(x, y) is { } s && place.Keep.Pixels[s.Y * place.SourceWidth + s.X] != 0)
                    {
                        if (shown) { map.Pixels[i] = Compositor.MapCell(s.X, s.Y); coverage = null; break; }
                        coverage ??= Compositor.MapCell(s.X, s.Y);
                        continue;
                    }
                    if (shown) { paint.Pixels[i] = PixelTools.Get(image, x, y); coverage = null; break; }
                }
                if (coverage is { } cell && map.Pixels[i] == 0 && paint.Pixels[i] == 0) map.Pixels[i] = cell;
            }
            var live = PackWriter.Clone(original);
            live.Map = null;
            live.Mask = null; live.Offset = new[] { 0, 0 }; live.AnchorTo = null; live.Flip = false; live.Visible = true;
            results.Add((index, live, map, paint, frame.Layers.Where(l => l is EffectLayer).ToList()));
        }
        if (results.Count == 0) { Ui.Toast("Already one picture", "Every frame of this pose is already a single picture.", ToastKind.Info); return; }
        string stem = PackDocument.ToContentId(Model.Id ?? "pose");
        var slots = Library is { } library ? FlatPose.PaintSlots(library, Model.For) : null;
        Save("Make it one picture", pose =>
        {
            foreach (var (frameIndex, live, _, _, effects) in results)
                pose.Frames[frameIndex].Layers = new List<Layer> { live, new PaintLayer { ColorSlots = slots == null ? null : new(slots, StringComparer.OrdinalIgnoreCase) } }.Concat(effects).ToList();
        }, lib =>
        {
            foreach (var (frameIndex, _, map, paint, _) in results)
            {
                var layers = Model.Frames[frameIndex].Layers;
                string mapPath = lib.UniquePath($"art/poses/{stem}/map-{frameIndex}.png"), paintPath = lib.UniquePath($"art/poses/{stem}/paint-{frameIndex}.png");
                lib.WriteImage(mapPath, map); lib.WriteImage(paintPath, paint);
                ((LiveLayer)layers[0]).Map = mapPath; ((PaintLayer)layers[1]).Image = paintPath;
            }
            RemoveUnusedEditMasks(lib, stem);
        });
        Tool = StudioTool.Select;
        Selection = null;
        LayerIndex = 1;
    }

    private static bool SameSource(Layer layer, LiveLayer original) => layer is LiveLayer live
        && string.Equals(live.Source?.Kind, original.Source?.Kind, StringComparison.OrdinalIgnoreCase)
        && string.Equals(live.Source?.Npc, original.Source?.Npc, StringComparison.OrdinalIgnoreCase)
        && string.Equals(live.Source?.Asset, original.Source?.Asset, StringComparison.OrdinalIgnoreCase)
        && live.Part == original.Part
        && live.SourceFrame?.Index == original.SourceFrame?.Index && live.SourceFrame?.Facing == original.SourceFrame?.Facing && live.SourceFrame?.Stand == original.SourceFrame?.Stand;

    /// <summary>This pose's own edit masks (stage 1 and 2 cut-outs) that no layer uses any more: gone, with their images.</summary>
    private void RemoveUnusedEditMasks(PackLibrary lib, string stem)
    {
        // This pose as it is now (it's saved after this runs), and every other pose as saved.
        var poses = lib.Items(LibraryKind.Pose).Where(i => !string.Equals(i.Id, Model.Id, StringComparison.OrdinalIgnoreCase)).Select(i => lib.Pose(i.Id)).Append(Model);
        var used = poses.Where(p => p != null).SelectMany(p => p!.Frames.SelectMany(f => f.Layers)).Select(l => l.Mask).Where(m => m != null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var masks = lib.Masks();
        foreach (var mask in masks.Masks.Where(m => m?.Id != null && m.Tags.Contains(EditMaskTag) && m.Id.StartsWith(stem + "-edit-", StringComparison.OrdinalIgnoreCase) && !used.Contains(m.Id)).ToList())
        {
            masks.Masks.Remove(mask);
            if (mask.Image != null) lib.RemoveFile(mask.Image);
        }
        lib.SaveMasks(masks);
    }
}
