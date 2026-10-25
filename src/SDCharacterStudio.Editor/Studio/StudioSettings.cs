using System.IO;
using Newtonsoft.Json;

namespace SDCharacterStudio.Editor.Studio;

/// <summary>Per-user Character Studio preferences, in %APPDATA%\SDCharacterStudio\studio.json (or SDCS_EDITOR_HOME).</summary>
public sealed class StudioSettings
{
    private static string FilePath => Path.Combine(EditorSettings.AppDataFolder, "studio.json");

    /// <summary>Colours the designer added to the palette, as #AARRGGBB.</summary>
    public List<string> CustomColors { get; set; } = new();
    /// <summary>Aseprite, LibreSprite or any image editor (doc 07 §11).</summary>
    public string? ExternalEditor { get; set; }
    public double PoseZoom { get; set; } = 12;
    public bool ShowOnionSkin { get; set; }
    public double OnionOpacity { get; set; } = 0.3;
    public bool ShowGhost { get; set; }
    public bool ShowGrid { get; set; } = true;
    public string? LastCharacter { get; set; }
    public double LibraryWidth { get; set; } = 280;
    /// <summary>Show layers, anchors, masks and the rig in the pose editor (D-48: off = edit what you see).</summary>
    public bool AdvancedPoseTools { get; set; }
    // Tool options (D-49), remembered between sessions.
    public int BrushSize { get; set; } = 1;
    public bool PixelPerfect { get; set; } = true;
    public bool Symmetry { get; set; }
    public bool FilledShapes { get; set; }
    public int OnionBefore { get; set; } = 1;
    public int OnionAfter { get; set; } = 1;
    public bool OnionTint { get; set; } = true;
    public bool OnionWrap { get; set; }
    /// <summary>What's behind the pose: checker, dark, light or scene (the floor where the scene plays).</summary>
    public string Backdrop { get; set; } = "checker";
    /// <summary>Which folding sections of the pose panel are open.</summary>
    public Dictionary<string, bool> OpenSections { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static StudioSettings Load()
    {
        try { if (File.Exists(FilePath)) return JsonConvert.DeserializeObject<StudioSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception ex) when (ex is JsonException or IOException) { }
        return new StudioSettings();
    }

    public void Save()
    {
        try { Directory.CreateDirectory(EditorSettings.AppDataFolder); File.WriteAllText(FilePath, JsonConvert.SerializeObject(this, Formatting.Indented)); }
        catch (IOException) { }
    }
}
