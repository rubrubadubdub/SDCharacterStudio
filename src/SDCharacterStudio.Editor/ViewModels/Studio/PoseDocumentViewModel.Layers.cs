using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Runtime.Rendering;
using Wpf.Ui.Controls;

namespace SDCharacterStudio.Editor.ViewModels;

/// <summary>One row of the layers panel (doc 07 §4): eye, lock, type, name and chips.</summary>
public sealed partial class LayerRowViewModel : ObservableObject
{
    private readonly PoseDocumentViewModel document;
    public LayerRowViewModel(PoseDocumentViewModel document, int index, Layer layer, bool locked)
    {
        this.document = document; Index = index; Layer = layer; isLocked = locked;
        SelectCommand = new RelayCommand(() => document.LayerIndex = index);
        Refresh();
    }
    public int Index { get; }
    public Layer Layer { get; }
    public string Name => PoseDocumentViewModel.LayerName(Layer);
    public SymbolRegular TypeIcon => Layer switch { LiveLayer { Source.Kind: "Farmer" } => SymbolRegular.Person24, LiveLayer => SymbolRegular.PersonCircle24, PaintLayer => SymbolRegular.HandDraw24, ImageLayer => SymbolRegular.Image24, EffectLayer => SymbolRegular.Sparkle24, _ => SymbolRegular.QuestionCircle24 };
    public bool IsLive => Layer is LiveLayer;
    public string? MaskChip => Layer.Mask ?? (Layer is LiveLayer { Part: "Head" } live && live.Source?.Kind != "Farmer" ? "rig head" : null);
    public bool HasSlots => Layer.ColorSlots is { Count: > 0 };
    public string? AnchorChip => Layer.AnchorTo?.Anchor;
    public bool IsVisible
    {
        get => Layer.Visible;
        set { if (value != Layer.Visible) document.SetLayerVisible(Index, value); }
    }
    [ObservableProperty] private bool isLocked;
    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private BitmapSource? thumbnail;
    partial void OnIsLockedChanged(bool value) => document.SetLayerLocked(Index, value);
    partial void OnIsSelectedChanged(bool value) { if (value && document.LayerIndex != Index) document.LayerIndex = Index; }
    public IRelayCommand SelectCommand { get; }
    public void Refresh() => Thumbnail = document.LayerThumbnail(Layer);
}

public partial class PoseDocumentViewModel
{
    public static string LayerName(Layer layer) => layer switch
    {
        LiveLayer { Source.Kind: "Farmer" } live => "Player · " + live.Part.Replace("+", " + "),
        LiveLayer live => $"{live.Source?.Npc ?? live.Source?.Asset ?? "?"} · {live.Part}" + (live.SourceFrame?.Index is { } i ? $" · frame {i}" : live.SourceFrame?.Facing is { } f ? $" · {f}" : ""),
        PaintLayer paint => Pretty(paint.Image) ?? "Paint",
        ImageLayer image => (Pretty(image.Image) ?? "Image") + $" · cell {image.Frame}",
        EffectLayer effect => "Effect · " + effect.Effect,
        _ => layer.Type
    };

    private static string? Pretty(string? path)
    {
        if (path == null) return null;
        // "body-3.png" → "Body": the frame number is in the file name only to keep frames apart.
        string name = System.Text.RegularExpressions.Regex.Replace(Path.GetFileNameWithoutExtension(path), @"[-_]\d+$", "").Replace('-', ' ').Replace('_', ' ');
        return name.Length == 0 ? null : char.ToUpperInvariant(name[0]) + name[1..];
    }

    public BitmapSource? LayerThumbnail(Layer layer)
    {
        if (Frame == null || !Frame.Layers.Contains(layer)) return null;
        var copy = PackWriter.Clone(Model);
        var clone = copy.Frames[FrameIndex].Layers[Frame.Layers.IndexOf(layer)];
        clone.Visible = true;
        var result = Context.RenderFrame(copy, FrameIndex, null, l => ReferenceEquals(l, clone));
        return StudioImages.ToBitmap(result.Image);
    }

    private void RebuildLayers()
    {
        Layers.Clear();
        var frame = Frame;
        if (frame == null) { LayerIndex = -1; return; }
        for (int i = frame.Layers.Count - 1; i >= 0; i--) Layers.Add(new LayerRowViewModel(this, i, frame.Layers[i], lockedLayers.Contains(i)));
        if (LayerIndex >= frame.Layers.Count || (LayerIndex < 0 && frame.Layers.Count > 0)) LayerIndex = frame.Layers.Count - 1;
        foreach (var row in Layers) row.IsSelected = row.Index == LayerIndex;
    }

    public void SetLayerLocked(int index, bool locked) { if (locked) lockedLayers.Add(index); else lockedLayers.Remove(index); }
    public bool IsLayerLocked(int index) => lockedLayers.Contains(index);

