using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SDCharacterStudio.Editor.Project;

/// <summary>
/// Crash insurance (doc 07 §10, Q-14): every two minutes an unsaved project's editable state is written to
/// <c>&lt;project&gt;.autosave\state.json</c>. Saving deletes it; opening a project with a newer autosave offers recovery.
/// </summary>
public static class Autosave
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);
    public static string Folder(string projectPath) => projectPath + ".autosave";
    private static string StateFile(string projectPath) => Path.Combine(Folder(projectPath), "state.json");

    public static void Write(PackDocument document)
    {
        Directory.CreateDirectory(Folder(document.ProjectPath));
        var envelope = new JObject { ["SavedAt"] = DateTime.UtcNow.ToString("O"), ["State"] = document.CaptureState() };
        string temporary = StateFile(document.ProjectPath) + ".tmp";
        File.WriteAllText(temporary, envelope.ToString(Formatting.None));
        File.Move(temporary, StateFile(document.ProjectPath), true);
    }

    /// <summary>The autosaved state if it's newer than the last save of the project, else null.</summary>
    public static (string State, DateTime SavedAt)? FindRecovery(string projectPath)
    {
        string file = StateFile(projectPath);
        if (!File.Exists(file)) return null;
        try
        {
            // Keep the timestamp as text: Json.NET would otherwise turn it into a DateTime and drop the sub-second part.
            using var reader = new JsonTextReader(new StringReader(File.ReadAllText(file))) { DateParseHandling = DateParseHandling.None };
            var envelope = JObject.Load(reader);
            var savedAt = DateTime.Parse((string)envelope["SavedAt"]!, null, System.Globalization.DateTimeStyles.RoundtripKind);
            if (File.Exists(projectPath) && File.GetLastWriteTimeUtc(projectPath) >= savedAt) return null;
            return ((string)envelope["State"]!, savedAt.ToLocalTime());
        }
        catch (Exception ex) when (ex is JsonException or FormatException or IOException) { return null; }
    }

    public static void Delete(string projectPath)
    {
        try { if (Directory.Exists(Folder(projectPath))) Directory.Delete(Folder(projectPath), true); }
        catch (IOException) { }
    }
}
