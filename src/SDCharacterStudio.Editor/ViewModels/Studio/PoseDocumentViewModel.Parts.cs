using CommunityToolkit.Mvvm.Input;
using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Project;
using SDCharacterStudio.Editor.Studio;

namespace SDCharacterStudio.Editor.ViewModels;

/// <summary>
/// Puppet parts (D-48 stage 2): split the character into head, arms, body and legs, then drag any part with the Select
/// tool. Each part is a live piece (a copy of the live layer that keeps only that part), so it still follows sprite
/// mods; the original stays underneath, hidden, as a reference.
/// </summary>
public partial class PoseDocumentViewModel
{
    public const string PartTag = "pose-part";

    /// <summary>The puppet part shown at a canvas point, if any ("Robin head").</summary>
    public string? PartAt(int x, int y) =>
        TopLayerAt(x, y) is { } hit && Frame!.Layers[hit].Mask is { } id && Context.FindMask(id) is { } mask && mask.Tags.Contains(PartTag) ? mask.DisplayName : null;

    /// <summary>A part made by <see cref="MakeParts"/>: a live piece, its keep mask, and where it sits on the canvas.</summary>
    private sealed record PartPiece(string Name, Layer Piece, Mask Mask, RgbaImage Keep);

    /// <summary>
    /// Cut <paramref name="layer"/> (as it shows in <paramref name="pose"/>) into live puppet parts. <paramref name="shape"/>
    /// can change each part's canvas pixels and say how far it moves (ready-made poses). Masks are working copies until saved.
    /// </summary>
    private List<PartPiece>? MakeParts(Pose pose, Layer layer, Func<string, bool[], RgbaImage, (bool[] Pixels, int X, int Y)>? shape = null)
    {
        if (Context.Placement(pose, FrameIndex, layer) is not { } place) return null;
        var frame = pose.Frames[FrameIndex];
        var shown = Context.RenderLayer(pose, FrameIndex, layer).Image;
        string facing = (layer as LiveLayer)?.SourceFrame?.Facing ?? pose.Facing ?? "down";
        var parts = PartSplitter.Split(shown, HeadTest(layer, place, facing, frame), facing);
        if (parts.Count < 2) return null;
        string who = PoseCharacter()?.Label ?? pose.For ?? "Character";
        var pieces = new List<PartPiece>();
        foreach (var part in parts)
        {
            var (pixels, dx, dy) = shape?.Invoke(part.Name, part.Pixels, shown) ?? (part.Pixels, 0, 0);
            var keep = new RgbaImage(place.SourceWidth, place.SourceHeight, new uint[place.SourceWidth * place.SourceHeight]);
            for (int y = 0; y < shown.Height; y++)
                for (int x = 0; x < shown.Width; x++)
                    if (pixels[y * shown.Width + x] && place.ToSource(x, y) is { } s) keep.Pixels[s.Y * place.SourceWidth + s.X] = 0xFF000000;
            var mask = NewEditMask(place);
            mask.DisplayName = $"{who} {part.Name}";
            mask.Tags.Add(PartTag);
            Working(mask.Image!, keep);
            var piece = PackWriter.Clone(layer);
            if (piece is LiveLayer live) live.Map = null;   // a part is cut from the real sprite, not a flat picture
            piece.Mask = mask.Id;
            piece.Visible = true;
            piece.Offset = new[] { piece.Offset[0] + dx, piece.Offset[1] + dy };
            pieces.Add(new PartPiece(part.Name, piece, mask, keep));
        }
        return pieces;
    }

