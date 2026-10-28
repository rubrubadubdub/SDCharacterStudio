using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core.Validation;

namespace SDCharacterStudio.Editor.Catalog;

/// <summary>Where an exported asset or data key comes from (Bridge origins, doc 06 §6).</summary>
public sealed class OriginInfo
{
    public bool Vanilla { get; set; } = true;
    public List<string> ProvidedBy { get; set; } = new();
    public List<string> EditedBy { get; set; } = new();
    public bool Certain { get; set; } = true;
    public static readonly OriginInfo VanillaOrigin = new();
    /// <summary>Not vanilla and no known provider: the designer must say which mod provides it (doc 12 §2.3).</summary>
    [JsonIgnore] public bool IsUnknown => !Vanilla && ProvidedBy.Count == 0;
    [JsonIgnore] public bool IsModProvided => !Vanilla && ProvidedBy.Count > 0;
    [JsonIgnore] public bool IsEdited => Vanilla && EditedBy.Count > 0;
}

public sealed class TextureEntry { public string Asset { get; set; } = ""; public string? File { get; set; } public int[]? Size { get; set; } }
public sealed class AppearanceEntry
{
    public string? Id { get; set; }
    public string? Season { get; set; }
    /// <summary>A game state query, e.g. "IS_EVENT festival_spring24, true" for the Flower Dance.</summary>
    public string? Condition { get; set; }
    public bool Indoors { get; set; } = true;
    public bool Outdoors { get; set; } = true;
    public string? Sprite { get; set; }
    public string? Portrait { get; set; }
    public bool IsIslandAttire { get; set; }
}
public sealed class CharacterEntry
{
    public string Name { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? TextureName { get; set; }
    public int[]? Size { get; set; }
    public OriginInfo Origin { get; set; } = new();
    public string? Sprite { get; set; }
    public string? Portrait { get; set; }
    public List<AppearanceEntry> Appearances { get; set; } = new();
    public CurrentAppearance? Current { get; set; }
    public int FrameWidth => Size is { Length: 2 } s && s[0] > 0 ? s[0] : 16;
    public int FrameHeight => Size is { Length: 2 } s && s[1] > 0 ? s[1] : 32;
    public string Label => string.IsNullOrWhiteSpace(DisplayName) || DisplayName == Name ? Name : $"{DisplayName} ({Name})";
}
public sealed class CurrentAppearance { public string? Sprite { get; set; } public string? Portrait { get; set; } public string? AppearanceId { get; set; } }
public sealed class WarpEntry { public int[]? Tile { get; set; } public string? To { get; set; } public int[]? ToTile { get; set; } }
public sealed class LocationEntry
{
    public string Name { get; set; } = "";
    public string? MapAsset { get; set; }
    public int[]? Size { get; set; }
    public string? Back { get; set; }
    public string? Front { get; set; }
    public string? Preview { get; set; }
    public string? Passable { get; set; }
    public List<WarpEntry> Warps { get; set; } = new();
    public OriginInfo Origin { get; set; } = new();
    /// <summary>A farmhouse's upgrade level (the save's own house, or a layout).</summary>
    public int? Level { get; set; }
    /// <summary>Where places are on this map (D-52), e.g. SpouseRoom → its spot; only those the game could find.</summary>
    public Dictionary<string, int[]> Places { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>For a house layout: the location it's a layout of (FarmHouse).</summary>
    public string? Of { get; set; }
    /// <summary>For a house layout: whose spouse's room is drawn in it (empty: the game's default room).</summary>
    public string? SpouseRoom { get; set; }
    /// <summary>A place's spot on this map, if the export knows it.</summary>
    public (int X, int Y)? Spot(string place) => Places.TryGetValue(place, out var spot) && spot is { Length: 2 } ? (spot[0], spot[1]) : null;
    public int Width => Size is { Length: 2 } ? Size[0] : 0;
    public int Height => Size is { Length: 2 } ? Size[1] : 0;
}
public sealed class ItemEntry
{
    public string Id { get; set; } = "";
    public string? QualifiedId { get; set; }
    public string? Name { get; set; }
    public string? DisplayName { get; set; }
    public string? Icon { get; set; }
    /// <summary>The item's picture in sprite pixels (the icon is the whole of it: furniture shows whole).</summary>
    public int[]? Size { get; set; }
    public bool IsErrorItem { get; set; }
    public OriginInfo Origin { get; set; } = new();
    public string Qualified => QualifiedId ?? Id;
    public string Label => string.IsNullOrWhiteSpace(DisplayName) ? Name ?? Qualified : DisplayName!;
}
public sealed class HairstyleEntry
{
    public int Index { get; set; }
    public string? Texture { get; set; }
    public int TileX { get; set; }
    public int TileY { get; set; }
    public bool IsBaldStyle { get; set; }
    public OriginInfo Origin { get; set; } = new();
}
public sealed class ModEntry
{
    public string UniqueID { get; set; } = "";
    public string? Name { get; set; }
    public string? Version { get; set; }
    public bool IsContentPack { get; set; }
    public string? ContentPackFor { get; set; }
    public string? Folder { get; set; }
    public string Label => string.IsNullOrWhiteSpace(Name) ? UniqueID : Name!;
}
public sealed class EmoteEntry { public string? Name { get; set; } public string? DisplayName { get; set; } public int Id { get; set; } public bool Hidden { get; set; } }

/// <summary>
/// The Bridge's export of the running, modded game (doc 06). It is what the designer's install contains:
/// vanilla content, SVE/RSV/ES and every other mod. It also answers Core's catalog questions, so editor
/// validation matches the runtime's world checks (V7, V14, V15, V19).
/// </summary>
public sealed class ExportCatalog : ICatalog
{
    private readonly Dictionary<string, TextureEntry> textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OriginInfo> assetOrigins = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CharacterEntry> characters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LocationEntry> locations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ItemEntry> items = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ModEntry> mods = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> trackedItemTypes = new(StringComparer.OrdinalIgnoreCase);

