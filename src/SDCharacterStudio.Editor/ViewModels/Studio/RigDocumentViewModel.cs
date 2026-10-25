using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using SDCharacterStudio.Core.Models;
using Wpf.Ui.Controls;

namespace SDCharacterStudio.Editor.ViewModels;

public enum RigTool { Neck, FaceBox }

/// <summary>
/// The rig editor (doc 03 §8): per facing, the standing and walking frames, the neck point heads hang from, the face box
/// expressions paint into and the head mask; the colour samples slot colours come from; and the portrait rig.
/// </summary>
public sealed partial class RigDocumentViewModel : StudioDocumentViewModel
{
    private (int X, int Y)? boxStart;

    public RigDocumentViewModel(CharacterStudioViewModel studio, string id) : base(studio, LibraryKind.Rig, id) { zoom = 12; }

    public override SymbolRegular Icon => SymbolRegular.PersonEdit24;
    public CharacterRig Model { get; private set; } = new();
    public ObservableCollection<AnchorMarker> Anchors { get; } = new();

    [ObservableProperty] private string facing = "left";
    [ObservableProperty] private bool portrait;
    [ObservableProperty] private RigTool tool = RigTool.Neck;
    [ObservableProperty] private double zoom;
    [ObservableProperty] private BitmapSource? frameImage;
    [ObservableProperty] private BitmapSource? overlay;
    [ObservableProperty] private PixelRect? faceBox;
    [ObservableProperty] private string? hoverText;

    public bool IsFarmer => Model.Kind == "Farmer";
    public int Width => Portrait ? Model.Portrait?.FrameSize[0] ?? 64 : Model.FrameSize[0];
    public int Height => Portrait ? Model.Portrait?.FrameSize[1] ?? 64 : Model.FrameSize[1];
    public string[] Facings => RigFactory.Facings;

    partial void OnFacingChanged(string value) { Render(); BuildFields(); }
    partial void OnPortraitChanged(bool value) { Render(); BuildFields(); OnPropertyChanged(nameof(Width)); OnPropertyChanged(nameof(Height)); }

    public override bool Reload()
    {
        if (Library?.Rig(ItemId) is not { } fresh) return false;
        Model = fresh;
        Title = Model.Id ?? ItemId;
        Render();
        if (!IsOwnCommit) BuildFields();
        OnPropertyChanged(nameof(IsFarmer)); OnPropertyChanged(nameof(Width)); OnPropertyChanged(nameof(Height));
        return true;
    }

    private int StandFrame => Model.Frames.TryGetValue(Facing, out var f) ? f.Stand : 0;

    public void Render()
    {
        Anchors.Clear();
        FaceBox = null;
        Problems = null;
        RgbaImageOrNull(out var frame);
        FrameImage = frame == null ? null : StudioImages.ToBitmap(frame);
        int[]? neck = Portrait ? Model.Portrait?.NeckPoint : Model.NeckPoint.GetValueOrDefault(Facing);
        if (neck is { Length: 2 }) Anchors.Add(new AnchorMarker("Neck", neck[0], neck[1]));
        int[]? box = Portrait ? Model.Portrait?.FaceBox : Model.FaceBox.GetValueOrDefault(Facing);
        if (box is { Length: 4 }) FaceBox = new PixelRect(box[0], box[1], box[2], box[3]);
        string? maskId = Portrait ? Model.Portrait?.HeadMask : Model.HeadMask.GetValueOrDefault(Facing);
        var mask = Context.PackImage(Library?.Mask(maskId)?.Image);
        if (mask != null && mask.Width == Width && mask.Height == Height)
        {
            var tint = StudioImages.Blank(Width, Height);
            for (int i = 0; i < tint.Pixels.Length; i++) if (mask.Pixels[i] >> 24 != 0) tint.Pixels[i] = 0xFF3080E0;
            Overlay = StudioImages.ToBitmap(tint);
        }
        else Overlay = null;
    }

    private void RgbaImageOrNull(out Core.Compositing.RgbaImage? frame)
    {
        frame = null;
        if (IsFarmer)
        {
            frame = Studio.StandingFrame(new CharacterChoice("farmer", "Player", null, true, null), Facing);
            if (frame == null) Problems = "The game export has no player renders yet. Load a save with the Bridge installed.";
            return;
        }
        if (Portrait)
        {
            var sheet = Context.NpcTexture(Model.Npc ?? "", portrait: true);
            frame = sheet == null ? null : StudioImages.Frame(sheet, Width, Height, 0);
            if (frame == null) Problems = $"The game export has no portrait for {Model.Npc}.";
            return;
        }
        try
        {
            var resolver = new EditorLayerSourceResolver(Context, new Pose { FrameSize = Model.FrameSize }, Facing, new List<string>());
            frame = resolver.SampleFrame(Model, StandFrame);
        }
        catch (ArgumentException ex) { Problems = ex.Message; }
    }

    // ---------------- canvas ----------------