    /// <summary>Layers in other frames that are "the same layer" for Apply to all frames: same position and type, or same source.</summary>
    private IEnumerable<Layer> Matching(int frameIndex, int layerIndex)
    {
        var frame = Model.Frames[frameIndex];
        var layer = frame.Layers[layerIndex];
        yield return layer;
        if (!ApplyToAllFrames) yield break;
        string Key(Layer l) => l switch { LiveLayer live => $"live|{live.Source?.Kind}|{live.Source?.Npc}|{live.Source?.Asset}|{live.Part}", PaintLayer => "paint", ImageLayer image => "image|" + image.Image, EffectLayer effect => "effect|" + effect.Effect, _ => l.Type };
        foreach (var other in Model.Frames.Where(f => !ReferenceEquals(f, frame)))
        {
            var candidate = layerIndex < other.Layers.Count && other.Layers[layerIndex].Type == layer.Type ? other.Layers[layerIndex]
                : other.Layers.FirstOrDefault(l => Key(l) == Key(layer));
            if (candidate != null) yield return candidate;
        }
    }

    public void SetLayerVisible(int index, bool visible) =>
        Save(visible ? "Show layer" : "Hide layer", _ => { foreach (var layer in Matching(FrameIndex, index)) layer.Visible = visible; }, rebuildFields: false);

    // ---------------- adding layers ----------------

    [RelayCommand]
    private void AddPaintLayer()
    {
        if (Frame == null) return;
        string id = Model.Id ?? ItemId;
        int at = LayerIndex + 1;
        Save("Add paint layer", _ => { }, lib =>
        {
            string path = lib.UniquePath($"art/poses/{id}/paint-{FrameIndex}.png");
            lib.WriteImage(path, StudioImages.Blank(Width, Height));
            Model.Frames[FrameIndex].Layers.Insert(Math.Clamp(at, 0, Model.Frames[FrameIndex].Layers.Count), new PaintLayer { Image = path });
        });
        LayerIndex = at;
        Tool = StudioTool.Pencil;
    }

    [RelayCommand]
    private void AddCharacterLayer() => AddLive("Full");

    [RelayCommand]
    private void AddHeadLayer() => AddLive("Head");

    private void AddLive(string part)
    {
        if (Frame == null || PoseCharacter() is not { } who) return;
        int at = LayerIndex + 1;
        string facing = IsSheet ? SheetFacing(FrameIndex) : Model.Facing ?? "down";
        Save(part == "Head" ? "Add live head" : "Add live character", pose =>
        {
            var layer = LiveFor(who, FrameIndex, part, facing);
            if (part == "Head")
            {
                layer.AnchorTo = new LayerAnchor { Anchor = "Neck", From = "rig" };
                EnsureNeck(pose.Frames[FrameIndex], who, facing);
            }
            var layers = pose.Frames[FrameIndex].Layers;
            layers.Insert(Math.Clamp(at, 0, layers.Count), layer);
        }, lib => EnsureRig(lib, who));
        LayerIndex = at;
    }

    private void EnsureRig(PackLibrary library, CharacterChoice who)
    {
        string rig = who.IsFarmer ? RigFactory.EnsureFarmerRig(library) : RigFactory.EnsureNpcRig(library, Studio.Shell.Catalog, who.Key);
        Model.For ??= rig;
    }

    /// <summary>A live layer of the character for a frame: the whole sprite, or its head (Part "Head").</summary>
    protected virtual LiveLayer LiveFor(CharacterChoice who, int frameIndex, string part, string facing)
    {
        var sourceFrame = IsSheet ? new SourceFrame { Index = frameIndex } : new SourceFrame { Facing = facing, Stand = true };
        return who.IsFarmer
            ? new LiveLayer { Source = new LiveSource { Kind = "Farmer" }, Part = part == "Head" ? "Head+Eyes+Accessory+Hair+Hat" : "Full", SourceFrame = sourceFrame }
            : new LiveLayer { Source = new LiveSource { Kind = "Npc", Npc = who.Key }, Part = part, SourceFrame = sourceFrame };
    }

    /// <summary>Where the head hangs by default: the rig's neck point for the facing.</summary>
    protected virtual int[] RigNeck(CharacterChoice who, string facing)
    {
        if (who.IsFarmer)
        {
            int direction = FarmerExport.FacingIndex(facing);
            var neck = FarmerExport.Neck(direction switch { 0 => 12, 1 or 3 => 6, _ => 0 }, direction);
            return new[] { neck.X, neck.Y };
        }
        var rig = Library?.RigFor(who.Key);
        return rig != null && rig.NeckPoint.TryGetValue(facing, out var point) ? (int[])point.Clone() : new[] { Width / 2, Height / 2 - 1 };
    }

    /// <summary>Anything Isolate head needs first (a portrait's head mask), inside its edit.</summary>
    protected virtual void PrepareIsolate(PackLibrary library, CharacterChoice who) { }

    private void EnsureNeck(PoseFrame frame, CharacterChoice who, string facing)
    {
        if (frame.Anchors.ContainsKey("Neck")) return;
        frame.Anchors["Neck"] = RigNeck(who, facing);
    }