    public string Root { get; }
    public string Profile { get; }
    public string? GameVersion { get; private set; }
    public DateTime ExportedAt { get; private set; }
    public IReadOnlyList<CharacterEntry> Characters { get; private set; } = Array.Empty<CharacterEntry>();
    public IReadOnlyList<LocationEntry> Locations { get; private set; } = Array.Empty<LocationEntry>();
    /// <summary>The upgraded farmhouse layouts (D-52), as mods make them; empty in an export made before they were exported.</summary>
    public IReadOnlyList<LocationEntry> HouseLayouts { get; private set; } = Array.Empty<LocationEntry>();
    /// <summary>The export has the house layouts (even if none could be drawn).</summary>
    public bool HasHouseLayouts { get; private set; }
    public IReadOnlyDictionary<string, IReadOnlyList<ItemEntry>> ItemsByType { get; private set; } = new Dictionary<string, IReadOnlyList<ItemEntry>>();
    public IReadOnlyList<HairstyleEntry> Hairstyles { get; private set; } = Array.Empty<HairstyleEntry>();
    public IReadOnlyList<ModEntry> Mods { get; private set; } = Array.Empty<ModEntry>();
    public IReadOnlyList<string> Sounds { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<EmoteEntry> Emotes { get; private set; } = Array.Empty<EmoteEntry>();
    public JObject? Player { get; private set; }
    /// <summary>A save-phase export happened, so locations and the current player are known.</summary>
    public bool HasWorld => Locations.Count > 0;

    private ExportCatalog(string root, string profile) { Root = root; Profile = profile; }

    /// <summary>Profiles (subfolders with a catalog.json) under the Bridge export folder, newest first.</summary>
    public static IReadOnlyList<string> FindProfiles(string exportFolder)
    {
        if (!Directory.Exists(exportFolder)) return Array.Empty<string>();
        return Directory.EnumerateDirectories(exportFolder)
            .Where(d => File.Exists(Path.Combine(d, "catalog.json")))
            .OrderByDescending(d => File.GetLastWriteTimeUtc(Path.Combine(d, "catalog.json"))).ToArray();
    }

    public static ExportCatalog Load(string profileFolder)
    {
        string catalogPath = Path.Combine(profileFolder, "catalog.json");
        if (!File.Exists(catalogPath)) throw new FileNotFoundException("No catalog.json in the export folder. Start the game with the SDCS Bridge installed and load a save.", catalogPath);
        var catalog = new ExportCatalog(Path.GetFullPath(profileFolder), Path.GetFileName(Path.GetFullPath(profileFolder).TrimEnd('\\', '/')));
        var json = JObject.Parse(File.ReadAllText(catalogPath));
        var serializer = JsonSerializer.CreateDefault(new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Ignore, NullValueHandling = NullValueHandling.Ignore });
        T? Read<T>(JToken? token) where T : class { try { return token?.ToObject<T>(serializer); } catch (JsonException) { return null; } }

        catalog.GameVersion = (string?)json["Game"]?["Version"];
        catalog.ExportedAt = json["ExportedAt"]?.Type == JTokenType.Date ? (DateTime)json["ExportedAt"]! : DateTime.TryParse((string?)json["ExportedAt"], out var at) ? at : File.GetLastWriteTime(catalogPath);
        foreach (var texture in Read<List<TextureEntry>>(json["Textures"]) ?? new())
            if (!string.IsNullOrEmpty(texture.Asset)) catalog.textures[Normalize(texture.Asset)] = texture;
        catalog.Characters = (Read<List<CharacterEntry>>(json["Characters"]) ?? new()).Where(c => !string.IsNullOrEmpty(c.Name)).OrderBy(c => c.Label, StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var character in catalog.Characters) catalog.characters[character.Name] = character;
        catalog.Locations = (Read<List<LocationEntry>>(json["Locations"]) ?? new()).Where(l => !string.IsNullOrEmpty(l.Name)).OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var location in catalog.Locations) catalog.locations[location.Name] = location;
        catalog.HasHouseLayouts = json["HouseLayouts"] is JArray;
        catalog.HouseLayouts = (Read<List<LocationEntry>>(json["HouseLayouts"]) ?? new()).Where(l => !string.IsNullOrEmpty(l.Name)).OrderBy(l => l.Level ?? 0).ToArray();
        var byType = new Dictionary<string, IReadOnlyList<ItemEntry>>(StringComparer.OrdinalIgnoreCase);
        if (json["Items"] is JObject itemGroups)
            foreach (var group in itemGroups.Properties())
            {
                var list = (Read<List<ItemEntry>>(group.Value) ?? new()).Where(i => !i.IsErrorItem && !string.IsNullOrEmpty(i.Id)).ToArray();
                byType[group.Name] = list; catalog.trackedItemTypes.Add(group.Name);
                foreach (var item in list) catalog.items[item.Qualified] = item;
            }
        catalog.ItemsByType = byType;
        catalog.Hairstyles = Read<List<HairstyleEntry>>(json["FarmerParts"]?["Hairstyles"]) ?? new();
        catalog.Mods = ReadFile<List<ModEntry>>(profileFolder, (string?)json["Mods"] ?? "mods.json") ?? new();
        foreach (var mod in catalog.Mods) catalog.mods[mod.UniqueID] = mod;
        if (ReadFile<JObject>(profileFolder, (string?)json["Origins"] ?? "origins.json")?["Assets"] is JObject origins)
            foreach (var property in origins.Properties())
                if (Read<OriginInfo>(property.Value) is { } origin) catalog.assetOrigins[Normalize(property.Name)] = origin;
        catalog.Sounds = (ReadFile<List<string>>(profileFolder, (string?)json["Sounds"] ?? "sounds.json") ?? new()).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        catalog.Emotes = ReadFile<List<EmoteEntry>>(profileFolder, (string?)json["Emotes"] ?? "emotes.json") ?? new();
        if (json["Player"] is JValue { Type: JTokenType.String } playerFile) catalog.Player = ReadFile<JObject>(profileFolder, (string)playerFile!);
        else if (File.Exists(Path.Combine(profileFolder, "farmer", "player.json"))) catalog.Player = ReadFile<JObject>(profileFolder, "farmer/player.json");
        return catalog;

