using System.Security.Cryptography;
using System.Text;
using SDCharacterStudio.Core.Models;

namespace SDCharacterStudio.Core.Audio;

public static class PackMusic
{
    public const string ReferencePrefix = "music:";
    public static string Reference(string id) => ReferencePrefix + id;
    public static bool TryGetId(string? cue, out string id)
    {
        id = "";
        if (string.IsNullOrWhiteSpace(cue) || !cue.StartsWith(ReferencePrefix, StringComparison.OrdinalIgnoreCase)) return false;
        id = cue[ReferencePrefix.Length..];
        return id.Length > 0;
    }
    public static string CueName(string packId, string id) => "SDCSMusic_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(packId.ToLowerInvariant() + "\n" + id.ToLowerInvariant()))).ToLowerInvariant()[..24];
    public static string ResolveCue(string packId, string cue) => TryGetId(cue, out var id) ? CueName(packId, id) : cue;
}