    [RelayCommand]
    private void AddEffectLayer()
    {
        int at = LayerIndex + 1;
        Save("Add effect layer", pose => pose.Frames[FrameIndex].Layers.Insert(Math.Clamp(at, 0, pose.Frames[FrameIndex].Layers.Count), new EffectLayer { Effect = "sweat-drop", Offset = new[] { Width / 2, 4 }, Every = 1500 }));
        LayerIndex = at;
    }

    /// <summary>Import a PNG as an Image layer (a sheet of FrameSize cells, or one frame).</summary>
    [RelayCommand]
    private void ImportImage()
    {
        string? file = Ui.OpenFile("Import an image", "PNG image (*.png)|*.png");
        if (file != null) ImportImage(file);
    }

    public void ImportImage(string file)
    {
        RgbaImage image;
        try { image = StudioImages.Decode(File.ReadAllBytes(file)); }
        catch (Exception ex) when (ex is IOException or InvalidDataException) { Ui.Toast("Can't import", ex.Message, ToastKind.Error); return; }
        if (image.Width % Width != 0 || image.Height % Height != 0)
        { Ui.Toast("Size doesn't fit", $"The image is {image.Width}×{image.Height}; frames are {Width}×{Height}. Use a sheet made of whole frames.", ToastKind.Warning); return; }
        int cells = image.Width / Width * (image.Height / Height);
        string id = Model.Id ?? ItemId;
        int at = LayerIndex + 1;
        Save("Import image", _ => { }, lib =>
        {
            string path = lib.UniquePath($"art/poses/{id}/{PackDocument.ToContentId(Path.GetFileNameWithoutExtension(file))}.png");
            lib.WriteImage(path, image);
            // A sheet with one cell per frame fills every frame; otherwise the current frame gets cell 0.
            if (cells == Model.Frames.Count && cells > 1)
                for (int i = 0; i < cells; i++) Model.Frames[i].Layers.Insert(Math.Min(at, Model.Frames[i].Layers.Count), new ImageLayer { Image = path, Frame = i, FrameSize = new[] { Width, Height } });
            else Model.Frames[FrameIndex].Layers.Insert(Math.Clamp(at, 0, Model.Frames[FrameIndex].Layers.Count), new ImageLayer { Image = path, Frame = 0, FrameSize = new[] { Width, Height } });
        });
        LayerIndex = at;
    }

    // ---------------- layer operations ----------------

    [RelayCommand]
    private void DeleteLayer()
    {
        if (SelectedLayer is not { } layer) return;
        int index = LayerIndex;
        Save("Delete layer", pose =>
        {
            foreach (var match in Matching(FrameIndex, index).ToArray())
                foreach (var frame in pose.Frames) frame.Layers.Remove(match);
        });
        LayerIndex = Math.Min(index, (Frame?.Layers.Count ?? 0) - 1);
    }

    [RelayCommand]
    private void DuplicateLayer()
    {
        if (SelectedLayer is not { } layer) return;
        int index = LayerIndex;
        Save("Duplicate layer", _ => { }, lib =>
        {
            var copy = PackWriter.Clone(layer);
            if (copy is PaintLayer paint) paint.Image = LibraryCopies.CopyImage(lib, paint.Image, $"art/poses/{Model.Id}/");
            if (copy is LiveLayer { Map: not null } live) live.Map = LibraryCopies.CopyImage(lib, live.Map, $"art/poses/{Model.Id}/");
            Model.Frames[FrameIndex].Layers.Insert(index + 1, copy);
        });
        LayerIndex = index + 1;
    }

    [RelayCommand] private void MoveLayerUp() => MoveLayer(1);
    [RelayCommand] private void MoveLayerDown() => MoveLayer(-1);

    private void MoveLayer(int delta)
    {
        int from = LayerIndex, to = from + delta;
        if (Frame == null || from < 0 || to < 0 || to >= Frame.Layers.Count) return;
        Save("Move layer", pose =>
        {
            foreach (var frame in ApplyToAllFrames ? pose.Frames : new List<PoseFrame> { pose.Frames[FrameIndex] })
                if (to < frame.Layers.Count && from < frame.Layers.Count) (frame.Layers[from], frame.Layers[to]) = (frame.Layers[to], frame.Layers[from]);
        });
        LayerIndex = to;
    }

    /// <summary>Bake a layer into an editable paint layer (doc 07 §4, doc 12 §1: baked pixels stop following sprite mods).</summary>
    [RelayCommand]
    private void ConvertToPaint()
    {
        if (SelectedLayer is not { } layer || layer is PaintLayer) return;
        if (layer is LiveLayer && !Ui.Confirm("Convert to paint?", "Baked pixels won't follow sprite mods or seasonal outfits anymore. Keep it live unless you need to repaint it.", "Convert", "Keep it live")) return;
        int index = LayerIndex;
        var copy = PackWriter.Clone(Model);
        var only = copy.Frames[FrameIndex].Layers[index];
        only.Visible = true;
        var pixels = Context.RenderFrame(copy, FrameIndex, null, l => ReferenceEquals(l, only)).Image;
        Save("Convert to paint", _ => { }, lib =>
        {
            string path = lib.UniquePath($"art/poses/{Model.Id}/baked-{FrameIndex}.png");
            lib.WriteImage(path, pixels);
            Model.Frames[FrameIndex].Layers[index] = new PaintLayer { Image = path, Visible = layer.Visible };
        });
    }

