using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Project;
using SDCharacterStudio.Editor.Studio;

namespace SDCharacterStudio.Editor.ViewModels;

/// <summary>
/// Direct editing (D-48): the eraser and moving a selection work on what you see, whichever layer it comes from.
/// Live art stays live: erasing hides its pixels with a pose-local keep mask (tag <c>pose-edit</c>); moving a region
/// cuts it into a copy of the layer that keeps only that region, offset by the drag, and hides the region on the
/// original. Paint and image pixels move as pixels. Every drag is one undo step.
/// </summary>
public partial class PoseDocumentViewModel
{
    public const string EditMaskTag = "pose-edit";

    /// <summary>The pose as it will be when the gesture in progress is committed (drawn instead of the model).</summary>
    private Pose? preview;
    private LiveErase? liveErase;
    private Lift? lift;
    /// <summary>The pieces the current selection already lifted: dragging it again moves them instead of cutting again.</summary>
    private (PixelRect Selection, int Frame, List<string> Keys)? lifted;
    private readonly HashSet<string> workingImages = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> workingMasks = new(StringComparer.OrdinalIgnoreCase);
    private void Working(string path, RgbaImage image) { Context.Overrides[path] = image; workingImages.Add(path); }
    private void Working(Mask mask) { Context.MaskOverrides[mask.Id!] = mask; workingMasks.Add(mask.Id!); }

    private sealed class LiveErase
    {
        public required int LayerIndex { get; init; }
        public required LayerPlacement Place { get; init; }
        public required Mask Mask { get; init; }
        public required RgbaImage Image { get; init; }
        public (int X, int Y) Last { get; set; }
        public bool Changed { get; set; }
    }

    /// <summary>A live layer cut by a lift: the region hidden on the original (unless copying) and a live piece above it that keeps only the region.</summary>
    private sealed record LivePiece(int LayerIndex, Layer Piece, Mask? BaseMask, RgbaImage? BaseKeep, Mask PieceMask, RgbaImage PieceKeep);
    /// <summary>Paint pixels moving within their own image, as in any paint program.</summary>
    private sealed record PaintMove(PaintTarget Target, RgbaImage Original, List<(int X, int Y, uint Color)> Pixels, bool Copy);

    private sealed class Lift
    {
        public required (int X, int Y) From { get; init; }
        public required PixelRect Selection { get; init; }
        public List<LivePiece> Live { get; } = new();
        public List<PaintMove> Paint { get; } = new();
        /// <summary>Live pieces lifted earlier by this selection (dragging it again moves them).</summary>
        public List<string> Existing { get; } = new();
        public (int X, int Y) Delta { get; set; }
    }

    /// <summary>The topmost visible layer with a pixel at a canvas point (effects don't count).</summary>
    public int? TopLayerAt(int x, int y)
    {
        if (Frame is not { } frame || x < 0 || y < 0 || x >= Width || y >= Height) return null;
        for (int i = frame.Layers.Count - 1; i >= 0; i--)
        {
            var layer = frame.Layers[i];
            if (!layer.Visible || layer is EffectLayer) continue;
            var image = Context.RenderLayer(Model, FrameIndex, layer).Image;
            if (PixelTools.Get(image, x, y) >> 24 != 0) return i;
        }
        return null;
    }

    private bool TryDirectDown(int x, int y, bool ctrl)
    {
        if (Frame == null) return false;
        if (Tool == StudioTool.Eraser)
        {
            if (TopLayerAt(x, y) is not { } hit) return true;   // nothing there to erase
            if (IsLayerLocked(hit)) { Ui.Toast("Layer locked", "Unlock it in the layers list to erase it.", ToastKind.Info); return true; }
            if (Frame.Layers[hit] is LiveLayer) return BeginLiveErase(hit, x, y);
            LayerIndex = hit;   // paint or image pixels: the normal eraser, on the layer you clicked
            return false;
        }
        if ((Tool == StudioTool.Select && Selection is { IsEmpty: false } inside && inside.Contains(x, y)) || (Tool == StudioTool.Move && Selection is { IsEmpty: false }))
            return BeginLift(Selection!.Value, x, y, ctrl);
        if (Tool == StudioTool.Select && !ctrl && BeginPartDrag(x, y)) return true;   // a puppet part: just drag it
        return false;
    }

