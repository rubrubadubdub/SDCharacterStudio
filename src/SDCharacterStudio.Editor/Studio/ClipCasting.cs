using SDCharacterStudio.Core.Models;

namespace SDCharacterStudio.Editor.Studio;

/// <summary>Casting a library clip's roles onto a scene's actors (doc 03 §17.1).</summary>
public static class ClipCasting
{
    /// <summary>
    /// Every role gets an actor of a matching kind: the preferred actor first (the one selected or dropped on), then an
    /// actor playing the character the role was previewed on, then any free actor. Roles nobody fits stay uncast.
    /// </summary>
    public static Dictionary<string, string> Cast(LibraryClip clip, Cutscene scene, string? preferredActor = null, IReadOnlyDictionary<string, string>? previewCast = null)
    {
        var cast = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool Fits(ClipRole role, Actor actor) => role.Kind switch { "Farmer" => actor.Kind == "Farmer", "Npc" => actor.Kind != "Farmer", _ => true };
        var actors = scene.Actors.Where(a => a.Id != null).ToList();
        foreach (var role in clip.Roles.Where(r => r.Id != null))
        {
            Actor? pick = null;
            if (preferredActor != null && cast.Count == 0)
                pick = actors.FirstOrDefault(a => string.Equals(a.Id, preferredActor, StringComparison.OrdinalIgnoreCase) && Fits(role, a));
            if (pick == null && previewCast?.GetValueOrDefault(role.Id!) is { } character)
                pick = actors.FirstOrDefault(a => !used.Contains(a.Id!) && Fits(role, a) && (string.Equals(a.Npc ?? a.Id, character, StringComparison.OrdinalIgnoreCase) || (character == "farmer" && a.Kind == "Farmer")));
            pick ??= actors.FirstOrDefault(a => !used.Contains(a.Id!) && Fits(role, a));
            if (pick == null) continue;
            cast[role.Id!] = pick.Id!;
            used.Add(pick.Id!);
        }
        return cast;
    }
}
