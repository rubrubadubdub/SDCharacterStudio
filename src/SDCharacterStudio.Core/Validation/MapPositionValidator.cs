using SDCharacterStudio.Core.Models;

namespace SDCharacterStudio.Core.Validation;

/// <summary>A location's map as the game export has it: its size in tiles and, when known, which tiles people can walk on.</summary>
public sealed record MapGrid(string Name, int Width, int Height, Func<int, int, bool?>? Walkable = null);

/// <summary>
/// Positions on the scene's map (D-54): a map tile off the map is a warning (V21); an actor starting on or walking to a
/// tile nobody can walk on is a gentler hint (V22), since a scene may mean it. Positions that count from a place (D-52)
/// are found in the game, so they're not checked here. Nor is a map whose layout depends on the save (the farmhouse and
/// cabins change with each upgrade) unless the editor knows which layout the scene is staged on.
/// </summary>
public static class MapPositionValidator
{
    /// <summary>Locations whose map changes with house upgrades: the export's copy is one player's house at one level.</summary>
    public static bool VariesByUpgrade(string? location) => location != null
        && (string.Equals(location, "FarmHouse", StringComparison.OrdinalIgnoreCase) || location.StartsWith("Cabin", StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<ValidationIssue> Validate(Cutscene scene, string file, MapGrid map)
    {
        var issues = new List<ValidationIssue>();
        if (map.Width <= 0 || map.Height <= 0) return issues;
        foreach (var (position, path, use, actor) in Places.Positions(scene))
        {
            if (Places.IsAnchored(position) || position.Tile is not { Length: 2 } tile) continue;
            int x = tile[0], y = tile[1];
            string at = $"{x},{y}";
            if (x < 0 || y < 0 || x >= map.Width || y >= map.Height)
            {
                issues.Add(new(ValidationSeverity.Warning, file, path + ".Tile", $"{What(use, actor, scene)} {at}, which is off the {map.Name} map ({map.Width}×{map.Height}).", "V21"));
                continue;
            }
            if (use is not (PositionUse.Start or PositionUse.Walk) || map.Walkable?.Invoke(x, y) != false) continue;
            issues.Add(new(ValidationSeverity.Warning, file, path + ".Tile",
                $"{What(use, actor, scene)} {at}, a tile nobody can walk on in the {map.Name} map (a wall or furniture?). Fine if you meant it.", "V22"));
        }
        return issues;
    }

    private static string What(PositionUse use, string? actor, Cutscene scene)
    {
        string who = Who(actor, scene);
        return use switch
        {
            PositionUse.Start => who + " starts at",
            PositionUse.Walk => who + " walks to",
            PositionUse.Camera => "The camera centres on",
            PositionUse.Prop => "The item stands on",
            _ => "The effect is at",
        };
    }

    private static string Who(string? actor, Cutscene scene)
    {
        if (string.IsNullOrWhiteSpace(actor)) return "Someone";
        var entry = scene.Actors?.FirstOrDefault(a => string.Equals(a?.Id, actor, StringComparison.OrdinalIgnoreCase));
        return string.Equals(entry?.Kind ?? actor, "Farmer", StringComparison.OrdinalIgnoreCase) || string.Equals(actor, "farmer", StringComparison.OrdinalIgnoreCase) ? "The player" : actor!;
    }
}
