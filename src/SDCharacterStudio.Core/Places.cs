using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core.Models;

namespace SDCharacterStudio.Core;

/// <summary>
/// Places a position can count from (D-52). Their tiles differ by save, house upgrade, map mods and spouse, so the game
/// finds them when the scene starts: a position <c>{"From": "SpouseRoom", "Tile": [1, 0]}</c> is one tile right of the
/// spouse's spot, and a position without From is a map tile as always. The compiled event still starts everyone on
/// map tiles (the event header needs them): a place's <see cref="Fallback"/> plus the offset. The engine then moves
/// them to the real spot before the scene shows.
/// </summary>
public static class Places
{
    /// <summary>
    /// The spouse's spot in the farmhouse (the game's <c>FarmHouse.GetSpouseRoomSpot</c>): the map's SpouseRoomPosition
    /// (or the room's usual corner) plus (3, 4), or the room's own marked tile. Only in a house upgraded at least once, with
    /// a spouse or roommate who is a villager.
    /// </summary>
    public const string SpouseRoom = "SpouseRoom";

    /// <summary>
    /// The game state query each engine copy registers under its own prefix (<see cref="EngineNames.Command"/>):
    /// <c>SDCS_HAS_PLACE SpouseRoom</c> is true when the place can be found here. A scene's trigger includes it.
    /// </summary>
    public const string Query = "HAS_PLACE";

    /// <summary>Where the spouse's room starts in an upgraded house without a SpouseRoomPosition map property.</summary>
    public static (int X, int Y) SpouseRoomCorner(int upgradeLevel) => upgradeLevel == 1 ? (29, 1) : (50, 20);

    /// <summary>The spouse's spot from the room's corner (the game adds 3 right, 4 down).</summary>
    public static (int X, int Y) SpouseRoomSpot((int X, int Y) corner) => (corner.X + 3, corner.Y + 4);

    private static readonly Dictionary<string, (int X, int Y)> fallbacks = new(StringComparer.OrdinalIgnoreCase)
    {
        // A once-upgraded house's spot: (29, 1) + (3, 4).
        [SpouseRoom] = SpouseRoomSpot(SpouseRoomCorner(1)),
    };

    /// <summary>Every place a position can count from.</summary>
    public static IReadOnlyCollection<string> All => fallbacks.Keys;

    public static bool IsKnown(string? place) => place != null && fallbacks.ContainsKey(place);

    /// <summary>The place's spot when it can't be found: the compiled event's start tiles, and where the scene plays then.</summary>
    public static (int X, int Y) Fallback(string place) => fallbacks.TryGetValue(place, out var spot) ? spot : (0, 0);

    /// <summary>The location a place is in (null: any). Using it elsewhere is a mistake the validator points out.</summary>
    public static string? LocationOf(string place) => string.Equals(place, SpouseRoom, StringComparison.OrdinalIgnoreCase) ? "FarmHouse" : null;

    /// <summary>The place in plain words, for the editor and messages.</summary>
    public static string Words(string? place) => place == null ? "the map" : string.Equals(place, SpouseRoom, StringComparison.OrdinalIgnoreCase) ? "the spouse's room" : place;

    public static bool IsAnchored(Position? position) => !string.IsNullOrEmpty(position?.From);

    /// <summary>The same spot as a map tile, given where the place is (its spot), or the position itself when it's on the map.</summary>
    public static Position Absolute(Position position, (int X, int Y) spot)
    {
        if (!IsAnchored(position)) return position;
        int[] tile = position.Tile is { Length: 2 } t ? t : new[] { 0, 0 };
        return new Position { Tile = new[] { spot.X + tile[0], spot.Y + tile[1] }, Px = position.Px is { Length: 2 } px ? (int[])px.Clone() : new[] { 0, 0 } };
    }

    /// <summary>A map tile as tiles from a place (the inverse of <see cref="Absolute"/>).</summary>
    public static int[] Offset(int tileX, int tileY, (int X, int Y) spot) => new[] { tileX - spot.X, tileY - spot.Y };

    /// <summary>A position's tile in the compiled event: on the map, or its place's fallback plus the offset.</summary>
    public static int[]? FallbackTile(Position? position) => position == null ? null
        : IsAnchored(position) ? Absolute(position, Fallback(position.From!)).Tile : position.Tile;

    /// <summary>The scene's own camera centre, if it's a position (not "follow").</summary>
    public static Position? SceneCamera(Cutscene scene) => scene.Camera is JObject camera
        ? new Position { Tile = camera["Tile"] is JArray { Count: 2 } t && t.All(x => x.Type == JTokenType.Integer) ? t.Select(x => (int)x).ToArray() : null, From = camera["From"]?.Type == JTokenType.String ? (string?)camera["From"] : null }
        : null;

    /// <summary>
    /// Every position in the scene, with its JSON path and what it is: actor starts, the scene's camera, walk targets,
    /// camera beats, items and effects, and clip camera keys.
    /// </summary>
    public static IEnumerable<(Position Position, string Path, PositionUse Use, string? Actor)> Positions(Cutscene scene)
    {
        for (int i = 0; i < (scene.Actors?.Count ?? 0); i++)
            if (scene.Actors![i]?.Start is { } start) yield return (start, $"Actors[{i}].Start", PositionUse.Start, scene.Actors[i].Id);
        if (SceneCamera(scene) is { } camera) yield return (camera, "Camera", PositionUse.Camera, null);
        foreach (var (beat, path) in Beats(scene))
            switch (beat)
            {
                case MoveBeat { To: { } to } move: yield return (to, path + ".To", PositionUse.Walk, move.Actor); break;
                case CameraBeat { Center: { } center }: yield return (center, path + ".Center", PositionUse.Camera, null); break;
                case PropBeat { At: { } at }: yield return (at, path + ".At", PositionUse.Prop, null); break;
                case EffectBeat { At: { } at }: yield return (at, path + ".At", PositionUse.Effect, null); break;
            }
        foreach (var (id, clip) in scene.Clips ?? new())
            for (int t = 0; t < (clip?.Tracks?.Count ?? 0); t++)
                for (int k = 0; k < (clip!.Tracks[t]?.Camera?.Count ?? 0); k++)
                    if (clip.Tracks[t].Camera![k]?.Center is { } center) yield return (center, $"Clips.{id}.Tracks[{t}].Camera[{k}].Center", PositionUse.Camera, null);
    }

    /// <summary>The places the scene's positions count from.</summary>
    public static IReadOnlyList<string> Used(Cutscene scene) => Positions(scene).Select(p => p.Position.From).Where(f => !string.IsNullOrEmpty(f))
        .Select(f => Canonical(f!)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>The place's own spelling ("spouseroom" → "SpouseRoom"), or the text as given when unknown.</summary>
    public static string Canonical(string place) => fallbacks.Keys.FirstOrDefault(k => string.Equals(k, place, StringComparison.OrdinalIgnoreCase)) ?? place;

    private static IEnumerable<(Beat Beat, string Path)> Beats(Cutscene scene)
    {
        for (int i = 0; i < (scene.Beats?.Count ?? 0); i++) if (scene.Beats![i] is { } beat) yield return (beat, $"Beats[{i}]");
        foreach (var (name, beats) in scene.Branches ?? new())
            for (int i = 0; i < (beats?.Count ?? 0); i++) if (beats![i] is { } beat) yield return (beat, $"Branches.{name}[{i}]");
    }
}

/// <summary>What a position in a scene is for.</summary>
public enum PositionUse { Start, Camera, Walk, Prop, Effect }
