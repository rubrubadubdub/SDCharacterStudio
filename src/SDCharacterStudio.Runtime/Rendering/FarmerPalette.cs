using Microsoft.Xna.Framework;
using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Palette;
using StardewValley;

namespace SDCharacterStudio.Runtime.Rendering;

/// <summary>Reads the native recolored palette; hair shading is sampled after native tinting.</summary>
internal static class FarmerPalette
{
    public static ColorRamp Resolve(FarmerPartsRenderer renderer,Farmer farmer,string name)
    {
        var pixels=renderer.ReadBase(farmer).Pixels;
        ColorRamp Indices(int dark,int mid,int light)=>new(pixels[dark],pixels[mid],pixels[light]);
        switch(name.ToLowerInvariant())
        {
            case "skin":return Indices(260,261,262);
            case "eyes":return Indices(277,276,276);
            case "shoes":return Indices(268,269,271);
            case "shirt":return Fixed(farmer.GetShirtColor());
            case "pants":return Fixed(farmer.GetPantsColor());
            case "outline":return new(0xFF000000,0xFF000000,0xFF000000);
            case "hair":
                var hair=renderer.Render(farmer,0,2,"Hair").Pixels.Where(x=>(x>>24)==255)
                    .GroupBy(x=>x).OrderByDescending(x=>x.Count()).Take(3).Select(x=>x.Key)
                    .OrderBy(Brightness).ToArray();
                return hair.Length switch{0=>Fixed(farmer.hairstyleColor.Value),1=>new(hair[0],hair[0],hair[0]),2=>new(hair[0],hair[1],hair[1]),_=>new(hair[0],hair[1],hair[2])};
            default:throw new ArgumentException($"Unknown farmer color source '{name}'.");
        }
    }
    private static long Brightness(uint c)=>((c>>16)&255)*299+((c>>8)&255)*587+(c&255)*114;
    private static ColorRamp Fixed(Color color)
    {
        uint Convert(bool light)=>0xFF000000u | Channel(color.R,light)<<16 | Channel(color.G,light)<<8 | Channel(color.B,light);
        static uint Channel(byte c,bool light)=>(uint)Math.Clamp(Math.Round(light?c+(255-c)*.35:c*.72,MidpointRounding.AwayFromZero),0,255);
        return new(Convert(false),0xFF000000u|(uint)color.R<<16|(uint)color.G<<8|color.B,Convert(true));
    }
}
