using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SDCharacterStudio.Core.Compositing;
using StardewModdingAPI;

namespace SDCharacterStudio.Runtime.Rendering;

/// <summary>Converts the game's premultiplied textures at the Core straight-alpha boundary.</summary>
internal static class TexturePixels
{
    public static RgbaImage Read(Texture2D texture)
    {
        var colors=new Color[checked(texture.Width*texture.Height)];
        texture.GetData(colors);
        return Read(texture.Width,texture.Height,colors);
    }
    public static RgbaImage Read(IRawTextureData data)=>Read(data.Width,data.Height,data.Data);
    private static RgbaImage Read(int width,int height,Color[] colors)
    {
        var pixels=new uint[colors.Length];
        for(var i=0;i<colors.Length;i++)
        {
            var c=colors[i];
            pixels[i]=PixelOperations.Unpremultiply((uint)c.A<<24|(uint)c.R<<16|(uint)c.G<<8|c.B);
        }
        return new(width,height,pixels);
    }

    public static Texture2D Upload(GraphicsDevice device,RgbaImage image)
    {
        var colors=new Color[image.Pixels.Length];
        for(var i=0;i<colors.Length;i++)
        {
            var c=PixelOperations.Premultiply(image.Pixels[i]);
            colors[i]=new Color((byte)(c>>16),(byte)(c>>8),(byte)c,(byte)(c>>24));
        }
        var texture=new Texture2D(device,image.Width,image.Height);
        try { texture.SetData(colors); return texture; }
        catch { texture.Dispose(); throw; }
    }
}
