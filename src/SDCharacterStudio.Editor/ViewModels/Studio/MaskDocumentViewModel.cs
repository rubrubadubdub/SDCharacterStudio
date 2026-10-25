using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Models;
using Wpf.Ui.Controls;

namespace SDCharacterStudio.Editor.ViewModels;

public enum MaskTool { Brush, Eraser, Wand }

/// <summary>One installed variant of the character with the mask applied (doc 05 §5.3).</summary>
public sealed record FitVariant(string Label, string? Detail, BitmapSource? Frame, BitmapSource? Kept, BitmapSource? OverPose);

/// <summary>
/// The mask editor (doc 07 §6.3): the mask drawn over the live source frame (red = the pixels the mask selects), with
/// brush, wand, above-neck, grow/shrink/invert, per-frame overrides, mirrored copies, and the fit check across every
/// installed variant of the character.
/// </summary>
public sealed partial class MaskDocumentViewModel : StudioDocumentViewModel
{
    private PaintSession? stroke;

    public MaskDocumentViewModel(CharacterStudioViewModel studio, string id) : base(studio, LibraryKind.Mask, id) { zoom = 12; }

    public override SymbolRegular Icon => SymbolRegular.CircleHalfFill24;
    public Mask Model { get; private set; } = new();
    public CharacterRig? Rig { get; private set; }
    public ObservableCollection<FitVariant> Fit { get; } = new();

    [ObservableProperty] private int sourceFrame;
    [ObservableProperty] private double zoom;
    [ObservableProperty] private bool showGrid = true;
    [ObservableProperty] private MaskTool tool = MaskTool.Brush;
    [ObservableProperty] private int wandTolerance = 8;
    [ObservableProperty] private bool wandGlobal;
    [ObservableProperty] private BitmapSource? frameImage;
    [ObservableProperty] private BitmapSource? overlay;
    [ObservableProperty] private string? hoverText;

    public int Width => Model.FrameSize is { Length: 2 } s ? s[0] : Rig?.FrameSize[0] ?? 16;
    public int Height => Model.FrameSize is { Length: 2 } s ? s[1] : Rig?.FrameSize[1] ?? 32;
    private string FrameKey => SourceFrame.ToString(CultureInfo.InvariantCulture);
    public bool HasOverride => Model.PerFrame.ContainsKey(FrameKey);
    public string? CurrentPath => HasOverride ? Model.PerFrame[FrameKey] : Model.Image;
    public string ModeText => Model.Mode == "remove" ? "Red pixels are removed from the layer." : "Red pixels are kept; the rest of the layer is hidden.";
    public string FrameText => $"Source frame {SourceFrame}{(HasOverride ? " · its own mask (per-frame)" : "")}";

    private bool loading;
    partial void OnSourceFrameChanged(int value) { if (loading) return; Render(); RenderFit(); BuildFields(); OnPropertyChanged(nameof(HasOverride)); OnPropertyChanged(nameof(FrameText)); }

    public override bool Reload()
    {
        if (Library?.Mask(ItemId) is not { } fresh) return false;
        bool first = Model.Id == null;
        Model = fresh;
        Title = string.IsNullOrWhiteSpace(Model.DisplayName) ? Model.Id ?? ItemId : Model.DisplayName!;
        Rig = FindRig();
        if (first) { loading = true; SourceFrame = DefaultFrame(); loading = false; }
        Render();
        RenderFit();
        if (!IsOwnCommit) BuildFields();
        OnPropertyChanged(nameof(Width)); OnPropertyChanged(nameof(Height)); OnPropertyChanged(nameof(HasOverride)); OnPropertyChanged(nameof(FrameText)); OnPropertyChanged(nameof(ModeText));
        return true;
    }

    /// <summary>The rig whose sprite the mask cuts: a tagged rig, a rig whose head masks use it, or one named like it.</summary>
    private CharacterRig? FindRig()
    {
        var library = Library;
        if (library == null) return null;
        foreach (var tag in Model.Tags) if (library.Rig(tag) is { } tagged) return tagged;
        foreach (var item in library.Items(LibraryKind.Rig))
            if (library.Rig(item.Id) is { } rig && (rig.HeadMask.Values.Any(m => string.Equals(m, Model.Id, StringComparison.OrdinalIgnoreCase)) || string.Equals(rig.Portrait?.HeadMask, Model.Id, StringComparison.OrdinalIgnoreCase)
                || Model.Id?.StartsWith(rig.Id + "-", StringComparison.OrdinalIgnoreCase) == true))
                return rig;
        return Studio.Character is { IsFarmer: false } who ? library.RigFor(who.Key) : null;
    }

