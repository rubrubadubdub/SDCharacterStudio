using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDCharacterStudio.Core.Palette;

namespace SDCharacterStudio.Editor.ViewModels;

public sealed partial class SwatchViewModel : ObservableObject
{
    public SwatchViewModel(string name, uint color, string? group = null, bool isSlot = false) { Name = name; Color = color; Group = group; IsSlot = isSlot; display = Brush(color); }
    public string Name { get; }
    public uint Color { get; }
    public string? Group { get; }
    public bool IsSlot { get; }
    /// <summary>What the swatch shows: the raw colour, or the slot resolved for the current character.</summary>
    [ObservableProperty] private Brush display;
    /// <summary>A tooltip of its own (swatches that show one colour and paint a slot).</summary>
    public string? Hint { get; init; }
    public string Tooltip => Hint ?? (IsSlot ? $"{Name} (reserved slot colour {StudioImages.Hex(Color)}): becomes the character's real colour in game" : $"{Name} {StudioImages.Hex(Color)}");
    public static Brush Brush(uint color)
    {
        var brush = new SolidColorBrush(StudioImages.ToColor(color == 0 ? 0x00000000 : color | (color >> 24 == 0 ? 0xFF000000 : 0)));
        brush.Freeze();
        return brush;
    }
}

/// <summary>A named colour in the palette: its name and its three shades to paint with.</summary>
public sealed class NamedColorRowViewModel
{
    public NamedColorRowViewModel(NamedRamp ramp)
    {
        Name = ramp.Name;
        Shades = new[] { ("dark", ramp.Dark), ("", ramp.Mid), ("light", ramp.Light) }
            .Select(s => new SwatchViewModel($"{ramp.Name}{(s.Item1.Length > 0 ? " " + s.Item1 : "")}", s.Item2) { Hint = $"{ramp.Name}{(s.Item1.Length > 0 ? " " + s.Item1 : "")} {StudioImages.Hex(s.Item2)}. Left click: left button, right click: right button. Shift+click: change just this shade." }).ToList();
    }
    public string Name { get; }
    public IReadOnlyList<SwatchViewModel> Shades { get; }
}

/// <summary>
/// The palette (doc 07 §6.1): reserved slot swatches first, by name; then the character's sampled colours (read-only);
/// then the designer's own colours. Left click sets the primary colour, right click the secondary.
/// </summary>
public sealed partial class PaletteViewModel : ObservableObject
{
    private readonly CharacterStudioViewModel studio;
    public PaletteViewModel(CharacterStudioViewModel studio)
    {
        this.studio = studio;
        foreach (var (name, color) in SlotColors) Slots.Add(new SwatchViewModel(name, color, name.Split(' ')[0], true));
        simple = !studio.Settings.AdvancedPoseTools;
        foreach (var slot in Slots) VisibleSlots.Add(slot);   // until a character is chosen
        RefreshCustom();
    }

    /// <summary>
    /// D-48: the slot swatches show the character's real colours, only the ones it has, named plainly ("Robin skin").
    /// You still paint the slot colour, so new pixels follow sprite mods and outfits; the fixed samples and Erase hide.
    /// </summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SlotsTitle), nameof(PrimaryBrush), nameof(SecondaryBrush), nameof(PrimaryName), nameof(SecondaryName))] private bool simple;
    partial void OnSimpleChanged(bool value) => Refresh();
    /// <summary>The slot swatches on show (all of them, or in simple mode the character's own).</summary>
    public ObservableCollection<SwatchViewModel> VisibleSlots { get; } = new();
    public string SlotsTitle => Simple ? $"{studio.Character?.Label ?? "The character"}'s colours (they follow outfits)" : "Reserved slots";
    private Dictionary<string, ColorRamp> ramps = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A slot colour as the character's real colour (when shown resolved, or <paramref name="always"/>), else the colour itself.</summary>
    private uint Shown(uint color, bool always = false)
    {
        if (!(ShowResolved || Simple || always)) return color;
        var slot = SlotColors.FirstOrDefault(s => s.Color == color && color >> 24 != 0);
        if (slot.Name == null) return color;
        var name = slot.Name.Split(' ');
        return ramps.TryGetValue(name[0], out var ramp) ? name.Length == 1 ? ramp.Mid : name[1] switch { "dark" => ramp.Dark, "light" => ramp.Light, _ => ramp.Mid } : color;
    }