    public void PointerDown(int x, int y)
    {
        if (IsFarmer) { Ui.Toast("The player isn't rigged by hand", "Player parts come from the game's own renderer; their neck follows each frame.", ToastKind.Info); return; }
        x = Math.Clamp(x, 0, Width - 1); y = Math.Clamp(y, 0, Height - 1);
        if (Tool == RigTool.Neck) { SetNeck(x, y); return; }
        boxStart = (x, y);
        FaceBox = new PixelRect(x, y, 1, 1);
    }

    public void PointerMove(int x, int y)
    {
        HoverText = x >= 0 && y >= 0 && x < Width && y < Height ? $"{x}, {y}" : null;
        if (boxStart is { } start) FaceBox = PixelRect.FromCorners(start.X, start.Y, Math.Clamp(x, 0, Width - 1), Math.Clamp(y, 0, Height - 1));
    }

    public void PointerUp()
    {
        if (boxStart == null || FaceBox is not { } box) return;
        boxStart = null;
        var value = new[] { box.X, box.Y, box.Width, box.Height };
        Commit(Portrait ? "Portrait face box" : $"Face box ({Facing})", lib =>
        {
            if (Portrait) (Model.Portrait ??= new PortraitRig()).FaceBox = value; else Model.FaceBox[Facing] = value;
            lib.SaveRig(Model);
        });
        Render(); BuildFields();
    }

    public void SetNeck(int x, int y)
    {
        Commit(Portrait ? "Portrait neck point" : $"Neck point ({Facing})", lib =>
        {
            if (Portrait) (Model.Portrait ??= new PortraitRig()).NeckPoint = new[] { x, y }; else Model.NeckPoint[Facing] = new[] { x, y };
            lib.SaveRig(Model);
        });
        Render(); BuildFields();
    }

    // ---------------- properties ----------------

    public override void BuildFields()
    {
        Fields.Clear();
        var commit = FieldCommit(lib => lib.SaveRig(Model));
        if (IsFarmer)
        {
            Fields.Add(new SectionField("Player rig", "The player is drawn from live parts; heads follow the game's own per-frame neck. Colours come from Farmer.Skin, Farmer.Hair…"));
            return;
        }
        Fields.Add(new SectionField($"Rig · {Model.Npc}", "Where this character's head, neck and face are, and which colours slots become."));
        Fields.Add(new NoteField($"Sprite frames {Model.FrameSize[0]}×{Model.FrameSize[1]}."));
        if (Portrait) { BuildPortraitFields(commit); BuildSamples(commit); return; }

        Fields.Add(new SectionField($"Facing {Facing}"));
        Model.Frames.TryAdd(Facing, new FacingFrames());
        var frames = Model.Frames[Facing];
        Fields.Add(new NumberField("Standing frame", "Frames.Stand", () => frames.Stand, v => frames.Stand = Math.Max(0, v ?? 0), commit) { Minimum = 0 });
        Fields.Add(new TextField("Walking frames", "Frames.Walk", () => string.Join(", ", frames.Walk), v => frames.Walk = ParseInts(v), commit) { Placeholder = "5, 4, 6, 4" });
        Fields.Add(new NumberField("Neck x", "NeckPoint", () => Model.NeckPoint.GetValueOrDefault(Facing)?[0], v => Model.NeckPoint[Facing] = new[] { v ?? 0, Model.NeckPoint.GetValueOrDefault(Facing)?[1] ?? 0 }, commit) { Help = "Or click the frame with the Neck tool." });
        Fields.Add(new NumberField("Neck y", "NeckPoint", () => Model.NeckPoint.GetValueOrDefault(Facing)?[1], v => Model.NeckPoint[Facing] = new[] { Model.NeckPoint.GetValueOrDefault(Facing)?[0] ?? 0, v ?? 0 }, commit));
        AddBox("Face box", () => Model.FaceBox.GetValueOrDefault(Facing), v => Model.FaceBox[Facing] = v, commit);
        Fields.Add(new ChoiceField("Head mask", "HeadMask", MaskOptions(), () => Model.HeadMask.GetValueOrDefault(Facing), v => { if (v == null) Model.HeadMask.Remove(Facing); else Model.HeadMask[Facing] = v; }, commit)
        { Rebuild = true, Help = "Cuts the head out of the live sprite for this facing. Live Head layers use it." });
        if (Model.HeadMask.GetValueOrDefault(Facing) is { } mask) Fields.Add(new ActionField("Edit the head mask", () => Studio.OpenItem(LibraryKind.Mask, mask)));
        BuildSamples(commit);
    }

