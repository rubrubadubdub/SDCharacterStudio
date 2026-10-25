using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SDCharacterStudio.Core;

public static class Ids
{
    public const string RuntimeModId = "Player.SDCharacterStudio";
    public const string BridgeModId = "Player.SDCharacterStudio.Bridge";
    /// <summary>The editor's preview copy of a pack is named after it with this suffix.</summary>
    public const string PreviewSuffix = ".DevPreview";
    /// <summary>The pack a scene's story belongs to: a preview copy keeps the real pack's flags and numbers.</summary>
    public static string StoryPack(string packId) => packId.EndsWith(PreviewSuffix, StringComparison.OrdinalIgnoreCase) ? packId[..^PreviewSuffix.Length] : packId;
    /// <summary>The stat a story number is kept in: SDCS.&lt;pack&gt;.&lt;name&gt; (D-51).</summary>
    public static string NumberStat(string packId, string name) => $"SDCS.{StoryPack(packId)}.{name}";
    /// <summary>The trigger action a skipped Change-a-number beat runs (named per engine copy).</summary>
    public static string SkipNumberAction => EngineNames.Command("SkipNumber");
    /// <summary>The story flag a random item sets when it remembers which one was given: SDCS.&lt;pack&gt;.&lt;scene&gt;.got-&lt;item&gt;.</summary>
    public static string PickedItemFlag(string packId, string sceneId, string item) =>
        $"SDCS.{StoryPack(packId)}.{sceneId}.got-{new string(item.Where(c => char.IsLetterOrDigit(c) || c is '_' or '.' or '-').ToArray())}";
    /// <summary>The trigger action a skipped Costume beat runs (named per engine copy, see <see cref="EngineNames"/>).</summary>
    public static string SkipCostumeAction => EngineNames.Command("SkipCostume");

    public static IReadOnlyDictionary<string, int> Facings { get; } = ReadOnly(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        { ["up"] = 0, ["right"] = 1, ["down"] = 2, ["left"] = 3 });
    public static IReadOnlyDictionary<string, int> Emotes { get; } = ReadOnly(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        { ["empty"] = 4, ["question"] = 8, ["angry"] = 12, ["exclamation"] = 16, ["heart"] = 20,
          ["sleep"] = 24, ["sad"] = 28, ["happy"] = 32, ["x"] = 36, ["pause"] = 40,
          ["game"] = 52, ["note"] = 56, ["blush"] = 60 });
    public static IReadOnlyDictionary<string, string> Emotions { get; } = ReadOnly(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        { ["neutral"] = "", ["happy"] = "$h", ["sad"] = "$s", ["unique"] = "$u", ["love"] = "$l", ["angry"] = "$a" });

    public static string PackHash(string uniqueId)
    {
        ArgumentNullException.ThrowIfNull(uniqueId);
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(uniqueId.ToLowerInvariant())))[..8].ToLowerInvariant();
    }

    public static bool TryEmote(string? value, out int id) => Emotes.TryGetValue(value ?? "", out id)
        || (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id >= 0);
    public static bool TryEmotion(string? value, out string code)
    {
        if (string.IsNullOrEmpty(value)) { code = ""; return true; }
        if (Emotions.TryGetValue(value, out var named)) { code = named; return true; }
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int frame) && frame >= 0)
        { code = "$" + frame.ToString(CultureInfo.InvariantCulture); return true; }
        code = ""; return false;
    }

    private static IReadOnlyDictionary<string, T> ReadOnly<T>(Dictionary<string, T> values) => new ReadOnlyDictionary<string, T>(values);
}