    public static readonly (string Name, uint Color)[] SlotColors =
    {
        ("Skin dark", ColorSlots.SkinDark), ("Skin mid", ColorSlots.SkinMid), ("Skin light", ColorSlots.SkinLight),
        ("Outline", ColorSlots.Outline),
        ("Hair dark", ColorSlots.HairDark), ("Hair mid", ColorSlots.HairMid), ("Hair light", ColorSlots.HairLight),
        ("Eyes dark", ColorSlots.EyesDark), ("Eyes light", ColorSlots.EyesLight),
        ("Shirt dark", ColorSlots.ShirtDark), ("Shirt mid", ColorSlots.ShirtMid), ("Shirt light", ColorSlots.ShirtLight),
        ("Pants dark", ColorSlots.PantsDark), ("Pants mid", ColorSlots.PantsMid), ("Pants light", ColorSlots.PantsLight),
        ("Shoes dark", ColorSlots.ShoesDark), ("Shoes mid", ColorSlots.ShoesMid), ("Shoes light", ColorSlots.ShoesLight),
        ("Custom1 dark", ColorSlots.Custom1Dark), ("Custom1 mid", ColorSlots.Custom1Mid), ("Custom1 light", ColorSlots.Custom1Light),
        ("Custom2 dark", ColorSlots.Custom2Dark), ("Custom2 mid", ColorSlots.Custom2Mid), ("Custom2 light", ColorSlots.Custom2Light),
        ("Erase", ColorSlots.Erase)
    };

