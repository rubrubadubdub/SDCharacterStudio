using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Project;
using SDCharacterStudio.Editor.Schema;

namespace SDCharacterStudio.Editor.ViewModels;

/// <summary>What condition rows offer to pick from: the export's people, places and mods, the project's flags and scenes.</summary>
public sealed class ConditionContext
{
    public IReadOnlyList<ChoiceItem> Npcs { get; init; } = Array.Empty<ChoiceItem>();
    public IReadOnlyList<ChoiceItem> Locations { get; init; } = Array.Empty<ChoiceItem>();
    public IReadOnlyList<ChoiceItem> Mods { get; init; } = Array.Empty<ChoiceItem>();
    public IReadOnlyList<ChoiceItem> Flags { get; init; } = Array.Empty<ChoiceItem>();
    public IReadOnlyList<ChoiceItem> Scenes { get; init; } = Array.Empty<ChoiceItem>();
    /// <summary>Story numbers the project changes or checks (their stat keys).</summary>
    public IReadOnlyList<ChoiceItem> Numbers { get; init; } = Array.Empty<ChoiceItem>();
    /// <summary>A typed number name becomes this pack's stat: SDCS.&lt;pack&gt;.&lt;name&gt;.</summary>
    public string NumberPrefix { get; init; } = "SDCS.";
    public Func<string, string?> ItemName { get; init; } = _ => null;
    /// <summary>A name for a new story flag in this scene ("SDCS.&lt;pack&gt;.&lt;scene&gt;.flag").</summary>
    public string SuggestedFlag { get; init; } = "SDCS.flag";
    public Func<string?, string?> PickItem { get; init; } = _ => null;

    public IReadOnlyList<ChoiceItem> Options(ConditionSubject subject) => subject switch
    {
        ConditionSubject.Npc => Npcs,
        ConditionSubject.Location => Locations,
        ConditionSubject.Mod => Mods,
        ConditionSubject.Flag => Flags,
        ConditionSubject.Scene => Scenes,
        ConditionSubject.Skill => Conditions.Skills,
        ConditionSubject.Number => Numbers,
        _ => Array.Empty<ChoiceItem>(),
    };

    /// <summary>Story flags the project sets or checks (Mail beats, conditions), for the flag list.</summary>
    public static IReadOnlyList<ChoiceItem> ProjectFlags(PackDocument? document)
    {
        var flags = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var scene in document?.Cutscenes.Select(c => c.Scene) ?? Enumerable.Empty<Cutscene>())
        {
            foreach (var beat in StoryboardViewModel.AllBeats(scene))
            {
                if (beat is MailBeat mail) { if (mail.Add != null) flags.Add(mail.Add); if (mail.Remove != null) flags.Add(mail.Remove); }
                // A random item that remembers which one it gave sets a flag per item.
                if (beat is ItemBeat { Remember: true, OneOf: { } oneOf } && document != null)
                    foreach (var pick in oneOf.Where(p => p.Item != null)) flags.Add(Core.Ids.PickedItemFlag(document.PackId, scene.Id!, pick.Item!));
                if (beat is CheckBeat check) foreach (var @case in check.Cases) Collect(@case.When);
                if (beat is ChoiceBeat choice) foreach (var option in choice.Options) Collect(option.When);
                if (beat is ItemBeat { OneOf: { } picks }) foreach (var pick in picks) Collect(pick.When);
            }
            Collect(scene.Trigger?.Conditions);
        }
        return flags.Select(f => new ChoiceItem(f, Conditions.Short(f), f)).ToArray();

