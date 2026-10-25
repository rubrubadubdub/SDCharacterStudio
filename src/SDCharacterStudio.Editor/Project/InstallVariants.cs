using SDCharacterStudio.Core.Models;

namespace SDCharacterStudio.Editor.Project;

/// <summary>
/// A scene split by install (doc 15 §3): the original plays only with a mod installed ("Player.SDCharacterStudio_HAS_MOD
/// mod"), its variant only without it ("!Player.SDCharacterStudio_HAS_MOD mod"). Read from the scenes' conditions, so
/// the stage knows which half it shows and never makes a second variant.
/// </summary>
public static class InstallVariants
{
    /// <summary>The runtime's game state query (its HasMod).</summary>
    public const string HasMod = "Player.SDCharacterStudio_HAS_MOD";
    public static string Query(string mod) => HasMod + " " + mod;

    /// <summary>The mod a scene is split on, and whether it's the half that plays without it.</summary>
    public static (string Mod, bool Without)? Of(Cutscene scene)
    {
        foreach (var part in (scene.Trigger?.Conditions ?? "").Split(','))
        {
            string condition = part.Trim();
            bool without = condition.StartsWith('!');
            if (without) condition = condition[1..].TrimStart();
            if (condition.StartsWith(HasMod + " ", StringComparison.Ordinal))
                return (condition[(HasMod.Length + 1)..].Trim(), without);
        }
        return null;
    }

    /// <summary>The other half of a split scene: same mod, the other side (the one named after it first).</summary>
    public static CutsceneDocument? Partner(PackDocument document, Cutscene scene)
    {
        if (Of(scene) is not { } me) return null;
        return document.Cutscenes
            .Where(c => !ReferenceEquals(c.Scene, scene) && Of(c.Scene) is { } other && string.Equals(other.Mod, me.Mod, StringComparison.OrdinalIgnoreCase) && other.Without != me.Without)
            .OrderByDescending(c => me.Without ? (scene.Id ?? "").StartsWith(c.Scene.Id ?? "\0", StringComparison.OrdinalIgnoreCase)
                                               : (c.Scene.Id ?? "").StartsWith(scene.Id ?? "\0", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
    }
}
