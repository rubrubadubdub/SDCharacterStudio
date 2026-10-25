using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Export;
using SDCharacterStudio.Editor.Project;
using SDCharacterStudio.Editor.Schema;
using SDCharacterStudio.Editor.Storyboard;
using Wpf.Ui.Controls;

namespace SDCharacterStudio.Editor.ViewModels;

public sealed record PaletteEntry(BeatKind Kind)
{
    public string Name => Kind.Name;
    public string Help => Kind.Help;
    public SymbolRegular Icon => Kind.Icon;
    public BeatCategory Category => Kind.Category;
}
public sealed record PaletteGroup(string Name, BeatCategory Category, IReadOnlyList<PaletteEntry> Entries);

/// <summary>The storyboard (doc 07 §7.1): the main list and each branch as a lane of beat cards.</summary>
public sealed partial class StoryboardViewModel : ObservableObject
{
    private readonly MainViewModel shell;
    public StoryboardViewModel(MainViewModel shell) => this.shell = shell;
    public Catalog.ExportCatalog? Catalog => shell.Catalog;

    public ObservableCollection<LaneViewModel> Lanes { get; } = new();
    public BranchGraph Graph { get; private set; } = new();
    public bool GraphMode { get => shell.Document?.Project.StoryboardGraph ?? false; set { if (shell.Document != null) shell.Document.Project.StoryboardGraph = value; OnPropertyChanged(); } }
    public static IReadOnlyList<PaletteGroup> Palette { get; } = BeatCatalog.All.GroupBy(k => k.Category)
        .Select(g => new PaletteGroup(BeatCatalog.CategoryName(g.Key), g.Key, g.Select(k => new PaletteEntry(k)).ToArray())).ToArray();
    public bool HasScene => shell.Scene != null;

    public void Rebuild()
    {
        var scene = shell.Scene;
        Lanes.Clear();
        OnPropertyChanged(nameof(HasScene));
        OnPropertyChanged(nameof(GraphMode));
        Graph = scene == null ? new() : BranchGraphBuilder.Build(scene);
        OnPropertyChanged(nameof(Graph));
        if (scene == null) return;
        var incoming = IncomingLinks(scene);
        Lanes.Add(new LaneViewModel(this, LaneNames.Main, scene.Beats, scene, incoming));
        foreach (var name in scene.Branches.Keys) Lanes.Add(new LaneViewModel(this, name, scene.Branches[name], scene, incoming));
        if (scene.Skippable || scene.OnSkip is { Count: > 0 }) Lanes.Add(new LaneViewModel(this, LaneNames.Skip, scene.OnSkip ?? new(), scene, incoming));
        SyncSelection();
        if (shell.LastValidation != null) ApplyProblems(shell.LastValidation.Problems);
    }

    public void SyncSelection()
    {
        OnPropertyChanged("Selection");
        foreach (var lane in Lanes)
        {
            lane.IsSelected = shell.Selection is BranchSelection b && b.Lane == lane.Name;
            foreach (var card in lane.Cards) card.IsSelected = shell.Selection is BeatSelection s && s.Lane == lane.Name && s.Index == card.Index;
        }
    }

    public void ApplyProblems(IReadOnlyList<Problem> problems)
    {
        string? file = shell.Current?.File;
        foreach (var lane in Lanes)
            foreach (var card in lane.Cards)
            {
                var mine = problems.Where(p => string.Equals(p.File, file, StringComparison.OrdinalIgnoreCase) && p.Target.Kind == TargetKind.Beat
                    && p.Target.Lane == lane.Name && p.Target.Index == card.Index).ToArray();
                card.ProblemText = mine.Length == 0 ? null : string.Join("\n", mine.Select(p => p.Message).Distinct());
                card.HasError = mine.Any(p => p.IsError);
                card.HasWarning = !card.HasError && mine.Length > 0;
            }
    }