        void Collect(string? text)
        {
            foreach (var row in Conditions.Parse(text).Rows) if (row.Kind.Id == "flag" && row.Subject != null) flags.Add(row.Subject);
        }
    }

    /// <summary>Story numbers the project changes (Change a number beats) or compares, as stat keys labelled by name.</summary>
    public static IReadOnlyList<ChoiceItem> ProjectNumbers(PackDocument? document)
    {
        if (document == null) return Array.Empty<ChoiceItem>();
        string prefix = $"SDCS.{document.PackId}.";
        var stats = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var scene in document.Cutscenes.Select(c => c.Scene))
        {
            foreach (var beat in StoryboardViewModel.AllBeats(scene))
            {
                if (beat is NumberBeat { Name: { } name }) stats.Add(Core.Ids.NumberStat(document.PackId, name));
                if (beat is CheckBeat check) foreach (var @case in check.Cases) Collect(@case.When);
                if (beat is ChoiceBeat choice) foreach (var option in choice.Options) Collect(option.When);
                if (beat is ItemBeat { OneOf: { } picks }) foreach (var pick in picks) Collect(pick.When);
            }
            Collect(scene.Trigger?.Conditions);
        }
        return stats.Select(s => new ChoiceItem(s, s.StartsWith(prefix, StringComparison.Ordinal) ? s[prefix.Length..] : s, s)).ToArray();

        void Collect(string? text)
        {
            foreach (var row in Conditions.Parse(text).Rows) if (row.Kind.Id == "number" && row.Subject != null) stats.Add(row.Subject);
        }
    }

    /// <summary>The project's scenes as the game knows them (pack ID and scene ID), for "has seen a scene".</summary>
    public static IReadOnlyList<ChoiceItem> ProjectScenes(PackDocument? document) => document?.Cutscenes
        .Select(c => new ChoiceItem($"{document.PackId}.{c.Scene.Id}", c.Scene.Title ?? c.Scene.Id ?? "?", c.Scene.Id)).ToArray() ?? Array.Empty<ChoiceItem>();
}

/// <summary>
/// Conditions, all or any of which must be true, edited as rows (D-51). Every change writes the group back as the game's
/// condition text through <c>save</c>.
/// </summary>
public sealed partial class ConditionGroupViewModel : ObservableObject
{
    private readonly ConditionGroup group;
    private readonly Action<string?, string, bool> save;
    public ConditionGroupViewModel(string? text, ConditionContext context, Action<string?, string, bool> save)
    {
        group = Conditions.Parse(text); Context = context; this.save = save;
        foreach (var row in group.Rows) Rows.Add(new ConditionRowViewModel(this, row));
        AddOptions = Conditions.Kinds.Select(k => new ChoiceItem(k.Id, k.Label, k.Category)).ToArray();
    }

    public ConditionContext Context { get; }
    public ObservableCollection<ConditionRowViewModel> Rows { get; } = new();
    public bool HasRows => Rows.Count > 0;
    public bool HasSeveral => Rows.Count > 1;
    public IReadOnlyList<ChoiceItem> AddOptions { get; }

    public bool IsAll { get => !group.Any; set { if (value && group.Any) { group.Any = false; Changed("All of these", false); } } }
    public bool IsAny { get => group.Any; set { if (value && !group.Any) { group.Any = true; Changed("Any of these", false); } } }

    /// <summary>Picking from "Add a condition" adds a row of that kind.</summary>
    public ChoiceItem? AddChoice
    {
        get => null;
        set
        {
            if (value?.Value is not { } id) return;
            var kind = Conditions.Kind(id);
            // Every new row says something whole, so it reads back as the same row: a person, a flag, an item to change.
            string? subject = kind.Subject switch
            {
                ConditionSubject.Flag => Context.Flags.FirstOrDefault()?.Value ?? Context.SuggestedFlag,
                ConditionSubject.Number => Context.Numbers.FirstOrDefault()?.Value ?? Context.NumberPrefix + "number",
                ConditionSubject.Item => "(O)74",
                ConditionSubject.Npc or ConditionSubject.Location or ConditionSubject.Mod or ConditionSubject.Scene => Context.Options(kind.Subject).FirstOrDefault()?.Value ?? "?",
                _ => null,
            };
            group.Rows.Add(Conditions.NewRow(kind, subject));
            Changed("Add a condition", true);
        }
    }