    private void BuildPortraitFields(FieldCommit commit)
    {
        var rig = Model.Portrait ??= new PortraitRig();
        Fields.Add(new SectionField("Portrait", "For composed portraits: the head of the live portrait floats on a drawn body's Neck."));
        Fields.Add(new NumberField("Neck x", "Portrait.NeckPoint", () => rig.NeckPoint?[0], v => rig.NeckPoint = new[] { v ?? 0, rig.NeckPoint?[1] ?? 0 }, commit));
        Fields.Add(new NumberField("Neck y", "Portrait.NeckPoint", () => rig.NeckPoint?[1], v => rig.NeckPoint = new[] { rig.NeckPoint?[0] ?? 0, v ?? 0 }, commit));
        AddBox("Face box", () => rig.FaceBox, v => rig.FaceBox = v, commit);
        Fields.Add(new ChoiceField("Head mask", "Portrait.HeadMask", MaskOptions(), () => rig.HeadMask, v => rig.HeadMask = v, commit) { Rebuild = true });
        if (rig.HeadMask != null) Fields.Add(new ActionField("Edit the portrait head mask", () => Studio.OpenItem(LibraryKind.Mask, rig.HeadMask)));
        else Fields.Add(new ActionField("Make a portrait head mask (above the neck)", MakePortraitMask));
    }

    private void MakePortraitMask()
    {
        var rig = Model.Portrait ??= new PortraitRig();
        int neck = rig.NeckPoint?[1] ?? 40;
        string id = $"{Model.Id}-portrait-head";
        Commit("Portrait head mask", lib =>
        {
            string path = lib.UniquePath($"masks/{id}.png");
            lib.WriteImage(path, PixelTools.AboveRow(64, 64, neck));
            lib.SaveMask(new Mask { Id = id, DisplayName = $"{Model.Npc} portrait head", FrameSize = new[] { 64, 64 }, Image = path, Tags = new() { Model.Id! } });
            rig.HeadMask = id;
            lib.SaveRig(Model);
        });
        Render(); BuildFields();
    }

    private void AddBox(string label, Func<int[]?> get, Action<int[]> set, FieldCommit commit)
    {
        string[] names = { "x", "y", "width", "height" };
        for (int i = 0; i < 4; i++)
        {
            int n = i;
            Fields.Add(new NumberField($"{label} {names[i]}", "FaceBox", () => get()?[n], v => { var box = (int[]?)get()?.Clone() ?? new[] { 0, 0, 1, 1 }; box[n] = v ?? 0; set(box); }, commit)
            { Minimum = n >= 2 ? 1 : 0, Help = n == 0 ? "Expressions paint here. Or drag a box on the frame with the Face box tool." : null });
        }
    }

    /// <summary>Colour samples: which pixels give a slot its dark, mid and light colours (doc 03 §8).</summary>
    private void BuildSamples(FieldCommit commit)
    {
        var list = new ListField("Colour samples", "ColorSamples", "Add sample", () => commit("Add colour sample", () =>
        {
            string name = Core.Palette.ColorSlots.Names.FirstOrDefault(n => n != "Erase" && !Model.ColorSamples.ContainsKey(n)) ?? "Custom1";
            Model.ColorSamples[name] = new ColorSample { Mode = "auto-common", Frame = StandFrame, Box = new[] { 6, 8, 3, 3 } };
        }, true)) { EmptyText = "No samples: slot colours on this character fall back to their defaults." };
        var problems = new List<string>();
        var resolver = new EditorLayerSourceResolver(Context, new Pose { FrameSize = Model.FrameSize }, Facing, problems);
        var colors = Context.Colors(resolver, problems);
        foreach (var (name, sample) in Model.ColorSamples.ToArray())
        {
            string key = name;
            string ramp;
            try { var r = colors($"{Model.Id}.{key}"); ramp = $"{StudioImages.Hex(r.Dark)} {StudioImages.Hex(r.Mid)} {StudioImages.Hex(r.Light)}"; }
            catch (ArgumentException) { ramp = "can't sample"; }
            var row = new ListRowViewModel(() => commit("Remove colour sample", () => Model.ColorSamples.Remove(key), true)) { Caption = $"{key}: {ramp}" };
            row.Cells.Add(new ChoiceField("", "", new[] { new ChoiceItem("auto-common", "Most common"), new ChoiceItem("auto-skin", "Skin tones"), new ChoiceItem("auto-edge", "Outline") },
                () => sample.Mode, v => sample.Mode = v ?? "auto-common", commit));
            row.Cells.Add(new NumberField("frame", "", () => sample.Frame, v => sample.Frame = Math.Max(0, v ?? 0), commit) { Minimum = 0 });
            row.Cells.Add(new TextField("box", "", () => sample.Box == null ? null : string.Join(",", sample.Box), v => { var box = ParseInts(v); if (box.Length == 4) sample.Box = box; }, commit) { Placeholder = "x,y,w,h" });
            list.Rows.Add(row);
        }
        Fields.Add(list);
    }

    private IEnumerable<ChoiceItem> MaskOptions() => new[] { new ChoiceItem(null, "None") }.Concat(
        (Library?.Masks().Masks ?? new()).Where(m => m.Id != null).Select(m => new ChoiceItem(m.Id, m.DisplayName ?? m.Id!, m.Id)));

    private static int[] ParseInts(string? text) => (text ?? "").Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(t => int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : -1).Where(n => n >= 0).ToArray();
}
