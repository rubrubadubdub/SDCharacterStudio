using System.IO;
using Newtonsoft.Json;

namespace SDCharacterStudio.Editor.Infrastructure;

/// <summary>Per-user editor settings, in %APPDATA%\SDCharacterStudio\editor.json.</summary>
public sealed class EditorSettings
{
    /// <summary>Set SDCS_EDITOR_HOME to keep settings, previews and crash logs in one folder (tests, side-by-side installs).</summary>
    private static string? Home => Environment.GetEnvironmentVariable("SDCS_EDITOR_HOME") is { Length: > 0 } home ? home : null;
    public static string AppDataFolder => Home is { } home ? Path.Combine(home, "AppData") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SDCharacterStudio");
    public static string LocalFolder => Home is { } home ? Path.Combine(home, "Local") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SDCharacterStudio");
    private static string FilePath => Path.Combine(AppDataFolder, "editor.json");

    /// <summary>The Bridge's export folder (doc 06 §1). Profiles are its subfolders.</summary>
    public string ExportFolder { get; set; } = Path.Combine(LocalFolder, "Export");
    /// <summary>Chosen profile folder; null = the newest profile in <see cref="ExportFolder"/>.</summary>
    public string? ExportProfile { get; set; }
    public string ProjectsFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SDCharacterStudio");
    public string? GameModsFolder { get; set; } = DetectModsFolder();
    public string PipeName { get; set; } = Preview.BridgeClient.DefaultPipe;
    public string Author { get; set; } = Environment.UserName;
    public List<string> RecentProjects { get; set; } = new();
    public double WindowWidth { get; set; } = 1500;
    public double WindowHeight { get; set; } = 920;
    public bool WindowMaximized { get; set; } = true;
    public double BrowserWidth { get; set; } = 300;
    public double InspectorWidth { get; set; } = 360;
    public double StoryboardHeight { get; set; } = 300;
    public string? DockLayoutXml { get; set; }

    [JsonIgnore] public string PreviewFolder => PreviewFolderOverride ?? Path.Combine(LocalFolder, "Preview", "packs");
    /// <summary>Where preview copies go instead (tests: one folder per editor, so parallel previews never share files).</summary>
    [JsonIgnore] public string? PreviewFolderOverride { get; set; }
    [JsonIgnore] public string OpenedPacksProjectsFolder => Path.Combine(AppDataFolder, "Projects");

    public static EditorSettings Load()
    {
        try { if (File.Exists(FilePath)) return JsonConvert.DeserializeObject<EditorSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception ex) when (ex is JsonException or IOException) { }
        return new EditorSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppDataFolder);
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(this, Formatting.Indented));
        }
        catch (IOException) { }
    }

    public void AddRecent(string projectPath)
    {
        RecentProjects.RemoveAll(p => string.Equals(p, projectPath, StringComparison.OrdinalIgnoreCase));
        RecentProjects.Insert(0, projectPath);
        if (RecentProjects.Count > 10) RecentProjects.RemoveRange(10, RecentProjects.Count - 10);
        Save();
    }

    private static string? DetectModsFolder()
    {
        string steam = @"C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley\Mods";
        return Directory.Exists(steam) ? steam : null;
    }
}
