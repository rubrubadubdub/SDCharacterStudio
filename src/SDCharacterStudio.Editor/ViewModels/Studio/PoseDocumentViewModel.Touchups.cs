using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Palette;
using SDCharacterStudio.Editor.Studio;

namespace SDCharacterStudio.Editor.ViewModels;

/// <summary>
/// Painting helpers after Pixelorama, for clothes on her: paint only inside her (alpha lock), a shading brush that steps
/// along her own colours, and an outline for what you painted.
/// </summary>
public partial class PoseDocumentViewModel
{
    // ---------------- paint inside her ----------------

    /// <summary>Painting touches only pixels where she (or your paint) already is: patterns stay inside her outline.</summary>
    [ObservableProperty] private bool paintInside;
    partial void OnPaintInsideChanged(bool value) { Studio.Settings.PaintInside = value; Studio.Settings.Save(); }

    /// <summary>The tools "inside her" applies to (the eraser only ever removes what's there).</summary>
    public bool UsesPaint => Tool is StudioTool.Pencil or StudioTool.Fill or StudioTool.Line or StudioTool.Rectangle or StudioTool.Ellipse or StudioTool.Shade;

    /// <summary>During a stroke with "inside her" on: the cells that show something (from the picture as the stroke began).</summary>
    private bool[]? insideHer;

    private static bool[] Visible(RgbaImage picture) => picture.Pixels.Select(p => p >> 24 != 0).ToArray();

    private void BeginInside() => insideHer = PaintInside && UsesPaint ? Visible(Context.RenderFrame(Model, FrameIndex).Image) : null;

    private bool InsideHer(int x, int y) => insideHer == null || (x >= 0 && y >= 0 && x < Width && y < Height && insideHer[y * Width + x]);

    // ---------------- the shading brush ----------------

    private bool shadeLighter;
    private RgbaImage? shadeShown;
    private IReadOnlyList<Shading.Ramp>? shadeRamps;
    private readonly HashSet<(int X, int Y)> shaded = new();

    /// <summary>
    /// The ramps shading steps along: each of her colours the paint layer maps (painting the slot, so it stays live), and
    /// your named colours (plain colours).
    /// </summary>
    private IReadOnlyList<Shading.Ramp> ShadeRamps(Layer? paint)
    {
        var ramps = new List<Shading.Ramp>();
        if (paint?.ColorSlots is { Count: > 0 } slots)
        {
            var problems = new List<string>();
            var colors = Context.Colors(new EditorLayerSourceResolver(Context, Model, Model.Facing ?? "down", problems), problems);
            foreach (var (slot, source) in slots)
            {
                if (slot.Equals("Outline", StringComparison.OrdinalIgnoreCase) || slot.Equals("Erase", StringComparison.OrdinalIgnoreCase)) continue;
                var ramp = colors(source);
                var raw = new uint?[] { ColorSlots.Raw(slot, 0), ColorSlots.Raw(slot, 1), ColorSlots.Raw(slot, 2) };
                ramps.Add(new Shading.Ramp(new uint?[] { raw[0] == null ? null : ramp.Dark, raw[1] == null ? null : ramp.Mid, raw[2] == null ? null : ramp.Light }, raw));
            }
        }
        foreach (var named in Studio.NamedColors())
            ramps.Add(new Shading.Ramp(new uint?[] { named.Dark, named.Mid, named.Light }, new uint?[] { named.Dark, named.Mid, named.Light }));
        return ramps;
    }

    private void BeginShade(bool lighter)
    {
        shadeLighter = lighter;
        shadeShown = Context.RenderFrame(Model, FrameIndex).Image;
        shadeRamps = ShadeRamps(SelectedLayer);
        shaded.Clear();
    }

    /// <summary>One dab of the shading brush: each pixel under it one step darker or lighter, once per stroke.</summary>
    private void ShadeDab(int x, int y)
    {
        if (stroke == null || strokeBase == null || shadeShown == null || shadeRamps == null) return;
        foreach (var (px, py) in BrushAt(x, y))
        {
            if (px < 0 || py < 0 || px >= Width || py >= Height || !InsideSelection(px, py) || !shaded.Add((px, py))) continue;
            if (stroke.ToImage(px, py) is not { } p) continue;
            if (Shading.Step(shadeShown.Pixels[py * Width + px], PixelTools.Get(strokeBase, p.X, p.Y), shadeLighter, shadeRamps) is { } paint)
                strokeChanged |= PixelTools.Set(stroke.Image, p.X, p.Y, paint);
        }
    }