    /// <summary>The group as the game's text (power users can paste or edit it).</summary>
    public string? QueryText
    {
        get => Conditions.Print(group);
        set
        {
            if (string.Equals(value?.Trim(), QueryText, StringComparison.Ordinal)) return;
            save(string.IsNullOrWhiteSpace(value) ? null : value.Trim(), "Edit the game query", true);
        }
    }

    internal void Remove(ConditionRowViewModel row) { group.Rows.Remove(row.Row); Changed("Remove a condition", true); }

    internal void Changed(string label, bool rebuild)
    {
        save(Conditions.Print(group), label, rebuild);
        OnPropertyChanged(nameof(QueryText)); OnPropertyChanged(nameof(IsAll)); OnPropertyChanged(nameof(IsAny));
    }
}

/// <summary>One condition row: what it checks, about whom, how it compares, and the value.</summary>
public sealed partial class ConditionRowViewModel : ObservableObject
{
    private readonly ConditionGroupViewModel group;
    public ConditionRowViewModel(ConditionGroupViewModel group, ConditionRow row)
    {
        this.group = group; Row = row;
        SubjectOptions = group.Context.Options(row.Kind.Subject);
        if (row.Subject != null && SubjectOptions.Count > 0 && !SubjectOptions.Any(o => string.Equals(o.Value, row.Subject, StringComparison.OrdinalIgnoreCase)))
            SubjectOptions = SubjectOptions.Prepend(new ChoiceItem(row.Subject, row.Subject, "not in the list")).ToArray();
        CompareOptions = Conditions.CompareOptions(row.Kind);
        ValueOptions = row.Kind.Values ?? Array.Empty<ChoiceItem>();
        RemoveCommand = new RelayCommand(() => group.Remove(this));
        PickItemCommand = new RelayCommand(() => { if (group.Context.PickItem(Row.Subject) is { } id) SubjectValue = id; });
    }

    public ConditionRow Row { get; }
    public string KindLabel => Row.Kind.Label;
    public string Category => Row.Kind.Category;
    public bool IsRaw => Row.Kind.Shape == ConditionShape.Raw;
    public string? Hint => Row.Kind.Hint;

    // ---- subject ----
    public bool HasSubject => Row.Kind.Subject != ConditionSubject.None;
    public bool SubjectIsItem => Row.Kind.Subject == ConditionSubject.Item;
    /// <summary>Flags are typed or picked: new ones are made by naming them.</summary>
    public bool SubjectIsTyped => Row.Kind.Subject is ConditionSubject.Flag or ConditionSubject.Number;
    public bool SubjectIsList => HasSubject && !SubjectIsItem && !SubjectIsTyped;
    public bool SubjectIsLong => SubjectIsList && SubjectOptions.Count > 40;
    public bool SubjectIsShort => SubjectIsList && !SubjectIsLong;
    public IReadOnlyList<ChoiceItem> SubjectOptions { get; }
    public ChoiceItem? Subject
    {
        get => SubjectOptions.FirstOrDefault(o => string.Equals(o.Value, Row.Subject, StringComparison.OrdinalIgnoreCase));
        set { if (value != null) SubjectValue = value.Value; }
    }
    public string? SubjectValue
    {
        get => Row.Subject;
        set
        {
            string? next = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            // A typed story number name is this pack's number.
            if (Row.Kind.Subject == ConditionSubject.Number && next != null && !next.StartsWith("SDCS.", StringComparison.Ordinal))
                next = group.Context.NumberPrefix + PackDocument.ToContentId(next);
            if (next == Row.Subject) return;
            Row.Subject = next; group.Changed("Change " + KindLabel, false);
            OnPropertyChanged(nameof(Subject)); OnPropertyChanged(nameof(SubjectValue)); OnPropertyChanged(nameof(ItemLabel));
        }
    }
    public string ItemLabel => Row.Subject == null ? "choose an item" : group.Context.ItemName(Row.Subject) is { } name ? $"{name}  {Row.Subject}" : Row.Subject;

