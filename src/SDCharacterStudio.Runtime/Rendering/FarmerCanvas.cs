using SDCharacterStudio.Core.Compositing;

namespace SDCharacterStudio.Runtime.Rendering;

internal static class FarmerCanvas
{
    public const int Width=32,Height=48,Scale=4,FrameX=8,FrameY=16;

    public static (int Start,int Height) BaseRows(int neckRow,bool head)
    {
        var split=Math.Clamp(neckRow+1,0,32);
        return head ? (0,split) : (split,32-split);
    }

    public static RgbaImage Downsample(RgbaImage image)
    {
        if(image.Width!=Width*Scale || image.Height!=Height*Scale)
            throw new ArgumentException("Expected a 128 by 192 render target.",nameof(image));
        var pixels=new uint[Width*Height];
        for(var y=0;y<Height;y++)
            for(var x=0;x<Width;x++) pixels[y*Width+x]=image.Pixels[y*Scale*image.Width+x*Scale];
        return new(Width,Height,pixels);
    }
}
