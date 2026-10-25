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
    /// <summary>Items used from the personal library (M8, doc 03 §17.3).</summary>
    public List<LibraryLink> LibraryLinks { get; set; } = new();
    public string? DockLayoutXml { get; set; }
    /// <summary>The designer's notes on story flags and numbers (the Story panel, D-51): ID → what it means.</summary>
    public Dictionary<string, string> StoryNotes { get; set; } = new(StringComparer.Ordinal);
    public bool StoryboardGraph { get; set; }

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
