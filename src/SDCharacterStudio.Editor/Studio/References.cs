using SDCharacterStudio.Core.Models;

namespace SDCharacterStudio.Editor.Studio;

/// <summary>Follow a library item's rename through the scenes and library that use it (must run inside an edit).</summary>
public static class References
{
    public static IEnumerable<Beat> AllBeats(Cutscene scene) =>
        scene.Beats.Concat(scene.Branches.Values.SelectMany(b => b)).Concat(scene.OnSkip ?? new List<Beat>());

    public static void RenamePose(PackLibrary library, PackDocument document, string old, string next)
    {
        bool Same(string? id) => string.Equals(id, old, StringComparison.OrdinalIgnoreCase);
        void Keys(IEnumerable<ClipTrack> tracks) { foreach (var key in tracks.SelectMany(t => t.Pose ?? new())) if (Same(key.Pose)) key.Pose = next; }
        foreach (var scene in document.Cutscenes.Select(c => c.Scene))
        {
            foreach (var actor in scene.Actors) if (Same(actor.Sprite?.Pose)) actor.Sprite!.Pose = next;
            foreach (var beat in AllBeats(scene))
                switch (beat)
                {
                    case PoseBeat pose when Same(pose.Pose): pose.Pose = next; break;
                    case CostumeBeat costume when Same(costume.Sprite?.Pose): costume.Sprite!.Pose = next; break;
                    case ClipBeat { PoseMap: { } map } clip:
                        clip.PoseMap = map.ToDictionary(p => Same(p.Key) ? next : p.Key, p => Same(p.Value) ? next : p.Value, StringComparer.OrdinalIgnoreCase);
                        break;
                }
            Keys(scene.Clips.Values.SelectMany(c => c.Tracks));
        }
        foreach (var item in library.Items(LibraryKind.Clip).ToArray())
        {
            if (library.Clip(item.Id) is not { } clip || !clip.Tracks.SelectMany(t => t.Pose ?? new()).Any(k => Same(k.Pose))) continue;
            Keys(clip.Tracks);
            library.SaveClip(clip);
        }
    }

    /// <summary>A mask's new ID in the poses and portraits that cut with it and the rigs that use it as a head mask.</summary>
    public static void RenameMask(PackLibrary library, string old, string next)
    {
        bool Same(string? id) => string.Equals(id, old, StringComparison.OrdinalIgnoreCase);
        foreach (var item in library.Items(LibraryKind.Pose).ToArray())
            if (library.Pose(item.Id) is { } pose && pose.Frames.SelectMany(f => f.Layers).Any(l => Same(l.Mask)))
            {
                foreach (var layer in pose.Frames.SelectMany(f => f.Layers)) if (Same(layer.Mask)) layer.Mask = next;
                library.SavePose(pose);
            }
        foreach (var item in library.Items(LibraryKind.Portrait).ToArray())
            if (library.Portrait(item.Id) is { } portrait && portrait.Emotions.Values.SelectMany(e => e.Layers).Any(l => Same(l.Mask)))
            {
                foreach (var layer in portrait.Emotions.Values.SelectMany(e => e.Layers)) if (Same(layer.Mask)) layer.Mask = next;
                library.SavePortrait(portrait);
            }
        foreach (var item in library.Items(LibraryKind.Rig).ToArray())
            if (library.Rig(item.Id) is { } rig && (rig.HeadMask.Values.Any(Same) || Same(rig.Portrait?.HeadMask)))
            {
                rig.HeadMask = rig.HeadMask.ToDictionary(p => p.Key, p => Same(p.Value) ? next : p.Value, StringComparer.OrdinalIgnoreCase);
                if (rig.Portrait != null && Same(rig.Portrait.HeadMask)) rig.Portrait.HeadMask = next;
                library.SaveRig(rig);
            }
    }

    public static void RenameExpression(PackLibrary library, PackDocument document, string old, string next)
    {
        bool Same(string? id) => string.Equals(id, old, StringComparison.OrdinalIgnoreCase);
        void Keys(IEnumerable<ClipTrack> tracks) { foreach (var key in tracks.SelectMany(t => t.Expression ?? new())) if (Same(key.Expression)) key.Expression = next; }
        foreach (var scene in document.Cutscenes.Select(c => c.Scene))
        {
            foreach (var say in AllBeats(scene).OfType<SayBeat>()) if (Same(say.Expression)) say.Expression = next;
            Keys(scene.Clips.Values.SelectMany(c => c.Tracks));
        }
        foreach (var item in library.Items(LibraryKind.Clip).ToArray())
            if (library.Clip(item.Id) is { } clip && clip.Tracks.SelectMany(t => t.Expression ?? new()).Any(k => Same(k.Expression)))
            {
                Keys(clip.Tracks);
                library.SaveClip(clip);
            }
    }

    public static void RenameClip(PackLibrary library, PackDocument document, string old, string next)
    {
        foreach (var beat in document.Cutscenes.SelectMany(c => AllBeats(c.Scene)).OfType<ClipBeat>())
            if (string.Equals(beat.Clip, old, StringComparison.OrdinalIgnoreCase)) beat.Clip = next;
    }

    /// <summary>Scenes and clips that use a library item, for "used by" lists and delete warnings.</summary>
    public static IReadOnlyList<string> UsesOf(PackLibrary library, PackDocument document, LibraryKind kind, string id)
    {
        bool Same(string? value) => string.Equals(value, id, StringComparison.OrdinalIgnoreCase);
        var uses = new List<string>();
        foreach (var scene in document.Cutscenes.Select(c => c.Scene))
        {
            bool used = kind switch
            {
                LibraryKind.Pose => scene.Actors.Any(a => Same(a.Sprite?.Pose)) || AllBeats(scene).Any(b => b is PoseBeat p && Same(p.Pose) || b is CostumeBeat c && Same(c.Sprite?.Pose) || b is ClipBeat { PoseMap: { } m } && m.Values.Any(Same))
                    || scene.Clips.Values.SelectMany(c => c.Tracks).SelectMany(t => t.Pose ?? new()).Any(k => Same(k.Pose)),
                LibraryKind.Clip => AllBeats(scene).OfType<ClipBeat>().Any(b => Same(b.Clip)),
                LibraryKind.Expression => AllBeats(scene).OfType<SayBeat>().Any(b => Same(b.Expression)) || scene.Clips.Values.SelectMany(c => c.Tracks).SelectMany(t => t.Expression ?? new()).Any(k => Same(k.Expression)),
                LibraryKind.Portrait => AllBeats(scene).Any(b => b is SayBeat s && Same(s.Portrait) || b is CostumeBeat c && Same(c.Portrait)),
                _ => false
            };
            if (used) uses.Add("scene " + (scene.Title ?? scene.Id));
        }
        if (kind is LibraryKind.Pose or LibraryKind.Expression)
            foreach (var item in library.Items(LibraryKind.Clip))
                if (library.Clip(item.Id) is { } clip && clip.Tracks.Any(t => kind == LibraryKind.Pose ? (t.Pose ?? new()).Any(k => Same(k.Pose)) : (t.Expression ?? new()).Any(k => Same(k.Expression))))
                    uses.Add("animation " + item.Title);
        if (kind == LibraryKind.Mask)
            foreach (var item in library.Items(LibraryKind.Pose))
                if (library.Pose(item.Id) is { } pose && pose.Frames.SelectMany(f => f.Layers).Any(l => Same(l.Mask)))
                    uses.Add("pose " + item.Id);
        return uses;
    }
}
