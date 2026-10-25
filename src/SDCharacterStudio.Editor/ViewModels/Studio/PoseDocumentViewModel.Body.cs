using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Studio;

namespace SDCharacterStudio.Editor.ViewModels;

/// <summary>A ready-made pose in the gallery, drawn for the character on screen.</summary>
public sealed record PresetItem(string Id, string Title, string Hint, BitmapSource? Thumbnail, bool IsAnimated);

/// <summary>
/// Her body (D-49 phase 4): a body map per character and facing says which pixels of her standing frame are her head,
/// body, arms and legs. Made automatically, fixed with a brush (Show parts), saved with her rig. It drives Select part
/// and the ready-made poses, which are therefore made for her body (the owner: "each pose needs to be made PER
/// character as they have different bodies").
/// </summary>
public partial class PoseDocumentViewModel
{
    public ObservableCollection<PresetItem> Presets { get; } = new();
    public IReadOnlyList<BodyPart> BodyParts => BodyMaps.Parts;
    /// <summary>Show her body parts in colour, and paint them with the part brush to fix them.</summary>
    [ObservableProperty] private bool showParts;
    [ObservableProperty] private string partBrush = "head";
    [ObservableProperty] private BitmapSource? partsOverlay;
    private RgbaImage? partsWorking;
    private string? presetsFor;

    public BitmapSource? CanvasOverlay => ShowParts ? PartsOverlay : IsolateOverlay;
    public double CanvasOverlayOpacity => ShowParts ? 0.6 : 0.72;
    partial void OnPartsOverlayChanged(BitmapSource? value) => OnPropertyChanged(nameof(CanvasOverlay));
    partial void OnIsolateOverlayChanged(BitmapSource? value) => OnPropertyChanged(nameof(CanvasOverlay));
    partial void OnShowPartsChanged(bool value)
    {
        if (value && !IsStandingPicture())
            Ui.Toast("Her standing frame", "Body parts are set on her standing frame. Start this frame from it (Her frames) or open a new pose to fix them.", ToastKind.Info);
        RefreshPartsOverlay();
        OnPropertyChanged(nameof(CanvasOverlay)); OnPropertyChanged(nameof(CanvasOverlayOpacity));
    }

    private string PoseFacing => Model.Facing ?? "down";

    /// <summary>Her standing frame in the outfit this pose wears (pinned) or the studio shows.</summary>
    private RgbaImage? StandingImage(string facing)
    {
        if (PoseCharacter() is not { IsFarmer: false } who) return null;
        if (PinnedOutfit is { } pinned && Library?.RigFor(who.Key) is { } rig && Context.AssetTexture(pinned) is { } sheet)
            return StudioImages.Frame(sheet, rig.FrameSize[0], rig.FrameSize[1], rig.Frames.TryGetValue(facing, out var frames) ? frames.Stand : 0);
        return Studio.StandingFrame(who, facing);
    }

    /// <summary>Her body map for a facing: the one saved with her rig, else a fresh guess from her standing frame.</summary>
    public RgbaImage? BodyMapFor(string facing, out bool saved)
    {
        saved = false;
        if (PoseCharacter() is not { IsFarmer: false } who || StandingImage(facing) is not { } standing) return null;
        return ReadyMadePoses.BodyMap(Context, Library?.RigFor(who.Key), standing, facing, out saved);
    }

    /// <summary>The flat picture shows her standing frame (so its map cells are standing-frame pixels).</summary>
    private bool IsStandingPicture()
    {
        if (Flat is not { } flat) return false;
        if (flat.Live.SourceFrame is null or { Stand: true }) return true;
        return PoseCharacter() is { } who && Library?.RigFor(who.Key) is { } rig && rig.Frames.TryGetValue(PoseFacing, out var frames) && flat.Live.SourceFrame.Index == frames.Stand;
    }