    /// <summary>
    /// "Isolate head" (doc 07 §6.4): keep the live character but hidden, float its live head on the Neck anchor, put an
    /// empty Body paint layer under it, place the neck at the rig's neck point, and turn on the ghost and onion skin.
    /// </summary>
    [RelayCommand]
    public void IsolateHead()
    {
        if (PoseCharacter() is not { } who) { Ui.Toast("Pick a character", "Isolate head needs the pose's character.", ToastKind.Info); return; }
        var frames = IsSheet || ApplyToAllFrames ? Enumerable.Range(0, Model.Frames.Count).ToArray() : new[] { FrameIndex };
        Save("Isolate head", _ => { }, lib =>
        {
            EnsureRig(lib, who);
            PrepareIsolate(lib, who);
            foreach (int index in frames)
            {
                var frame = Model.Frames[index];
                string facing = IsSheet ? SheetFacing(index) : Model.Facing ?? "down";
                var full = frame.Layers.OfType<LiveLayer>().FirstOrDefault(l => l.Part == "Full");
                if (full == null)
                {
                    full = LiveFor(who, index, "Full", facing);
                    frame.Layers.Insert(0, full);
                }
                full.Visible = false;
                EnsureNeck(frame, who, facing);
                string path = lib.UniquePath($"art/poses/{Model.Id}/body-{index}.png");
                lib.WriteImage(path, StudioImages.Blank(Width, Height));
                var body = new PaintLayer { Image = path };
                if (!who.IsFarmer && lib.RigFor(who.Key) is { Id: { } rigId } && lib.Rig(rigId)!.ColorSamples.Count > 0)
                    body.ColorSlots = lib.Rig(rigId)!.ColorSamples.Keys.Where(k => Core.Palette.ColorSlots.Names.Contains(k, StringComparer.OrdinalIgnoreCase))
                        .ToDictionary(k => k, k => $"{rigId}.{k}", StringComparer.OrdinalIgnoreCase);
                else if (who.IsFarmer)
                    body.ColorSlots = new(StringComparer.OrdinalIgnoreCase) { ["Skin"] = "Farmer.Skin", ["Shirt"] = "Farmer.Shirt", ["Pants"] = "Farmer.Pants", ["Shoes"] = "Farmer.Shoes", ["Outline"] = "#000000" };
                var head = LiveFor(who, index, "Head", facing);
                head.AnchorTo = new LayerAnchor { Anchor = "Neck", From = "rig" };
                frame.Layers.RemoveAll(l => l is LiveLayer { Part: "Head" } || l is LiveLayer live && live.Part.StartsWith("Head+", StringComparison.Ordinal));
                int fullIndex = frame.Layers.IndexOf(full);
                frame.Layers.Insert(fullIndex + 1, body);
                frame.Layers.Insert(fullIndex + 2, head);
            }
        });
        ShowGhost = true; ShowOnion = true;
        LayerIndex = Frame?.Layers.FindIndex(l => l is PaintLayer p && p.Image?.Contains("/body-", StringComparison.Ordinal) == true) ?? -1;
        Tool = StudioTool.Pencil;
        Palette.ShowResolved = false;
    }

    // ---------------- properties ----------------

    public override void BuildFields()
    {
        Fields.Clear();
        if (Frame is not { } frame) return;
        var commit = FieldCommit(Persist);
        BuildPoseFields(commit);
        BuildFrameFields(frame, commit);
        if (SelectedLayer is { } layer) BuildLayerFields(layer, commit);
        else Fields.Add(new NoteField("Select a layer to edit it. Add paint to draw, or Isolate head to draw a new body under the real head."));
    }