    [RelayCommand]
    private void SplitIntoParts()
    {
        if (Frame is not { } frame) return;
        int index = frame.Layers.FindLastIndex(l => l.Visible && l is LiveLayer { Part: "Full" });
        if (index < 0) { Ui.Toast("Nothing to split", "This frame has no visible live character. Split works on the character's real sprite.", ToastKind.Info); return; }
        try
        {
            if (MakeParts(Model, frame.Layers[index]) is not { } pieces)
            { Ui.Toast("Can't split this one", "Couldn't find separate parts in this sprite (or the export doesn't have it). Select a part and drag it instead.", ToastKind.Info); return; }
            Save("Split into parts", pose =>
            {
                var layers = pose.Frames[FrameIndex].Layers;
                layers[index].Visible = false;   // stays underneath as the reference
                layers.InsertRange(index + 1, pieces.Select(p => PackWriter.Clone(p.Piece)));
            }, lib =>
            {
                foreach (var piece in pieces) { lib.WriteImage(piece.Mask.Image!, piece.Keep); lib.SaveMask(piece.Mask); }
            });
            Ui.Toast("Split into parts", $"{string.Join(", ", pieces.Select(p => p.Name))}. Drag any part to move it.", ToastKind.Success);
        }
        finally { EndPreview(); }
        Tool = StudioTool.Select;
        Selection = null;
    }

    public IReadOnlyList<PoseRecipes.Recipe> Recipes => PoseRecipes.All;

    /// <summary>
    /// A ready-made pose (D-48 stage 4) for this frame, from the character's untouched standing sprite: split into live
    /// parts, moved by the recipe (kneeling trims the legs to the thighs and drops everything onto the knees). One undo step.
    /// </summary>
    [RelayCommand]
    public void ApplyRecipe(string? id)
    {
        if (Frame is not { } frame || id == null || PoseRecipes.Find(id) is not { } recipe) return;
        // The character's own sprite: the first live full layer, unmasked and unmoved.
        var original = frame.Layers.FirstOrDefault(l => l is LiveLayer { Part: "Full" });
        if (original == null) { Ui.Toast("No character here", "Ready-made poses start from the character's real sprite. Make a new pose for them first.", ToastKind.Info); return; }
        var fresh = PackWriter.Clone(original);
        fresh.Mask = null; fresh.Offset = new[] { 0, 0 }; fresh.Visible = true;
        ((LiveLayer)fresh).Map = null;   // her real sprite, not this frame's picture
        var scratch = PackWriter.Clone(Model);
        scratch.Frames[FrameIndex].Layers = new List<Layer> { fresh };
        string facing = (fresh as LiveLayer)?.SourceFrame?.Facing ?? Model.Facing ?? "down";

        int drop = 0;
        var thighs = recipe.KeepThighs < 1 ? LegRows(scratch, fresh, recipe.KeepThighs, out drop) : null;
        try
        {
            var pieces = MakeParts(scratch, fresh, (name, pixels, shown) =>
            {
                var kept = pixels;
                if (thighs != null && name == "legs")
                {
                    kept = (bool[])pixels.Clone();
                    for (int i = 0; i < kept.Length; i++) if (kept[i] && i / shown.Width > thighs.Value) kept[i] = false;
                }
                var (mx, my) = recipe.Move(name, facing);
                return (kept, mx, my + drop);
            });
            if (pieces == null) { Ui.Toast("Can't pose this one", "Couldn't find separate parts in this sprite (or the export doesn't have it).", ToastKind.Info); return; }
            string label = "Ready-made pose: " + recipe.Title;
            var history = Studio.Shell.Document?.History;
            int steps = history?.UndoCount ?? 0;
            Save(label, pose =>
            {
                var hidden = PackWriter.Clone(fresh);
                hidden.Visible = false;   // the reference underneath
                pose.Frames[FrameIndex].Layers = new List<Layer> { hidden }.Concat(pieces.Select(p => PackWriter.Clone(p.Piece))).ToList();
            }, lib =>
            {
                foreach (var piece in pieces) { lib.WriteImage(piece.Mask.Image!, piece.Keep); lib.SaveMask(piece.Mask); }
            });
            // The simple editor keeps one flat picture: the parts become it, in the same undo step.
            if (!Studio.Settings.AdvancedPoseTools && history != null && history.UndoCount == steps + 1)
            {
                MakeOnePicture();
                if (history.UndoCount == steps + 2) history.MergeLast(label);
            }
        }
        finally { EndPreview(); }
        Tool = StudioTool.Select;
        Selection = null;
    }

