using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Editor.Studio;

namespace SDCharacterStudio.Editor.ViewModels;

/// <summary>
/// Polish (D-49 phase 5): what's behind the frame (checks, dark, light, or the floor where the scene plays), panel
/// sections that fold and stay how you left them, an empty frame that says what to do, and the selection's size in the
/// status bar.
/// </summary>
public partial class PoseDocumentViewModel
{
    public static IReadOnlyList<ChoiceItem> Backdrops { get; } = new[]
    {
        new ChoiceItem("checker", "Checks"), new ChoiceItem("dark", "Dark"), new ChoiceItem("light", "Light"), new ChoiceItem("scene", "Where the scene plays"),
    };

    public IReadOnlyList<ChoiceItem> BackdropChoices => Backdrops;

    /// <summary>checker, dark, light or scene (remembered).</summary>
    public string Backdrop
    {
        get => Studio.Settings.Backdrop;
        set
        {
            if (value == Studio.Settings.Backdrop) return;
            Studio.Settings.Backdrop = value; Studio.Settings.Save();
            OnPropertyChanged(); RefreshBackdrop();
        }
    }
    [ObservableProperty] private BitmapSource? backdropImage;
    [ObservableProperty] private Brush? backdropBrush;
    [ObservableProperty] private string? backdropNote;

    /// <summary>
    /// The floor where the scene plays: the current scene's map around the actor this pose is for (or the player, or the
    /// camera), with the pose's feet on that actor's tile.
    /// </summary>
    private void RefreshBackdrop()
    {
        BackdropImage = null; BackdropBrush = null; BackdropNote = null;
        switch (Backdrop)
        {
            case "dark": BackdropBrush = Frozen(Color.FromRgb(0x14, 0x15, 0x18)); return;
            case "light": BackdropBrush = Frozen(Color.FromRgb(0xE8, 0xE6, 0xDF)); return;
            case "scene": break;
            default: return;
        }
        var shell = Studio.Shell;
        if (shell.Scene is not { } scene || shell.Catalog?.Location(scene.Location) is not { Back: { } back }) { BackdropNote = "Open a scene whose location is in the game export to see its floor here."; return; }
        if (StudioImages.LoadFile(shell.Catalog.FullPath(back)) is not { } map) { BackdropNote = "The game export has no picture of " + scene.Location + "."; return; }
        string? who = PoseCharacter() is { IsFarmer: false } character ? character.Key : null;
        var actor = scene.Actors.FirstOrDefault(a => who != null && string.Equals(a.Npc ?? a.Id, who, StringComparison.OrdinalIgnoreCase))
            ?? scene.Actors.FirstOrDefault(a => a.Kind == "Farmer") ?? scene.Actors.FirstOrDefault();
        int[] tile = actor?.Start?.Tile ?? new[] { 10, 10 };
        var pivot = Pivot;
        // NPC.draw: the frame's feet (the pose's pivot) on the bottom middle of the actor's tile.
        int left = tile[0] * 16 + 8 - pivot[0], top = tile[1] * 16 + 15 - pivot[1];
        var floor = StudioImages.Blank(Width, Height);
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                floor.Pixels[y * Width + x] = PixelTools.Get(map, left + x, top + y);
        BackdropImage = StudioImages.ToBitmap(floor);
        BackdropNote = $"{scene.Location}, where {actor?.Id ?? "the scene"} stands";
    }

    private static SolidColorBrush Frozen(Color color) { var brush = new SolidColorBrush(color); brush.Freeze(); return brush; }

    // ---------------- folding sections ----------------

    private bool Section(string name, bool fallback) => Studio.Settings.OpenSections.TryGetValue(name, out var open) ? open : fallback;
    private void Section(string name, bool open, string property)
    {
        Studio.Settings.OpenSections[name] = open; Studio.Settings.Save();
        OnPropertyChanged(property);
    }
    public bool PresetsOpen { get => Section("presets", true); set => Section("presets", value, nameof(PresetsOpen)); }
    public bool BodyOpen { get => Section("body", true); set => Section("body", value, nameof(BodyOpen)); }
    public bool OutfitOpen { get => Section("outfit", true); set => Section("outfit", value, nameof(OutfitOpen)); }

    /// <summary>Ready-made poses need her standing sprite (the gallery is drawn on her).</summary>
    public bool HasPresetPictures => Presets.Any(p => p.Thumbnail != null);

    // ---------------- an empty frame, and the selection's size ----------------

    [ObservableProperty] private bool isFrameEmpty;

    public string? SelectionText => SelectionShape is { } shape
        ? shape.IsRectangle ? $"{shape.Bounds.Width}×{shape.Bounds.Height} selected" : $"{shape.Count} pixels selected"
        : null;
    partial void OnSelectionShapeChanged(SelectionMask? value) => OnPropertyChanged(nameof(SelectionText));
}