    /// <summary>"robin-head-left" starts on Robin's left standing frame.</summary>
    private int DefaultFrame()
    {
        if (Rig == null) return 0;
        string? id = Model.Id;
        foreach (var facing in RigFactory.Facings)
            if (id?.Contains("-" + facing, StringComparison.OrdinalIgnoreCase) == true && Rig.Frames.TryGetValue(facing, out var frames)) return frames.Stand;
        return Rig.Frames.TryGetValue("down", out var down) ? down.Stand : 0;
    }

    private RgbaImage? SourceImage(string? variant = null)
    {
        if (Rig?.Npc is not { } npc) return null;
        using var _ = variant != null ? Context.SheetOverride(npc, variant) : null;
        try
        {
            var problems = new List<string>();
            var resolver = new EditorLayerSourceResolver(Context, new Pose { FrameSize = new[] { Width, Height } }, "down", problems);
            return resolver.SampleFrame(Rig, SourceFrame);
        }
        catch (ArgumentException) { return null; }
    }

    private RgbaImage MaskImage() => Context.PackImage(CurrentPath) is { } mask && mask.Width == Width && mask.Height == Height ? mask : StudioImages.Blank(Width, Height);

    public void Render()
    {
        var source = SourceImage();
        FrameImage = source == null ? null : StudioImages.ToBitmap(source);
        Problems = source == null ? (Rig == null ? "This mask isn't linked to a character's rig. Add the rig's ID to its tags." : $"The game export has no sprite frame {SourceFrame} for {Rig.Npc}.") : null;
        var mask = MaskImage();
        var tint = StudioImages.Blank(Width, Height);
        for (int i = 0; i < tint.Pixels.Length; i++) if (mask.Pixels[i] >> 24 != 0) tint.Pixels[i] = 0xFFE03030;
        Overlay = StudioImages.ToBitmap(tint);
    }

