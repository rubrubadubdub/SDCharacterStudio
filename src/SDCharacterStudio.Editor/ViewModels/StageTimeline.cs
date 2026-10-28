using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core.Compiler;
using SDCharacterStudio.Core.Loading;
using SDCharacterStudio.Core.Models;

namespace SDCharacterStudio.Editor.ViewModels;

/// <summary>A beat that has played by the stage's moment, and where it lives on the storyboard.</summary>
public sealed record PlayedBeat(string Lane, int Index, Beat Beat)
{
    public string Title => $"beat {Index + 1} ({Beat.Type})" + (Lane == LaneNames.Main ? "" : " in " + LaneNames.Title(Lane));
}

/// <summary>Where an actor stands at the stage's moment (D-50): tile, pixel offset and facing.</summary>
public sealed record StagePlace(int TileX, int TileY, int PxX, int PxY, string Facing)
{
    /// <summary>The beat that last put them here (a Move, or a Clip with positions); null: their start.</summary>
    public PlayedBeat? PlacedBy { get; init; }
    /// <summary>The beat that last turned them (a Face, a Move's last leg or a Clip); null: their start.</summary>
    public PlayedBeat? TurnedBy { get; init; }
    /// <summary>When the moment's own beat walks them: where they were before it.</summary>
    public StagePlace? From { get; init; }
    /// <summary>When the moment's own beat walks them: the corners of the walk, in tiles (start, turn, end).</summary>
    public IReadOnlyList<(int X, int Y)> Path { get; init; } = Array.Empty<(int, int)>();
}