    /// <summary>The last canvas row of the thighs when only a share of the legs stays, and how far the rest must drop.</summary>
    private int? LegRows(Pose pose, Layer layer, double keep, out int drop)
    {
        drop = 0;
        if (Context.Placement(pose, FrameIndex, layer) is not { } place) return null;
        var shown = Context.RenderLayer(pose, FrameIndex, layer).Image;
        string facing = (layer as LiveLayer)?.SourceFrame?.Facing ?? pose.Facing ?? "down";
        var legs = PartSplitter.Split(shown, HeadTest(layer, place, facing, pose.Frames[FrameIndex]), facing).FirstOrDefault(p => p.Name == "legs");
        if (legs == null) return null;
        int top = -1, bottom = -1;
        for (int i = 0; i < legs.Pixels.Length; i++) if (legs.Pixels[i]) { int row = i / shown.Width; if (top < 0) top = row; bottom = row; }
        if (top < 0) return null;
        int last = top + Math.Max(1, (int)Math.Ceiling((bottom - top + 1) * keep)) - 1;
        drop = bottom - last;
        return last;
    }

    /// <summary>Canvas pixel → is it the head? The rig's head mask for this facing, else everything above the Neck anchor.</summary>
    private Func<int, int, bool> HeadTest(Layer layer, LayerPlacement place, string facing, PoseFrame frame)
    {
        if (layer is LiveLayer { Source.Kind: not "Farmer" } && PoseCharacter() is { } who && Library?.RigFor(who.Key) is { } rig
            && rig.HeadMask.TryGetValue(facing, out var maskId) && Context.FindMask(maskId) is { } mask)
        {
            string? path = mask.PerFrame.TryGetValue(place.SourceFrameIndex.ToString(System.Globalization.CultureInfo.InvariantCulture), out var perFrame) ? perFrame : mask.Image;
            if (Context.PackImage(path) is { } image && image.Width == place.SourceWidth && image.Height == place.SourceHeight)
            {
                bool remove = string.Equals(mask.Mode, "remove", StringComparison.OrdinalIgnoreCase);
                return (x, y) => place.ToSource(x, y) is { } s && (image.Pixels[s.Y * image.Width + s.X] >> 24 != 0) != remove;
            }
        }
        int neckY = frame.Anchors.TryGetValue("Neck", out var neck) && neck is { Length: 2 } ? neck[1] : Height / 2;
        return (_, y) => y <= neckY;
    }

    /// <summary>Press on a part with the Select tool: drag it (no selection box needed).</summary>
    private bool BeginPartDrag(int x, int y)
    {
        if (TopLayerAt(x, y) is not { } hit || IsLayerLocked(hit)) return false;
        var layer = Frame!.Layers[hit];
        if (layer.Mask is not { } id || Context.FindMask(id) is not { } mask || !mask.Tags.Contains(PartTag)) return false;
        // The part's box on the canvas, so the selection follows it.
        var shown = Context.RenderLayer(Model, FrameIndex, layer).Image;
        int l = Width, t = Height, r = -1, b = -1;
        for (int py = 0; py < shown.Height; py++)
            for (int px = 0; px < shown.Width; px++)
                if (shown.Pixels[py * shown.Width + px] >> 24 != 0) { l = Math.Min(l, px); t = Math.Min(t, py); r = Math.Max(r, px); b = Math.Max(b, py); }
        if (r < 0) return false;
        var box = new PixelRect(l, t, r - l + 1, b - t + 1);
        Selection = box;
        lifted = (box, FrameIndex, new List<string> { PieceKey(layer) });
        var moving = new Lift { From = (x, y), Selection = box };
        moving.Existing.Add(PieceKey(layer));
        lift = moving;
        return true;
    }
}