    protected virtual void BuildPoseFields(FieldCommit commit)
    {
        Fields.Add(new SectionField("Pose", Model.Kind switch { "sheet" => "A whole replacement walk sheet: vanilla moves keep working while it's on.", "effect" => "An effect animation (sparkles, steam…).", _ => "A stand-in animation for a character." }));
        Fields.Add(new TextField("ID", "Id", () => Model.Id, v => Rename(v), (l, a, r) => a()) { AllowNull = false, Help = "How scenes refer to this pose. Renaming updates the scenes and animations that use it." });
        var rigs = (Library?.Items(LibraryKind.Rig) ?? Enumerable.Empty<LibraryItem>()).Select(r => new ChoiceItem(r.Id, r.Id, r.Detail));
        Fields.Add(new ChoiceField("For (rig)", "For", rigs, () => Model.For, v => Model.For = v, commit) { Help = "Whose rig gives head masks, neck points and colour samples." });
        if (Model.Kind != "sheet")
        {
            Fields.Add(new ChoiceField("Drawn facing", "Facing", new[] { new ChoiceItem("down", "Down"), new ChoiceItem("right", "Right"), new ChoiceItem("up", "Up"), new ChoiceItem("left", "Left") },
                () => Model.Facing, v => Model.Facing = v, commit) { Rebuild = true });
            Fields.Add(new BoolField("Mirror for the opposite facing", "MirrorForOppositeFacing", () => Model.MirrorForOppositeFacing, v => Model.MirrorForOppositeFacing = v, commit)
            { Description = "A pose drawn facing left also works facing right, flipped." });
        }
        Fields.Add(new NumberField("Pivot X", "Pivot", () => Pivot[0], v => Model.Pivot = new[] { v ?? Width / 2, Pivot[1] }, commit) { Minimum = 0, Maximum = Width - 1, Help = "The point that sits on the actor's tile position (feet). Default: bottom centre." });
        Fields.Add(new NumberField("Pivot Y", "Pivot", () => Pivot[1], v => Model.Pivot = new[] { Pivot[0], v ?? Height - 1 }, commit) { Minimum = 0, Maximum = Height - 1 });
        Fields.Add(new NoteField($"Frames are {Width}×{Height} px." + (IsSheet ? $" Sheet {Model.SheetSize?[0]}×{Model.SheetSize?[1]}." : "")));
    }

    protected virtual void BuildFrameFields(PoseFrame frame, FieldCommit commit)
    {
        Fields.Add(new SectionField($"Frame {FrameIndex + 1}"));
        if (!IsPortrait)
        {
            Fields.Add(new NumberField("Duration", "Duration", () => frame.Duration, v => frame.Duration = Math.Max(1, v ?? 100), commit) { Minimum = 1, Maximum = 10000, Suffix = "ms", Step = 10 });
            var sounds = (Studio.Shell.Catalog?.Sounds ?? new[] { "dwop", "thudStep", "shwip" }).Select(s => new ChoiceItem(s, s));
            Fields.Add(new ChoiceField("Sound when it starts", "Sound", new[] { new ChoiceItem(null, "None") }.Concat(sounds), () => frame.Sound, v => frame.Sound = v, commit) { IsEditable = true });
            Fields.Add(new BoolField("Dust puff when it starts", "Dust", () => frame.Dust, v => frame.Dust = v, commit));
        }
        var anchors = new ListField("Anchors", "Anchors", "Add anchor", () => { AnchorName = Ui.Prompt("New anchor", "Anchor name (e.g. Hand)", "Hand") ?? AnchorName; Tool = StudioTool.Anchor; })
        { EmptyText = "No anchors. Use the anchor tool (N) to place the Neck." };
        foreach (var (name, point) in frame.Anchors.ToArray())
        {
            string key = name;
            var row = new ListRowViewModel(() => Save("Remove anchor", p => p.Frames[FrameIndex].Anchors.Remove(key))) { Caption = key };
            row.Cells.Add(new NumberField("x", "", () => point[0], v => frame.Anchors[key] = new[] { v ?? 0, frame.Anchors[key][1] }, commit));
            row.Cells.Add(new NumberField("y", "", () => point[1], v => frame.Anchors[key] = new[] { frame.Anchors[key][0], v ?? 0 }, commit));
            anchors.Rows.Add(row);
        }
        Fields.Add(anchors);
        Fields.Add(new ActionField("Copy anchors to every frame", () => Save("Copy anchors", p => { foreach (var f in p.Frames) foreach (var (n, pt) in frame.Anchors) f.Anchors[n] = (int[])pt.Clone(); })));
    }