    public ObservableCollection<SwatchViewModel> Slots { get; } = new();
    public ObservableCollection<SwatchViewModel> Sampled { get; } = new();
    public ObservableCollection<SwatchViewModel> Custom { get; } = new();
    /// <summary>Colours painted or filled with lately, newest first (D-49).</summary>
    public ObservableCollection<SwatchViewModel> Recent { get; } = new();
    /// <summary>Every colour in the frame being edited, darkest first within each hue (D-49).</summary>
    public ObservableCollection<SwatchViewModel> InPicture { get; } = new();
    public const int RecentCount = 12, PictureCount = 48;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(PrimaryBrush), nameof(PrimaryName))] private uint primary = ColorSlots.SkinMid;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SecondaryBrush), nameof(SecondaryName))] private uint secondary;
    [ObservableProperty] private bool showResolved;
    public Brush PrimaryBrush => SwatchViewModel.Brush(Shown(Primary));
    public Brush SecondaryBrush => SwatchViewModel.Brush(Shown(Secondary));
    public string PrimaryName => Simple ? FriendlyName(Primary) : NameOf(Primary);
    public string SecondaryName => Simple ? FriendlyName(Secondary) : NameOf(Secondary);
    private string FriendlyName(uint color)
    {
        string name = NameOf(color);
        if (name == "Erase") return "Erase (removes their own pixels)";
        return SlotColors.Any(s => s.Name == name) && name != "Outline" ? $"{studio.Character?.Label ?? "Character"} {name.Replace(" mid", "").ToLowerInvariant()}" : name;
    }
    public string? SampledFrom { get; private set; }

    partial void OnShowResolvedChanged(bool value) => Refresh();

    /// <summary>A colour as it looks for the character (a slot colour becomes her real colour).</summary>
    public uint Resolved(uint color) => Shown(color, always: true);

    /// <summary>The slot a slot colour paints ("Skin" for Skin dark), or null for plain colours and Erase.</summary>
    public static string? SlotGroup(uint color) => color >> 24 == 0 || color == ColorSlots.Erase ? null
        : SlotColors.FirstOrDefault(s => s.Color == color).Name?.Split(' ')[0];

    public string NameOf(uint color) => color >> 24 == 0 ? "Transparent" : SlotColors.FirstOrDefault(s => s.Color == color).Name ?? StudioImages.Hex(color);

    [RelayCommand] private void PickPrimary(SwatchViewModel? swatch) { if (swatch != null) Primary = swatch.Color; }
    [RelayCommand] private void PickSecondary(SwatchViewModel? swatch) { if (swatch != null) Secondary = swatch.Color; }
    [RelayCommand] private void Swap() => (Primary, Secondary) = (Secondary, Primary);
    [RelayCommand] private void PickTransparent() => Secondary = 0;

    [RelayCommand]
    private void AddCustom()
    {
        string? text = studio.Shell.Ui.Prompt("Add a colour", "Colour as #RRGGBB (or #AARRGGBB)", StudioImages.Hex(Primary >> 24 == 0 ? 0xFFFFFFFF : Primary),
            v => Parse(v) == null ? "Use #RRGGBB, for example #A05A2C." : null);
        if (Parse(text) is not { } color) return;
        studio.Settings.CustomColors.Add($"#{color:X8}");
        studio.Settings.Save();
        RefreshCustom();
        Primary = color;
    }

    /// <summary>
    /// The colour picker's colour (D-49). It starts from the colour it changes (the left button's, or the right's) as you
    /// see it, and you paint with what you mix as you mix it, like any paint program; closing it remembers the colour.
    /// </summary>
    [ObservableProperty] private uint pickerColor = 0xFFFFFFFF;
    [ObservableProperty] private bool isPickerOpen;
    /// <summary>The picker changes the right button's colour (clicked the right square) instead of the left's.</summary>
    [ObservableProperty] private bool pickingSecondary;
    private bool seedingPicker, mixed;
    partial void OnIsPickerOpenChanged(bool value)
    {
        if (value)
        {
            seedingPicker = true;
            try { PickerColor = Shown(PickingSecondary ? Secondary : Primary, always: true) is var shown && shown >> 24 != 0 ? shown | 0xFF000000 : 0xFFFFFFFF; }
            finally { seedingPicker = false; }
            return;
        }
        if (mixed) Remember(PickerColor);
        mixed = false; EditingNamed = null; PickingSecondary = false;
    }
    partial void OnPickerColorChanged(uint value)
    {
        // Opening the picker on her skin doesn't turn the live skin into a plain colour: only mixing does.
        if (seedingPicker || !IsPickerOpen || EditingNamed != null) return;
        mixed = true;
        if (PickingSecondary) Secondary = value; else Primary = value;
    }

    /// <summary>Click the left colour square: mix it.</summary>
    [RelayCommand] private void EditPrimary() { IsPickerOpen = false; PickingSecondary = false; IsPickerOpen = true; }
    /// <summary>Click the right colour square: mix it.</summary>
    [RelayCommand] private void EditSecondary() { IsPickerOpen = false; PickingSecondary = true; IsPickerOpen = true; }
    /// <summary>Double-click a swatch: paint with it, and open the picker on it to change it a little.</summary>
    [RelayCommand]
    private void EditSwatch(SwatchViewModel? swatch)
    {
        if (swatch == null || swatch.Color >> 24 == 0) return;
        Primary = swatch.Color;
        EditPrimary();
    }
    public string PickerTitle => IsEditingNamed ? $"CHANGE {EditingNamed?.ToUpperInvariant()}" : PickingSecondary ? "RIGHT BUTTON COLOUR" : "LEFT BUTTON COLOUR";
    partial void OnPickingSecondaryChanged(bool value) => OnPropertyChanged(nameof(PickerTitle));

    // ---------------- named colours (change once, changes everywhere) ----------------

    public ObservableCollection<NamedColorRowViewModel> Named { get; } = new();
    /// <summary>The named colour the picker is changing (null: the picker mixes a colour to paint with).</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsEditingNamed), nameof(EditingNamedLabel), nameof(PickerTitle))] private string? editingNamed;
    private int? editingShade;
    public bool IsEditingNamed => EditingNamed != null;
    public string EditingNamedLabel => editingShade switch { 0 => $"Change {EditingNamed} dark everywhere", 2 => $"Change {EditingNamed} light everywhere", _ => $"Change {EditingNamed} everywhere" };

    private void RefreshNamed()
    {
        Named.Clear();
        foreach (var ramp in studio.NamedColors()) Named.Add(new NamedColorRowViewModel(ramp));
    }

    /// <summary>Name the left colour (as you see it): it becomes a dark, mid and light to paint with and change later.</summary>
    [RelayCommand]
    private void AddNamed()
    {
        uint shown = Shown(Primary, always: true);
        uint mid = shown >> 24 != 0 ? shown : PickerColor;
        string? name = studio.Shell.Ui.Prompt("Name a colour", $"Name for {StudioImages.Hex(mid | 0xFF000000)} (e.g. \"Coat\"). Change it later and it changes everywhere it's painted.", "Coat",
            v => string.IsNullOrWhiteSpace(v) ? "Give it a name." : null);
        if (string.IsNullOrWhiteSpace(name) || studio.AddNamedColor(name, mid) is not { } ramp) return;
        Primary = ramp.Mid;
    }

    /// <summary>Open the picker to change a named colour (its whole ramp around a new mid).</summary>
    [RelayCommand]
    private void EditNamed(NamedColorRowViewModel? row)
    {
        if (row == null) return;
        IsPickerOpen = true;
        editingShade = null;
        EditingNamed = row.Name;
        OnPropertyChanged(nameof(EditingNamedLabel));
        PickerColor = row.Shades[1].Color;
    }

    /// <summary>Open the picker to change one shade of a named colour.</summary>
    [RelayCommand]
    private void EditNamedShade(SwatchViewModel? swatch)
    {
        if (swatch == null || Named.FirstOrDefault(r => r.Shades.Contains(swatch)) is not { } row) return;
        IsPickerOpen = true;
        editingShade = row.Shades.ToList().IndexOf(swatch);
        EditingNamed = row.Name;
        OnPropertyChanged(nameof(EditingNamedLabel));
        PickerColor = swatch.Color;
    }

    [RelayCommand]
    private void ApplyNamed()
    {
        if (EditingNamed is not { } name) return;
        studio.ChangeNamedColor(name, PickerColor, editingShade);
        IsPickerOpen = false;
    }

    [RelayCommand]
    private void ForgetNamed(NamedColorRowViewModel? row) { if (row != null) studio.ForgetNamedColor(row.Name); }

    /// <summary>Done: paint with the picker's colour (on the button it's for).</summary>
    [RelayCommand]
    private void UsePicked()
    {
        if (PickingSecondary) Secondary = PickerColor; else Primary = PickerColor;
        mixed = true;
        IsPickerOpen = false;
    }

    /// <summary>Keep the picker's colour in your colours, and paint with it.</summary>
    [RelayCommand]
    private void AddPicked()
    {
        if (!studio.Settings.CustomColors.Any(c => Parse(c) == PickerColor)) { studio.Settings.CustomColors.Add($"#{PickerColor:X8}"); studio.Settings.Save(); RefreshCustom(); }
        UsePicked();
    }

    [RelayCommand]
    private void RemoveCustom(SwatchViewModel? swatch)
    {
        if (swatch == null) return;
        studio.Settings.CustomColors.RemoveAll(c => Parse(c) == swatch.Color);
        studio.Settings.Save();
        RefreshCustom();
    }

    /// <summary>A swatch that shows <paramref name="shown"/> but paints <paramref name="paints"/> (a slot shown as her real colour).</summary>
    private SwatchViewModel SwatchFor(uint paints, uint shown)
    {
        bool slot = paints != shown;
        string name = slot ? (Simple ? FriendlyName(paints) : NameOf(paints)) : StudioImages.Hex(shown);
        var swatch = new SwatchViewModel(name, paints)
        {
            Hint = slot ? $"{name} {StudioImages.Hex(shown)}: follows sprite mods and outfits. Left click: left button, right click: right button." : $"{name}. Left click: left button, right click: right button.",
        };
        swatch.Display = SwatchViewModel.Brush(shown);
        return swatch;
    }

    /// <summary>Put a colour at the front of the recent colours.</summary>
    public void Remember(uint color)
    {
        if (color >> 24 == 0) return;
        if (Recent.FirstOrDefault()?.Color == color) return;
        foreach (var old in Recent.Where(s => s.Color == color).ToList()) Recent.Remove(old);
        Recent.Insert(0, SwatchFor(color, Shown(color, always: true)));
        while (Recent.Count > RecentCount) Recent.RemoveAt(Recent.Count - 1);
    }

    /// <summary>
    /// The slot colour that shows as <paramref name="shown"/> for this character, if any. The eyedropper and the
    /// picture's colours use it, so painting with a colour picked from her sprite follows her outfits.
    /// </summary>
    public uint? SlotFor(uint shown)
    {
        if (shown >> 24 == 0) return null;
        foreach (var (name, color) in SlotColors)
            if (color != ColorSlots.Erase && Shown(color, always: true) == shown && Shown(color, always: true) != color) return color;
        return null;
    }

    /// <summary>The colours of the frame being edited (what you see), as swatches that paint her slots where they can.</summary>
    public void ShowPictureColors(Core.Compositing.RgbaImage picture)
    {
        var colors = picture.Pixels.Where(p => p >> 24 != 0).Distinct().Take(PictureCount * 4).ToList();
        static (double Hue, double Light) Key(uint c)
        {
            var color = StudioImages.ToColor(c);
            double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0, max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double hue = max == min ? -1 : max == r ? (g - b) / (max - min) % 6 : max == g ? (b - r) / (max - min) + 2 : (r - g) / (max - min) + 4;
            return (max - min < 0.08 ? -1 : Math.Round((hue + 6) % 6, 0), (max + min) / 2);
        }
        var sorted = colors.OrderBy(c => Key(c).Hue).ThenBy(c => Key(c).Light).Take(PictureCount).ToList();
        if (InPicture.Select(s => s.Color).SequenceEqual(sorted.Select(c => SlotFor(c) ?? c))) return;
        InPicture.Clear();
        foreach (var color in sorted) InPicture.Add(SwatchFor(SlotFor(color) ?? color, color));
    }

    public static uint? Parse(string? text)
    {
        text = text?.Trim().TrimStart('#');
        if (text == null || (text.Length != 6 && text.Length != 8) || !uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)) return null;
        return text.Length == 6 ? 0xFF000000 | value : value;
    }

    private void RefreshCustom()
    {
        Custom.Clear();
        foreach (var text in studio.Settings.CustomColors)
            if (Parse(text) is { } color) Custom.Add(new SwatchViewModel(StudioImages.Hex(color), color));
    }

    /// <summary>The chosen character's sampled ramps, and slot swatches resolved with them when asked.</summary>
    public void Refresh()
    {
        RefreshNamed();
        Sampled.Clear();
        SampledFrom = null;
        ramps = new Dictionary<string, ColorRamp>(StringComparer.OrdinalIgnoreCase);
        if (studio.Character is { } who && studio.Library is { } library)
        {
            var problems = new List<string>();
            var resolver = new EditorLayerSourceResolver(studio.Context, new Core.Models.Pose { FrameSize = new[] { 16, 32 } }, "down", problems);
            var colors = studio.Context.Colors(resolver, problems);
            IEnumerable<string> samples;
            string owner;
            if (who.IsFarmer) { owner = "Farmer"; samples = new[] { "Skin", "Hair", "Eyes", "Shirt", "Pants", "Shoes" }; }
            else if (library.RigFor(who.Key) is { } rig) { owner = rig.Id!; samples = rig.ColorSamples.Keys; }
            else { owner = ""; samples = Array.Empty<string>(); }
            foreach (var sample in samples)
            {
                var ramp = colors($"{owner}.{sample}");
                if (problems.Count > 0) { problems.Clear(); continue; }
                ramps[sample] = ramp;
                Sampled.Add(new SwatchViewModel($"{who.Label} {sample} dark", ramp.Dark, sample));
                Sampled.Add(new SwatchViewModel($"{who.Label} {sample}", ramp.Mid, sample));
                Sampled.Add(new SwatchViewModel($"{who.Label} {sample} light", ramp.Light, sample));
            }
            SampledFrom = owner.Length > 0 ? owner : null;
        }
        OnPropertyChanged(nameof(SampledFrom));
        foreach (var slot in Slots)
        {
            var name = slot.Name.Split(' ');
            uint shown = slot.Color;
            if ((ShowResolved || Simple) && ramps.TryGetValue(name[0], out var ramp))
                shown = name.Length == 1 ? ramp.Mid : name[1] switch { "dark" => ramp.Dark, "light" => ramp.Light, _ => ramp.Mid };
            slot.Display = SwatchViewModel.Brush(shown);
        }
        VisibleSlots.Clear();
        foreach (var slot in Slots)
            if (!Simple || slot.Name == "Outline" || ramps.ContainsKey(slot.Name.Split(' ')[0])) VisibleSlots.Add(slot);
        foreach (var name in new[] { nameof(SlotsTitle), nameof(PrimaryBrush), nameof(SecondaryBrush), nameof(PrimaryName), nameof(SecondaryName) }) OnPropertyChanged(name);
    }
}
