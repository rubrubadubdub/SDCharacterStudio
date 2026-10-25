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
        .Where(l => l.Source?.Kind is { } kind && (kind.Equals("Npc", StringComparison.OrdinalIgnoreCase) || kind.Equals("NpcSheet", StringComparison.OrdinalIgnoreCase)));

    /// <summary>The sheet this pose always wears, or null when it wears whatever the game gives her.</summary>
    public string? PinnedOutfit => CharacterLayers(Model).FirstOrDefault(l => string.Equals(l.Source!.Kind, "NpcSheet", StringComparison.OrdinalIgnoreCase))?.Source!.Asset;
    public bool CanPinOutfit => PoseCharacter() is { IsFarmer: false } && (Studio.Outfit?.Asset != null || PinnedOutfit != null);
    public string OutfitText => PinnedOutfit is { } pinned
        ? $"This pose always wears: {Studio.OutfitName(pinned)}."
        : "In the game she wears whatever the season or festival gives her. Pick an outfit above to see her in it.";

    /// <summary>Always wear the outfit the studio is showing (or, with null, whatever the game gives her). One undo step.</summary>
    [RelayCommand]
    public void WearOutfit(string? asset)
    {
        if (PoseCharacter() is not { IsFarmer: false } who) return;
        Save(asset == null ? "Wear what the game gives" : "Wear " + Studio.OutfitName(asset), pose =>
        {
            foreach (var live in CharacterLayers(pose))
                live.Source = asset == null ? new LiveSource { Kind = "Npc", Npc = live.Source?.Npc ?? who.Key } : new LiveSource { Kind = "NpcSheet", Npc = live.Source?.Npc ?? who.Key, Asset = asset };
        });
        OutfitChanged();
    }

    [RelayCommand] private void PinShownOutfit() { if (Studio.Outfit?.Asset is { } asset) WearOutfit(asset); }
    [RelayCommand] private void UnpinOutfit() => WearOutfit(null);

    /// <summary>The sheet the frames come from: the pinned outfit, else what the studio is showing.</summary>
    private RgbaImage? CharacterSheet(out int width, out int height)
    {
        width = Width; height = Height;
        if (PoseCharacter() is not { IsFarmer: false } who) return null;
        var rig = Library?.RigFor(who.Key);
        if (rig != null) { width = rig.FrameSize[0]; height = rig.FrameSize[1]; }
        return PinnedOutfit is { } pinned ? Context.AssetTexture(pinned) : Context.NpcTexture(who.Key, false);
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
        Save($"Start from her frame {index}", pose =>
        {
            var live = (LiveLayer)pose.Frames[FrameIndex].Layers[0];
            live.SourceFrame = new SourceFrame { Index = index };
        }, lib => { lib.WriteImage(flat.Live.Map!, map); lib.WriteImage(flat.Paint.Image!, paint); });
        Selection = null;
    }

    /// <summary>Add one of her real frames as a new frame after this one (it joins the play order). One undo step.</summary>
    [RelayCommand]
    public void AddSheetFrame(int index)
    {
        if (Frame is null || Flat is null) { Ui.Toast("One picture only", "Adding her frames works on a pose that is one picture.", ToastKind.Info); return; }
        int at = FrameIndex + 1;
        var (map, paint) = FreshPicture();
        Save($"Add her frame {index}", pose => KeepingOrder(pose, p =>
        {
            var copy = CopyFrame(p.Frames[FrameIndex], at);
            ((LiveLayer)copy.Layers[0]).SourceFrame = new SourceFrame { Index = index };
            p.Frames.Insert(at, copy);
        }, k => k > 0 && at <= k ? k + 1 : k), lib =>
        {
            CopyFrameImages(lib, Model, at);
            var copy = FlatOf(Model.Frames[at])!;
            lib.WriteImage(copy.Live.Map!, map); lib.WriteImage(copy.Paint.Image!, paint);
        });
        FrameIndex = at;
    }
}
