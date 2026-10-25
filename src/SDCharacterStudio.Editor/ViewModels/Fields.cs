using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SDCharacterStudio.Editor.ViewModels;

/// <summary>Applies a field's change as one undo step. <paramref name="rebuild"/> redraws the inspector (fields depend on it).</summary>
public delegate void FieldCommit(string label, Action apply, bool rebuild = false);

/// <summary>One editable property in the inspector (doc 07 §5). Problems show inline under the field.</summary>
public abstract partial class FieldViewModel : ObservableObject
{
    protected FieldViewModel(string label, string key) { Label = label; Key = key; }
    public string Label { get; }
    /// <summary>The JSON property path below the selected object that this field edits ("Text", "To.Tile").</summary>
    public string Key { get; }
    public string? Help { get; init; }
    [ObservableProperty] private string? problem;
    [ObservableProperty] private bool problemIsError;
    public bool HasLabel => Label.Length > 0;
}

public sealed partial class SectionField : FieldViewModel
{
    public SectionField(string title, string? note = null) : base(title, "") { Note = note; }
    public string? Note { get; }
}

public sealed partial class NoteField : FieldViewModel
{
    public NoteField(string text, string? actionLabel = null, Action? action = null, bool warning = false) : base("", "")
    { Text = text; ActionLabel = actionLabel; IsWarning = warning; if (action != null) Action = new RelayCommand(action); }
    public string Text { get; }
    public string? ActionLabel { get; }
    public IRelayCommand? Action { get; }
    public bool IsWarning { get; }
}

public sealed partial class ActionField : FieldViewModel
{
    public ActionField(string label, Action action, bool primary = false) : base("", "") { ButtonText = label; Action = new RelayCommand(action); IsPrimary = primary; }
    public string ButtonText { get; }
    public IRelayCommand Action { get; }
    public bool IsPrimary { get; }
}

public sealed partial class TextField : FieldViewModel
{
    private readonly Func<string?> get;
    private readonly Action<string?> set;
    private readonly FieldCommit commit;
    public TextField(string label, string key, Func<string?> get, Action<string?> set, FieldCommit commit) : base(label, key)
    { this.get = get; this.set = set; this.commit = commit; }
    public bool Multiline { get; init; }
    public string? Placeholder { get; init; }
    public bool Rebuild { get; init; }
    public string? Value
    {
        get => get();
        set
        {
            string? normalized = string.IsNullOrEmpty(value) && AllowNull ? null : value;
            if (normalized == get()) return;
            commit("Edit " + Label.ToLowerInvariant(), () => set(normalized), Rebuild);
            OnPropertyChanged();
        }
    }
    public bool AllowNull { get; init; } = true;
}

/// <summary>Dialogue text: a literal line, or a translation key into i18n/&lt;lang&gt;.json (doc 03 §5.3).</summary>
public sealed partial class TranslatableTextField : FieldViewModel
{
    private readonly Func<Core.Models.TextValue?> get;
    private readonly Action<Core.Models.TextValue?> set;
    private readonly FieldCommit commit;
    public TranslatableTextField(string label, string key, Func<Core.Models.TextValue?> get, Action<Core.Models.TextValue?> set, FieldCommit commit) : base(label, key)
    { this.get = get; this.set = set; this.commit = commit; }
    public bool Multiline { get; init; } = true;
    public string Placeholder { get; init; } = "What they say. Ctrl+Enter commits.";
    public bool UseKey
    {
        get => get()?.I18n != null;
        set
        {
            if (value == UseKey) return;
            var current = get();
            commit(value ? "Use a translation key" : "Use literal text", () => set(value ? new Core.Models.TextValue { I18n = SuggestKey(current?.Literal) } : new Core.Models.TextValue { Literal = "" }), true);
        }
    }
    public string? Text
    {
        get => get()?.Literal ?? get()?.I18n;
        set
        {
            var current = get();
            string next = value ?? "";
            if (UseKey ? current?.I18n == next : current?.Literal == next) return;
            commit("Edit " + Label.ToLowerInvariant(), () => set(UseKey ? new Core.Models.TextValue { I18n = next } : new Core.Models.TextValue { Literal = next }));
            OnPropertyChanged();
        }
    }
    private static string SuggestKey(string? text) => string.IsNullOrWhiteSpace(text) ? "line" : Project.PackDocument.ToContentId(string.Join('-', text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(4)));
}