    private void BuildLayerFields(Layer layer, FieldCommit commit)
    {
        int layerIndex = LayerIndex;
        FieldCommit layerCommit = (label, apply, rebuild) =>
        {
            // Apply to all frames: copy whatever the field changed to the matching layers.
            var before = JObject.Parse(Core.Serialization.PackJson.Serialize(layer));
            apply();
            var after = JObject.Parse(Core.Serialization.PackJson.Serialize(layer));
            commit(label, () =>
            {
                if (!ApplyToAllFrames) return;
                foreach (var other in Matching(FrameIndex, layerIndex).Skip(1).ToArray())
                    foreach (var property in after.Properties().Where(p => !JToken.DeepEquals(p.Value, before[p.Name])))
                        CopyProperty(other, property.Name, property.Value);
            }, rebuild);
        };
        Fields.Add(new SectionField("Layer · " + LayerName(layer), layer switch
        {
            LiveLayer => "Live art from the game: follows sprite mods and seasonal outfits.",
            PaintLayer => "Your pixels for this frame. Draw with the pencil; slot colours become the character's real colours.",
            ImageLayer => "A cell of your own image sheet.",
            _ => null
        }));
        Fields.Add(new BoolField("Visible", "Visible", () => layer.Visible, v => layer.Visible = v, layerCommit) { Description = layer is LiveLayer { Part: "Full" } ? "Hidden full layers stay as a drawing reference." : null });
        if (layer is LiveLayer live) BuildLiveFields(live, layerCommit);
        if (layer is ImageLayer image)
            Fields.Add(new NumberField("Cell", "Frame", () => image.Frame, v => image.Frame = Math.Max(0, v ?? 0), layerCommit) { Minimum = 0, Help = "Which FrameSize cell of the image, left to right then down." });
        if (layer is EffectLayer effect)
        {
            Fields.Add(new ChoiceField("Effect", "Effect", new[] { "dust", "sweat-drop", "steam", "sparkle", "heart-burst", "tears" }.Select(e => new ChoiceItem(e, e)), () => effect.Effect, v => effect.Effect = v, layerCommit) { IsEditable = true });
            Fields.Add(new NumberField("Every", "Every", () => effect.Every, v => effect.Every = Math.Max(0, v ?? 0), layerCommit) { Minimum = 0, Suffix = "ms" });
        }
        Fields.Add(new NumberField("Offset X", "Offset", () => layer.Offset[0], v => layer.Offset = new[] { v ?? 0, layer.Offset[1] }, layerCommit) { Help = "Moves the layer within the frame (after anchoring)." });
        Fields.Add(new NumberField("Offset Y", "Offset", () => layer.Offset[1], v => layer.Offset = new[] { layer.Offset[0], v ?? 0 }, layerCommit));
        Fields.Add(new BoolField("Flip horizontally", "Flip", () => layer.Flip, v => layer.Flip = v, layerCommit));
        Fields.Add(new NumberField("Opacity", "Opacity", () => (int)Math.Round(layer.Opacity * 100), v => layer.Opacity = Math.Clamp((v ?? 100) / 100.0, 0, 1), layerCommit) { Minimum = 0, Maximum = 100, Suffix = "%" });
        var masks = new[] { new ChoiceItem(null, layer is LiveLayer { Part: "Head" } ? "Rig head mask (default)" : "No mask") }
            .Concat((Library?.Masks().Masks ?? new()).Where(m => m?.Id != null).Select(m => new ChoiceItem(m.Id, m.DisplayName ?? m.Id!, m.Id)));
        Fields.Add(new ChoiceField("Mask", "Mask", masks, () => layer.Mask, v => layer.Mask = v, layerCommit) { Help = "Keeps (or removes) only the masked pixels of the source frame." });
        if (layer.Mask != null) Fields.Add(new ActionField("Edit mask…", () => Studio.OpenItem(LibraryKind.Mask, layer.Mask!)));
        var anchors = new[] { new ChoiceItem(null, "Not anchored") }.Concat((Frame?.Anchors.Keys ?? Enumerable.Empty<string>()).Select(a => new ChoiceItem(a, a)));
        Fields.Add(new ChoiceField("Anchor to", "AnchorTo.Anchor", anchors, () => layer.AnchorTo?.Anchor,
            v => layer.AnchorTo = v == null ? null : new LayerAnchor { Anchor = v, From = layer.AnchorTo?.From ?? (layer is LiveLayer ? "rig" : new JArray(Width / 2, Height / 2)) }, layerCommit) { Rebuild = true, Help = "Pins this layer's point onto the frame's anchor (a head's neck onto the body's neck)." });
        Fields.Add(ColorSlotList(layer, layerCommit));
        if (layer is PaintLayer or ImageLayer)
        {
            Fields.Add(new ActionField("Edit in external editor…", () => EditExternally(layer)));
            Fields.Add(new ActionField("Clear pixels", () => ClearLayer(layer)));
        }
        if (layer is not PaintLayer) Fields.Add(new ActionField("Convert to paint (bake)", ConvertToPaint));
    }

