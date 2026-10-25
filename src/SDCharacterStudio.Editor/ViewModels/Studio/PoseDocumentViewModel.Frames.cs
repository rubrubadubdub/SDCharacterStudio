using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Studio;

namespace SDCharacterStudio.Editor.ViewModels;

/// <summary>One frame of the character's sprite sheet, to start from.</summary>
public sealed record SheetFrameItem(int Index, BitmapSource Thumbnail, string Label);

/// <summary>
/// Her real frames and outfits (D-49 phase 4): every frame of the outfit's sheet (walking, special frames, a festival
/// dress on the main sheet) to start a frame from or add as a new one, still live; and pinning an outfit to a pose so
/// it always wears it (otherwise she wears whatever the game gives her that day).
/// </summary>
public partial class PoseDocumentViewModel
{
    public ObservableCollection<SheetFrameItem> SheetFrames { get; } = new();
    [ObservableProperty] private bool showSheetFrames;
    private string? sheetFramesFor;

    partial void OnShowSheetFramesChanged(bool value) { if (value) RefreshSheetFrames(); }

    /// <summary>The studio's outfit changed: draw everything again in it.</summary>
    public void OutfitChanged()
    {
        Context.InvalidatePack();
        sheetFramesFor = null;
        if (ShowSheetFrames) RefreshSheetFrames();
        Render(); RenderThumbnails(); RefreshBackdrop(); RefreshPresets();
        OnPropertyChanged(nameof(PinnedOutfit)); OnPropertyChanged(nameof(OutfitText)); OnPropertyChanged(nameof(CanPinOutfit));
    }

    /// <summary>The live character layers of this pose (the ones an outfit applies to).</summary>
    private IEnumerable<LiveLayer> CharacterLayers(Pose pose) => pose.Frames.SelectMany(f => f.Layers).OfType<LiveLayer>()
        .Where(l => l.Source?.Kind is { } kind && (kind.Equals("Npc", StringComparison.OrdinalIgnoreCase) || kind.Equals("NpcSheet", StringComparison.OrdinalIgnoreCase) || Outfits.IsOutfitSource(l.Source)));

    /// <summary>
    /// The sheet this pose always wears (a game sheet, or one of your outfits as an <see cref="StudioContext.OutfitKey"/>),
    /// or null when it wears whatever the game gives her.
    /// </summary>
    public string? PinnedOutfit => CharacterLayers(Model).Select(l => l.Source!).FirstOrDefault(s => !string.Equals(s.Kind, "Npc", StringComparison.OrdinalIgnoreCase)) is { } source
        ? Outfits.IsOutfitSource(source) ? StudioContext.OutfitKey(source.Outfit ?? "") : source.Asset : null;
    /// <summary>The outfit the studio shows, when it's one this pose may wear (an outfit can't wear itself).</summary>
    private string? WearableShownOutfit => Studio.Outfit?.Asset is { } shown && !string.Equals(StudioContext.OutfitId(shown), Model.Id, StringComparison.OrdinalIgnoreCase) ? shown : null;
    public bool CanPinOutfit => PoseCharacter() is { IsFarmer: false } && (WearableShownOutfit != null || PinnedOutfit != null);
    public string OutfitText => PinnedOutfit is { } pinned
        ? IsSheet ? $"Made from: {Studio.OutfitName(pinned)}." : $"This pose always wears: {Studio.OutfitName(pinned)}."
        : IsSheet ? "Made from her own look: it follows seasons and sprite mods under your paint."
        : "In the game she wears whatever the season or festival gives her. Pick an outfit above to see her in it.";

    /// <summary>Always wear the outfit the studio is showing (or, with null, whatever the game gives her). One undo step.</summary>
    [RelayCommand]
    public void WearOutfit(string? asset)
    {
        if (PoseCharacter() is not { IsFarmer: false } who) return;
        if (string.Equals(StudioContext.OutfitId(asset), Model.Id, StringComparison.OrdinalIgnoreCase))
        { Ui.Toast("Not itself", "An outfit can't be made from itself. Pick her own look or another outfit.", ToastKind.Info); return; }
        Save(asset == null ? "Wear what the game gives" : "Wear " + Studio.OutfitName(asset), pose => Wear(pose, who.Key, asset));
        OutfitChanged();
    }

    /// <summary>Point every character layer at <paramref name="asset"/> (null: her own look; or a game sheet, or one of your outfits).</summary>
    private void Wear(Pose pose, string npc, string? asset)
    {
        foreach (var live in CharacterLayers(pose))
        {
            string who = live.Source?.Npc ?? npc;
            live.Source = asset == null ? new LiveSource { Kind = "Npc", Npc = who }
                : StudioContext.OutfitId(asset) is { } outfit ? new LiveSource { Kind = Outfits.SourceKind, Npc = who, Outfit = outfit }
                : new LiveSource { Kind = "NpcSheet", Npc = who, Asset = asset };
        }
    }