    private bool TryDirectMove(int x, int y)
    {
        if (liveErase is { } erase)
        {
            foreach (var (px, py) in PixelTools.Line(erase.Last.X, erase.Last.Y, x, y)) EraseLivePixel(erase, px, py);
            erase.Last = (x, y);
            Render();
            return true;
        }
        if (lift == null && Tool == StudioTool.Select && PartAt(x, y) is { } part) HoverText = $"{x}, {y} · {part}: drag to move";
        if (lift is { } moving)
        {
            moving.Delta = (x - moving.From.X, y - moving.From.Y);
            preview = PackWriter.Clone(Model);
            ApplyLift(preview, moving);
            foreach (var paint in moving.Paint) Working(paint.Target.Path, MovedPaint(paint, moving.Delta));
            Selection = moving.Selection with { X = moving.Selection.X + moving.Delta.X, Y = moving.Selection.Y + moving.Delta.Y };
            Render();
            return true;
        }
        return false;
    }

    private bool TryDirectUp()
    {
        if (liveErase is { } erase)
        {
            liveErase = null;
            try
            {
                if (erase.Changed)
                    Save("Erase", pose => pose.Frames[FrameIndex].Layers[erase.LayerIndex].Mask = erase.Mask.Id,
                        lib => { lib.WriteImage(erase.Mask.Image!, erase.Image); lib.SaveMask(erase.Mask); }, rebuildFields: false);
            }
            finally { EndPreview(); }
            return true;
        }
        if (lift is { } moving)
        {
            lift = null;
            try
            {
                if (moving.Delta != (0, 0))
                {
                    var keys = moving.Live.Select(l => PieceKey(l.Piece)).Concat(moving.Existing).ToList();
                    Save("Move pixels", pose => ApplyLift(pose, moving), lib =>
                    {
                        foreach (var live in moving.Live)
                        {
                            if (live.BaseMask != null) { lib.WriteImage(live.BaseMask.Image!, live.BaseKeep!); lib.SaveMask(live.BaseMask); }
                            lib.WriteImage(live.PieceMask.Image!, live.PieceKeep); lib.SaveMask(live.PieceMask);
                        }
                        foreach (var paint in moving.Paint) lib.WriteImage(paint.Target.Path, MovedPaint(paint, moving.Delta));
                    }, rebuildFields: false);
                    lifted = keys.Count > 0 ? (Selection ?? moving.Selection, FrameIndex, keys) : null;
                }
            }
            finally { EndPreview(); }
            return true;
        }
        return false;
    }

    private void CancelDirect()
    {
        if (liveErase == null && lift == null) return;
        liveErase = null; lift = null;
        EndPreview();
    }

    private void EndPreview()
    {
        preview = null;
        foreach (var path in workingImages) Context.Overrides.Remove(path);
        foreach (var id in workingMasks) Context.MaskOverrides.Remove(id);
        workingImages.Clear(); workingMasks.Clear();
        Context.InvalidatePack();
        Render();
    }

    // ---------------- erasing live art ----------------

    private bool BeginLiveErase(int layerIndex, int x, int y)
    {
        var layer = Frame!.Layers[layerIndex];
        if (Context.Placement(Model, FrameIndex, layer) is not { } place)
        {
            Ui.Toast("Can't erase this yet", "The game export doesn't have this live art. Reload the export, or erase on a paint layer.", ToastKind.Info);
            return true;
        }
        var mask = EditMaskFor(layer, place);
        var image = StudioImages.Clone(place.Keep);
        Working(mask);
        Working(mask.Image!, image);
        preview = PackWriter.Clone(Model);
        preview.Frames[FrameIndex].Layers[layerIndex].Mask = mask.Id;
        liveErase = new LiveErase { LayerIndex = layerIndex, Place = place, Mask = mask, Image = image, Last = (x, y) };
        EraseLivePixel(liveErase, x, y);
        Render();
        return true;
    }