    private void BuildLiveFields(LiveLayer live, FieldCommit commit)
    {
        var kinds = new[] { ("Npc", "Character's current sprite"), ("NpcSheet", "A specific sprite sheet"), ("Portrait", "Character's current portrait"), ("PortraitSheet", "A specific portrait sheet"), ("Farmer", "The player"), ("Asset", "Any game texture (rectangle)"), (Outfits.SourceKind, "One of your outfits") };
        Fields.Add(new ChoiceField("Source", "Source.Kind", kinds.Select(k => new ChoiceItem(k.Item1, k.Item2)), () => live.Source?.Kind, v => { live.Source ??= new LiveSource(); live.Source.Kind = v; if (v == "Farmer") live.Part = "Full"; }, commit) { Rebuild = true });
        string kind = live.Source?.Kind ?? "Npc";
        if (kind is "Npc" or "Portrait")
            Fields.Add(new ChoiceField("Character", "Source.Npc", Studio.Characters.Where(c => !c.IsFarmer).Select(c => new ChoiceItem(c.Key, c.Label, c.Detail)), () => live.Source?.Npc, v => live.Source!.Npc = v, commit) { IsEditable = true });
        if (kind == Outfits.SourceKind && PoseCharacter() is { IsFarmer: false } wearer)
            Fields.Add(new ChoiceField("Outfit", "Source.Outfit", Studio.OwnOutfits(wearer.Key).Where(id => !string.Equals(id, Model.Id, StringComparison.OrdinalIgnoreCase))
                .Select(id => new ChoiceItem(id, OutfitFactory.Label(id, wearer.Key))), () => live.Source?.Outfit, v => { live.Source!.Outfit = v; live.Source.Npc ??= wearer.Key; }, commit));
        if (kind is "NpcSheet" or "PortraitSheet" or "Asset")
            Fields.Add(new ChoiceField("Texture", "Source.Asset", SheetOptions(kind), () => live.Source?.Asset, v => live.Source!.Asset = v, commit) { IsEditable = true, Help = "Picking a texture another mod adds makes that mod a dependency." });
        if (kind == "Asset")
        {
            var rect = live.Source?.Rect ?? new[] { 0, 0, 16, 16 };
            string[] labels = { "Rect X", "Rect Y", "Rect width", "Rect height" };
            for (int i = 0; i < 4; i++)
            {
                int n = i;
                Fields.Add(new NumberField(labels[i], "Source.Rect", () => (live.Source?.Rect ?? rect)[n], v => { var r = (int[])(live.Source!.Rect ?? rect).Clone(); r[n] = v ?? 0; live.Source.Rect = r; }, commit) { Minimum = 0 });
            }
        }
        if (kind == "Farmer") BuildFarmerParts(live, commit);
        else if (kind != "Asset")
            Fields.Add(new ChoiceField("Part", "Part", new[] { new ChoiceItem("Full", "Whole sprite"), new ChoiceItem("Head", "Head (rig head mask)"), new ChoiceItem("Mask", "Masked part (choose a mask)") },
                () => live.Part, v => live.Part = v ?? "Full", commit) { Rebuild = true });
        if (kind != "Asset")
        {
            Fields.Add(new BoolField("Standing frame for a facing", "SourceFrame", () => live.SourceFrame?.Index == null, v => live.SourceFrame = v ? new SourceFrame { Facing = Model.Facing ?? "down", Stand = true } : new SourceFrame { Index = 0 }, commit) { Rebuild = true });
            if (live.SourceFrame?.Index is not null)
                Fields.Add(new NumberField("Source frame", "SourceFrame.Index", () => live.SourceFrame?.Index ?? 0, v => live.SourceFrame = new SourceFrame { Index = Math.Max(0, v ?? 0) }, commit)
                { Minimum = 0, Help = kind == "Farmer" ? "The player's base frame (0–125). Exported frames: " + string.Join(", ", Context.Farmer?.FramesFor("down") ?? Enumerable.Empty<int>()) : "Frame index in the sheet (4 per row: down 0–3, right 4–7, up 8–11, left 12–15)." });
            else
                Fields.Add(new ChoiceField("Facing", "SourceFrame.Facing", new[] { "down", "right", "up", "left" }.Select(f => new ChoiceItem(f, f)), () => live.SourceFrame?.Facing ?? Model.Facing,
                    v => live.SourceFrame = new SourceFrame { Facing = v, Stand = true }, commit));
        }
    }

    /// <summary>Player parts helper (doc 07 §6.5): tick the parts this layer shows.</summary>
    private void BuildFarmerParts(LiveLayer live, FieldCommit commit)
    {
        Fields.Add(new SectionField("Player parts", "Untick a part to hide it in this layer. The preview uses your current look from the game export."));
        var parts = SafeParts(live.Part);
        foreach (var part in new[] { FarmerPart.Head, FarmerPart.Body, FarmerPart.Eyes, FarmerPart.Hair, FarmerPart.Hat, FarmerPart.Accessory, FarmerPart.Shirt, FarmerPart.Pants, FarmerPart.Shoes, FarmerPart.Arms })
        {
            var flag = part;
            Fields.Add(new BoolField(flag.ToString(), "Part", () => SafeParts(live.Part).HasFlag(flag), v =>
            {
                var current = SafeParts(live.Part);
                current = v ? current | flag : current & ~flag;
                live.Part = PartString(current);
            }, commit) { Rebuild = true });
        }
        Fields.Add(new ActionField("Whole player", () => commit("Whole player", () => live.Part = "Full", true)));
        Fields.Add(new ActionField("Player head only", () => commit("Player head only", () => live.Part = "Head+Eyes+Accessory+Hair+Hat", true)));
        _ = parts;
    }

    private static FarmerPart SafeParts(string parts) { try { return FarmerParts.Parse(parts); } catch (ArgumentException) { return FarmerPart.Full; } }

    public static string PartString(FarmerPart parts)
    {
        if (parts == FarmerPart.Full) return "Full";
        if (parts == FarmerPart.None) return "Head";
        var names = new List<string>();
        if ((parts & FarmerPart.Base) == FarmerPart.Base) { names.Add("Base"); parts &= ~FarmerPart.Base; }
        foreach (FarmerPart flag in Enum.GetValues<FarmerPart>())
            if (flag is not (FarmerPart.None or FarmerPart.Base or FarmerPart.Full) && parts.HasFlag(flag)) names.Add(flag.ToString());
        return string.Join("+", names);
    }

