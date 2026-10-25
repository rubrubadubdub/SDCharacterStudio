using SDCharacterStudio.Core.Loading;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Palette;

namespace SDCharacterStudio.Runtime.Rendering;

/// <summary>Resolves pack rig aliases and NPC names into sampled colour ramps.</summary>
public sealed class ColorSlotResolver
{
    private readonly LoadedPack library;
    private readonly Func<CharacterRig,int,Core.Compositing.RgbaImage> frame;
    private readonly Action<string>? trace;
    private readonly Func<string,ColorRamp>? farmer;
    private readonly Dictionary<string,ColorRamp> colors=new(StringComparer.OrdinalIgnoreCase);

    public ColorSlotResolver(LoadedPack library,Func<CharacterRig,int,Core.Compositing.RgbaImage> frame,Action<string>? trace=null,Func<string,ColorRamp>? farmer=null)
    { this.library=library;this.frame=frame;this.trace=trace;this.farmer=farmer; }

    public ColorRamp Resolve(string source)
    {
        if(colors.TryGetValue(source,out var color)) return color;
        var separator=source.LastIndexOf('.');
        if(separator<=0 || separator==source.Length-1) throw new ArgumentException($"Invalid colour source '{source}'.");
        var owner=source[..separator];var sample=source[(separator+1)..];
        if(owner.Equals("Farmer",StringComparison.OrdinalIgnoreCase))
            return colors[source]=farmer?.Invoke(sample)??throw new ArgumentException("No live farmer is available for color sampling.");
        var rig=library.Characters.Select(x=>x.Value).FirstOrDefault(x=>Same(x.Id,owner)||Same(x.Npc,owner))
            ?? throw new ArgumentException($"No rig matches colour source '{source}'.");
        color=ColorSampler.Resolve(rig,sample,index=>frame(rig,index),trace);
        colors[source]=color;
        return color;
    }

    private static bool Same(string? a,string? b)=>string.Equals(a,b,StringComparison.OrdinalIgnoreCase);
}