/// <summary>
/// The scene at a moment of the storyboard (D-50, the owner: "as I advance the timeline with movement of characters, why
/// aren't they represented as being there?"). The stage shows the scene as it is once the selected beat has played: every
/// beat before it, and the beat itself. A beat in a branch includes the main lane up to the choice (or go-to) that leads
/// there. Which option a player picked is unknown, so beats after a choice on the main lane don't include any branch.
/// </summary>
public static class StageTimeline
{
    /// <summary>The beats that have played once the moment's beat has (in order, the moment's beat last). Empty: the start.</summary>
    public static IReadOnlyList<PlayedBeat> Played(Cutscene scene, BeatSelection? moment)
    {
        if (moment == null || moment.Lane == LaneNames.Skip || LaneNames.Get(scene, moment.Lane) is not { } lane || lane.Count == 0) return Array.Empty<PlayedBeat>();
        var result = moment.Lane == LaneNames.Main ? new List<PlayedBeat>() : Leading(scene, moment.Lane, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        int through = Math.Min(moment.Index, lane.Count - 1);
        for (int i = 0; i <= through; i++) result.Add(new PlayedBeat(moment.Lane, i, lane[i]));
        return result;
    }

    /// <summary>The beats before the first choice or go-to that leads into a branch.</summary>
    private static List<PlayedBeat> Leading(Cutscene scene, string branch, HashSet<string> visited)
    {
        visited.Add(branch);
        foreach (string lane in new[] { LaneNames.Main }.Concat(scene.Branches.Keys))
        {
            if (visited.Contains(lane) || LaneNames.Get(scene, lane) is not { } beats) continue;
            int at = beats.FindIndex(b => BeatFlow.Targets(b).Any(t => Same(t, branch)));
            if (at < 0) continue;
            var before = lane == LaneNames.Main ? new List<PlayedBeat>() : Leading(scene, lane, visited);
            for (int i = 0; i < at; i++) before.Add(new PlayedBeat(lane, i, beats[i]));
            return before;
        }
        return new List<PlayedBeat>();
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Where the actor is once the played beats have: their start, then each Move, Face and Clip that names them. Tiles are
    /// on <paramref name="map"/>: a position that counts from a place (D-52) stands where the place is on it.
    /// </summary>
    public static StagePlace Place(Cutscene scene, Actor actor, IReadOnlyList<PlayedBeat> played, LoadedPack? library = null, StagedMap? map = null)
    {
        map ??= NoMap;
        var start = map.Tile(actor.Start);
        var place = new StagePlace(start.X, start.Y,
            actor.Start?.Px?.ElementAtOrDefault(0) ?? 0, actor.Start?.Px?.ElementAtOrDefault(1) ?? 0, actor.Facing ?? "down");
        bool Mine(string? id) => Same(id, actor.Id);
        for (int n = 0; n < played.Count; n++)
        {
            var step = played[n];
            bool last = n == played.Count - 1;
            switch (step.Beat)
            {
                case MoveBeat { To.Tile: { Length: 2 } } move when Mine(move.Actor):
                {
                    var (toX, toY) = map.Tile(move.To);
                    int[] to = { toX, toY };
                    // MovePlanner: one leg per axis (x first unless "y-first"); vanilla 'move' leaves them facing along the last leg.
                    int dx = to[0] - place.TileX, dy = to[1] - place.TileY;
                    string? alongX = dx == 0 ? null : dx > 0 ? "right" : "left", alongY = dy == 0 ? null : dy > 0 ? "down" : "up";
                    bool yFirst = move.Path == "y-first";
                    string facing = (yFirst ? alongX ?? alongY : alongY ?? alongX) ?? place.Facing;
                    var corner = yFirst ? (place.TileX, to[1]) : (to[0], place.TileY);
                    var before = place with { From = null, Path = Array.Empty<(int, int)>() };
                    place = place with
                    {
                        TileX = to[0], TileY = to[1], Facing = facing, PlacedBy = step, TurnedBy = dx == 0 && dy == 0 ? place.TurnedBy : step,
                        From = last ? before : null,
                        Path = last ? new[] { (before.TileX, before.TileY), corner, (to[0], to[1]) }.Distinct().ToArray() : Array.Empty<(int, int)>(),
                    };
                    break;
                }
                case FaceBeat face when Mine(face.Actor) && !string.IsNullOrEmpty(face.Facing):
                    place = place with { Facing = face.Facing!, TurnedBy = step, From = null, Path = Array.Empty<(int, int)>() };
                    break;
                case ClipBeat clipBeat when Clip(scene, clipBeat, library) is { } clip:
                    foreach (var track in clip.Tracks.Where(t => Mine(t.Actor)))
                    {
                        // Its last key wins: an absolute tile, or an offset from where they stood when the clip began.
                        if (track.Position?.Where(k => k.Tile is { Length: 2 }).OrderBy(k => k.At).LastOrDefault() is { } key)
                        {
                            int[] px = key.Px is { Length: 2 } p ? p : new[] { 0, 0 };
                            place = key.Relative
                                ? place with { TileX = place.TileX + key.Tile![0], TileY = place.TileY + key.Tile[1], PxX = place.PxX + px[0], PxY = place.PxY + px[1], PlacedBy = step }
                                : place with { TileX = key.Tile![0], TileY = key.Tile[1], PxX = px[0], PxY = px[1], PlacedBy = step };
                        }
                        if (track.Facing?.Where(k => k.Facing != null).OrderBy(k => k.At).LastOrDefault()?.Facing is { } facing)
                            place = place with { Facing = facing, TurnedBy = step };
                    }
                    place = place with { From = null, Path = Array.Empty<(int, int)>() };
                    break;
            }
        }
        return place;
    }

    /// <summary>A clip beat's clip: the scene's own, or a library clip cast onto this scene (as the compiler expands it).</summary>
    public static Clip? Clip(Cutscene scene, ClipBeat beat, LoadedPack? library)
    {
        if (beat.Clip == null) return null;
        if (scene.Clips.TryGetValue(beat.Clip, out var own)) return own;
        return LibraryClips.Find(library, beat.Clip) is { } shared ? LibraryClips.ToSceneClip(shared, beat.Cast ?? new(), beat.Mirror, beat.PoseMap) : null;
    }

    /// <summary>
    /// The items on the map once the played beats have (Prop beats, D-50): each placed one, less those taken away since, with
    /// the tile it stands on (on <paramref name="map"/>, D-52).
    /// </summary>
    public static IReadOnlyList<(PlayedBeat By, PropBeat Prop, int X, int Y)> Props(IReadOnlyList<PlayedBeat> played, StagedMap? map = null)
    {
        map ??= NoMap;
        var props = new List<(PlayedBeat By, PropBeat Prop, int X, int Y)>();
        foreach (var step in played)
        {
            if (step.Beat is not PropBeat { At.Tile: { Length: 2 } } prop) continue;
            var (x, y) = map.Tile(prop.At);
            if (!prop.Remove) { if (prop.Item != null) props.Add((step, prop, x, y)); continue; }
            props.RemoveAll(p => p.X == x && p.Y == y && (prop.Item == null || Same(p.Prop.Item, prop.Item)));
        }
        return props;
    }

    /// <summary>Where the camera is centred once the played beats have: the last Camera beat's tile on <paramref name="map"/> (null: the scene's own camera).</summary>
    public static (int[] Tile, PlayedBeat By)? Camera(IReadOnlyList<PlayedBeat> played, StagedMap? map = null)
    {
        map ??= NoMap;
        for (int n = played.Count - 1; n >= 0; n--)
            if (played[n].Beat is CameraBeat { Center.Tile: { Length: 2 } } camera) { var (x, y) = map.Tile(camera.Center); return (new[] { x, y }, played[n]); }
        return null;
    }

    /// <summary>No export: places stand at their usual spots.</summary>
    private static readonly StagedMap NoMap = new(null, null, false);
}
