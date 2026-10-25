using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDCharacterStudio.Editor.Export;

namespace SDCharacterStudio.Editor.ViewModels;

/// <summary>The Problems panel (doc 07 §12): every validation result, with jump-to and one-click fixes.</summary>
public sealed partial class ProblemsViewModel : ObservableObject
{
    private readonly MainViewModel shell;
    private IReadOnlyList<Problem> all = Array.Empty<Problem>();
    public ProblemsViewModel(MainViewModel shell) => this.shell = shell;

    public ObservableCollection<ProblemItemViewModel> Items { get; } = new();
    [ObservableProperty] private int errorCount;
    [ObservableProperty] private int warningCount;
    [ObservableProperty] private bool currentCutsceneOnly;
    /// <summary>Set when something wants the Problems tab shown (e.g. a blocked preview).</summary>
    [ObservableProperty] private bool isOpenRequested;
    public string Summary => ErrorCount == 0 && WarningCount == 0 ? "No problems" : $"{ErrorCount} error{(ErrorCount == 1 ? "" : "s")}, {WarningCount} warning{(WarningCount == 1 ? "" : "s")}";
    public string TabHeader => ErrorCount > 0 ? $"Problems ({ErrorCount})" : WarningCount > 0 ? $"Problems ({WarningCount})" : "Problems";

    public void Update(IReadOnlyList<Problem> problems)
    {
        all = problems;
        ErrorCount = problems.Count(p => p.IsError);
        WarningCount = problems.Count - ErrorCount;
        Refill();
        OnPropertyChanged(nameof(Summary)); OnPropertyChanged(nameof(TabHeader));
    }

    partial void OnCurrentCutsceneOnlyChanged(bool value) => Refill();

    private void Refill()
    {
        Items.Clear();
        string? current = shell.Current?.File;
        foreach (var problem in all.Where(p => !CurrentCutsceneOnly || string.Equals(p.File, current, StringComparison.OrdinalIgnoreCase)))
            Items.Add(new ProblemItemViewModel(problem, shell));
    }
}

public sealed partial class ProblemItemViewModel : ObservableObject
{
    public ProblemItemViewModel(Problem problem, MainViewModel shell)
    {
        Problem = problem;
        JumpCommand = new RelayCommand(() => shell.Navigate(problem));
        if (problem.FixLabel != null) FixCommand = new RelayCommand(() => shell.Fix(problem));
    }
    public Problem Problem { get; }
    public bool IsError => Problem.IsError;
    public string Message => Problem.Message;
    public string Location => Problem.Location;
    public string? Rule => Problem.Rule;
    public string? FixLabel => Problem.FixLabel;
    public IRelayCommand JumpCommand { get; }
    public IRelayCommand? FixCommand { get; }
}

public sealed record OutputLine(DateTime Time, string Level, string Text)
{
    public string Stamp => Time.ToString("HH:mm:ss");
    public bool IsError => Level.Equals("Error", StringComparison.OrdinalIgnoreCase);
    public bool IsWarning => Level.StartsWith("Warn", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Bridge, Runtime and editor messages (doc 07 §9).</summary>
public sealed partial class OutputViewModel : ObservableObject
{
    public ObservableCollection<OutputLine> Lines { get; } = new();
    [ObservableProperty] private int unread;
    public void Add(string level, string text)
    {
        Lines.Add(new OutputLine(DateTime.Now, level, text));
        while (Lines.Count > 500) Lines.RemoveAt(0);
        Unread++;
    }
    [RelayCommand] private void Clear() { Lines.Clear(); Unread = 0; }
}