    /// <summary>A map cell's standing-frame pixel, or null when it shows another of her frames.</summary>
    private static (int X, int Y)? Source(uint cell) => Compositor.MapSource(cell) is { Frame: null } s ? (s.X, s.Y) : null;

    /// <summary>For each canvas pixel of the picture, the body part it shows (null: none or not her).</summary>
    private string?[] PartsOnCanvas()
    {
        var parts = new string?[Width * Height];
        if (FlatImages() is not { } images) return parts;
        var shown = Context.RenderFrame(Model, FrameIndex).Image;
        if (IsStandingPicture() && (partsWorking ?? BodyMapFor(PoseFacing, out _)) is { } body)
        {
            for (int i = 0; i < parts.Length; i++)
                if (shown.Pixels[i] >> 24 != 0 && images.Map.Pixels[i] >> 24 != 0 && images.Paint.Pixels[i] >> 24 == 0 && Source(images.Map.Pixels[i]) is (int sx, int sy))
                    parts[i] = BodyMaps.PartAt(body, sx, sy) ?? "body";
            return parts;
        }
        // Another frame of hers: guess from what's on the canvas.
        int neckY = Frame?.Anchors.TryGetValue("Neck", out var neck) == true && neck is { Length: 2 } ? neck[1] : Height / 3;
        foreach (var part in PartSplitter.Split(shown, (_, y) => y <= neckY, PoseFacing))
            for (int i = 0; i < parts.Length; i++) if (part.Pixels[i]) parts[i] = part.Name;
        return parts;
    }

    private void RefreshPartsOverlay()
    {
        if (!ShowParts) { PartsOverlay = null; return; }
        var parts = PartsOnCanvas();
        var overlay = StudioImages.Blank(Width, Height);
        for (int i = 0; i < parts.Length; i++) if (BodyMaps.Find(parts[i]) is { } part) overlay.Pixels[i] = part.Color;
        PartsOverlay = StudioImages.ToBitmap(overlay);
    }

    /// <summary>Select a body part's pixels on the canvas (arms: both).</summary>
    [RelayCommand]
    public void SelectPart(string? part)
    {
        if (part == null || Flat == null) return;
        var parts = PartsOnCanvas();
        var cells = Enumerable.Range(0, parts.Length).Where(i => parts[i] == part || (part == "arms" && BodyMaps.IsArm(parts[i]))).Select(i => (i % Width, i / Width));
        var shape = SelectionMask.FromCells(Width, Height, cells);
        if (shape.IsEmpty) { Ui.Toast("No " + (part == "arms" ? "arms" : BodyMaps.Find(part)?.Label.ToLowerInvariant() ?? part), "This frame doesn't show that part. Fix her parts with Show parts.", ToastKind.Info); return; }
        if (!IsSelectionTool(Tool) && Tool != StudioTool.Move) Tool = StudioTool.Select;
        SetShape(shape);
    }

    // ---------------- painting her parts ----------------

    private bool TryPartsDown(int x, int y)
    {
        if (!ShowParts) return false;
        if (!IsStandingPicture() || BodyMapFor(PoseFacing, out _) is not { } body) return true;
        partsWorking = StudioImages.Clone(body);
        PaintParts(x, y);
        return true;
    }

    private bool TryPartsMove(int x, int y)
    {
        if (partsWorking == null) return false;
        PaintParts(x, y);
        return true;
    }

    private void PaintParts(int x, int y)
    {
        if (partsWorking == null || FlatImages() is not { } images || BodyMaps.Find(PartBrush) is not { } part || StandingImage(PoseFacing) is not { } standing) return;
        foreach (var (px, py) in BrushAt(x, y))
        {
            if (px < 0 || py < 0 || px >= Width || py >= Height) continue;
            uint cell = images.Map.Pixels[py * Width + px];
            if (cell >> 24 == 0 || Source(cell) is not (int sx, int sy)) continue;
            if (PixelTools.Get(standing, sx, sy) >> 24 != 0) PixelTools.Set(partsWorking, sx, sy, part.Color);
        }
        RefreshPartsOverlay();
    }