    private static Dictionary<string, List<string>> IncomingLinks(Cutscene scene)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        void Scan(string lane, List<Beat> beats)
        {
            for (int i = 0; i < beats.Count; i++)
            {
                foreach (var target in BeatFlow.Targets(beats[i]).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!map.TryGetValue(target, out var list)) map[target] = list = new();
                    list.Add($"{LaneNames.Title(lane)} #{i + 1} ({beats[i] switch { ChoiceBeat => "choice", CheckBeat => "check", ChanceBeat => "chance", _ => "go to" }})");
                }
            }
        }
        Scan(LaneNames.Main, scene.Beats);
        foreach (var (name, beats) in scene.Branches) Scan(name, beats);
        return map;
    }

    // ---------- operations (each is one undo step) ----------

    public BeatContext Context(string? preferredActor = null)
    {
        var scene = shell.Scene!;
        string? actor = preferredActor ?? (shell.Selection is ActorSelection a && a.Index < scene.Actors.Count ? scene.Actors[a.Index].Id : null);
        return new BeatContext(scene.Actors, scene.Branches.Keys.ToArray(), actor);
    }

    public void Insert(string lane, int index, BeatKind kind, Action<Beat>? configure = null)
    {
        var scene = shell.Scene;
        if (scene == null) return;
        var beat = kind.Create(Context());
        configure?.Invoke(beat);
        if (beat is ChoiceBeat choice && choice.Options.Count == 0)
        {
            // A new choice gets two options, each with its own new branch, so it works immediately.
            string first = UniqueBranch(scene, "option-1"), second = UniqueBranch(scene, "option-2", first);
            choice.Options.Add(new ChoiceOption { Text = "First answer", Then = first });
            choice.Options.Add(new ChoiceOption { Text = "Second answer", Then = second });
            shell.Edit("Add choice", () =>
            {
                scene.Branches[first] = new List<Beat>();
                scene.Branches[second] = new List<Beat>();
                InsertInto(scene, lane, index, beat);
            });
        }
        else if (beat is CheckBeat or ChanceBeat && BeatFlow.Ways(beat).Any(w => w.Branch == null && !w.KeepGoing))
        {
            // A new Check's cases and a new Chance's outcomes get their own new branches, like a Choice's answers.
            var branches = new List<string>();
            string stem = beat is CheckBeat ? "case-" : "outcome-";
            foreach (var @case in (beat as CheckBeat)?.Cases.Where(c => c.Then == null) ?? Enumerable.Empty<CheckCase>())
                branches.Add(@case.Then = UniqueBranch(scene, stem + (branches.Count + 1), branches.LastOrDefault()));
            foreach (var outcome in (beat as ChanceBeat)?.Outcomes.Where(o => o.Then == null) ?? Enumerable.Empty<ChanceOutcome>())
                branches.Add(outcome.Then = UniqueBranch(scene, stem + (branches.Count + 1), branches.LastOrDefault()));
            shell.Edit("Add " + kind.Name.ToLowerInvariant(), () =>
            {
                foreach (string branch in branches) scene.Branches[branch] = new List<Beat>();
                InsertInto(scene, lane, index, beat);
            });
        }
        else shell.Edit("Add " + kind.Name.ToLowerInvariant(), () => InsertInto(scene, lane, index, beat));
        shell.Select(new BeatSelection(scene.Id!, lane, Math.Clamp(index, 0, LaneNames.Get(scene, lane)!.Count - 1)));
    }

    private static void InsertInto(Cutscene scene, string lane, int index, Beat beat)
    {
        var list = LaneNames.GetOrCreate(scene, lane);
        list.Insert(Math.Clamp(index, 0, list.Count), beat);
    }

    public void Append(string lane, BeatKind kind) => Insert(lane, LaneNames.Get(shell.Scene!, lane)?.Count ?? 0, kind);

    public void Delete(string lane, int index)
    {
        var scene = shell.Scene;
        if (scene == null || LaneNames.Get(scene, lane) is not { } list || index >= list.Count) return;
        shell.Edit("Delete " + BeatCatalog.Kind(list[index].Type).Name.ToLowerInvariant(), () => list.RemoveAt(index));
        shell.Select(list.Count == 0 ? new BranchSelection(scene.Id!, lane) : new BeatSelection(scene.Id!, lane, Math.Min(index, list.Count - 1)));
    }

    public void Duplicate(string lane, int index)
    {
        var scene = shell.Scene;
        if (scene == null || LaneNames.Get(scene, lane) is not { } list || index >= list.Count) return;
        var copy = Core.Serialization.PackJson.Deserialize<Beat>(PackWriter.Serialize(list[index]))!;
        shell.Edit("Duplicate beat", () => list.Insert(index + 1, copy));
        shell.Select(new BeatSelection(scene.Id!, lane, index + 1));
    }

    /// <summary>Move a beat within or between lanes (drag and drop, Ctrl+←/→).</summary>
    public void Move(string fromLane, int fromIndex, string toLane, int toIndex)
    {
        var scene = shell.Scene;
        if (scene == null || LaneNames.Get(scene, fromLane) is not { } from || fromIndex < 0 || fromIndex >= from.Count) return;
        var to = LaneNames.GetOrCreate(scene, toLane);
        if (ReferenceEquals(from, to) && (toIndex == fromIndex || toIndex == fromIndex + 1)) return;
        var beat = from[fromIndex];
        int target = toIndex;
        shell.Edit("Move beat", () =>
        {
            from.RemoveAt(fromIndex);
            if (ReferenceEquals(from, to) && toIndex > fromIndex) target--;
            to.Insert(Math.Clamp(target, 0, to.Count), beat);
        });
        shell.Select(new BeatSelection(scene.Id!, toLane, Math.Clamp(target, 0, to.Count - 1)));
    }

    public string AddBranch(string? name = null, bool select = true)
    {
        var scene = shell.Scene!;
        string id = UniqueBranch(scene, PackDocument.ToContentId(name ?? "branch"));
        shell.Edit("Add branch " + id, () => scene.Branches[id] = new List<Beat>());
        if (select) shell.Select(new BranchSelection(scene.Id!, id));
        return id;
    }

    /// <summary>Rename a branch and every Choice/Goto that points at it.</summary>
    public bool RenameBranch(string oldName, string newName)
    {
        var scene = shell.Scene;
        newName = PackDocument.ToContentId(newName);
        if (scene == null || oldName == newName || !scene.Branches.ContainsKey(oldName)) return false;
        if (scene.Branches.ContainsKey(newName) || newName is LaneNames.Main or LaneNames.Skip) { shell.Ui.Toast("Name taken", $"There's already a branch called '{newName}'.", Infrastructure.ToastKind.Warning); return false; }
        shell.Edit($"Rename branch {oldName}", () =>
        {
            var ordered = scene.Branches.ToList();
            scene.Branches.Clear();
            foreach (var (key, beats) in ordered) scene.Branches[key == oldName ? newName : key] = beats;
            foreach (var beat in AllBeats(scene)) BeatFlow.Rename(beat, oldName, newName);
        });
        shell.Select(new BranchSelection(scene.Id!, newName));
        return true;
    }

    public void DeleteBranch(string name)
    {
        var scene = shell.Scene;
        if (scene == null || !scene.Branches.TryGetValue(name, out var beats)) return;
        int uses = AllBeats(scene).Count(b => BeatFlow.Targets(b).Contains(name, StringComparer.OrdinalIgnoreCase));
        if ((beats.Count > 0 || uses > 0) && !shell.Ui.Confirm("Delete branch?", $"Delete '{name}' and its {beats.Count} beat(s)?{(uses > 0 ? $" {uses} choice, check or go-to beat(s) point at it and will show a problem until you change them." : "")}", "Delete")) return;
        shell.Edit("Delete branch " + name, () => scene.Branches.Remove(name));
        shell.Select(new SceneSelection(scene.Id!));
    }

    public static IEnumerable<Beat> AllBeats(Cutscene scene) => scene.Beats.Concat(scene.Branches.Values.SelectMany(b => b)).Concat(scene.OnSkip ?? new());

    public static string UniqueBranch(Cutscene scene, string id, string? alsoTaken = null)
    {
        string candidate = id; int n = 2;
        while (scene.Branches.ContainsKey(candidate) || candidate == alsoTaken || candidate is LaneNames.Main or LaneNames.Skip) candidate = $"{id}-{n++}";
        return candidate;
    }

    public MainViewModel Shell => shell;

    /// <summary>Palette click: insert after the selected beat, or at the end of the selected lane (main by default).</summary>
    [RelayCommand]
    private void AddFromPalette(PaletteEntry? entry)
    {
        if (entry == null || shell.Scene is not { } scene) return;
        switch (shell.Selection)
        {
            case BeatSelection s: Insert(s.Lane, s.Index + 1, entry.Kind); break;
            case BranchSelection b: Append(b.Lane, entry.Kind); break;
            default: Append(LaneNames.Main, entry.Kind); break;
        }
    }

    // Keyboard helpers for the selected beat.
    [RelayCommand] private void DeleteSelected() { if (shell.Selection is BeatSelection s) Delete(s.Lane, s.Index); else if (shell.Selection is BranchSelection b && b.Lane is not (LaneNames.Main or LaneNames.Skip)) DeleteBranch(b.Lane); }
    [RelayCommand] private void DuplicateSelected() { if (shell.Selection is BeatSelection s) Duplicate(s.Lane, s.Index); }
    [RelayCommand] private void MoveSelectedLeft() { if (shell.Selection is BeatSelection s && s.Index > 0) Move(s.Lane, s.Index, s.Lane, s.Index - 1); }
    [RelayCommand] private void MoveSelectedRight() { if (shell.Selection is BeatSelection s) Move(s.Lane, s.Index, s.Lane, s.Index + 2); }
    [RelayCommand] private void SelectPrevious() => Step(-1);
    [RelayCommand] private void SelectNext() => Step(1);
    [RelayCommand] private void AddBranchInteractive()
    {
        if (shell.Scene == null) return;
        string? name = shell.Ui.Prompt("New branch", "Branch name (what happens on this path, e.g. \"trust\")", "branch");
        if (!string.IsNullOrWhiteSpace(name)) AddBranch(name);
    }

    private void Step(int delta)
    {
        if (shell.Selection is not BeatSelection s || shell.Scene is not { } scene || LaneNames.Get(scene, s.Lane) is not { } list) return;
        int next = s.Index + delta;
        if (next >= 0 && next < list.Count) shell.Select(s with { Index = next });
    }
}