    /// <summary>The same cells shaded on every other picked frame, each from its own colours (inside the current save).</summary>
    private void ShadeOtherPicked(PackLibrary lib, IReadOnlyCollection<(int X, int Y)> cells, bool lighter)
    {
        foreach (var (frame, flat, _, paint) in OtherPickedFlatFrames().ToList())
        {
            var shown = Context.RenderFrame(Model, frame).Image;
            var ramps = ShadeRamps(flat.Paint);
            var result = StudioImages.Clone(paint);
            bool changed = false;
            foreach (var (x, y) in cells)
            {
                int i = y * Width + x;
                if (PaintInside && shown.Pixels[i] >> 24 == 0) continue;
                if (Shading.Step(shown.Pixels[i], paint.Pixels[i], lighter, ramps) is { } value && value != result.Pixels[i]) { result.Pixels[i] = value; changed = true; }
            }
            if (changed) lib.WriteImage(flat.Paint.Image!, result);
        }
    }

    private void EndShade() { shadeShown = null; shadeRamps = null; shaded.Clear(); }

    // ---------------- outline what you painted ----------------

    /// <summary>
    /// Her outline colour around your paint (Pixelorama's Outline effect): every empty pixel next to something you painted
    /// gets it, on this frame or every picked one, as one undo step. Her own outline colour when the picture paints her
    /// colours (so it follows sprite mods); otherwise the darkest colour in the picture.
    /// </summary>
    [RelayCommand]
    public void OutlinePaint()
    {
        // Linked frames share a picture: outline it once.
        var frames = (HasSeveralPicked ? PickedFrames : new[] { FrameIndex }).Distinct().GroupBy(i => PictureOf(Model.Frames.ElementAtOrDefault(i)) ?? $"#{i}", StringComparer.OrdinalIgnoreCase).Select(g => g.Contains(FrameIndex) ? FrameIndex : g.First()).ToList();
        var work = new List<(FlatFrame Flat, RgbaImage Paint)>();
        foreach (int index in frames)
        {
            if (FlatOf(Model.Frames.ElementAtOrDefault(index)) is not { } flat || Context.PackImage(flat.Paint.Image) is not { } paint || paint.Width != Width || paint.Height != Height) continue;
            var shown = Context.RenderFrame(Model, index).Image;
            uint outline = OutlineColor(flat, shown);
            var result = StudioImages.Clone(paint);
            int added = 0;
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    int i = y * Width + x;
                    if (shown.Pixels[i] >> 24 != 0 || !InsideSelection(x, y)) continue;
                    bool Painted(int nx, int ny) => nx >= 0 && ny >= 0 && nx < Width && ny < Height && paint.Pixels[ny * Width + nx] >> 24 != 0 && shown.Pixels[ny * Width + nx] >> 24 != 0;
                    if (Painted(x - 1, y) || Painted(x + 1, y) || Painted(x, y - 1) || Painted(x, y + 1)) { result.Pixels[i] = outline; added++; }
                }
            if (added > 0) work.Add((flat, result));
        }
        if (work.Count == 0)
        {
            Ui.Toast("Nothing to outline", IsFlat ? "Nothing you painted reaches the edge of the character yet: paint past their outline, then outline it." : "Outlining works on a pose that is one picture.", ToastKind.Info);
            return;
        }
        Save(work.Count > 1 ? $"Outline on {work.Count} frames" : "Outline what you painted", _ => { }, lib => { foreach (var (flat, image) in work) lib.WriteImage(flat.Paint.Image!, image); }, rebuildFields: false);
        Context.InvalidatePack();
        Render(); RenderThumbnails();
    }

    private uint OutlineColor(FlatFrame flat, RgbaImage shown)
    {
        if (flat.Paint.ColorSlots?.ContainsKey("Outline") == true) return ColorSlots.Outline;
        // Her sampled outline, else the darkest colour she shows.
        if (Library?.Rig(Model.For) is { } rig && rig.ColorSamples.ContainsKey("Outline"))
        {
            var problems = new List<string>();
            var ramp = Context.Colors(new EditorLayerSourceResolver(Context, Model, Model.Facing ?? "down", problems), problems)($"{rig.Id}.Outline");
            if (problems.Count == 0) return ramp.Mid | 0xFF000000;
        }
        return shown.Pixels.Where(p => p >> 24 == 255).DefaultIfEmpty(0xFF000000).MinBy(p => ((p >> 16) & 255) * 299 + ((p >> 8) & 255) * 587 + (p & 255) * 114);
    }
}
