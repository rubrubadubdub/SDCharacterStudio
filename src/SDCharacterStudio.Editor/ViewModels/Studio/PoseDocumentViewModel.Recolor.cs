using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDCharacterStudio.Core.Models;

namespace SDCharacterStudio.Editor.ViewModels;

/// <summary>One live recolour in the panel: which of her colours, the new colour, and how wide a range of hues it takes.</summary>
public sealed partial class RecolorRowViewModel : ObservableObject
{
    private readonly PoseDocumentViewModel owner;
    public RecolorRowViewModel(PoseDocumentViewModel owner, int index, RecolorRule rule, IReadOnlyList<ChoiceItem> sources)
    {
        this.owner = owner; Index = index; Sources = sources;
        from = rule.From; Range = rule.Range;
        To = PaletteViewModel.Parse(rule.To) ?? 0xFF000000;
        ToBrush = SwatchViewModel.Brush(To);
    }
    public int Index { get; }
    public IReadOnlyList<ChoiceItem> Sources { get; }
    public uint To { get; }
    public Brush ToBrush { get; }
    public int Range { get; }
    public string ToText => StudioImages.Hex(To);
    public static IReadOnlyList<ChoiceItem> Ranges { get; } = new[] { new ChoiceItem("12", "Just that colour"), new ChoiceItem("25", "That colour and its shades"), new ChoiceItem("40", "Nearby colours too") };
    private string? from;
    public string? From { get => from; set { if (value != null && value != from) owner.ChangeRecolor(Index, r => r.From = value); } }
    public string RangeChoice { get => Range <= 12 ? "12" : Range >= 40 ? "40" : "25"; set { if (int.TryParse(value, out int range) && range != Range) owner.ChangeRecolor(Index, r => r.Range = range); } }
    [RelayCommand] private void UseLeftColour() => owner.ChangeRecolor(Index, r => r.To = $"#{owner.Palette.Resolved(owner.Palette.Primary) & 0xFFFFFF:X6}");
    [RelayCommand] private void Remove() => owner.RemoveRecolor(Index);

    /// <summary>Click the new colour: a picker right there, starting from it.</summary>
    [ObservableProperty] private bool isPicking;
    [ObservableProperty] private uint pickColor;
    partial void OnIsPickingChanged(bool value) { if (value) PickColor = To | 0xFF000000; }
    [RelayCommand] private void Pick() => IsPicking = true;
    [RelayCommand]
    private void UsePick()
    {
        IsPicking = false;
        if ((PickColor | 0xFF000000) != (To | 0xFF000000)) owner.ChangeRecolor(Index, r => r.To = $"#{PickColor & 0xFFFFFF:X6}");
    }
}

/// <summary>
/// Recolour her, still live (after Pixelorama's hue/saturation effects): "the same dress, but red". Each rule changes one
/// of her sampled colours (and its shades) on every frame's live picture; sprite mods still draw her underneath.
/// </summary>
public partial class PoseDocumentViewModel
{
    public ObservableCollection<RecolorRowViewModel> Recolors { get; } = new();
    public bool RecolorOpen { get => Section("recolor", true); set => Section("recolor", value, nameof(RecolorOpen)); }
    /// <summary>Recolouring changes a villager's own colours (the player changes clothes with a Costume beat).</summary>
    public bool CanRecolor => PoseCharacter() is { IsFarmer: false } && CharacterLayers(Model).Any();

    /// <summary>Her colours to recolour: the rig's samples (her hair, shirt, pants...), named plainly.</summary>
    private IReadOnlyList<ChoiceItem> RecolorSources()
    {
        if (Library?.Rig(Model.For) is not { } rig || PoseCharacter() is not { } who) return Array.Empty<ChoiceItem>();
        var problems = new List<string>();
        var colors = Context.Colors(new EditorLayerSourceResolver(Context, Model, Model.Facing ?? "down", problems), problems);
        // Clothes first: what people recolour most.
        string[] order = { "Shirt", "Pants", "Shoes", "Hair", "Eyes", "Skin" };
        int Rank(string key) => Array.FindIndex(order, o => o.Equals(key, StringComparison.OrdinalIgnoreCase)) is >= 0 and var i ? i : order.Length;
        return rig.ColorSamples.Keys.Where(k => !k.Equals("Outline", StringComparison.OrdinalIgnoreCase)).OrderBy(Rank)
            .Select(k =>new ChoiceItem($"{rig.Id}.{k}", $"{who.Label}'s {k.ToLowerInvariant()}", StudioImages.Hex(colors($"{rig.Id}.{k}").Mid)))
            .ToList();
    }

    private List<RecolorRule> CurrentRecolors() =>
        (CharacterLayers(Model).FirstOrDefault()?.Recolor ?? new()).Select(r => new RecolorRule { From = r.From, To = r.To, Range = r.Range }).ToList();

    private void RefreshRecolors()
    {
        Recolors.Clear();
        var sources = RecolorSources();
        var rules = CurrentRecolors();
        for (int i = 0; i < rules.Count; i++) Recolors.Add(new RecolorRowViewModel(this, i, rules[i], sources));
        OnPropertyChanged(nameof(CanRecolor));
    }

    /// <summary>Every live picture of hers gets the same recolours (one undo step).</summary>
    private void SetRecolors(List<RecolorRule> rules, string label)
    {
        Save(label, pose =>
        {
            foreach (var live in CharacterLayers(pose))
                live.Recolor = rules.Count == 0 ? null : rules.Select(r => new RecolorRule { From = r.From, To = r.To, Range = r.Range }).ToList();
        });
        Context.InvalidatePack();
        RefreshRecolors();
        Render(); RenderThumbnails();
    }

    /// <summary>Recolour one of her colours that isn't yet, to the left colour (as you see it).</summary>
    [RelayCommand]
    public void AddRecolor()
    {
        var sources = RecolorSources();
        var rules = CurrentRecolors();
        if (sources.FirstOrDefault(s => !rules.Any(r => string.Equals(r.From, s.Value, StringComparison.OrdinalIgnoreCase))) is not { } source)
        { Ui.Toast("Nothing left to recolour", sources.Count == 0 ? "Their rig has no colour samples yet." : "Each of their colours already has a recolour.", ToastKind.Info); return; }
        uint to = Palette.Resolved(Palette.Primary);
        if (to >> 24 == 0) to = 0xFFC03030;
        rules.Add(new RecolorRule { From = source.Value, To = $"#{to & 0xFFFFFF:X6}" });
        SetRecolors(rules, "Recolour " + source.Label);
    }

    public void ChangeRecolor(int index, Action<RecolorRule> change)
    {
        var rules = CurrentRecolors();
        if (index < 0 || index >= rules.Count) return;
        change(rules[index]);
        SetRecolors(rules, "Change recolour");
    }

    public void RemoveRecolor(int index)
    {
        var rules = CurrentRecolors();
        if (index < 0 || index >= rules.Count) return;
        rules.RemoveAt(index);
        SetRecolors(rules, "Remove recolour");
    }
}
