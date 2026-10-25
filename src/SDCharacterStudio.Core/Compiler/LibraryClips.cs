using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using SDCharacterStudio.Core.Loading;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Serialization;

namespace SDCharacterStudio.Core.Compiler;

/// <summary>
/// Library clips (doc 03 §17.1): reusable choreography whose tracks address roles and whose positions are relative.
/// A <c>Clip</c> beat casts the roles to actors; <see cref="Expand"/> turns each such use into an ordinary scene clip,
/// so the compiler and the runtime's clip player need nothing new.
/// </summary>
public static class LibraryClips
{
    /// <summary>Generated scene clip IDs start with this; designers' own clip IDs never need it.</summary>
    public const string GeneratedPrefix = "lib.";

    public static LibraryClip? Find(LoadedPack? pack, string? id) =>
        id == null ? null : pack?.Clips.Select(x => x.Value).FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>True when the beat plays a library clip (the scene has no clip of that ID).</summary>
    public static bool UsesLibraryClip(Cutscene scene, ClipBeat beat, LoadedPack? pack) =>
        beat.Clip != null && !(scene.Clips?.ContainsKey(beat.Clip) ?? false) && Find(pack, beat.Clip) != null;

    /// <summary>
    /// The scene with every library-clip use expanded into a generated scene clip. Returns the same instance when the
    /// scene uses no library clips; otherwise a deep copy, so the authored scene is never changed.
    /// </summary>
    public static Cutscene Expand(Cutscene scene, LoadedPack? pack)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (pack == null || pack.Clips.Count == 0 || !AllBeats(scene).OfType<ClipBeat>().Any(b => UsesLibraryClip(scene, b, pack)))
            return scene;
        var copy = DeepCopy(scene);
        copy.Clips ??= new(StringComparer.OrdinalIgnoreCase);
        var counters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var beat in AllBeats(copy).OfType<ClipBeat>().ToArray())
        {
            if (!UsesLibraryClip(copy, beat, pack) || copy.Clips.ContainsKey(beat.Clip!)) continue;
            var clip = Find(pack, beat.Clip)!;
            string id;
            do
            {
                counters[clip.Id!] = counters.TryGetValue(clip.Id!, out int n) ? n + 1 : 1;
                id = $"{GeneratedPrefix}{clip.Id!.ToLowerInvariant()}.{counters[clip.Id!]}";
            }
            while (copy.Clips.ContainsKey(id));
            copy.Clips[id] = ToSceneClip(clip, beat.Cast ?? new(), beat.Mirror, beat.PoseMap);
            beat.Clip = id;
            beat.Cast = null; beat.Mirror = false; beat.PoseMap = null;
        }
        return copy;
    }

    /// <summary>
    /// A deep copy that leaves absent values absent. Serialising nulls would bring JToken members (Actor.Costume, Emote…)
    /// back as JSON-null tokens rather than null, which the runtime reads as "set" (a farmer's initial costume, say).
    /// </summary>
    private static T DeepCopy<T>(T value) where T : class
    {
        var serializer = PackJson.CreateSerializer();
        serializer.NullValueHandling = NullValueHandling.Ignore;
        return JToken.FromObject(value, serializer).ToObject<T>(serializer)!;
    }

    /// <summary>One cast of a library clip as a scene clip: roles → actors, relative positions, optional mirror and pose swaps.</summary>
    public static Clip ToSceneClip(LibraryClip clip, IReadOnlyDictionary<string, string> cast, bool mirror, IReadOnlyDictionary<string, string>? poseMap)
    {
        ArgumentNullException.ThrowIfNull(clip);
        var result = new Clip { Duration = clip.Duration, StepFps = clip.StepFps, Loop = clip.Loop };
        foreach (var source in clip.Tracks)
        {
            if (source == null) continue;
            var track = DeepCopy(source);
            track.Actor = source.Role != null && cast.TryGetValue(source.Role, out var actor) ? actor : source.Actor ?? source.Role;
            track.Role = null;
            track.Camera = null;   // library clips leave the camera to the scene
            foreach (var key in track.Position ?? new())
            {
                key.Relative = true;
                if (mirror)
                {
                    if (key.Tile is { Length: 2 } tile) key.Tile = new[] { -tile[0], tile[1] };
                    if (key.Px is { Length: 2 } px) key.Px = new[] { -px[0], px[1] };
                }
            }
            if (mirror)
                foreach (var key in track.Facing ?? new()) key.Facing = MirrorFacing(key.Facing);
            if (poseMap != null)
                foreach (var key in track.Pose ?? new())
                    if (key.Pose != null && poseMap.TryGetValue(key.Pose, out var swapped)) key.Pose = swapped;
            result.Tracks.Add(track);
        }
        return result;
    }

    /// <summary>
    /// The reverse, for "Save to library": a scene clip's actors become roles (named after the actors) and absolute
    /// positions become offsets from each actor's first position (or <paramref name="startTiles"/> when it has none).
    /// </summary>
    public static LibraryClip FromSceneClip(string id, Clip clip, IReadOnlyDictionary<string, string> actorKinds, IReadOnlyDictionary<string, int[]>? startTiles = null)
    {
        ArgumentNullException.ThrowIfNull(clip);
        var result = new LibraryClip { Format = "1.0", Id = id, Duration = clip.Duration, StepFps = clip.StepFps, Loop = clip.Loop };
        foreach (var source in clip.Tracks)
        {
            if (source == null || source.Camera is { Count: > 0 } && source.Actor == null) continue;
            var track = DeepCopy(source);
            string role = RoleId(source.Actor ?? "actor");
            track.Role = role; track.Actor = null; track.Camera = null;
            int[]? origin = null;
            foreach (var key in track.Position ?? new())
            {
                if (key.Relative) continue;
                origin ??= startTiles != null && source.Actor != null && startTiles.TryGetValue(source.Actor, out var start) ? start : key.Tile ?? new[] { 0, 0 };
                if (key.Tile is { Length: 2 } tile) key.Tile = new[] { tile[0] - origin[0], tile[1] - origin[1] };
                key.Relative = true;
            }
            result.Tracks.Add(track);
            if (!result.Roles.Any(r => string.Equals(r.Id, role, StringComparison.OrdinalIgnoreCase)))
            {
                string kind = source.Actor != null && actorKinds.TryGetValue(source.Actor, out var k) ? k : "Any";
                result.Roles.Add(new ClipRole { Id = role, Kind = kind is "Npc" or "Farmer" ? kind : "Any" });
            }
        }
        return result;
    }

    public static string? MirrorFacing(string? facing) => facing?.ToLowerInvariant() switch
    {
        "left" => "right",
        "right" => "left",
        _ => facing
    };

    /// <summary>A role ID from an actor name: lowercase content ID.</summary>
    public static string RoleId(string actor)
    {
        var chars = actor.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '-').ToArray();
        var id = new string(chars).Trim('-', '.', '_');
        return id.Length == 0 ? "actor" : char.IsLetterOrDigit(id[0]) ? id : "r" + id;
    }

    public static IEnumerable<Beat> AllBeats(Cutscene scene) =>
        (scene.Beats ?? new()).Concat((scene.Branches ?? new()).Values.SelectMany(b => b ?? new())).Concat(scene.OnSkip ?? new()).Where(b => b != null);
}
