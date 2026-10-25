using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDCharacterStudio.Core;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Schema;

namespace SDCharacterStudio.Editor.ViewModels;

/// <summary>Where a story flag or number is set or checked: a beat, or a scene's conditions (Lane null).</summary>
public sealed record StoryPlace(string Text, IRelayCommand Go);

/// <summary>One story flag or number across the project (D-51): where it's set, where it's checked, and the designer's note.</summary>
public sealed partial class StoryEntryViewModel : ObservableObject
{
    private readonly StoryViewModel owner;
    public StoryEntryViewModel(StoryViewModel owner, bool number, string id, string label, IReadOnlyList<StoryPlace> sets, IReadOnlyList<StoryPlace> checks)
    {
        this.owner = owner; IsNumber = number; Id = id; Label = label; Sets = sets; Checks = checks;
        (Warning, IsWarning) = (number, sets.Count, checks.Count) switch
        {
            (false, 0, > 0) => ("Nothing in this project sets it. A typo, or set by the game or another mod?", true),
            (true, 0, > 0) => ("Nothing changes it, so it stays 0.", true),
            (_, > 0, 0) => ("Nothing checks it yet.", false),
            _ => ((string?)null, false),
        };
    }

    public bool IsNumber { get; }
    public string Id { get; }
    public string Label { get; }
    public IReadOnlyList<StoryPlace> Sets { get; }
    public IReadOnlyList<StoryPlace> Checks { get; }
    public bool HasSets => Sets.Count > 0;
    public bool HasChecks => Checks.Count > 0;
    public string SetsTitle => IsNumber ? "Changed in" : "Set in";
    public string Summary => $"{(IsNumber ? "changed" : "set")} in {Places(Sets.Count)} · checked in {Places(Checks.Count)}";
    private static string Places(int n) => n == 1 ? "1 place" : $"{n} places";
    public string? Warning { get; }
    public bool HasWarning => Warning != null;
    /// <summary>A real problem (likely a typo), not just "nothing checks it yet".</summary>
    public bool IsWarning { get; }

    /// <summary>The designer's note: what it means. Kept in the project file.</summary>
    public string? Note
    {
        get => owner.NoteFor(Id);
        set => owner.SetNote(Id, value);
    }
}

/// <summary>
/// The Story panel (D-51): every story flag and story number the project sets or checks, with where, so a flag set in one
/// scene and misspelled in the Check of another stands out.
/// </summary>
public sealed partial class StoryViewModel : ObservableObject
{
    private readonly MainViewModel shell;
    public StoryViewModel(MainViewModel shell) => this.shell = shell;

    public ObservableCollection<StoryEntryViewModel> Entries { get; } = new();
    [ObservableProperty] private string? emptyText = "Open a project to see its story flags and numbers.";
    public int WarningCount => Entries.Count(e => e.IsWarning);

    public void Refresh()
    {
        Entries.Clear();
        var document = shell.Document;
        if (document == null) { EmptyText = "Open a project to see its story flags and numbers."; OnPropertyChanged(nameof(WarningCount)); return; }
        var sets = new Dictionary<(bool Number, string Id), List<StoryPlace>>();
        var checks = new Dictionary<(bool Number, string Id), List<StoryPlace>>();
        void Add(Dictionary<(bool, string), List<StoryPlace>> into, bool number, string? id, StoryPlace place)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            if (!into.TryGetValue((number, id), out var list)) into[(number, id)] = list = new();
            list.Add(place);
        }
        void Conditions(string? text, StoryPlace place)
        {
            foreach (var row in Schema.Conditions.Parse(text).Rows)
            {
                if (row.Kind.Id == "flag") Add(checks, false, row.Subject, place);
                if (row.Kind.Id == "number") Add(checks, true, row.Subject, place);
            }
        }

