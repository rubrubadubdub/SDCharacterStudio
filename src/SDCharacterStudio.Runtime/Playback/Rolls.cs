using StardewValley;

namespace SDCharacterStudio.Runtime.Playback;

/// <summary>
/// Random picks for scene logic (D-51). By default a beat rolls with the game's day-and-save random, seeded by the beat,
/// so reloading the morning lands the same way and different beats roll differently; <c>fresh</c> rolls anew each time.
/// </summary>
public static class Rolls
{
    public static Random For(string sceneKey, string beatId, bool fresh) =>
        fresh ? Game1.random : Utility.CreateDaySaveRandom(Seed(sceneKey), Seed(beatId));

    /// <summary>The index of the weight picked, in proportion to the weights (negative counts as 0); -1 when all are 0.</summary>
    public static int Pick(IReadOnlyList<int> weights, Random random)
    {
        int total = weights.Sum(w => Math.Max(0, w));
        if (total <= 0) return -1;
        int roll = random.Next(total);
        for (int i = 0; i < weights.Count; i++)
        {
            roll -= Math.Max(0, weights[i]);
            if (roll < 0) return i;
        }
        return weights.Count - 1;
    }

    /// <summary>A stable number for a name (FNV-1a), the same in every process; string.GetHashCode isn't.</summary>
    private static double Seed(string text)
    {
        uint hash = 2166136261;
        foreach (char c in text) { hash ^= c; hash *= 16777619; }
        return hash % 1_000_000;
    }
}
