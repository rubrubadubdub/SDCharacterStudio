using SDCharacterStudio.Editor.Project;

namespace SDCharacterStudio.Editor.Studio;

/// <summary>
/// What the studio calls an item: "abigail-wave" is "Wave" on Abigail's shelf and tab (the ID stays in tooltips and in
/// the pack). The character's name comes off the front, dashes become spaces.
/// </summary>
public static class LibraryNames
{
    public static string Friendly(string id, params string?[] owners)
    {
        string name = id;
        foreach (var owner in owners)
        {
            if (string.IsNullOrWhiteSpace(owner)) continue;
            string prefix = PackDocument.ToContentId(owner) + "-";
            if (name.Length > prefix.Length && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { name = name[prefix.Length..]; break; }
        }
        name = name.Replace('-', ' ').Replace('_', ' ').Trim();
        return name.Length == 0 ? id : char.ToUpperInvariant(name[0]) + name[1..];
    }
}
