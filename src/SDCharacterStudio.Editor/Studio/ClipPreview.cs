using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Runtime.Animation;

namespace SDCharacterStudio.Editor.Studio;

/// <summary>Where one actor is and what it shows at a moment of a clip (doc 03 §7, doc 04 §6).</summary>
public sealed record ActorSample(string Actor, WorldPoint Position, string Facing, string? Pose, int PoseFrame, bool Visible, string? Expression, string? Emote, string? Effect,
    IReadOnlyDictionary<string, string> Parts, IReadOnlyDictionary<string, bool> HiddenParts);

/// <summary>
/// Samples a scene clip the way the runtime plays it: positions eased between keys (relative keys from the actor's start),
/// discrete tracks from the last key at or before the time, poses advanced from their key. Library clips are first
/// expanded with <see cref="Core.Compiler.LibraryClips.ToSceneClip"/>, exactly as the compiler does.
/// </summary>
public static class ClipPreview
{
    public const int EmoteMilliseconds = 1000;
    public const int EffectMilliseconds = 600;

    /// <summary>The time the stage shows: StepFps holds poses and positions on steps ("on twos").</summary>
    public static double VisualTime(double time, int stepFps) => stepFps > 0 ? Math.Floor(time / (1000d / stepFps)) * (1000d / stepFps) : time;

    public static ActorSample Sample(ClipTrack? track, string actor, double time, WorldPoint origin, string startFacing, Func<string, Pose?> poses)
    {
        T? Last<T>(IEnumerable<T>? keys) where T : Keyframe => keys?.Where(k => k.At <= time).OrderBy(k => k.At).LastOrDefault();
        var positions = track?.Position?.OrderBy(k => k.At).ToList() ?? new List<PositionKey>();
        var position = AnimationMath.PositionAt(positions, time, origin);
        string facing = Last(track?.Facing)?.Facing ?? startFacing;
        bool visible = Last(track?.Visible)?.Value ?? true;
        string? poseId = null; int frame = 0;
        if (Last(track?.Pose) is { Pose: { } id } poseKey && poses(id) is { } pose)
        {
            poseId = id;
            try
            {
                var timeline = new PoseTimeline(pose, poseKey.Play, poseKey.Speed);
                timeline.Advance(Math.Max(0, time - poseKey.At));
                frame = timeline.Frame;
            }
            catch (ArgumentException) { frame = 0; }
        }
        string? expression = Last(track?.Expression) is { } e && (e.Duration == null || time < e.At + e.Duration) ? e.Expression : null;
        string? emote = track?.Emote?.Where(k => k.At <= time && time < k.At + EmoteMilliseconds).Select(k => k.Emote?.ToString()).LastOrDefault();
        string? effect = track?.Effect?.Where(k => k.At <= time && time < k.At + (k.Duration ?? EffectMilliseconds)).Select(k => k.Effect).LastOrDefault();
        var parts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var hidden = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in (track?.Parts ?? new()).Where(k => k.At <= time).OrderBy(k => k.At))
        {
            foreach (var (part, value) in key.Set ?? new()) parts[part] = value.ToString();
            foreach (var (part, shown) in key.Visible ?? new()) hidden[part] = !shown;
        }
        return new ActorSample(actor, position, facing, poseId, frame, visible, expression, emote, effect, parts, hidden);
    }

    /// <summary>World position (4× pixels) of a tile and pixel offset.</summary>
    public static WorldPoint World(int[]? tile, int[]? px = null) => new((tile?.ElementAtOrDefault(0) ?? 0) * 64 + (px?.ElementAtOrDefault(0) ?? 0) * 4, (tile?.ElementAtOrDefault(1) ?? 0) * 64 + (px?.ElementAtOrDefault(1) ?? 0) * 4);
}
