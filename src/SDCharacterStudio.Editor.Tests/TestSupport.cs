using System.IO;
using System.Runtime.CompilerServices;
using SDCharacterStudio.Editor.Catalog;
using SDCharacterStudio.Editor.Infrastructure;
using SDCharacterStudio.Editor.Project;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Tests;

internal static partial class TestEnvironment
{
    /// <summary>Keep editor settings, previews and crash logs out of the real %APPDATA% while tests run.</summary>
    [ModuleInitializer]
    internal static void Init() =>
        Environment.SetEnvironmentVariable("SDCS_EDITOR_HOME", Path.Combine(Path.GetTempPath(), "sdcs-editor-tests", "home-" + Environment.ProcessId));

    public static string TestData => Path.Combine(AppContext.BaseDirectory, "TestData");
    public static string MiniExport => Path.Combine(TestData, "Export", "mini");

    /// <summary>A complete example pack under TestData/Examples, when there is one (null otherwise).</summary>
    public static string? ExamplePack =>
        Directory.Exists(Path.Combine(TestData, "Examples"))
            ? Directory.EnumerateDirectories(Path.Combine(TestData, "Examples")).FirstOrDefault(d => File.Exists(Path.Combine(d, "manifest.json")))
            : null;

    /// <summary>The repository root (for optional fixtures that aren't committed, like the native dev exports).</summary>
    public static string? RepoRoot
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "src", "SDCharacterStudio.sln"))) return dir.FullName;
            return null;
        }
    }

    public static ExportCatalog LoadMini() => ExportCatalog.Load(MiniExport);
}

/// <summary>A temporary folder deleted after the test.</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sdcs-editor-tests", Guid.NewGuid().ToString("N")[..10]); Directory.CreateDirectory(Path); }
    public string Path { get; }
    public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
}

/// <summary>Scripted stand-in for the window system: answers prompts from queues and records toasts.</summary>
internal sealed class FakeUi : IUserInterface
{
    private readonly Dictionary<string, Action> pending = new();
    public Queue<string?> Prompts { get; } = new();
    public Queue<string?> Folders { get; } = new();
    public Queue<string?> OpenFiles { get; } = new();
    public List<(string Title, string Message, ToastKind Kind)> Toasts { get; } = new();
    public bool ConfirmAnswer { get; set; } = true;
    public bool? AskAnswer { get; set; } = false;
    /// <summary>Called for every dialog; return true to accept. Default: cancel.</summary>
    public Func<object, bool> Dialogs { get; set; } = _ => false;

    public void Toast(string title, string message, ToastKind kind = ToastKind.Info) => Toasts.Add((title, message, kind));
    public bool Confirm(string title, string message, string confirm = "OK", string cancel = "Cancel") => ConfirmAnswer;
    public bool? Ask(string title, string message, string yes, string no) => AskAnswer;
    public string? Prompt(string title, string label, string initial, Func<string, string?>? validate = null) => Prompts.Count > 0 ? Prompts.Dequeue() : null;
    public string? OpenFile(string title, string filter, string? folder = null) => OpenFiles.Count > 0 ? OpenFiles.Dequeue() : null;
    public string? OpenFolder(string title, string? folder = null) => Folders.Count > 0 ? Folders.Dequeue() : null;
    public string? SaveFile(string title, string filter, string fileName, string? folder = null) => null;
    public bool ShowDialog(object viewModel) => Dialogs(viewModel);
    public Action? PendingFlush { get; set; }
    public void FlushPendingEdits() => PendingFlush?.Invoke();
    public void Post(Action action) => action();
    public void Debounce(string key, TimeSpan delay, Action action) => pending[key] = action;

    /// <summary>Run debounced work now (validation, search).</summary>
    public void RunPending()
    {
        var actions = pending.Values.ToArray();
        pending.Clear();
        foreach (var action in actions) action();
    }
}

/// <summary>An editor shell wired to a fake UI, the mini export and a temporary projects folder.</summary>
internal sealed class EditorHarness : IDisposable
{
    private readonly TempFolder temp = new();
    public FakeUi Ui { get; } = new();
    public MainViewModel Shell { get; }
    public string Folder => temp.Path;

    public EditorHarness(string? exportProfile = null, string? pipeName = null)
    {
        var settings = new EditorSettings
        {
            ExportFolder = Path.GetDirectoryName(exportProfile ?? TestEnvironment.MiniExport)!,
            ExportProfile = exportProfile ?? TestEnvironment.MiniExport,
            ProjectsFolder = temp.Path, GameModsFolder = null, Author = "Player", PreviewFolderOverride = Path.Combine(temp.Path, ".preview"),
            PipeName = pipeName ?? "SDCS.EditorTests." + Guid.NewGuid().ToString("N")
        };
        Shell = new MainViewModel(Ui, settings);
        Shell.LoadCatalog();
    }

    /// <summary>File › New project, through the dialog view model.</summary>
    public PackDocument NewProject(string name = "Test Pack", string id = "Player.TestPack", string location = "Town", string firstScene = "First scene")
    {
        var dialog = new NewProjectViewModel(Shell) { Name = name, Author = "Player", UniqueId = id, FirstScene = firstScene, Location = location, ParentFolder = Folder };
        dialog.CreateCommand.Execute(null);
        Assert.Null(dialog.Error);
        Shell.Attach(dialog.Created!);
        return dialog.Created!;
    }

    public T Field<T>(string label) where T : FieldViewModel =>
        Shell.Inspector.Fields.OfType<T>().FirstOrDefault(f => f.Label == label)
        ?? throw new Xunit.Sdk.XunitException($"No {typeof(T).Name} '{label}' in the inspector. Fields: {string.Join(", ", Shell.Inspector.Fields.Select(f => f.GetType().Name + ":" + f.Label))}");

    public void Choose(string label, string? value)
    {
        var field = Field<ChoiceField>(label);
        var option = field.Options.FirstOrDefault(o => o.Value == value);
        if (option != null) field.Selected = option;
        else field.Text = value;
    }

    public void Dispose() { Shell.Dispose(); temp.Dispose(); }
}
