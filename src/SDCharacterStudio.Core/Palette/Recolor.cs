using System.Globalization;
using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Models;

namespace SDCharacterStudio.Core.Palette;

/// <summary>
/// Live recolouring (after Pixelorama's hue/saturation and gradient-map effects, but live): her pixels near one of her
/// colours take on a new colour. Matching is by hue (greys by brightness), so every shade of her dress changes, however
/// many shades a sprite mod gives it; each keeps its place between dark and light.
/// </summary>
public static class Recolor
{
    /// <summary>Recolour <paramref name="pixels"/> in place. A rule whose colours can't be resolved is skipped.</summary>
    public static void Apply(RgbaImage pixels, IReadOnlyList<RecolorRule>? rules, Func<string, ColorRamp>? resolveColor)
    {
        if (rules == null || rules.Count == 0) return;
        var prepared = new List<(double FH, double FS, double FV, bool FromGrey, bool FromSkin, double TH, double TS, double TV, bool ToGrey, int Range)>();
        foreach (var rule in rules)
        {
            if (rule == null || Mid(rule.From, resolveColor) is not { } from || Mid(rule.To, resolveColor) is not { } to) continue;
            var (fh, fs, fv) = Shading.Hsv(from);
            var (th, ts, tv) = Shading.Hsv(to);
            prepared.Add((fh, fs, fv, fs < 0.12, ColorSampler.IsSkinTone(from), th, ts, tv, ts < 0.06, Math.Clamp(rule.Range, 1, 180)));
        }
        if (prepared.Count == 0) return;
        var cache = new Dictionary<uint, uint>();
        for (int i = 0; i < pixels.Pixels.Length; i++)
        {
            uint pixel = pixels.Pixels[i];
            if (pixel >> 24 == 0) continue;
            if (cache.TryGetValue(pixel, out var known)) { pixels.Pixels[i] = known; continue; }
            uint result = pixel;
            var (h, s, v) = Shading.Hsv(pixel);
            foreach (var r in prepared)
            {
                bool match = r.FromGrey
                    ? s < 0.15 && Math.Abs(v - r.FV) <= 0.4
                    : HueDistance(h, r.FH) <= r.Range && s >= 0.1 && Math.Abs(s - r.FS) <= 0.5 && (r.FromSkin || !ColorSampler.IsSkinTone(pixel));
                if (!match) continue;
                double hue = r.ToGrey || r.FromGrey ? r.TH : (h + r.TH - r.FH + 360) % 360;
                result = Shading.FromHsv(hue, Along(s, r.FS, r.TS), Along(v, r.FV, r.TV), pixel >> 24);
                break;
            }
            pixels.Pixels[i] = cache[pixel] = result;
        }
    }

    /// <summary>Map 0→0, from→to, 1→1 in straight pieces: each shade keeps its place between dark and light.</summary>
    private static double Along(double value, double from, double to)
    {
        if (from <= 0.001) return Math.Clamp(to + value, 0, 1);
        if (from >= 0.999) return Math.Clamp(value / from * to, 0, 1);
        return Math.Clamp(value <= from ? value / from * to : to + (value - from) / (1 - from) * (1 - to), 0, 1);
    }

    private static double HueDistance(double a, double b) { double d = Math.Abs(a - b) % 360; return Math.Min(d, 360 - d); }

    /// <summary>A colour source's mid colour: "#RRGGBB", or a sampled source ("abigail.Shirt"); null when it can't be read.</summary>
    public static uint? Mid(string? source, Func<string, ColorRamp>? resolveColor)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        if (source.StartsWith('#'))
            return source.Length == 7 && uint.TryParse(source.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rgb) ? rgb | 0xFF000000 : null;
        if (resolveColor == null) return null;
        try { return resolveColor(source).Mid; }
        catch (ArgumentException) { return null; }   // a missing sample: the validator reports it; the rest still draws
    }
}
