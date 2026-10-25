using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Project;

namespace SDCharacterStudio.Editor.Studio;

/// <summary>One step of a flipbook animation (D-48): show a pose, walk, or the character's own look, for a time.</summary>
public sealed class FlipbookStep
{
    /// <summary>"Pose", "Walk" or "Own".</summary>
    public string Do { get; set; } = "Pose";
    public string? Pose { get; set; }
    public int Ms { get; set; } = 1000;
    /// <summary>How the pose plays: loop, once, once-then-loop, hold-first.</summary>
    public string Play { get; set; } = "loop";
    /// <summary>Walk: how far, in tiles (x right, y down).</summary>
    public int[] Tiles { get; set; } = { 0, 0 };
    /// <summary>Turn to face this way when the step starts (null: keep). A walk faces where it goes.</summary>
    public string? Facing { get; set; }
}

/// <summary>
/// A flipbook is a library animation whose steps are kept in its <c>Flipbook</c> extra data; the editor regenerates the
/// animation's tracks (facing, pose, position) from them. The runtime only reads the tracks, so nothing else changes.
/// A walk uses a walking pose made from the rig's walk frames (live, so it follows sprite mods), moving over its time.
/// </summary>
public static class Flipbook
{
    public const string Key = "Flipbook";
    public const string CharacterKey = "FlipbookCharacter";

    public static bool Has(LibraryClip clip) => clip.Extra?.ContainsKey(Key) == true;

    public static List<FlipbookStep> Steps(LibraryClip clip) =>
        clip.Extra?.TryGetValue(Key, out var token) == true && token is JArray array ? array.ToObject<List<FlipbookStep>>() ?? new() : new();

    public static string? Character(LibraryClip clip) => clip.Extra?.TryGetValue(CharacterKey, out var token) == true ? (string?)token : null;

    /// <summary>Store the steps and rebuild the clip's tracks for its first role.</summary>
    public static void Write(LibraryClip clip, IReadOnlyList<FlipbookStep> steps, Func<string, string?> walkPose)
    {
        clip.Extra ??= new Dictionary<string, JToken>();
        clip.Extra[Key] = JArray.FromObject(steps);
        string role = clip.Roles.FirstOrDefault()?.Id ?? "actor";
        var facing = new List<FacingKey>();
        var poses = new List<PoseKey>();
        var position = new List<PositionKey> { new() { At = 0, Tile = new[] { 0, 0 } } };
        int at = 0, x = 0, y = 0;
        string? lastFacing = null;
        foreach (var step in steps)
        {
            int ms = Math.Max(50, step.Ms);
            string? face = step.Do == "Walk" ? WalkFacing(step.Tiles) ?? step.Facing : step.Facing;
            if (face != null && face != lastFacing) { facing.Add(new FacingKey { At = at, Facing = face }); lastFacing = face; }
            string? pose = step.Do switch
            {
                "Walk" => walkPose(face ?? lastFacing ?? "down"),
                "Own" => null,
                _ => step.Pose,
            };
            poses.Add(new PoseKey { At = at, Pose = pose, Play = step.Do == "Walk" ? "loop" : step.Play });
            if (step.Do == "Walk" && (step.Tiles[0] != 0 || step.Tiles[1] != 0))
            {
                // Hold where we are until the walk starts, then move steadily over the step.
                if (position[^1].At != at) position.Add(new PositionKey { At = at, Tile = new[] { x, y } });
                x += step.Tiles[0]; y += step.Tiles[1];
                position.Add(new PositionKey { At = at + ms, Tile = new[] { x, y }, Ease = "linear" });
            }
            at += ms;
        }
        clip.Duration = Math.Max(at, 50);
        var track = new ClipTrack { Role = role, Position = position, Facing = facing.Count > 0 ? facing : null, Pose = poses.Count > 0 ? poses : null };
        // A pose that ends the flipbook stays until the clip ends; then the scene's Clip beat decides (Hold).
        clip.Tracks = new List<ClipTrack> { track };
    }

    public static string? WalkFacing(int[] tiles) => tiles is not { Length: 2 } || (tiles[0] == 0 && tiles[1] == 0) ? null
        : Math.Abs(tiles[0]) >= Math.Abs(tiles[1]) ? tiles[0] < 0 ? "left" : "right" : tiles[1] < 0 ? "up" : "down";

    /// <summary>The character's walking pose for a facing, made from the rig's walk frames if the pack doesn't have it.</summary>
    public static string? EnsureWalkPose(PackLibrary library, string character, string facing)
    {
        if (library.RigFor(character) is not { Id: { } rigId } rig || rig.Kind.Equals("Farmer", StringComparison.OrdinalIgnoreCase)) return null;
        if (!rig.Frames.TryGetValue(facing, out var frames) || frames.Walk.Length == 0) return null;
        string id = PackDocument.ToContentId($"{rigId}-walk-{facing}");
        if (library.Pose(id) != null) return id;
        var pose = new Pose
        {
            Format = "1.0", Id = id, Kind = "character", For = rigId, FrameSize = (int[])rig.FrameSize.Clone(), Facing = facing, MirrorForOppositeFacing = false,
            Frames = frames.Walk.Select(index => new PoseFrame
            {
                Duration = 150,
                Layers = { new LiveLayer { Source = new LiveSource { Kind = "Npc", Npc = rig.Npc ?? character }, Part = "Full", SourceFrame = new SourceFrame { Index = index } } }
            }).ToList(),
        };
        pose.Loop = Enumerable.Range(0, pose.Frames.Count).ToArray();
        library.SavePose(pose);
        return id;
    }
}
