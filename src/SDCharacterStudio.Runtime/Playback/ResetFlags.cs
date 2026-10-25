namespace SDCharacterStudio.Runtime.Playback;

public static class ResetFlags
{
    public static IEnumerable<string> ForCutscene(string key, IEnumerable<string> flags) =>
        flags.Where(flag => flag.StartsWith("SDCS." + key + ".", StringComparison.OrdinalIgnoreCase)).ToArray();
}
