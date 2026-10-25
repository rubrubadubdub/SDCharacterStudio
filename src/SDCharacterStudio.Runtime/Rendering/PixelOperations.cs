using SDCharacterStudio.Core.Compositing;
namespace SDCharacterStudio.Runtime.Rendering;
internal static class PixelOperations
{
    public static uint Unpremultiply(uint color)
    {
        var alpha=color>>24;
        if(alpha==0) return 0;
        if(alpha==255) return color;
        uint Channel(int shift)=>Math.Min(255,(((color>>shift)&255)*255+alpha/2)/alpha);
        return alpha<<24|Channel(16)<<16|Channel(8)<<8|Channel(0);
    }
    public static uint Premultiply(uint color)
    {
        var alpha=color>>24;
        uint Channel(int shift)=>(((color>>shift)&255)*alpha+127)/255;
        return alpha<<24|Channel(16)<<16|Channel(8)<<8|Channel(0);
    }
    public static RgbaImage CropFrame(RgbaImage sheet,int width,int height,int index)
    {
        if(width<=0 || height<=0 || sheet.Width%width!=0 || sheet.Height%height!=0 || index<0 ||
            (long)index>=(long)(sheet.Width/width)*(sheet.Height/height))
            throw new ArgumentException("Source frame must be a complete cell within the texture.");
        return Crop(sheet,index%(sheet.Width/width)*width,index/(sheet.Width/width)*height,width,height);
    }
    public static RgbaImage Crop(RgbaImage sheet,int x,int y,int width,int height)
    {
        if(x<0 || y<0 || width<=0 || height<=0 || (long)x+width>sheet.Width || (long)y+height>sheet.Height)
            throw new ArgumentException("Source rectangle must fit within the texture.");
        var pixels=new uint[checked(width*height)];
        for(var row=0;row<height;row++) Array.Copy(sheet.Pixels,(y+row)*sheet.Width+x,pixels,row*width,width);
        return new(width,height,pixels);
    }
}
