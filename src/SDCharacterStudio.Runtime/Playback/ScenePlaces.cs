using SDCharacterStudio.Core;
using SDCharacterStudio.Core.Models;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;

namespace SDCharacterStudio.Runtime.Playback;

/// <summary>
/// Where a playing scene's places are (D-52), found once when it begins. A place the game doesn't have here (no spouse,
/// a house never upgraded) is logged and stands at its fallback spot, the tiles the compiled event started everyone on;
/// a scene's own trigger already waits for its places, so this only happens when something plays it anyway (a preview,
/// or a mod playing it without its conditions).
/// </summary>
public sealed class ScenePlaces
{
    private readonly Dictionary<string, (int X, int Y)> spots = new(StringComparer.OrdinalIgnoreCase);

    public static ScenePlaces None { get; } = new();

    /// <summary>The places this scene uses, found in <paramref name="location"/> (where it plays).</summary>
    public static ScenePlaces Find(Cutscene scene, GameLocation? location, IMonitor monitor, string key)
    {
        var result = new ScenePlaces();
        foreach (string place in Places.Used(scene))
        {
            if (PlaceFinder.Find(place, location, out string missing) is { } spot) { result.spots[place] = spot; continue; }
            var fallback = Places.Fallback(place);
            result.spots[place] = fallback;
            monitor.Log($"{key}: {Places.Words(place)} isn't here ({missing}). Positions from it use the usual spot instead, {fallback.X},{fallback.Y}.", LogLevel.Warn);
        }
        return result;
    }

    public bool Any => spots.Count > 0;

    /// <summary>The position as a map tile: its place's spot plus its tile, or itself when it's on the map.</summary>
    public Position Absolute(Position position) => !Places.IsAnchored(position) ? position
        : Places.Absolute(position, spots.TryGetValue(position.From!, out var spot) ? spot : Places.Fallback(position.From!));

    /// <summary>The same, null-safe.</summary>
    public Position? AbsoluteOrNull(Position? position) => position == null ? null : Absolute(position);
}

/// <summary>Finds a place in the game (D-52). Shared by the scene's start and its trigger's check (<see cref="Places.Query"/>).</summary>
public static class PlaceFinder
{
    public static (int X, int Y)? Find(string place, GameLocation? location, out string missing)
    {
        missing = "";
        if (string.Equals(place, Places.SpouseRoom, StringComparison.OrdinalIgnoreCase))
        {
            // The farmhouse (or a cabin) the scene plays in: its own spouse's room, as the game placed it (map property,
            // upgrade level, map mods, the spouse's own room and its marked spot).
            if (location is not FarmHouse house) { missing = "the scene isn't in a farmhouse"; return null; }
            if (house.upgradeLevel < 1) { missing = "the house hasn't been upgraded, so it has no spouse's room"; return null; }
            if (!house.HasNpcSpouseOrRoommate()) { missing = "nobody lives here as a spouse or roommate"; return null; }
            var spot = house.GetSpouseRoomSpot();
            return (spot.X, spot.Y);
        }
        missing = "SDCharacterStudio doesn't know this place";
        return null;
    }

    /// <summary>Register <c>&lt;prefix&gt;_HAS_PLACE &lt;place&gt;+</c>: true when every place can be found where the query runs.</summary>
    public static void RegisterQuery()
    {
        string name = EngineNames.Command(Places.Query);
        if (GameStateQuery.Exists(name)) return;
        GameStateQuery.Register(name, (query, context) =>
        {
            if (query.Length < 2) return GameStateQuery.Helpers.ErrorResult(query, "expected at least one place");
            var location = context.Location ?? Game1.currentLocation;
            for (int i = 1; i < query.Length; i++)
                if (Find(query[i], location, out _) == null) return false;
            return true;
        });
    }
}
