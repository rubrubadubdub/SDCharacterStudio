namespace SDCharacterStudio.Core;

/// <summary>
/// The names this copy of the engine registers in the game: event commands, trigger actions and save-data keys.
/// The framework mod uses the published <c>SDCS_</c> names. An engine embedded in another mod (Q-17) gets names of its
/// own, derived from that mod's ID, because the game keeps one handler per event command: two copies registering
/// <c>SDCS_Clip</c> would silently replace each other. Each embedded copy is loaded separately, so this static is
/// per copy.
/// </summary>
public static class EngineNames
{
    public const string FrameworkPrefix = "SDCS";

    /// <summary>The prefix of every event command and trigger action, without the underscore.</summary>
    public static string Prefix { get; private set; } = FrameworkPrefix;

    /// <summary>The mod that owns this copy (the framework's own ID, or the embedding mod's).</summary>
    public static string OwnerId { get; private set; } = Ids.RuntimeModId;

    public static bool IsEmbedded => OwnerId != Ids.RuntimeModId;

    /// <summary><c>SDCS_Clip</c> in the framework, <c>SDCS1a2b3c4d_Clip</c> in an embedded copy.</summary>
    public static string Command(string verb) => Prefix + "_" + verb;

    /// <summary>A key in the player's save data (modData), owned by this copy.</summary>
    public static string DataKey(string name) => IsEmbedded ? $"{OwnerId}/SDCS.{name}" : $"{Ids.RuntimeModId}/{name}";

    /// <summary>Use names of this embedding mod's own. Call once, before anything is registered.</summary>
    public static void UseEmbedded(string ownerModId)
    {
        if (string.IsNullOrWhiteSpace(ownerModId)) throw new ArgumentException("The embedding mod needs an ID.", nameof(ownerModId));
        OwnerId = ownerModId;
        Prefix = FrameworkPrefix + Ids.PackHash(ownerModId);
    }
}