public sealed partial class LaneViewModel : ObservableObject
{
    private readonly StoryboardViewModel board;
    public LaneViewModel(StoryboardViewModel board, string name, List<Beat> beats, Cutscene scene, Dictionary<string, List<string>> incoming)
    {
        this.board = board; Name = name;
        for (int i = 0; i < beats.Count; i++) Cards.Add(new BeatCardViewModel(board, this, i, beats[i], scene));
        IncomingText = incoming.TryGetValue(name, out var from) ? "from " + string.Join(", ", from) : IsBranch ? "nothing leads here yet" : null;
        IsUnreachable = IsBranch && !incoming.ContainsKey(name);
        var last = beats.LastOrDefault();
        EndText = name switch
        {
            LaneNames.Skip => "applied when the player skips",
            LaneNames.Main => last is EndBeat or GotoBeat || BeatFlow.IsFork(last) ? null : "then the cutscene ends",
            _ => last is EndBeat or GotoBeat || BeatFlow.IsFork(last) ? null : "then continues after the beat that led here"
        };
        SelectCommand = new RelayCommand(() => board.Shell.Select(new BranchSelection(scene.Id!, Name)));
        AddCommand = new RelayCommand<PaletteEntry>(entry => { if (entry != null) board.Append(Name, entry.Kind); });
        RenameCommand = new RelayCommand(() =>
        {
            string? next = board.Shell.Ui.Prompt("Rename branch", "New name", Name);
            if (!string.IsNullOrWhiteSpace(next)) board.RenameBranch(Name, next);
        }, () => IsBranch);
        DeleteCommand = new RelayCommand(() => board.DeleteBranch(Name), () => IsBranch);
    }

