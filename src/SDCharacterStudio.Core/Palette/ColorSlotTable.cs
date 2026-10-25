using System.Globalization;

namespace SDCharacterStudio.Core.Palette;

public readonly record struct ColorRamp(uint Dark, uint Mid, uint Light);
public sealed class ColorSlotTable
{
    private readonly Dictionary<string, ColorRamp> ramps = new(StringComparer.OrdinalIgnoreCase);
    private ColorSlotTable() { }

    /// <summary>Resolves fixed RGB values locally; external sources must supply already resolved ramps.</summary>
    public static ColorSlotTable FromMappings(IReadOnlyDictionary<string, string> mappings, Func<string, ColorRamp>? resolveColor = null)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        var result = new ColorSlotTable();
        foreach (var mapping in mappings)
        {
            if (!ColorSlots.Names.Contains(mapping.Key, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException($"Unknown color slot '{mapping.Key}'.", nameof(mappings));
            if (mapping.Value is null)
                throw new ArgumentException($"Color slot '{mapping.Key}' has no source.", nameof(mappings));
            if (mapping.Key.Equals("Erase", StringComparison.OrdinalIgnoreCase))
                continue;
            if (mapping.Value.StartsWith('#'))
            {
                if (mapping.Value.Length != 7 || !uint.TryParse(mapping.Value.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rgb))
                    throw new ArgumentException("A fixed color must be #RRGGBB.", nameof(mappings));
                result.ramps[mapping.Key] = FromFixedColor(rgb);
            }
            else
                result.ramps[mapping.Key] = resolveColor?.Invoke(mapping.Value)
                    ?? throw new ArgumentException($"No resolver supplied for color source '{mapping.Value}'.", nameof(resolveColor));
        }
        return result;
    }

    internal bool TryGet(string slot, out ColorRamp ramp) => ramps.TryGetValue(slot, out ramp);

    private static ColorRamp FromFixedColor(uint rgb)
    {
        uint Shade(bool light)
        {
            uint Channel(int shift)
            {
                var c = (rgb >> shift) & 255;
                return (uint)Math.Clamp(Math.Round(light ? c + (255 - c) * 0.35 : c * 0.72, MidpointRounding.AwayFromZero), 0, 255);
            }
            return 0xFF000000 | Channel(16) << 16 | Channel(8) << 8 | Channel(0);
        }
        return new(Shade(false), rgb | 0xFF000000, Shade(true));
    }
}