public sealed partial class NumberField : FieldViewModel
{
    private readonly Func<int?> get;
    private readonly Action<int?> set;
    private readonly FieldCommit commit;
    public NumberField(string label, string key, Func<int?> get, Action<int?> set, FieldCommit commit) : base(label, key)
    { this.get = get; this.set = set; this.commit = commit; }
    public int Minimum { get; init; } = int.MinValue;
    public int Maximum { get; init; } = int.MaxValue;
    public string? Suffix { get; init; }
    public bool Nullable { get; init; }
    public int Step { get; init; } = 1;
    public double? Value
    {
        get => get();
        set
        {
            int? next = value is { } v && !double.IsNaN(v) ? (int)Math.Clamp(Math.Round(v), Minimum, Maximum) : Nullable ? null : get();
            if (next == get()) return;
            commit("Edit " + Label.ToLowerInvariant(), () => set(next), Rebuild);
            OnPropertyChanged();
            OnPropertyChanged(nameof(Hint));
        }
    }
    /// <summary>Optional live explanation of the number, e.g. "= 1 heart".</summary>
    public Func<int?, string?>? Describe { get; init; }
    /// <summary>Redraw the inspector after a change (other fields depend on it, e.g. chances as percentages).</summary>
    public bool Rebuild { get; init; }
    public string? Hint => Describe?.Invoke(get());
}

public sealed partial class BoolField : FieldViewModel
{
    private readonly Func<bool> get;
    private readonly Action<bool> set;
    private readonly FieldCommit commit;
    public BoolField(string label, string key, Func<bool> get, Action<bool> set, FieldCommit commit) : base(label, key)
    { this.get = get; this.set = set; this.commit = commit; }
    public string? Description { get; init; }
    public bool Rebuild { get; init; }
    public bool Value
    {
        get => get();
        set { if (value == get()) return; commit((value ? "Turn on " : "Turn off ") + Label.ToLowerInvariant(), () => set(value), Rebuild); OnPropertyChanged(); }
    }
}

public sealed record ChoiceItem(string? Value, string Label, string? Detail = null, ImageSource? Image = null)
{
    public override string ToString() => Label;
}

/// <summary>Pick one of a list (NPCs, branches, cues…). Editable choices also accept typed values.</summary>
public sealed partial class ChoiceField : FieldViewModel
{
    private readonly Func<string?> get;
    private readonly Action<string?> set;
    private readonly FieldCommit commit;
    public ChoiceField(string label, string key, IEnumerable<ChoiceItem> options, Func<string?> get, Action<string?> set, FieldCommit commit) : base(label, key)
    {
        this.get = get; this.set = set; this.commit = commit;
        Options = new ObservableCollection<ChoiceItem>(options);
        if (get() is { } current && !Options.Any(o => string.Equals(o.Value, current, StringComparison.Ordinal)))
            Options.Insert(0, new ChoiceItem(current, current, "not in the list"));
    }
    public ObservableCollection<ChoiceItem> Options { get; }
    public bool IsEditable { get; init; }
    public bool Rebuild { get; init; }
    /// <summary>A long fixed list shows a searchable picker instead of a plain drop-down.</summary>
    public bool IsLong => !IsEditable && Options.Count > 12;
    public ChoiceItem? Selected
    {
        get => Options.FirstOrDefault(o => string.Equals(o.Value, get(), StringComparison.Ordinal));
        set
        {
            if (value == null || string.Equals(value.Value, get(), StringComparison.Ordinal)) return;
            commit("Change " + Label.ToLowerInvariant(), () => set(value.Value), Rebuild);
            OnPropertyChanged(); OnPropertyChanged(nameof(Text));
        }
    }
    /// <summary>Typed text for editable choices (e.g. a sound cue the export doesn't list).</summary>
    public string? Text
    {
        get => Selected?.Label ?? get();
        set
        {
            // Committing the displayed label must preserve the selected ID even when another option has that label.
            if (!IsEditable || value == null || string.Equals(value, Text, StringComparison.Ordinal)) return;
            var match = Options.FirstOrDefault(o => string.Equals(o.Label, value, StringComparison.OrdinalIgnoreCase) || string.Equals(o.Value, value, StringComparison.OrdinalIgnoreCase));
            // A matching option wins even when its value is null ("Their usual look"); otherwise the typed text is the value.
            string? next = match != null ? match.Value : value.Trim().Length == 0 ? null : value.Trim();
            if (string.Equals(next, get(), StringComparison.Ordinal)) return;
            commit("Change " + Label.ToLowerInvariant(), () => set(next), Rebuild);
            OnPropertyChanged(); OnPropertyChanged(nameof(Selected));
        }
    }
}