    public string Name { get; }
    public string Title => LaneNames.Title(Name);
    public bool IsMain => Name == LaneNames.Main;
    public bool IsSkip => Name == LaneNames.Skip;
    public bool IsBranch => !IsMain && !IsSkip;
    public string? IncomingText { get; }
    public bool IsUnreachable { get; }
    public string? EndText { get; }
    public ObservableCollection<BeatCardViewModel> Cards { get; } = new();
    public IReadOnlyList<PaletteGroup> Palette => StoryboardViewModel.Palette;
    [ObservableProperty] private bool isSelected;
    public IRelayCommand SelectCommand { get; }
    public IRelayCommand<PaletteEntry> AddCommand { get; }
    public IRelayCommand RenameCommand { get; }
    public IRelayCommand DeleteCommand { get; }
    public StoryboardViewModel Board => board;
}

public sealed record CardLink(string Label, string Target, IRelayCommand Go);

public sealed partial class BeatCardViewModel : ObservableObject
{
    private readonly StoryboardViewModel board;
    public BeatCardViewModel(StoryboardViewModel board, LaneViewModel lane, int index, Beat beat, Cutscene scene)
    {
        this.board = board; Lane = lane; Index = index; Beat = beat;
        Kind = BeatCatalog.Kind(beat.Type);
        Summary = BeatCatalog.Summary(beat, board.Catalog);
        Detail = beat switch
        {
            SayBeat say => string.Join(" · ", new[] { say.Emotion?.ToString() is { Length: > 0 } e && e != "neutral" ? e : null, say.Portrait != null ? "portrait " + say.Portrait : null, say.Expression != null ? "expression " + say.Expression : null }.Where(x => x != null)),
            _ => ""
        };
        var links = new List<CardLink>();
        if (beat is ChoiceBeat choice)
            foreach (var option in choice.Options)
                links.Add(new CardLink(BeatCatalog.Shorten((option.Text?.Literal ?? option.Text?.I18n ?? "…")
                    + (option.When != null ? " · only if " + Conditions.Describe(option.When) : ""), 46), option.Then ?? "?", GoTo(scene, option.Then)));
        if (beat is CheckBeat check)
        {
            foreach (var @case in check.Cases)
                links.Add(new CardLink(BeatCatalog.Shorten("if " + Conditions.Describe(@case.When, id => board.Catalog?.Item(id)?.Label), 46), @case.Then ?? "?", GoTo(scene, @case.Then)));
            links.Add(new CardLink("otherwise", check.Otherwise ?? "keep going", GoTo(scene, check.Otherwise)));
        }
        if (beat is ChanceBeat chance)
            foreach (var outcome in chance.Outcomes)
                links.Add(new CardLink($"{BeatCatalog.Percent(outcome.Weight, chance.Outcomes.Select(o => o.Weight))}% chance", outcome.Then ?? "?", GoTo(scene, outcome.Then)));
        Links = links;
        SelectCommand = new RelayCommand(() => board.Shell.Select(new BeatSelection(scene.Id!, lane.Name, Index)));
        DeleteCommand = new RelayCommand(() => board.Delete(lane.Name, Index));
        DuplicateCommand = new RelayCommand(() => board.Duplicate(lane.Name, Index));
        MoveLeftCommand = new RelayCommand(() => board.Move(lane.Name, Index, lane.Name, Index - 1), () => Index > 0);
        MoveRightCommand = new RelayCommand(() => board.Move(lane.Name, Index, lane.Name, Index + 2), () => Index < lane.Cards.Count - 1);
        InsertBeforeCommand = new RelayCommand<PaletteEntry>(entry => { if (entry != null) board.Insert(lane.Name, Index, entry.Kind); });
        InsertAfterCommand = new RelayCommand<PaletteEntry>(entry => { if (entry != null) board.Insert(lane.Name, Index + 1, entry.Kind); });
        PreviewFromHereCommand = new RelayCommand(() => { board.Shell.Select(new BeatSelection(scene.Id!, lane.Name, Index)); board.Shell.PreviewFromHereCommand.Execute(null); });
        GoToCommand = beat is GotoBeat g ? GoTo(scene, g.Branch) : null;
    }

