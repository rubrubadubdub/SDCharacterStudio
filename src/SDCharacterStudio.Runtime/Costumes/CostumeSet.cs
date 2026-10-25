using System.Globalization;
using Newtonsoft.Json.Linq;

namespace SDCharacterStudio.Runtime.Costumes;

/// <summary>Parses all costume values before any game state is changed.</summary>
public static class CostumeSet
{
    public static readonly IReadOnlyList<string> Keys = Array.AsReadOnly(new[]
        { "Hat", "Shirt", "Pants", "Shoes", "Hair", "HairColor", "Accessory", "Skin", "EyeColor" });

    public static IReadOnlyDictionary<string, CostumeValue> Parse(IReadOnlyDictionary<string, JToken> values)
    {
        var result = new Dictionary<string, CostumeValue>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, token) in values)
        {
            var canonical = Keys.FirstOrDefault(x => x.Equals(key, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"Unknown farmer costume part '{key}'.");
            if (result.ContainsKey(canonical)) throw new ArgumentException($"Duplicate costume part '{key}'.");
            string? value = token?.Type == JTokenType.String ? token.Value<string>() : null;
            if (value == "original") { result.Add(canonical, new(CostumeValueKind.Original)); continue; }
            if (value == "none")
            {
                if (canonical is not ("Hat" or "Shoes" or "Accessory"))
                    throw new ArgumentException($"'{canonical}' doesn't support 'none'; use a Parts Visible mask to hide it.");
                result.Add(canonical, new(CostumeValueKind.None)); continue;
            }
            if (canonical is "Hair" or "Accessory" or "Skin")
            {
                if (token?.Type != JTokenType.Integer || !int.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
                    || index < (canonical == "Accessory" ? -1 : 0)
                    || (canonical == "Accessory" && index >= 30) || (canonical == "Skin" && index >= 24))
                    throw new ArgumentException($"Invalid {canonical} index '{token}'.");
                result.Add(canonical, new(CostumeValueKind.Index, Index: index)); continue;
            }
            if (canonical is "HairColor" or "EyeColor")
            {
                if (value == null || value.Length is not (7 or 9) || value[0] != '#'
                    || !uint.TryParse(value.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint rgba))
                    throw new ArgumentException($"'{canonical}' must be #RRGGBB or #RRGGBBAA.");
                if (value.Length == 7) rgba = (rgba << 8) | 255;
                result.Add(canonical, new(CostumeValueKind.Color, Rgba: rgba)); continue;
            }
            string prefix = canonical switch { "Hat" => "(H)", "Shirt" => "(S)", "Pants" => "(P)", _ => "(B)" };
            if (string.IsNullOrWhiteSpace(value) || (value.StartsWith('(') && !value.StartsWith(prefix, StringComparison.Ordinal))
                || (!value.StartsWith(prefix, StringComparison.Ordinal) && canonical is not ("Shirt" or "Pants")))
                throw new ArgumentException($"'{canonical}' requires a {prefix} item ID.");
            string id = value.StartsWith(prefix, StringComparison.Ordinal) ? value : prefix + value;
            if (id.Length == prefix.Length || id.Contains('/') || id.Contains('\\'))
                throw new ArgumentException($"Invalid {canonical} item ID '{value}'.");
            result.Add(canonical, new(CostumeValueKind.Item, ItemId: id));
        }
        return result;
    }
}

public enum CostumeValueKind { Original, None, Item, Index, Color }
public sealed record CostumeValue(CostumeValueKind Kind, string? ItemId = null, int Index = 0, uint Rgba = 0);