    private static void EraseLivePixel(LiveErase erase, int x, int y)
    {
        if (erase.Place.ToSource(x, y) is not { } source) return;
        int index = source.Y * erase.Image.Width + source.X;
        if (erase.Image.Pixels[index] == 0) return;
        erase.Image.Pixels[index] = 0;
        erase.Changed = true;
    }

    /// <summary>The layer's own edit mask (reused), or a new one for it: a keep mask at its source frame's size.</summary>
    private Mask EditMaskFor(Layer layer, LayerPlacement place)
    {
        if (layer.Mask != null && Context.FindMask(layer.Mask) is { Image: { } image } existing && existing.Tags.Contains(EditMaskTag)
            && string.Equals(existing.Mode, "keep", StringComparison.OrdinalIgnoreCase) && existing.PerFrame.Count == 0)
            return new Mask { Id = existing.Id, DisplayName = existing.DisplayName, Image = image, Mode = "keep", FrameSize = new[] { place.SourceWidth, place.SourceHeight }, Tags = { EditMaskTag } };
        return NewEditMask(place);
    }

    private Mask NewEditMask(LayerPlacement place)
    {
        string stem = PackDocument.ToContentId(Model.Id ?? "pose");
        for (int n = 1; ; n++)
        {
            string id = $"{stem}-edit-{n}", path = $"masks/poses/{stem}/edit-{n}.png";
            if (Context.FindMask(id) != null || Context.Overrides.ContainsKey(path) || Context.PackImage(path) != null) continue;
            var mask = new Mask { Id = id, DisplayName = $"{Model.Id}: edited pixels", Image = path, Mode = "keep", FrameSize = new[] { place.SourceWidth, place.SourceHeight }, Tags = { EditMaskTag } };
            Working(mask);   // reserve the name for this gesture
            Working(path, new RgbaImage(place.SourceWidth, place.SourceHeight, new uint[place.SourceWidth * place.SourceHeight]));
            return mask;
        }
    }

    // ---------------- moving a selection ----------------

    private static string PieceKey(Layer layer) => "mask:" + layer.Mask;

    private bool BeginLift(PixelRect selection, int x, int y, bool copy)
    {
        var frame = Frame!;
        var moving = new Lift { From = (x, y), Selection = selection };
        // The same selection again: move the live pieces it lifted instead of cutting the originals again.
        bool again = !copy && lifted is { } earlier && earlier.Frame == FrameIndex && earlier.Selection == selection
            && earlier.Keys.All(k => frame.Layers.Any(l => PieceKey(l) == k));
        if (again) moving.Existing.AddRange(lifted!.Value.Keys);
        else lifted = null;
        for (int i = 0; i < frame.Layers.Count; i++)
        {
            var layer = frame.Layers[i];
            if (!layer.Visible || IsLayerLocked(i)) continue;
            if (layer is LiveLayer && !again && CutLive(i, layer, selection, copy) is { } piece) moving.Live.Add(piece);
            else if (layer is PaintLayer && LiftPaint(i, selection, copy) is { } paint) moving.Paint.Add(paint);
        }
        if (moving.Live.Count == 0 && moving.Paint.Count == 0 && moving.Existing.Count == 0)
        { Ui.Toast("Nothing to move", "The selection is empty here. Select some of the character first.", ToastKind.Info); return true; }
        lift = moving;
        preview = PackWriter.Clone(Model);
        ApplyLift(preview, moving);
        Render();
        return true;
    }

