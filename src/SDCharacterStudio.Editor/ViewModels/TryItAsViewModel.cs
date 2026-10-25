using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Schema;

namespace SDCharacterStudio.Editor.ViewModels;

/// <summary>One Check, Chance or random item in "Try it as…": how it should land in the next preview.</summary>
public sealed partial class TryItAsEntry : ObservableObject
{
    private readonly TryItAsViewModel owner;
    private readonly string key;
    public TryItAsEntry(TryItAsViewModel owner, string key, string title, IReadOnlyList<ChoiceItem> options)
    { this.owner = owner; this.key = key; Title = title; Options = options; }
    public string Title { get; }
    public IReadOnlyList<ChoiceItem> Options { get; }
    public ChoiceItem? Selected
    {
        get => Options.FirstOrDefault(o => o.Value == owner.Forced(key)) ?? Options[0];
        set { owner.Force(key, value?.Value); OnPropertyChanged(); }
    }
}

/// <summary>
/// "Try it as…" in preview (D-51): pick how this scene's Checks, Chances and random items land, for previews only. The
/// picks change the preview's own copy of the scene (a case becomes TRUE and the others FALSE; an outcome or item
/// gets the only chance), so the pack and the player's save are never touched.
/// </summary>
public sealed partial class TryItAsViewModel : ObservableObject
{
    private const string Decide = "";
    private readonly MainViewModel shell;
    /// <summary>"scene|lane|index|type" → the way picked (a case, an outcome or an item index).</summary>
    private readonly Dictionary<string, string> forced = new(StringComparer.Ordinal);
    public TryItAsViewModel(MainViewModel shell) => this.shell = shell;

    public ObservableCollection<TryItAsEntry> Entries { get; } = new();
    [ObservableProperty] private string? emptyText;
    public int Count => shell.Scene is { } scene ? forced.Keys.Count(k => k.StartsWith(scene.Id + "|", StringComparison.Ordinal)) : 0;
    public string ButtonText => Count == 0 ? "Try it as…" : $"Try it as… ({Count})";

    internal string Forced(string key) => forced.TryGetValue(key, out var way) ? way : Decide;
    internal void Force(string key, string? way)
    {
        if (string.IsNullOrEmpty(way)) forced.Remove(key); else forced[key] = way;
        OnPropertyChanged(nameof(Count)); OnPropertyChanged(nameof(ButtonText));
    }

    [RelayCommand]
    private void Clear()
    {
        if (shell.Scene is { } scene) foreach (var key in forced.Keys.Where(k => k.StartsWith(scene.Id + "|", StringComparison.Ordinal)).ToArray()) forced.Remove(key);
        Refresh();
    }

    private static string Key(string scene, string lane, int index, Beat beat) => $"{scene}|{lane}|{index}|{beat.Type}";

    /// <summary>The current scene's Checks, Chances and random items.</summary>
    public void Refresh()
    {
        Entries.Clear();
        if (shell.Scene is not { } scene) { EmptyText = "Open a scene first."; Notify(); return; }
        var decide = new ChoiceItem(Decide, "Let the game decide");
        foreach (var (lane, beats) in Lanes(scene))
            for (int i = 0; i < beats.Count; i++)
            {
                string where = $"beat {i + 1}{(lane == LaneNames.Main ? "" : " of " + LaneNames.Title(lane))}";
                switch (beats[i])
                {
                    case CheckBeat check:
                        Entries.Add(new(this, Key(scene.Id!, lane, i, check), $"Check · {where}", check.Cases
                            .Select((c, n) => new ChoiceItem(n.ToString(), $"if {Conditions.Describe(c.When)} → {c.Then}"))
                            .Append(new ChoiceItem(check.Cases.Count.ToString(), $"none true → {check.Otherwise ?? "keep going"}")).Prepend(decide).ToArray()));
                        break;
                    case ChanceBeat chance:
                        Entries.Add(new(this, Key(scene.Id!, lane, i, chance), $"Chance · {where}", chance.Outcomes
                            .Select((o, n) => new ChoiceItem(n.ToString(), $"→ {o.Then} ({BeatCatalog.Percent(o.Weight, chance.Outcomes.Select(x => x.Weight))}%)"))
                            .Prepend(decide).ToArray()));
                        break;
                    case ItemBeat { OneOf.Count: > 0 } bucket:
                        Entries.Add(new(this, Key(scene.Id!, lane, i, bucket), $"Random item · {where}", bucket.OneOf!
                            .Select((c, n) => new ChoiceItem(n.ToString(), shell.Catalog?.Item(c.Item ?? "")?.Label ?? c.Item ?? "?"))
                            .Prepend(decide).ToArray()));
                        break;
                }
            }
        EmptyText = Entries.Count == 0 ? "This scene has no Checks, Chances or random items." : null;
        Notify();
    }

    private void Notify() { OnPropertyChanged(nameof(Count)); OnPropertyChanged(nameof(ButtonText)); }

    private static IEnumerable<(string Lane, List<Beat> Beats)> Lanes(Cutscene scene) =>
        new[] { (LaneNames.Main, scene.Beats) }.Concat(scene.Branches.Select(b => (b.Key, b.Value)));

    /// <summary>The scene as the preview should play it: a copy with the picked ways forced (the same scene when nothing is).</summary>
    public Cutscene Apply(Cutscene scene)
    {
        if (!forced.Keys.Any(k => k.StartsWith(scene.Id + "|", StringComparison.Ordinal))) return scene;
        var copy = Project.PackWriter.Clone(scene);
        foreach (var (lane, beats) in Lanes(copy))
            for (int i = 0; i < beats.Count; i++)
            {
                if (!forced.TryGetValue(Key(copy.Id!, lane, i, beats[i]), out var picked) || !int.TryParse(picked, out int way)) continue;
                switch (beats[i])
                {
                    case CheckBeat check:
                        for (int n = 0; n < check.Cases.Count; n++) check.Cases[n].When = n == way ? "TRUE" : "FALSE";
                        break;
                    case ChanceBeat chance:
                        for (int n = 0; n < chance.Outcomes.Count; n++) chance.Outcomes[n].Weight = n == way ? 1 : 0;
                        break;
                    case ItemBeat { OneOf: { } oneOf }:
                        for (int n = 0; n < oneOf.Count; n++) { oneOf[n].Weight = n == way ? 1 : 0; if (n == way) oneOf[n].When = null; }
                        break;
                }
            }
        return copy;
    }
}