        T? ReadFile<T>(string root, string relative) where T : class
        {
            string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) return null;
            try { return JToken.Parse(File.ReadAllText(path)).ToObject<T>(serializer); } catch (JsonException) { return null; }
        }
    }

    public string FullPath(string? relative) => relative == null ? "" : Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
    /// <summary>The exported PNG for a texture asset, if the export has it.</summary>
    public string? TextureFile(string? asset)
    {
        if (asset == null || !textures.TryGetValue(Normalize(asset), out var entry) || entry.File == null) return null;
        string path = FullPath(entry.File);
        return File.Exists(path) ? path : null;
    }
    public int[]? TextureSize(string asset) => textures.TryGetValue(Normalize(asset), out var entry) ? entry.Size : null;
    public CharacterEntry? Character(string name) => characters.TryGetValue(name, out var c) ? c : characters.Values.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
    public LocationEntry? Location(string? name) => name != null && locations.TryGetValue(name, out var l) ? l : null;
    /// <summary>A house layout by name (FarmHouse1, FarmHouse2).</summary>
    public LocationEntry? HouseLayout(string? name) => name == null ? null : HouseLayouts.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
    public ItemEntry? Item(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        if (items.TryGetValue(id, out var item)) return item;
        return id.StartsWith('(') ? null : items.Values.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
    }
    public ModEntry? Mod(string? id) => id != null && mods.TryGetValue(id, out var mod) ? mod : null;
    public string ModLabel(string id) => Mod(id)?.Label ?? id;
    /// <summary>Origin of a texture asset; null when the export doesn't know it.</summary>
    public OriginInfo? AssetOrigin(string? asset) => asset != null && assetOrigins.TryGetValue(Normalize(asset), out var origin) ? origin : null;