    private bool TryPartsUp()
    {
        if (partsWorking is not { } map) return false;
        partsWorking = null;
        if (PoseCharacter() is not { IsFarmer: false } who) return true;
        string facing = PoseFacing;
        Save("Fix her body parts", _ => { }, lib =>
        {
            var rig = lib.RigFor(who.Key) ?? lib.Rig(RigFactory.EnsureNpcRig(lib, Studio.Shell.Catalog, who.Key))!;
            rig.BodyMap ??= new(StringComparer.OrdinalIgnoreCase);
            string path = rig.BodyMap.TryGetValue(facing, out var existing) ? existing : lib.UniquePath($"art/rigs/{PackDocument.ToContentId(rig.Id!)}/body-{facing}.png");
            lib.WriteImage(path, map);
            rig.BodyMap[facing] = path;
            lib.SaveRig(rig);
        }, rebuildFields: false);
        presetsFor = null;
        RefreshPresets();
        RefreshPartsOverlay();
        return true;
    }

    /// <summary>Forget the fixes for this facing: back to the automatic guess.</summary>
    [RelayCommand]
    private void ResetBodyParts()
    {
        if (PoseCharacter() is not { IsFarmer: false } who || Library?.RigFor(who.Key) is not { BodyMap: { } maps } || !maps.ContainsKey(PoseFacing)) return;
        string facing = PoseFacing;
        Save("Body parts: automatic", _ => { }, lib =>
        {
            var rig = lib.RigFor(who.Key)!;
            if (rig.BodyMap!.Remove(facing, out var path)) lib.RemoveFile(path);
            if (rig.BodyMap.Count == 0) rig.BodyMap = null;
            lib.SaveRig(rig);
        }, rebuildFields: false);
        presetsFor = null;
        RefreshPresets();
        RefreshPartsOverlay();
    }

    // ---------------- ready-made poses, made for her ----------------

    /// <summary>The preset gallery, each drawn on her (her body map, her outfit, the pose's facing).</summary>
    public void RefreshPresets()
    {
        string key = $"{PoseCharacter()?.Key}|{PoseFacing}|{PinnedOutfit}|{Studio.Outfit?.Asset}|{Library?.RigFor(PoseCharacter()?.Key)?.BodyMap?.GetValueOrDefault(PoseFacing)}";
        if (key == presetsFor) return;
        presetsFor = key;
        Presets.Clear();
        var standing = StandingImage(PoseFacing);
        var body = BodyMapFor(PoseFacing, out _);
        foreach (var preset in PosePresets.All)
        {
            BitmapSource? thumbnail = null;
            if (standing != null && body != null)
            {
                var frame = preset.Frames[preset.Frames.Count / 2];
                thumbnail = StudioImages.ToBitmap(Compositor.Remap(standing, PosePresets.Map(standing, body, frame, PoseFacing)));
            }
            Presets.Add(new PresetItem(preset.Id, preset.Title, preset.Hint + (preset.IsAnimated ? " (a little animation)" : ""), thumbnail, preset.IsAnimated));
        }
        OnPropertyChanged(nameof(HasPresetPictures));
    }

    /// <summary>The ready-made pose the preview is showing instead of this pose (hovering one in the gallery).</summary>
    private List<(BitmapSource Image, int Ms)>? presetPreview;
    private int presetShown;
    private double presetElapsed;
    /// <summary>"Ready-made: Wave…" while one plays in the preview.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(PreviewTitle))] private string? previewCaption;
    private string? previewPresetTitle;
    /// <summary>The preview panel's title: PREVIEW, or the ready-made pose playing there.</summary>
    public string PreviewTitle => previewPresetTitle is { } title ? $"PREVIEW · {title.ToUpperInvariant()} (READY-MADE)" : "PREVIEW";