    // ---- comparison ----
    public bool HasCompare => CompareOptions.Count > 0;
    public IReadOnlyList<ChoiceItem> CompareOptions { get; }
    public ChoiceItem? Compare
    {
        get => CompareOptions.FirstOrDefault(o => o.Value == Row.Compare);
        set
        {
            if (value?.Value == null || value.Value == Row.Compare) return;
            Row.Compare = value.Value;
            if (Row.Compare == "between" && Row.Value2 == null) Row.Value2 = Row.Value;
            group.Changed("Change " + KindLabel, false);
            OnPropertyChanged(nameof(IsBetween));
        }
    }

    // ---- value ----
    public bool HasValueOptions => ValueOptions.Count > 0;
    public bool HasValueText => !HasValueOptions && Row.Kind.Shape is ConditionShape.Range or ConditionShape.Chance or ConditionShape.Values;
    public IReadOnlyList<ChoiceItem> ValueOptions { get; }
    public ChoiceItem? ValueChoice
    {
        get => ValueOptions.FirstOrDefault(o => o.Value == Row.Value?.Split(' ')[0]);
        set { if (value != null) Value = value.Value; }
    }
    public string? Value
    {
        get => Row.Value;
        set
        {
            if (value == Row.Value) return;
            Row.Value = value; group.Changed("Change " + KindLabel, false);
            OnPropertyChanged(); OnPropertyChanged(nameof(ValueChoice));
        }
    }
    public bool IsBetween => Row.Kind.Shape == ConditionShape.Range && Row.Compare == "between";
    public string? Value2
    {
        get => Row.Value2;
        set { if (value == Row.Value2) return; Row.Value2 = value; group.Changed("Change " + KindLabel, false); OnPropertyChanged(); }
    }
    public string? Unit => Row.Kind.Unit;
    public bool HasUnit => Unit != null;

    public string? Raw
    {
        get => Row.Raw;
        set { if (value == Row.Raw) return; Row.Raw = value; group.Changed("Edit the game query", false); OnPropertyChanged(); }
    }

    public IRelayCommand RemoveCommand { get; }
    public IRelayCommand PickItemCommand { get; }
}

/// <summary>Conditions as one field (the scene's "only when" conditions).</summary>
public sealed class ConditionGroupField : FieldViewModel
{
    public ConditionGroupField(string label, string key, Func<string?> get, Action<string?> set, ConditionContext context, FieldCommit commit) : base(label, key)
        => Group = new ConditionGroupViewModel(get(), context, (text, what, rebuild) => commit(what, () => set(text), rebuild));
    public ConditionGroupViewModel Group { get; }
    public string? EmptyText { get; init; }
}

/// <summary>A Check beat's cases, each its conditions and the branch it leads to, then what happens otherwise (D-51).</summary>
public sealed partial class CheckField : FieldViewModel
{
    public const string NewBranch = "\u0001new";
    private readonly Cutscene scene;
    private readonly CheckBeat check;
    private readonly FieldCommit commit;

    public CheckField(Cutscene scene, CheckBeat check, ConditionContext context, FieldCommit commit) : base("", "Cases")
    {
        this.scene = scene; this.check = check; this.commit = commit;
        for (int i = 0; i < check.Cases.Count; i++) Cases.Add(new CheckCaseViewModel(this, i, check.Cases[i], context));
        AddCaseCommand = new RelayCommand(() =>
        {
            string branch = StoryboardViewModel.UniqueBranch(scene, "case-" + (check.Cases.Count + 1));
            commit("Add a case", () => { scene.Branches[branch] = new List<Beat>(); check.Cases.Add(new CheckCase { When = DefaultCondition(scene, context), Then = branch }); }, true);
        });
    }

    public ObservableCollection<CheckCaseViewModel> Cases { get; } = new();
    public IRelayCommand AddCaseCommand { get; }

