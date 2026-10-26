namespace SDCharacterStudio.Core.Palette;

/// <summary>
/// One shade darker or lighter (after Pixelorama's Shading tool). A colour that belongs to a ramp steps along it (her
/// skin mid to her skin dark, keeping the slot so it still follows sprite mods); past the end of a ramp, or for any other
/// colour, it hue-shifts: darker leans to blue and richer, lighter leans to yellow and paler, as pixel artists shade.
/// </summary>
public static class Shading
{
    /// <summary>
    /// A ramp: what each shade (dark, mid, light) looks like, and what to paint to get it (a slot's reserved colour, or
    /// the colour itself). A ramp may lack a shade (her eyes have no mid).
    /// </summary>
    public sealed record Ramp(uint?[] Shown, uint?[] Paint);

    /// <summary>
    /// What to paint to shade a pixel one step. <paramref name="shown"/> is the pixel as you see it, <paramref name="painted"/>
    /// what's painted there (0 when nothing is). Null when there's nothing to shade (a transparent pixel).
    /// </summary>
    public static uint? Step(uint shown, uint painted, bool lighter, IReadOnlyList<Ramp> ramps)
    {
        if (shown >> 24 == 0) return null;
        if (painted >> 24 != 0)
            foreach (var ramp in ramps)
                for (int i = 0; i < 3; i++)
                    if (ramp.Paint[i] == painted) return Next(ramp, i, lighter, shown);
        foreach (var ramp in ramps)
            for (int i = 0; i < 3; i++)
                if (ramp.Shown[i] is { } colour && (colour | 0xFF000000) == (shown | 0xFF000000)) return Next(ramp, i, lighter, shown);
        return HueShift(shown, lighter);
    }

    private static uint Next(Ramp ramp, int index, bool lighter, uint shown)
    {
        for (int j = lighter ? index + 1 : index - 1; j >= 0 && j < 3; j += lighter ? 1 : -1)
            if (ramp.Paint[j] is { } paint) return paint;
        return HueShift(shown, lighter);   // the end of the ramp: keep going by eye
    }

    /// <summary>A colour one step darker (towards blue, richer) or lighter (towards yellow, paler).</summary>
    public static uint HueShift(uint color, bool lighter, double amount = 1)
    {
        var (h, s, v) = Hsv(color);
        bool grey = s < 0.06;
        double target = lighter ? 60 : 240;
        if (!grey)
        {
            double difference = ((target - h + 540) % 360) - 180;
            h = (h + Math.Clamp(difference, -12 * amount, 12 * amount) + 360) % 360;
            s = Math.Clamp(s + (lighter ? -0.08 : 0.08) * amount, 0, 1);
        }
        v = Math.Clamp(v + (lighter ? 0.12 : -0.12) * amount, 0, 1);
        return FromHsv(h, s, v, color >> 24 == 0 ? 0xFFu : color >> 24);
    }

    /// <summary>A whole ramp (dark, mid, light) around a colour, shaded the same way.</summary>
    public static ColorRamp RampFor(uint mid) => new(HueShift(mid, false, 1.5), mid | 0xFF000000, HueShift(mid, true, 1.5));

    public static (double Hue, double Saturation, double Value) Hsv(uint color)
    {
        double r = ((color >> 16) & 255) / 255.0, g = ((color >> 8) & 255) / 255.0, b = (color & 255) / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), delta = max - min;
        double hue = delta == 0 ? 0 : max == r ? 60 * (((g - b) / delta + 6) % 6) : max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
        return (hue, max == 0 ? 0 : delta / max, max);
    }

    public static uint FromHsv(double hue, double saturation, double value, uint alpha = 0xFF)
    {
        double c = value * saturation, x = c * (1 - Math.Abs(hue / 60 % 2 - 1)), m = value - c;
        var (r, g, b) = (hue % 360) switch
        {
            < 60 => (c, x, 0.0), < 120 => (x, c, 0.0), < 180 => (0.0, c, x),
            < 240 => (0.0, x, c), < 300 => (x, 0.0, c), _ => (c, 0.0, x),
        };
        uint Channel(double v) => (uint)Math.Clamp(Math.Round((v + m) * 255, MidpointRounding.AwayFromZero), 0, 255);
        return alpha << 24 | Channel(r) << 16 | Channel(g) << 8 | Channel(b);
    }
}
