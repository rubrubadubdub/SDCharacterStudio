using System.IO;
using Newtonsoft.Json;

namespace SDCharacterStudio.Editor.Project;

/// <summary>How a mod that provides content to the pack is written on export (doc 12, doc 15 §3.2, Q-15).</summary>
public enum DependencyMode
{
    /// <summary>Required by the whole pack: manifest IsRequired true and listed in studio.json Requires.</summary>
    Required,
    /// <summary>"Only some scenes need it": manifest IsRequired false; scenes that use it skip themselves without it.</summary>
    Optional,
}

/// <summary>A named colour ramp (#RRGGBB each): dark, mid and light.</summary>
public sealed class NamedColor
{
    public string Name { get; set; } = "";
    public string Dark { get; set; } = "#000000";
    public string Mid { get; set; } = "#000000";
    public string Light { get; set; } = "#000000";
}

public sealed class DependencyChoice
{
    public string Mod { get; set; } = "";
    public DependencyMode Mode { get; set; }
}

/// <summary>A dependency the designer added by hand. Kept on export even if nothing in the pack uses it (doc 12 §3).</summary>
public sealed class ManualDependency
{
    public string Mod { get; set; } = "";
    public bool IsRequired { get; set; } = true;
    public string? Reason { get; set; }
}

/// <summary>An item copied from the personal library (doc 03 §17.3), so Update from library can refresh it.</summary>
public sealed class LibraryLink
{
    /// <summary>"Pose" or "Clip".</summary>
    public string Kind { get; set; } = "";
    /// <summary>The item's ID in the personal library.</summary>
    public string Source { get; set; } = "";
    /// <summary>The library version it was copied from (its UpdatedAt).</summary>
    public DateTime Version { get; set; }
    /// <summary>What the copy became in this pack: "Pose:id", "Clip:id", "Rig:id", "Mask:id", "Expression:id", "File:path" → pack ID or path.</summary>
    public Dictionary<string, string> Items { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>The editor-only project file (*.sdcs). The pack folder next to it is the source of truth (doc 07 §10).</summary>
public sealed class ProjectFile
{
    public string ProjectFormat { get; set; } = "1.0";
    /// <summary>The pack folder, relative to the project file when possible.</summary>
    public string PackFolder { get; set; } = "";
    public string? OpenCutscene { get; set; }
    public List<DependencyChoice> Dependencies { get; set; } = new();
    /// <summary>Non-vanilla references with no known provider: reference → mod UniqueID, or "none" for "not a dependency".</summary>
    public Dictionary<string, string> UnknownProviders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Mods that only repaint vanilla content, written as optional dependencies by the designer's choice (Q-7, default off).</summary>
    public List<string> OptionalEditedBy { get; set; } = new();
    public List<ManualDependency> ManualDependencies { get; set; } = new();
    public double StageZoom { get; set; } = 3;
    public bool ShowGrid { get; set; } = true;
    public bool ShowPassability { get; set; }
    /// <summary>
    /// Which upgraded farmhouse the stage shows a scene on (D-52): scene ID → a house layout from the game export (FarmHouse1,
    /// FarmHouse2). A view choice, like the zoom; the map checks (D-54) use it too.
    /// </summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public Dictionary<string, string>? StageLayouts { get; set; }
    public string? StageLayout(string? scene) => scene != null && StageLayouts?.TryGetValue(scene, out var name) == true ? name : null;
    public void SetStageLayout(string scene, string? layout)
    {
        if (layout == null) { StageLayouts?.Remove(scene); if (StageLayouts?.Count == 0) StageLayouts = null; return; }
        (StageLayouts ??= new(StringComparer.OrdinalIgnoreCase))[scene] = layout;
    }
    /// <summary>Items used from the personal library (M8, doc 03 §17.3).</summary>
    public List<LibraryLink> LibraryLinks { get; set; } = new();
    public string? DockLayoutXml { get; set; }
    /// <summary>The designer's notes on story flags and numbers (the Story panel, D-51): ID → what it means.</summary>
    public Dictionary<string, string> StoryNotes { get; set; } = new(StringComparer.Ordinal);
    public bool StoryboardGraph { get; set; }
    /// <summary>
    /// Named colours (after Pixelorama's indexed colours): a ramp the designer paints with and can change later, which
    /// changes it everywhere it's painted in the pack. Plain colours in the pack's art; only the editor knows the names.
    /// </summary>
    public List<NamedColor> Colors { get; set; } = new();

    public DependencyMode ModeFor(string mod) => Dependencies.FirstOrDefault(d => string.Equals(d.Mod, mod, StringComparison.OrdinalIgnoreCase))?.Mode ?? DependencyMode.Required;
    public void SetMode(string mod, DependencyMode mode)
    {
        Dependencies.RemoveAll(d => string.Equals(d.Mod, mod, StringComparison.OrdinalIgnoreCase));
        if (mode != DependencyMode.Required) Dependencies.Add(new DependencyChoice { Mod = mod, Mode = mode });
    }

    public static ProjectFile Read(string path) => JsonConvert.DeserializeObject<ProjectFile>(File.ReadAllText(path)) ?? new ProjectFile();
    public string ToJson() => JsonConvert.SerializeObject(this, Formatting.Indented).Replace("\r\n", "\n") + "\n";

    public string ResolvePackFolder(string projectPath)
    {
        string folder = Path.IsPathRooted(PackFolder) ? PackFolder : Path.Combine(Path.GetDirectoryName(projectPath)!, PackFolder);
        return Path.GetFullPath(folder);
    }
}
