using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Catalog;
using SDCharacterStudio.Editor.Infrastructure;

namespace SDCharacterStudio.Editor.ViewModels;

/// <summary>An actor's look on the stage: a farmer's outfit (costume keys and values) or an NPC's sprite sheet.</summary>
public sealed record StageLook(IReadOnlyDictionary<string, string> Outfit, string? SpriteAsset)
{
    public static readonly StageLook None = new(new Dictionary<string, string>(), null);
    /// <summary>A stable key for the outfit (the same outfit renders once).</summary>
    public string OutfitKey => string.Join(";", Outfit.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => $"{p.Key}={p.Value}"));
}

/// <summary>
/// What each actor wears at the selected beat (D-49): the scene's starting costume, then every Costume beat that has
/// played by it (<see cref="StageTimeline.Played"/>), so earmuffs put on in beat 3 show from beat 3 on.
/// </summary>
public static class StageLooks
{
    public static StageLook For(Cutscene scene, Actor actor, PackDocument? document, Selection? selection) =>
        For(scene, actor, document, StageTimeline.Played(scene, selection as BeatSelection));

    public static StageLook For(Cutscene scene, Actor actor, PackDocument? document, IReadOnlyList<PlayedBeat> played)
    {
        var outfit = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? sprite = actor.Sprite?.Asset;
        void Merge(JToken? values)
        {
            if (values is JObject set) foreach (var (key, value) in set) if (value != null) outfit[key] = value.ToString();
        }
        void Preset(string? id)
        {
            var costume = document?.Library.Costumes?.Value.Costumes.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
            foreach (var (key, value) in costume?.Set ?? new Dictionary<string, JToken>()) outfit[key] = value.ToString();
        }
        if (actor.Costume?.Type == JTokenType.String) Preset((string?)actor.Costume); else Merge(actor.Costume);

        foreach (var step in played)
        {
            if (step.Beat is not CostumeBeat costume || !string.Equals(costume.Actor, actor.Id, StringComparison.OrdinalIgnoreCase)) continue;
            if (costume.Restore) { outfit.Clear(); Merge(actor.Costume?.Type == JTokenType.Object ? actor.Costume : null); sprite = actor.Sprite?.Asset; continue; }
            if (costume.Preset != null) Preset(costume.Preset);
            if (costume.Set != null) foreach (var (key, value) in costume.Set) outfit[key] = value.ToString();
            if (costume.Sprite?.Asset is { } asset) sprite = asset;
        }
        return new StageLook(outfit, sprite);
    }
}

/// <summary>
/// Outfits the game has rendered for the stage (the Bridge's RenderFarmer, D-49), remembered between sessions per game
/// export: the same outfit is rendered once, then shows even with the game closed.
/// </summary>
public sealed class CostumeRenders
{
    private static string FilePath => Path.Combine(EditorSettings.AppDataFolder, "costume-renders.json");
    private readonly Dictionary<string, string> folders;

    private CostumeRenders(Dictionary<string, string> folders) => this.folders = folders;

    public static CostumeRenders Load()
    {
        try { if (File.Exists(FilePath)) return new(JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(FilePath)) ?? new()); }
        catch (Exception ex) when (ex is JsonException or IOException) { }
        return new(new());
    }

    private static string Key(ExportCatalog catalog, string outfit) => catalog.Root + "|" + outfit;

    /// <summary>The parts folder (export-relative) rendered for this outfit, when it's still there.</summary>
    public string? Find(ExportCatalog catalog, string outfit) =>
        folders.TryGetValue(Key(catalog, outfit), out var folder) && Directory.Exists(catalog.FullPath(folder)) ? folder : null;

    public void Remember(ExportCatalog catalog, string outfit, string folder)
    {
        folders[Key(catalog, outfit)] = folder;
        try { Directory.CreateDirectory(EditorSettings.AppDataFolder); File.WriteAllText(FilePath, JsonConvert.SerializeObject(folders, Formatting.Indented)); }
        catch (IOException) { }
    }
}