    private IRelayCommand GoTo(Cutscene scene, string? lane) => new RelayCommand(() =>
    {
        if (lane != null && scene.Branches.ContainsKey(lane)) board.Shell.Select(new BranchSelection(scene.Id!, lane));
    });

    public LaneViewModel Lane { get; }
    public int Index { get; }
    public int Number => Index + 1;
    public Beat Beat { get; }
    public BeatKind Kind { get; }
    public string Title => Kind.Name;
    public string Summary { get; }
    public string Detail { get; }
    public bool HasDetail => Detail.Length > 0;
    public SymbolRegular Icon => Kind.Icon;
    public BeatCategory Category => Kind.Category;
    public IReadOnlyList<CardLink> Links { get; }
    public bool HasLinks => Links.Count > 0;
    public bool IsEnd => Beat is EndBeat;
    public string? GoToTarget => (Beat as GotoBeat)?.Branch;
    public IReadOnlyList<PaletteGroup> Palette => StoryboardViewModel.Palette;
    [ObservableProperty] private bool isSelected;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasProblem))] private string? problemText;
    [ObservableProperty] private bool hasError;
    [ObservableProperty] private bool hasWarning;
    public bool HasProblem => ProblemText != null;
    public IRelayCommand SelectCommand { get; }
    public IRelayCommand DeleteCommand { get; }
    public IRelayCommand DuplicateCommand { get; }
    public IRelayCommand MoveLeftCommand { get; }
    public IRelayCommand MoveRightCommand { get; }
    public IRelayCommand<PaletteEntry> InsertBeforeCommand { get; }
    public IRelayCommand<PaletteEntry> InsertAfterCommand { get; }
    public IRelayCommand PreviewFromHereCommand { get; }
    public IRelayCommand? GoToCommand { get; }
    public StoryboardViewModel Board => board;
}
