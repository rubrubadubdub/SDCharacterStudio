using StardewModdingAPI;
using StardewValley;

namespace SDCharacterStudio.Runtime.Conditions;

/// <summary>
/// Game state queries for installs that differ. SVE and other mods redraw vanilla maps, so a pack can stage
/// one variant per install: <c>"Conditions": "Player.SDCharacterStudio_HAS_MOD FlashShifter.StardewValleyExpandedCP"</c>
/// on one cutscene and the <c>!</c>-negated query on the other (doc 15 §3).
/// </summary>
internal static class ModQueries
{
    public const string HasMod = "Player.SDCharacterStudio_HAS_MOD";

    /// <summary>True when every listed mod ID is loaded.</summary>
    /// <remarks>Every engine copy (the framework and each embedding mod) asks the same question, so the first copy to load
    /// registers it and the others use that one; the game refuses a second registration.</remarks>
    public static void Register(IModRegistry mods)
    {
        if (!GameStateQuery.Exists(HasMod)) GameStateQuery.Register(HasMod, (query, _) => HasAll(mods, query));
    }

    private static bool HasAll(IModRegistry mods, string[] query)
    {
        if (query.Length < 2) return GameStateQuery.Helpers.ErrorResult(query, "expected at least one mod ID");
        for (int i = 1; i < query.Length; i++)
            if (!mods.IsLoaded(query[i])) return false;
        return true;
    }
}
