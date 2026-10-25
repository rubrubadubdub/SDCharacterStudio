using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Runtime.Rendering;
using Xunit;

namespace SDCharacterStudio.Core.Tests.RuntimeRendering;
public sealed class PixelOperationsTests
{
    [Theory]
    [InlineData(0x80402010u,0x80804020u)]
    [InlineData(0x00112233u,0u)]
    [InlineData(0xFF112233u,0xFF112233u)]
    public void UnpremultiplyExpandsPartialChannelsAndClearsTransparentRgb(uint input,uint expected)
        =>Assert.Equal(expected,PixelOperations.Unpremultiply(input));
    [Theory]
    [InlineData(0x80804020u,0x80402010u)]
    [InlineData(0x00112233u,0u)]
    public void PremultiplyScalesChannelsForSpriteBatch(uint input,uint expected)
        =>Assert.Equal(expected,PixelOperations.Premultiply(input));
    [Fact]
    public void GridCropUsesRowsAndReturnsIndependentPixels()
    {
        var sheet=new RgbaImage(4,2,new uint[]{1,2,3,4,5,6,7,8});
        var crop=PixelOperations.CropFrame(sheet,2,1,3);
        Assert.Equal(new uint[]{7,8},crop.Pixels);
        crop.Pixels[0]=99;
        Assert.Equal(7u,sheet.Pixels[6]);
    }
    [Theory]
    [InlineData(3,1,0)]
    [InlineData(2,1,4)]
    [InlineData(0,1,0)]
    [InlineData(2,1,-1)]
    public void GridCropRejectsPartialCellsAndOutOfBoundsFrames(int width,int height,int index)
        =>Assert.ThrowsAny<ArgumentException>(()=>PixelOperations.CropFrame(new(4,2,new uint[8]),width,height,index));
}