    private LivePiece? CutLive(int index, Layer layer, PixelRect selection, bool copy)
    {
        if (Context.Placement(Model, FrameIndex, layer) is not { } place) return null;
        // Only what the layer actually shows inside the selection counts (a transparent corner isn't a piece).
        var shown = Context.RenderLayer(Model, FrameIndex, layer).Image;
        var pieceKeep = new RgbaImage(place.SourceWidth, place.SourceHeight, new uint[place.SourceWidth * place.SourceHeight]);
        var baseKeep = StudioImages.Clone(place.Keep);
        bool any = false;
        for (int cy = selection.Y; cy < selection.Y + selection.Height; cy++)
        for (int cx = selection.X; cx < selection.X + selection.Width; cx++)
        {
            if (PixelTools.Get(shown, cx, cy) >> 24 == 0 || place.ToSource(cx, cy) is not { } s) continue;
            int i = s.Y * place.SourceWidth + s.X;
            pieceKeep.Pixels[i] = 0xFF000000;
            if (!copy) baseKeep.Pixels[i] = 0;
            any = true;
        }
        if (!any) return null;
        var pieceMask = NewEditMask(place);
        Working(pieceMask.Image!, pieceKeep);
        Mask? baseMask = null;
        if (!copy)
        {
            baseMask = EditMaskFor(layer, place);
            Working(baseMask);
            Working(baseMask.Image!, baseKeep);
        }
        var piece = PackWriter.Clone(layer);
        piece.Mask = pieceMask.Id;
        return new LivePiece(index, piece, baseMask, copy ? null : baseKeep, pieceMask, pieceKeep);
    }

    private PaintMove? LiftPaint(int index, PixelRect selection, bool copy)
    {
        if (TargetFor(index) is not { } target) return null;
        var pixels = new List<(int X, int Y, uint Color)>();
        for (int cy = selection.Y; cy < selection.Y + selection.Height; cy++)
        for (int cx = selection.X; cx < selection.X + selection.Width; cx++)
            if (target.ToImage(cx, cy) is { } p && PixelTools.Get(target.Image, p.X, p.Y) is var color && color >> 24 != 0)
                pixels.Add((p.X, p.Y, color));
        return pixels.Count == 0 ? null : new PaintMove(target, StudioImages.Clone(target.Image), pixels, copy);
    }

    /// <summary>The paint image with its lifted pixels moved by the drag (kept inside the layer's cell).</summary>
    private static RgbaImage MovedPaint(PaintMove move, (int X, int Y) delta)
    {
        var image = StudioImages.Clone(move.Original);
        int dx = move.Target.Flip ? -delta.X : delta.X, dy = delta.Y;
        var cell = move.Target.Cell;
        if (!move.Copy) foreach (var (x, y, _) in move.Pixels) PixelTools.Set(image, x, y, 0);
        foreach (var (x, y, color) in move.Pixels)
            if (cell.Contains(x + dx, y + dy)) PixelTools.Set(image, x + dx, y + dy, color);
        return image;
    }

    /// <summary>Apply a lift's layers to a pose: base masks, live pieces above their originals, moved by the drag.</summary>
    private void ApplyLift(Pose pose, Lift moving)
    {
        var layers = pose.Frames[FrameIndex].Layers;
        var (dx, dy) = moving.Delta;
        foreach (var layer in layers.Where(l => moving.Existing.Contains(PieceKey(l))))
            layer.Offset = new[] { layer.Offset[0] + dx, layer.Offset[1] + dy };
        foreach (var live in moving.Live.OrderByDescending(l => l.LayerIndex))
        {
            if (live.BaseMask != null) layers[live.LayerIndex].Mask = live.BaseMask.Id;
            var piece = PackWriter.Clone(live.Piece);
            piece.Offset = new[] { piece.Offset[0] + dx, piece.Offset[1] + dy };
            layers.Insert(live.LayerIndex + 1, piece);
        }
    }
}
