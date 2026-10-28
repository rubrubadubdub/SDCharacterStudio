using SDCharacterStudio.Core;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Validation;
using SDCharacterStudio.Editor.Catalog;
using SDCharacterStudio.Editor.Project;

namespace SDCharacterStudio.Editor.ViewModels;

/// <summary>
/// The map a scene is staged on (D-52): its location from the game export or, for the farmhouse, an upgraded house as mods
/// make it. It knows where the places are, so the stage draws a position that counts from a place where the place is.
/// </summary>
public sealed record StagedMap(LocationEntry? Entry, LocationEntry? Layout, bool Chosen)
{
    /// <summary>The value of the choice "the farmhouse as the game export has it" (instead of a layout's name).</summary>
    public const string ExportedHouse = "FarmHouse";

    /// <summary>The map drawn: the layout, or the location.</summary>
    public LocationEntry? Map => Layout ?? Entry;

    /// <summary>Whether this map shows where the place is.</summary>
    public bool Knows(string place) => Map?.Spot(place) != null;

    /// <summary>The place's spot on this map; when the map doesn't have it, its usual spot (the compiled event's fallback).</summary>
    public (int X, int Y) Spot(string place) => Map?.Spot(place) ?? Places.Fallback(place);

    /// <summary>A position as a tile on this map.</summary>
    public (int X, int Y) Tile(Position? position)
    {
        if (position == null) return (0, 0);
        var tile = Places.IsAnchored(position) ? Places.Absolute(position, Spot(position.From!)).Tile : position.Tile;
        return (tile?.ElementAtOrDefault(0) ?? 0, tile?.ElementAtOrDefault(1) ?? 0);
    }

    /// <summary>A tile on this map as a position counting from <paramref name="from"/> (null: the map), keeping the pixel nudge.</summary>
    public Position At(int tileX, int tileY, string? from, int[]? px = null) => new()
    {
        Tile = from == null ? new[] { tileX, tileY } : Places.Offset(tileX, tileY, Spot(from)),
        Px = px is { Length: 2 } ? (int[])px.Clone() : new[] { 0, 0 },
        From = from,
    };

    /// <summary>The map as the location checks (D-54) see it: a farmhouse only when staged on a known layout.</summary>
    public MapGrid? Grid(Cutscene scene, Func<string, bool?[,]?>? walkable = null)
    {
        var map = Map;
        if (map == null || map.Width <= 0 || map.Height <= 0) return null;
        if (Layout == null && MapPositionValidator.VariesByUpgrade(scene.Location)) return null;
        var grid = walkable?.Invoke(map.Passable ?? "");
        return new MapGrid(Layout != null ? $"{scene.Location} ({Words(Layout)})" : map.Name, map.Width, map.Height,
            grid == null ? null : (x, y) => x < grid.GetLength(0) && y < grid.GetLength(1) ? grid[x, y] : null);
    }

    /// <summary>An upgraded house in words: "upgraded once", "upgraded twice".</summary>
    public static string Words(LocationEntry layout) => layout.Level switch { 1 => "upgraded once", 2 => "upgraded twice", { } n => $"upgrade {n}", _ => layout.Name };

    /// <summary>
    /// The map to stage <paramref name="scene"/> on: for the farmhouse, the house the designer chose; or, when the scene counts
    /// from a place the export's own house doesn't have (no spouse's room yet), the first upgraded house that has it.
    /// </summary>
    public static StagedMap For(Cutscene? scene, ExportCatalog? catalog, ProjectFile? project)
    {
        var live = catalog?.Location(scene?.Location);
        if (scene == null || catalog == null || !string.Equals(scene.Location, ExportedHouse, StringComparison.OrdinalIgnoreCase)) return new(live, null, false);
        string? chosen = project?.StageLayout(scene.Id);
        if (string.Equals(chosen, ExportedHouse, StringComparison.OrdinalIgnoreCase)) return new(live, null, true);
        if (catalog.HouseLayout(chosen) is { } layout) return new(live, layout, true);
        var used = Places.Used(scene);
        if (used.Count > 0 && used.Any(p => live?.Spot(p) == null)
            && catalog.HouseLayouts.FirstOrDefault(l => used.All(p => l.Spot(p) != null)) is { } fits)
            return new(live, fits, false);
        return new(live, null, false);
    }
}