    private IEnumerable<ChoiceItem> SheetOptions(string kind)
    {
        var catalog = Studio.Shell.Catalog;
        if (catalog == null) return Enumerable.Empty<ChoiceItem>();
        bool portraits = kind == "PortraitSheet";
        var assets = catalog.Characters.SelectMany(c => portraits ? new[] { c.Portrait }.Concat(c.Appearances.Select(a => a.Portrait)) : new[] { c.Sprite }.Concat(c.Appearances.Select(a => a.Sprite)))
            .Where(a => a != null).Distinct(StringComparer.OrdinalIgnoreCase);
        return assets.Select(a => new ChoiceItem(a, a!, catalog.AssetOrigin(a) is { IsModProvided: true } o ? "from " + catalog.ModLabel(o.ProvidedBy[0]) : null));
    }

    private ListField ColorSlotList(Layer layer, FieldCommit commit)
    {
        var field = new ListField("Colour slots", "ColorSlots", "Add slot", () => commit("Add colour slot", () =>
        {
            layer.ColorSlots ??= new(StringComparer.OrdinalIgnoreCase);
            string slot = Core.Palette.ColorSlots.Names.FirstOrDefault(n => n != "Erase" && !layer.ColorSlots.ContainsKey(n)) ?? "Custom1";
            layer.ColorSlots[slot] = DefaultSource(slot);
        }, true)) { EmptyText = "Reserved colours in this layer stay as painted. Add a slot to swap them for the character's real colours." };
        foreach (var (slot, source) in (layer.ColorSlots ?? new()).ToArray())
        {
            string key = slot;
            var row = new ListRowViewModel(() => commit("Remove colour slot", () => layer.ColorSlots!.Remove(key), true));
            row.Cells.Add(new ChoiceField("", "", Core.Palette.ColorSlots.Names.Where(n => n != "Erase").Select(n => new ChoiceItem(n, n)), () => key,
                v => { if (v == null || v == key) return; var value = layer.ColorSlots![key]; layer.ColorSlots.Remove(key); layer.ColorSlots[v] = value; }, commit) { Rebuild = true });
            row.Cells.Add(new ChoiceField("from", $"ColorSlots.{key}", SourceOptions(), () => layer.ColorSlots![key], v => layer.ColorSlots![key] = v ?? "#000000", commit) { IsEditable = true });
            field.Rows.Add(row);
        }
        return field;
    }

    private string DefaultSource(string slot)
    {
        var rig = Library?.Rig(Model.For);
        if (rig?.Kind == "Farmer") return slot is "Outline" ? "#000000" : $"Farmer.{slot}";
        if (rig != null && rig.ColorSamples.ContainsKey(slot)) return $"{rig.Id}.{slot}";
        return "#FF0000";
    }

    private IEnumerable<ChoiceItem> SourceOptions()
    {
        foreach (var rig in Library?.Pack.Characters.Select(r => r.Value) ?? Enumerable.Empty<CharacterRig>())
            foreach (var sample in rig.ColorSamples.Keys) yield return new ChoiceItem($"{rig.Id}.{sample}", $"{rig.Id} · {sample}", "sampled live");
        foreach (var sample in new[] { "Skin", "Hair", "Eyes", "Shirt", "Pants", "Shoes" }) yield return new ChoiceItem($"Farmer.{sample}", $"Player · {sample}", "the player's colour");
    }

    private static void CopyProperty(Layer target, string name, JToken value)
    {
        var json = JObject.Parse(Core.Serialization.PackJson.Serialize(target));
        json[name] = value.DeepClone();
        var updated = Core.Serialization.PackJson.Deserialize<Layer>(json.ToString())!;
        foreach (var property in target.GetType().GetProperties().Where(p => p.CanWrite && p.Name == name)) property.SetValue(target, property.GetValue(updated));
    }

    private void Rename(string? value)
    {
        string? next = value == null ? null : PackDocument.ToContentId(value);
        if (next == null || next == Model.Id || Library == null) return;
        if (Library.Exists(LibraryKind.Pose, next)) { Ui.Toast("Name taken", $"There's already a pose called '{next}'.", ToastKind.Warning); return; }
        string old = Model.Id!;
        Commit($"Rename pose {old}", lib =>
        {
            lib.Delete(LibraryKind.Pose, old);
            Model.Id = next;
            lib.SavePose(Model);
            References.RenamePose(lib, Studio.Shell.Document!, old, next);
        });
        ItemId = next;
        Title = next;
        Reload();
    }

    private void ClearLayer(Layer layer)
    {
        string? path = (layer as PaintLayer)?.Image ?? (layer as ImageLayer)?.Image;
        if (path == null || TargetFor(LayerIndex) is not { } target) return;
        var image = StudioImages.Clone(target.Image);
        PixelTools.Clear(image, new PixelRect(target.CellX, target.CellY, target.CellWidth, target.CellHeight));
        Commit("Clear layer", lib => lib.WriteImage(path, image));
    }
}