    /// <summary>A new case starts from hearts with the scene's first NPC (or the first character the export knows).</summary>
    public static string DefaultCondition(Cutscene scene, ConditionContext? context = null)
    {
        string who = scene.Actors.FirstOrDefault(a => a.Kind == "Npc")?.Npc ?? scene.Actors.FirstOrDefault(a => a.Kind == "Npc")?.Id
            ?? context?.Npcs.FirstOrDefault()?.Value ?? "Abigail";
        var group = new ConditionGroup();
        group.Rows.Add(Conditions.NewRow(Conditions.Kind("hearts"), who));
        return Conditions.Print(group)!;
    }

    internal IReadOnlyList<ChoiceItem> BranchOptions(bool keepGoing) =>
        (keepGoing ? new[] { new ChoiceItem(null, "keep going", "the beats after the Check") } : Array.Empty<ChoiceItem>())
        .Concat(scene.Branches.Keys.Select(b => new ChoiceItem(b, "→ " + b)))
        .Append(new ChoiceItem(NewBranch, "→ ＋ new branch")).ToArray();

    public IReadOnlyList<ChoiceItem> OtherwiseOptions => BranchOptions(true);
    public ChoiceItem? Otherwise
    {
        get => OtherwiseOptions.FirstOrDefault(o => string.Equals(o.Value, check.Otherwise, StringComparison.OrdinalIgnoreCase));
        set
        {
            if (value == null || string.Equals(value.Value, check.Otherwise, StringComparison.OrdinalIgnoreCase)) return;
            commit("Change otherwise", () => check.Otherwise = value.Value == NewBranch ? NewBranchNamed("otherwise") : value.Value, true);
        }
    }

    internal string NewBranchNamed(string hint)
    {
        string id = StoryboardViewModel.UniqueBranch(scene, PackDocument.ToContentId(hint));
        scene.Branches[id] = new List<Beat>();
        return id;
    }

    internal void Commit(string label, Action apply, bool rebuild) => commit(label, apply, rebuild);
    internal CheckBeat Check => check;
}

/// <summary>One case: "If all/any of these are true → go to a branch".</summary>
public sealed partial class CheckCaseViewModel : ObservableObject
{
    private readonly CheckField field;
    private readonly CheckCase @case;
    public CheckCaseViewModel(CheckField field, int index, CheckCase @case, ConditionContext context)
    {
        this.field = field; this.@case = @case; Index = index;
        Group = new ConditionGroupViewModel(@case.When, context, (text, what, rebuild) => field.Commit(what, () => @case.When = text, rebuild));
        RemoveCommand = new RelayCommand(() => field.Commit("Remove a case", () => field.Check.Cases.Remove(@case), true), () => field.Check.Cases.Count > 1);
        MoveUpCommand = new RelayCommand(() => field.Commit("Move a case up", () => { field.Check.Cases.RemoveAt(Index); field.Check.Cases.Insert(Index - 1, @case); }, true), () => Index > 0);
    }

    public int Index { get; }
    public string Title => Index == 0 ? "If" : "Otherwise, if";
    public ConditionGroupViewModel Group { get; }
    public IReadOnlyList<ChoiceItem> TargetOptions => field.BranchOptions(false);
    public ChoiceItem? Target
    {
        get => TargetOptions.FirstOrDefault(o => string.Equals(o.Value, @case.Then, StringComparison.OrdinalIgnoreCase));
        set
        {
            if (value?.Value == null || string.Equals(value.Value, @case.Then, StringComparison.OrdinalIgnoreCase)) return;
            field.Commit("Change where the case goes", () => @case.Then = value.Value == CheckField.NewBranch
                ? field.NewBranchNamed(string.Join('-', Conditions.Describe(@case.When).Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(3))) : value.Value, true);
        }
    }
    public IRelayCommand RemoveCommand { get; }
    public IRelayCommand MoveUpCommand { get; }
}