    /// <summary>
    /// Built from the frames of one of your outfits the studio shows: the pose wears that outfit (the game never puts
    /// it on her by itself). Call inside the save; true when it now wears it.
    /// </summary>
    private bool WearShownOwnOutfit(Pose pose)
    {
        if (IsSheet || PinnedOutfit != null || WearableShownOutfit is not { } shown || StudioContext.OutfitId(shown) == null || PoseCharacter() is not { IsFarmer: false } who) return false;
        Wear(pose, who.Key, shown);
        return true;
    }

    private void SaidWearsOutfit() =>
        Ui.Toast("Wears your outfit", $"This pose now always wears {Studio.OutfitName(PinnedOutfit)}. Change it under Outfit, on the right.", ToastKind.Info);

    [RelayCommand] private void PinShownOutfit() { if (WearableShownOutfit is { } asset) WearOutfit(asset); }
    [RelayCommand] private void UnpinOutfit() => WearOutfit(null);

    /// <summary>The sheet the frames come from: the pinned outfit, else what the studio is showing.</summary>
    private RgbaImage? CharacterSheet(out int width, out int height)
    {
        width = Width; height = Height;
        if (PoseCharacter() is not { IsFarmer: false } who) return null;
        var rig = Library?.RigFor(who.Key);
        if (rig != null) { width = rig.FrameSize[0]; height = rig.FrameSize[1]; }
        // An outfit's frames are her own (or what it's made from), never the outfit the studio previews.
        return PinnedOutfit is { } pinned ? Context.AssetTexture(pinned) : Context.NpcTexture(who.Key, false, ownOutfits: !IsSheet);
    }

    private void RefreshSheetFrames()
    {
        string key = $"{PoseCharacter()?.Key}|{PinnedOutfit}|{Studio.Outfit?.Asset}";
        if (key == sheetFramesFor) return;
        sheetFramesFor = key;
        SheetFrames.Clear();
        if (CharacterSheet(out int width, out int height) is not { } sheet || width <= 0 || height <= 0) return;
        int columns = sheet.Width / width, count = columns * (sheet.Height / height);
        for (int index = 0; index < count; index++)
        {
            if (StudioImages.Frame(sheet, width, height, index) is not { } frame || StudioImages.IsEmpty(frame)) continue;
            string label = index < 16 ? $"{index}: walking {SheetFacing(index)}, step {index % 4 + 1}" : $"{index}";
            SheetFrames.Add(new SheetFrameItem(index, StudioImages.ToBitmap(frame), label));
        }
    }

    /// <summary>A fresh picture of a sheet frame: an identity map (every pixel her own) and empty paint.</summary>
    private (RgbaImage Map, RgbaImage Paint) FreshPicture()
    {
        var map = new RgbaImage(Width, Height, new uint[Width * Height]);
        for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++) map.Pixels[y * Width + x] = Compositor.MapCell(x, y);
        return (map, StudioImages.Blank(Width, Height));
    }

    /// <summary>Start this frame again from one of her real frames (still live: it follows sprite mods and outfits). One undo step.</summary>
    [RelayCommand]
    public void StartFromSheetFrame(int index)
    {
        if (Flat is not { } flat) { Ui.Toast("One picture only", "Starting from her frames works on a pose that is one picture.", ToastKind.Info); return; }
        var (map, paint) = FreshPicture();
        bool wears = false;
        Save($"Start from her frame {index}", pose =>
        {
            var live = (LiveLayer)pose.Frames[FrameIndex].Layers[0];
            live.SourceFrame = new SourceFrame { Index = index };
            wears = WearShownOwnOutfit(pose);
        }, lib => { lib.WriteImage(flat.Live.Map!, map); lib.WriteImage(flat.Paint.Image!, paint); });
        Selection = null;
        if (wears) { OutfitChanged(); SaidWearsOutfit(); }
    }

    /// <summary>Add one of her real frames as a new frame after this one (it joins the play order). One undo step.</summary>
    [RelayCommand]
    public void AddSheetFrame(int index)
    {
        if (Frame is null || Flat is null) { Ui.Toast("One picture only", "Adding her frames works on a pose that is one picture.", ToastKind.Info); return; }
        if (IsSheet) { StartFromSheetFrame(index); return; }   // an outfit's frames stay in their places
        int at = FrameIndex + 1;
        var (map, paint) = FreshPicture();
        bool wears = false;
        Save($"Add her frame {index}", pose => KeepingOrder(pose, p =>
        {
            var copy = CopyFrame(p.Frames[FrameIndex], at);
            ((LiveLayer)copy.Layers[0]).SourceFrame = new SourceFrame { Index = index };
            p.Frames.Insert(at, copy);
            wears = WearShownOwnOutfit(p);
        }, k => k > 0 && at <= k ? k + 1 : k), lib =>
        {
            CopyFrameImages(lib, Model, at);
            var copy = FlatOf(Model.Frames[at])!;
            lib.WriteImage(copy.Live.Map!, map); lib.WriteImage(copy.Paint.Image!, paint);
        });
        FrameIndex = at;
        if (wears) { OutfitChanged(); SaidWearsOutfit(); }
    }
}