/// <summary>A tile on the stage (+ optional pixel offset), with "pick on stage".</summary>
public sealed partial class TileField : FieldViewModel
{
    private readonly Func<int[]?> getTile;
    private readonly Action<int[]> setTile;
    private readonly Func<int[]?>? getPx;
    private readonly Action<int[]>? setPx;
    private readonly FieldCommit commit;
    public TileField(string label, string key, Func<int[]?> getTile, Action<int[]> setTile, FieldCommit commit, Action<TileField>? pick,
        Func<int[]?>? getPx = null, Action<int[]>? setPx = null) : base(label, key)
    {
        this.getTile = getTile; this.setTile = setTile; this.commit = commit; this.getPx = getPx; this.setPx = setPx;
        if (pick != null) PickCommand = new RelayCommand(() => pick(this));
    }
    public IRelayCommand? PickCommand { get; }
    public bool HasPixels => getPx != null;
    public double X { get => getTile()?[0] ?? 0; set => SetTile((int)Math.Round(value), (int)Y); }
    public double Y { get => getTile()?[1] ?? 0; set => SetTile((int)X, (int)Math.Round(value)); }
    public double PxX { get => getPx?.Invoke()?[0] ?? 0; set => SetPx((int)Math.Round(value), (int)PxY); }
    public double PxY { get => getPx?.Invoke()?[1] ?? 0; set => SetPx((int)PxX, (int)Math.Round(value)); }
    public void SetTile(int x, int y)
    {
        if (getTile() is { Length: 2 } t && t[0] == x && t[1] == y) return;
        commit("Move " + Label.ToLowerInvariant(), () => setTile(new[] { x, y }));
        OnPropertyChanged(nameof(X)); OnPropertyChanged(nameof(Y));
    }
    private void SetPx(int x, int y)
    {
        if (setPx == null || (getPx?.Invoke() is { Length: 2 } p && p[0] == x && p[1] == y)) return;
        commit("Nudge " + Label.ToLowerInvariant(), () => setPx(new[] { x, y }));
        OnPropertyChanged(nameof(PxX)); OnPropertyChanged(nameof(PxY));
    }
}

/// <summary>An item from the game (with icon), chosen from the export's item list.</summary>
public sealed partial class ItemField : FieldViewModel
{
    private readonly Func<string?> get;
    private readonly Action<string?> set;
    private readonly FieldCommit commit;
    private readonly Func<string?, (string Label, ImageSource? Icon)> describe;
    public ItemField(string label, string key, Func<string?> get, Action<string?> set, FieldCommit commit, Func<string?, (string, ImageSource?)> describe, Func<string?, string?> browse) : base(label, key)
    {
        this.get = get; this.set = set; this.commit = commit; this.describe = describe;
        BrowseCommand = new RelayCommand(() => { if (browse(get()) is { } chosen) Value = chosen; });
    }
    public IRelayCommand BrowseCommand { get; }
    public string? Value
    {
        get => get();
        set
        {
            string? next = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (next == get()) return;
            commit("Change " + Label.ToLowerInvariant(), () => set(next));
            OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); OnPropertyChanged(nameof(Icon));
        }
    }
    public string DisplayName => describe(get()).Label;
    public ImageSource? Icon => describe(get()).Icon;
}

/// <summary>A list editor whose rows each edit part of the model (choice options, costume parts, preconditions…).</summary>
public sealed partial class ListField : FieldViewModel
{
    public ListField(string label, string key, string addLabel, Action add) : base(label, key)
    { AddLabel = addLabel; AddCommand = new RelayCommand(add); }
    public ObservableCollection<ListRowViewModel> Rows { get; } = new();
    public string AddLabel { get; }
    public IRelayCommand AddCommand { get; }
    public string? EmptyText { get; init; }
}

/// <summary>One row: a few inline editors plus remove / move.</summary>
public sealed partial class ListRowViewModel : ObservableObject
{
    public ListRowViewModel(Action remove, Action? moveUp = null, Action? moveDown = null)
    {
        RemoveCommand = new RelayCommand(remove);
        if (moveUp != null) MoveUpCommand = new RelayCommand(moveUp);
        if (moveDown != null) MoveDownCommand = new RelayCommand(moveDown);
    }
    public ObservableCollection<FieldViewModel> Cells { get; } = new();
    public IRelayCommand RemoveCommand { get; }
    public IRelayCommand? MoveUpCommand { get; }
    public IRelayCommand? MoveDownCommand { get; }
    public string? Caption { get; init; }
}
