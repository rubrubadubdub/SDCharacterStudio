using SDCharacterStudio.Core.Compiler;
using SDCharacterStudio.Core.Models;

namespace SDCharacterStudio.Editor.ViewModels;

/// <summary>The Clip beat (doc 03 §6.4, §17.1): a scene's own clip, or a library animation cast onto this scene's actors.</summary>
public sealed partial class InspectorViewModel
{
    private void BuildClipBeat(Cutscene scene, ClipBeat clip)
    {
        var doc = shell.Document!;
        var options = scene.Clips.Keys.Select(k => new ChoiceItem(k, k, "this scene's clip"))
            .Concat(doc.Library.Clips.Where(c => c.Value.Id != null && !scene.Clips.ContainsKey(c.Value.Id!))
                .Select(c => new ChoiceItem(c.Value.Id, string.IsNullOrWhiteSpace(c.Value.Title) ? c.Value.Id! : c.Value.Title!, "animation · " + string.Join(", ", c.Value.Roles.Select(r => r.Id)))));
        Fields.Add(new ChoiceField("Clip", "Clip", options, () => clip.Clip, v =>
        {
            clip.Clip = v;
            // A library animation starts cast onto whoever fits; a scene clip needs no cast.
            var library = LibraryClips.Find(doc.Library, v);
            clip.Cast = library != null && !scene.Clips.ContainsKey(v ?? "") ? ClipCasting.Cast(library, scene) : null;
            clip.PoseMap = null; clip.Mirror = false;
        }, Commit) { Rebuild = true, Help = "Animations are made in the Character Studio. A scene's own clip moves this scene's actors directly." });

        var animation = clip.Clip != null && !scene.Clips.ContainsKey(clip.Clip) ? LibraryClips.Find(doc.Library, clip.Clip) : null;
        if (animation != null)
        {
            Fields.Add(new SectionField("Cast", "Who plays each role in this scene."));
            foreach (var role in animation.Roles.Where(r => r.Id != null))
            {
                string id = role.Id!;
                var actors = scene.Actors.Where(a => a.Id != null && (role.Kind == "Any" || (role.Kind == "Farmer") == (a.Kind == "Farmer")))
                    .Select(a => new ChoiceItem(a.Id, a.Kind == "Farmer" ? "Player (farmer)" : Catalog?.Character(a.Npc ?? a.Id!)?.Label ?? a.Id!));
                Fields.Add(new ChoiceField(id, $"Cast.{id}", new[] { new ChoiceItem(null, "Nobody yet") }.Concat(actors), () => clip.Cast?.GetValueOrDefault(id), v =>
                {
                    clip.Cast ??= new(StringComparer.OrdinalIgnoreCase);
                    if (v == null) clip.Cast.Remove(id); else clip.Cast[id] = v;
                }, Commit) { Help = role.Kind switch { "Farmer" => "This role is the player.", "Npc" => "A character (not the player).", _ => null } });
            }
            Fields.Add(new BoolField("Mirror", "Mirror", () => clip.Mirror, v => clip.Mirror = v, Commit) { Description = "Flip it left to right: moves and left/right facings swap." });
            var poses = animation.Tracks.SelectMany(t => t.Pose ?? new()).Select(k => k.Pose).Where(p => p != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (poses.Count > 0)
            {
                Fields.Add(new SectionField("Pose swaps", "Poses are drawn for one character. Swap in a copy made for whoever plays the role (pose menu › Make a copy for…)."));
                var targets = doc.Library.Poses.Where(p => p.Value.Id != null && p.Value.Kind == "character").Select(p => new ChoiceItem(p.Value.Id, p.Value.Id!, p.Value.For)).ToList();
                foreach (var pose in poses)
                {
                    string from = pose!;
                    Fields.Add(new ChoiceField(from, $"PoseMap.{from}", new[] { new ChoiceItem(null, "Keep it") }.Concat(targets.Where(t => t.Value != from)), () => clip.PoseMap?.GetValueOrDefault(from), v =>
                    {
                        clip.PoseMap ??= new(StringComparer.OrdinalIgnoreCase);
                        if (v == null) clip.PoseMap.Remove(from); else clip.PoseMap[from] = v;
                        if (clip.PoseMap.Count == 0) clip.PoseMap = null;
                    }, Commit));
                }
            }
            Fields.Add(new ActionField("Edit the animation in Character Studio", () => { shell.CharacterStudio.OpenItem(Studio.LibraryKind.Clip, animation.Id!); shell.IsCharacterStudio = true; }));
        }
        else if (clip.Clip != null && scene.Clips.ContainsKey(clip.Clip))
            Fields.Add(new ActionField("Edit the clip in Character Studio", () => shell.CharacterStudio.OpenSceneClip(scene.Id!, clip.Clip!)));
        else
            Fields.Add(new ActionField("Make a new clip for this scene", () =>
            {
                if (shell.CharacterStudio.NewSceneClip() is { } made) Commit("Use clip " + made, () => clip.Clip = made, true);
            }));
        Fields.Add(new BoolField("Hold the last frame afterwards", "Hold", () => clip.Hold, v => clip.Hold = v, Commit));
    }
}