    /// <summary>
    /// Play a ready-made pose in the preview panel, drawn on her, without changing the pose (null: back to the pose).
    /// The owner: "I can't preview the ready-made poses?"
    /// </summary>
    public void PreviewPreset(string? id)
    {
        presetPreview = null; previewPresetTitle = null; PreviewCaption = null;
        if (id != null && PosePresets.Find(id) is { } preset && StandingImage(PoseFacing) is { } standing && BodyMapFor(PoseFacing, out _) is { } body)
        {
            presetPreview = ReadyMadePoses.Maps(preset, standing, body, PoseFacing)
                .Select((map, k) => (Image: (BitmapSource)StudioImages.ToBitmap(Compositor.Remap(standing, map)), Ms: Math.Max(60, preset.Frames[k].Ms))).ToList();
            presetShown = 0; presetElapsed = 0;
            previewPresetTitle = preset.Title;
            PreviewCaption = $"Ready-made: {preset.Title}{(preset.IsAnimated ? $" ({presetPreview.Count} frames)" : "")}. Click it to use it on this frame.";
            PreviewImage = presetPreview[0].Image;
            return;
        }
        RestartPreview();
        TickPreview(0);
    }

    /// <summary>Advance a ready-made pose playing in the preview; false when none is.</summary>
    private bool TickPresetPreview(double ms)
    {
        if (presetPreview is not { Count: > 0 } frames) return false;
        presetElapsed += ms;
        while (presetElapsed >= frames[presetShown].Ms) { presetElapsed -= frames[presetShown].Ms; presetShown = (presetShown + 1) % frames.Count; }
        PreviewImage = frames[presetShown].Image;
        return true;
    }

    /// <summary>
    /// Make this frame a ready-made pose (more frames for an animated one), from her standing frame and body map: her own
    /// pixels, still live. One undo step. Layered (Advanced) frames use the older recipes.
    /// </summary>
    [RelayCommand]
    public void ApplyPreset(string? id)
    {
        if (PosePresets.Find(id) is not { } preset) return;
        if (Flat is not { } flat) { ApplyRecipe(id); return; }
        PreviewPreset(null);
        string facing = PoseFacing;
        if (StandingImage(facing) is not { } standing || BodyMapFor(facing, out _) is not { } body)
        { Ui.Toast("No standing frame", "Ready-made poses start from the character's standing sprite, which the game export doesn't have for this pose.", ToastKind.Info); return; }
        if (standing.Width != Width || standing.Height != Height)
        { Ui.Toast("A different size", "Ready-made poses need a pose the size of her sprite.", ToastKind.Info); return; }
        var maps = ReadyMadePoses.Maps(preset, standing, body, facing);
        int at = FrameIndex;
        Save("Ready-made pose: " + preset.Title, pose =>
        {
            var frame = pose.Frames[at];
            ((LiveLayer)frame.Layers[0]).SourceFrame = new SourceFrame { Facing = facing, Stand = true };
            if (preset.IsAnimated) frame.Duration = preset.Frames[0].Ms;
            for (int k = 1; k < maps.Count; k++)
            {
                var copy = CopyFrame(frame, at + k);
                copy.Duration = preset.Frames[k].Ms;
                pose.Frames.Insert(at + k, copy);
            }
            if (preset.Plays == "loop") SetLoopFrom(pose, 0);
            else if (preset.Plays == "once") SetLoopFrom(pose, pose.Frames.Count - 1);
        }, lib =>
        {
            lib.WriteImage(flat.Live.Map!, maps[0]); lib.WriteImage(flat.Paint.Image!, StudioImages.Blank(Width, Height));
            for (int k = 1; k < maps.Count; k++)
            {
                CopyFrameImages(lib, Model, at + k);
                var copy = FlatOf(Model.Frames[at + k])!;
                lib.WriteImage(copy.Live.Map!, maps[k]); lib.WriteImage(copy.Paint.Image!, StudioImages.Blank(Width, Height));
            }
        });
        Selection = null;
        NotifyPlayOrder();
        if (preset.IsAnimated) PreviewPlaying = true;
    }
}