    /// <summary>The fit check: every installed variant's frame, what the mask keeps of it, and an open pose drawn with it.</summary>
    [RelayCommand]
    public void RenderFit()
    {
        Fit.Clear();
        if (Rig?.Npc is not { } npc || Studio.Shell.Catalog is not { } catalog || catalog.Character(npc) is not { } character) return;
        var mask = MaskImage();
        var mode = Model.Mode == "remove" ? MaskMode.Remove : MaskMode.Keep;
        var pose = Studio.Documents.OfType<PoseDocumentViewModel>().FirstOrDefault(p => UsesThisMask(p.Model));
        var assets = new[] { character.Current?.Sprite ?? character.Sprite, character.Sprite }.Concat(character.Appearances.Select(a => a.Sprite))
            .Where(a => a != null).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in assets)
        {
            var frame = SourceImage(asset);
            if (frame == null) continue;
            var kept = StudioImages.Clone(frame);
            Compositor.ApplyMask(kept, mask, mode);
            BitmapSource? over = null;
            if (pose != null)
            {
                using (Context.SheetOverride(npc, asset!)) over = StudioImages.ToBitmap(Context.RenderFrame(pose.Model, pose.FrameIndex).Image);
            }
            var origin = catalog.AssetOrigin(asset!);
            string? detail = origin?.IsModProvided == true ? catalog.ModLabel(origin.ProvidedBy[0]) : origin?.IsEdited == true ? "vanilla, edited by " + catalog.ModLabel(origin.EditedBy[0]) : "vanilla";
            Fit.Add(new FitVariant(asset!.Replace("Characters/", ""), detail, StudioImages.ToBitmap(frame), StudioImages.ToBitmap(kept), over));
        }
    }

    private bool UsesThisMask(Pose pose) => pose.Frames.SelectMany(f => f.Layers).Any(l => string.Equals(l.Mask, Model.Id, StringComparison.OrdinalIgnoreCase)
        || l is LiveLayer { Part: "Head" } && l.Mask == null && Rig != null && Rig.HeadMask.Values.Any(m => string.Equals(m, Model.Id, StringComparison.OrdinalIgnoreCase)));

    // ---------------- editing ----------------

    /// <summary>Write the mask image (one undo step); a mask without an image gets one.</summary>
    private void WriteMask(string label, RgbaImage image)
    {
        Commit(label, lib =>
        {
            string? path = CurrentPath;
            if (path == null)
            {
                path = lib.UniquePath($"masks/{Model.Id}.png");
                Model.Image = path;
                Model.FrameSize ??= new[] { Width, Height };
                lib.SaveMask(Model);
            }
            lib.WriteImage(path, image);
        });
        Context.InvalidatePack();
        Render(); RenderFit();
    }

    public void PointerDown(int x, int y, PointerButton button, bool shift)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return;
        if (Tool == MaskTool.Wand)
        {
            if (SourceImage() is not { } source) return;
            var mask = StudioImages.Clone(MaskImage());
            bool remove = shift || button == PointerButton.Secondary;
            foreach (var (px, py) in PixelTools.Wand(source, x, y, WandTolerance, WandGlobal)) PixelTools.Set(mask, px, py, remove ? 0u : PixelTools.MaskOn);
            WriteMask(remove ? "Wand subtract" : "Wand add", mask);
            return;
        }
        uint color = Tool == MaskTool.Eraser || button == PointerButton.Secondary ? 0u : PixelTools.MaskOn;
        stroke = PaintSession.Begin(Context, CurrentPath ?? "masks/__new.png", MaskImage(), 0, 0, color);
        stroke.DrawTo(x, y);
        Render();
    }

    public void PointerMove(int x, int y)
    {
        HoverText = x >= 0 && y >= 0 && x < Width && y < Height ? $"{x}, {y}" : null;
        if (stroke == null) return;
        stroke.DrawTo(x, y);
        Render();
    }

    public void PointerUp()
    {
        if (stroke is not { } done) return;
        stroke = null;
        var image = done.End();
        if (done.Changed) WriteMask(Tool == MaskTool.Eraser ? "Erase mask" : "Paint mask", image); else Render();
    }

    /// <summary>Keep everything at or above the rig's neck line (doc 07 §6.3).</summary>
    [RelayCommand]
    private void AboveNeck()
    {
        string facing = Rig?.Frames.FirstOrDefault(f => f.Value.Stand == SourceFrame || f.Value.Walk.Contains(SourceFrame)).Key ?? "down";
        int neck = Rig != null && Rig.NeckPoint.TryGetValue(facing, out var point) ? point[1] : Height / 2 - 1;
        WriteMask("Above neck line", PixelTools.AboveRow(Width, Height, neck));
    }

    [RelayCommand] private void Grow() => WriteMask("Grow mask", PixelTools.GrowShrink(MaskImage(), grow: true));
    [RelayCommand] private void Shrink() => WriteMask("Shrink mask", PixelTools.GrowShrink(MaskImage(), grow: false));
    [RelayCommand] private void Invert() => WriteMask("Invert mask", PixelTools.Invert(MaskImage()));
    [RelayCommand] private void ClearMask() => WriteMask("Clear mask", StudioImages.Blank(Width, Height));

    /// <summary>Give this source frame its own mask (a walk frame where the head bobs).</summary>
    [RelayCommand]
    private void MakeOverride()
    {
        if (HasOverride) return;
        var copy = MaskImage();
        string key = FrameKey;
        Commit($"Own mask for frame {key}", lib =>
        {
            string path = lib.UniquePath($"masks/{Model.Id}-{key}.png");
            lib.WriteImage(path, copy);
            Model.PerFrame[key] = path;
            lib.SaveMask(Model);
        });
        Reload();
    }

    [RelayCommand]
    private void RemoveOverride()
    {
        if (!HasOverride) return;
        string key = FrameKey, path = Model.PerFrame[key];
        Commit($"Frame {key} uses the main mask", lib => { Model.PerFrame.Remove(key); lib.SaveMask(Model); lib.RemoveFile(path); });
        Reload();
    }

    /// <summary>"robin-head-left" → a mirrored "robin-head-right" (doc 07 §6.3 Copy to facings).</summary>
    [RelayCommand]
    private void CopyMirrored()
    {
        string id = Model.Id ?? "";
        string? target = id.Contains("-left", StringComparison.OrdinalIgnoreCase) ? ReplaceFacing(id, "-left", "-right")
            : id.Contains("-right", StringComparison.OrdinalIgnoreCase) ? ReplaceFacing(id, "-right", "-left") : null;
        if (target == null) { Ui.Toast("Name it by facing", "Mirrored copies work for masks named …-left or …-right.", ToastKind.Info); return; }
        if (Library?.Mask(target) != null && !Ui.Confirm("Replace?", $"'{target}' exists. Replace it with a mirrored copy of this mask?", "Replace")) return;
        var flipped = StudioImages.FlipHorizontal(MaskImage());
        Commit("Mirrored copy " + target, lib =>
        {
            string path = lib.Mask(target)?.Image ?? lib.UniquePath($"masks/{target}.png");
            lib.WriteImage(path, flipped);
            lib.SaveMask(new Mask
            {
                Id = target, DisplayName = Model.DisplayName?.Replace("left", "§").Replace("right", "left").Replace("§", "right"),
                FrameSize = Model.FrameSize, Image = path, Mode = Model.Mode, Tags = Model.Tags.ToList()
            });
        });
        Ui.Toast("Mirrored", $"'{target}' is a flipped copy. Check it on the {(target.Contains("right") ? "right" : "left")} standing frame.", ToastKind.Success);
    }

    private static string ReplaceFacing(string id, string from, string to) => id.Replace(from, to, StringComparison.OrdinalIgnoreCase);

    // ---------------- properties ----------------

    public override void BuildFields()
    {
        Fields.Clear();
        var commit = FieldCommit(lib => lib.SaveMask(Model));
        Fields.Add(new SectionField("Mask", "Cuts part of a live sprite: a head, a body part. Layers use it by ID; rigs use it as their head mask."));
        Fields.Add(new TextField("ID", "Id", () => Model.Id, v => Rename(v), (l, a, r) => a()) { AllowNull = false, Help = "Renaming updates the poses and rigs that use it." });
        Fields.Add(new TextField("Name", "DisplayName", () => Model.DisplayName, v => Model.DisplayName = v, commit));
        Fields.Add(new ChoiceField("Mode", "Mode", new[] { new ChoiceItem("keep", "Keep the red pixels"), new ChoiceItem("remove", "Remove the red pixels") }, () => Model.Mode, v => Model.Mode = v ?? "keep", commit) { Rebuild = true });
        Fields.Add(new TextField("Tags", "Tags", () => string.Join(", ", Model.Tags), v => Model.Tags = (v ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(), commit)
        { Help = "The rig's ID in the tags ties the mask to that character." });
        Fields.Add(new SectionField("Source frame", "The live frame you draw the mask on. Frames can have their own mask (Per-frame) when the head moves."));
        var frames = new List<ChoiceItem>();
        if (Rig != null)
            foreach (var (facing, f) in Rig.Frames)
            {
                frames.Add(new ChoiceItem(f.Stand.ToString(CultureInfo.InvariantCulture), $"{facing} standing ({f.Stand})"));
                foreach (int walk in f.Walk.Distinct().Where(w => w != f.Stand)) frames.Add(new ChoiceItem(walk.ToString(CultureInfo.InvariantCulture), $"{facing} walking ({walk})"));
            }
        FieldCommit local = (label, apply, rebuild) => apply();
        Fields.Add(new ChoiceField("Frame", "", frames, () => SourceFrame.ToString(CultureInfo.InvariantCulture), v => SourceFrame = int.TryParse(v, out int n) ? n : 0, local) { IsEditable = true });
        if (Model.PerFrame.Count > 0) Fields.Add(new NoteField("Frames with their own mask: " + string.Join(", ", Model.PerFrame.Keys)));
        var uses = Library == null ? Array.Empty<string>() : References.UsesOf(Library, Studio.Shell.Document!, LibraryKind.Mask, Model.Id ?? "");
        Fields.Add(new NoteField(uses.Count == 0 ? "No pose uses it yet." : "Used by " + string.Join(", ", uses) + "."));
    }

    private void Rename(string? value)
    {
        string? next = value == null ? null : PackDocument.ToContentId(value);
        if (next == null || next == Model.Id || Library == null) return;
        if (Library.Mask(next) != null) { Ui.Toast("Name taken", $"There's already a mask called '{next}'.", ToastKind.Warning); return; }
        string old = Model.Id!;
        Commit($"Rename mask {old}", lib =>
        {
            var masks = lib.Masks();
            foreach (var mask in masks.Masks.Where(m => m.Id == old)) mask.Id = next;
            lib.SaveMasks(masks);
            References.RenameMask(lib, old, next);
        });
        ItemId = next;
        Reload();
    }

    public override void Closing() { if (stroke != null) { stroke.End(); stroke = null; } }
}
