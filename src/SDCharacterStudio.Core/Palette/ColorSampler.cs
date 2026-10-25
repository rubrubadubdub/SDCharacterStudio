using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Models;

namespace SDCharacterStudio.Core.Palette;

/// <summary>Samples straight-alpha rig frames without modifying their pixels.</summary>
public static class ColorSampler
{
    public static ColorRamp Resolve(CharacterRig rig, string sampleName, Func<int,RgbaImage> frame, Action<string>? trace = null)
    {
        ArgumentNullException.ThrowIfNull(rig);
        ArgumentNullException.ThrowIfNull(frame);
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolved = new Dictionary<string, ColorRamp>(StringComparer.OrdinalIgnoreCase);
        ColorRamp Sample(string name)
        {
            if (resolved.TryGetValue(name, out var cached)) return cached;
            if (!active.Add(name)) throw new ArgumentException($"Colour sample fallback cycle at '{name}'.");
            try
            {
                if (!rig.ColorSamples.TryGetValue(name, out var spec))
                    throw new ArgumentException($"Rig '{rig.Id}' has no colour sample '{name}'.");
                var image = frame(spec.Frame);
                var box = spec.Box ?? new[] { 0, 0, image.Width, image.Height };
                if (box.Length != 4 || box[0] < 0 || box[1] < 0 || box[2] <= 0 || box[3] <= 0 ||
                    (long)box[0] + box[2] > image.Width || (long)box[1] + box[3] > image.Height)
                    throw new ArgumentException($"Invalid sampling box for '{name}'.");
                uint? Common(Func<int,int,uint,bool> accept)
                {
                    var counts = new Dictionary<uint,int>();
                    for (var y = box[1]; y < box[1] + box[3]; y++)
                        for (var x = box[0]; x < box[0] + box[2]; x++)
                        {
                            var c = image.Pixels[y * image.Width + x];
                            if (c >> 24 == 255 && accept(x,y,c)) counts[c] = counts.TryGetValue(c, out var n) ? n + 1 : 1;
                        }
                    // Dictionary insertion order preserves row-major tie breaking.
                    return counts.Count == 0 ? null : counts.OrderByDescending(p => p.Value).First().Key;
                }
                bool Clear(int x, int y) => x < 0 || y < 0 || x >= image.Width || y >= image.Height || image.Pixels[y * image.Width + x] >> 24 == 0;
                uint? Edge() => Common((x,y,_) => Clear(x-1,y) || Clear(x+1,y) || Clear(x,y-1) || Clear(x,y+1));
                ColorRamp Fallback(uint color)
                {
                    trace?.Invoke($"No pixels qualified for colour sample '{rig.Id}.{name}'; using fallback.");
                    return spec.Fallback is { Length: > 0 } other ? Sample(other) : FromColor(color);
                }
                ColorRamp result;
                switch (spec.Mode?.ToLowerInvariant())
                {
                    case "auto-skin":
                        var skin = Common((_,_,c) => IsSkinTone(c));
                        if (skin == null && spec.Fallback is { Length: > 0 }) { result = Fallback(0xFFFFDCB4); break; }
                        if (skin == null) trace?.Invoke($"No skin pixels qualified for '{rig.Id}.{name}'; using #FFDCB4.");
                        var mid = skin ?? 0xFFFFDCB4;
                        var hsv = Hsv(mid);
                        bool Similar(uint c) { var difference = Math.Abs(Hsv(c).Hue - hsv.Hue); return Math.Min(difference,360-difference) <= 20; }
                        var dark = Common((_,_,c) => Similar(c) && Brightness(c) < Brightness(mid));
                        var light = Common((_,_,c) => Similar(c) && Brightness(c) > Brightness(mid));
                        result = new(dark ?? Scale(mid,0.85),mid,light ?? FromColor(mid).Light);
                        break;
                    case "auto-edge":
                        var edge = Edge();
                        if (edge == null && spec.Fallback is { Length: > 0 }) { result = Fallback(0xFF000000); break; }
                        if (edge == null) trace?.Invoke($"No edge pixels qualified for '{rig.Id}.{name}'; using black.");
                        var outline = edge ?? 0xFF000000;
                        result = new(outline,outline,outline);
                        break;
                    case "auto-common":
                        var excluded = !name.Equals("Outline",StringComparison.OrdinalIgnoreCase) && rig.ColorSamples.ContainsKey("Outline") ? Sample("Outline").Mid : Edge() ?? 0xFF000000;
                        var clothing = Common((_,_,c) => c != excluded && !IsSkinTone(c));
                        result = clothing.HasValue ? FromColor(clothing.Value) : Fallback(excluded);
                        break;
                    case "pixel":
                        var pixels = new List<uint>();
                        foreach (var point in spec.Pixels ?? Array.Empty<int[]>())
                        {
                            if (point.Length != 2 || point[0] < 0 || point[1] < 0 || point[0] >= image.Width || point[1] >= image.Height)
                                throw new ArgumentException($"Invalid sample pixel for '{name}'.");
                            var c = image.Pixels[point[1] * image.Width + point[0]];
                            if (c >> 24 == 255) pixels.Add(c);
                        }
                        pixels.Sort((a,b) => Brightness(a).CompareTo(Brightness(b)));
                        result = pixels.Count switch
                        {
                            0 => Fallback(0xFF000000),
                            1 => FromColor(pixels[0]),
                            _ => new(pixels[0],pixels[pixels.Count / 2],pixels[^1])
                        };
                        break;
                    default: throw new ArgumentException($"Unknown colour sample mode '{spec.Mode}'.");
                }
                resolved[name] = result;
                return result;
            }
            finally { active.Remove(name); }
        }
        return Sample(sampleName);
    }

    public static ColorRamp FromColor(uint color)
    {
        uint LightChannel(int shift) { var c=(color >> shift)&255; return Round(c+(255-c)*0.35); }
        return new(Scale(color,0.72),color|0xFF000000,0xFF000000|LightChannel(16)<<16|LightChannel(8)<<8|LightChannel(0));
    }

    public static bool IsSkinTone(uint color)
    {
        var hsv=Hsv(color);
        return hsv.Value >= 0.75 && hsv.Saturation >= 0.15 && hsv.Saturation <= 0.6 && (hsv.Hue <= 60 || hsv.Hue >= 340);
    }

    private static uint Scale(uint c,double factor)=>0xFF000000|Round(((c>>16)&255)*factor)<<16|Round(((c>>8)&255)*factor)<<8|Round((c&255)*factor);
    private static uint Round(double value)=>(uint)Math.Clamp(Math.Round(value,MidpointRounding.AwayFromZero),0,255);
    private static double Brightness(uint color)=>((color>>16)&255)*0.2126+((color>>8)&255)*0.7152+(color&255)*0.0722;
    private static (double Hue,double Saturation,double Value) Hsv(uint color)
    {
        double r=((color>>16)&255)/255.0,g=((color>>8)&255)/255.0,b=(color&255)/255.0;
        double max=Math.Max(r,Math.Max(g,b)),min=Math.Min(r,Math.Min(g,b)),delta=max-min;
        double hue=delta==0 ? 0 : max==r ? 60*(((g-b)/delta+6)%6) : max==g ? 60*((b-r)/delta+2) : 60*((r-g)/delta+4);
        return(hue,max==0?0:delta/max,max);
    }
}