    // ICatalog: answers for Core's validator. Unknown kinds count as existing (the runtime checks them again).
    public bool NpcExists(string name) => FindNpc(name) != null;
    public string? FindNpc(string name) => characters.ContainsKey(name) ? name : characters.Keys.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
    public string? SuggestNpc(string name)
    {
        var byDisplay = characters.Values.FirstOrDefault(c => string.Equals(c.DisplayName, name, StringComparison.OrdinalIgnoreCase));
        if (byDisplay != null) return byDisplay.Name;
        return name.Length < 4 ? null : characters.Keys.FirstOrDefault(k => k.Contains(name, StringComparison.OrdinalIgnoreCase));
    }
    public bool LocationExists(string name) => !HasWorld || locations.ContainsKey(name);
    public AssetOrigin? FindAsset(string asset) => AssetOrigin(asset) is { } o ? new AssetOrigin(o.Vanilla, o.ProvidedBy.FirstOrDefault()) : null;
    public bool AssetExists(string asset) => true;
    public bool ItemExists(string qualifiedItemId)
    {
        if (Item(qualifiedItemId) != null) return true;
        int close = qualifiedItemId.IndexOf(')');
        // Only types the export enumerates can be judged missing.
        return !(qualifiedItemId.StartsWith('(') && close > 0 && trackedItemTypes.Contains(qualifiedItemId[..(close + 1)]));
    }
    public bool HairstyleExists(int index) => Hairstyles.Count == 0 || Hairstyles.Any(h => h.Index == index);

    private static string Normalize(string asset) => asset.Replace('\\', '/');
}