        foreach (var cutscene in document.Cutscenes)
        {
            var scene = cutscene.Scene;
            string sceneId = scene.Id ?? "?", title = scene.Title ?? sceneId;
            var lanes = new List<(string Lane, List<Beat> Beats)> { (LaneNames.Main, scene.Beats) };
            lanes.AddRange(scene.Branches.Select(b => (b.Key, b.Value)));
            if (scene.OnSkip != null) lanes.Add((LaneNames.Skip, scene.OnSkip));
            foreach (var (lane, beats) in lanes)
                for (int i = 0; i < beats.Count; i++)
                {
                    int index = i;
                    StoryPlace Place(string what) => new($"{title} · {(lane == LaneNames.Main ? "" : LaneNames.Title(lane) + " ")}beat {index + 1}: {what}",
                        new RelayCommand(() => GoTo(sceneId, new BeatSelection(sceneId, lane, index))));
                    switch (beats[i])
                    {
                        case MailBeat mail:
                            Add(sets, false, mail.Add, Place("sets it"));
                            Add(sets, false, mail.Remove, Place("clears it"));
                            break;
                        case ItemBeat { OneOf: { } oneOf } bucket:
                            if (bucket.Remember)
                                foreach (var pick in oneOf.Where(p => p.Item != null)) Add(sets, false, Ids.PickedItemFlag(document.PackId, sceneId, pick.Item!), Place("a random item"));
                            foreach (var pick in oneOf) Conditions(pick.When, Place("a random item's only if"));
                            break;
                        case NumberBeat { Name: { } name } number:
                            Add(sets, true, Ids.NumberStat(document.PackId, name), Place(BeatCatalog.NumberSummary(number)));
                            break;
                        case CheckBeat check:
                            foreach (var @case in check.Cases) Conditions(@case.When, Place("a Check"));
                            break;
                        case ChoiceBeat choice:
                            foreach (var option in choice.Options) Conditions(option.When, Place("an answer's only if"));
                            break;
                    }
                }
            if (scene.Trigger is { } trigger)
            {
                var place = new StoryPlace($"{title} · when it plays", new RelayCommand(() => GoTo(sceneId, new SceneSelection(sceneId))));
                Conditions(trigger.Conditions, place);
                Add(checks, false, trigger.OnDayAfterFlag, place);
                foreach (string precondition in trigger.Preconditions)
                    if (precondition.StartsWith("n ", StringComparison.Ordinal) || precondition.StartsWith("l ", StringComparison.Ordinal))
                        Add(checks, false, precondition[2..].Trim(), place);
            }
        }

        string prefix = $"SDCS.{document.PackId}.";
        var entries = sets.Keys.Union(checks.Keys).Select(key => new StoryEntryViewModel(this, key.Number, key.Id,
            key.Number ? (key.Id.StartsWith(prefix, StringComparison.Ordinal) ? key.Id[prefix.Length..] : key.Id) : Schema.Conditions.Short(key.Id),
            sets.TryGetValue(key, out var s) ? s : new(), checks.TryGetValue(key, out var c) ? c : new()));
        // What looks wrong first (a flag nothing sets, a number nothing changes), then numbers and flags by name.
        foreach (var entry in entries.OrderByDescending(e => e.IsWarning).ThenByDescending(e => e.IsNumber).ThenBy(e => e.Label, StringComparer.OrdinalIgnoreCase))
            Entries.Add(entry);
        EmptyText = Entries.Count == 0 ? "No story flags or numbers yet. Story flag beats set flags; Change a number beats change numbers; Checks read them." : null;
        OnPropertyChanged(nameof(WarningCount));
    }

    private void GoTo(string sceneId, Selection selection)
    {
        shell.OpenCutscene(sceneId);
        shell.Select(selection);
    }

    internal string? NoteFor(string id) => shell.Document?.Project.StoryNotes.TryGetValue(id, out var note) == true ? note : null;

    internal void SetNote(string id, string? note)
    {
        if (shell.Document is not { } document || (note ?? "") == (NoteFor(id) ?? "")) return;
        shell.Edit("Story note", () =>
        {
            if (string.IsNullOrWhiteSpace(note)) document.Project.StoryNotes.Remove(id);
            else document.Project.StoryNotes[id] = note.Trim();
        });
    }
}
