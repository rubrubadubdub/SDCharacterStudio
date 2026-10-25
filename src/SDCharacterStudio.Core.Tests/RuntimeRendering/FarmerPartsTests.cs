using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Runtime.Rendering;
using Xunit;

namespace SDCharacterStudio.Core.Tests.RuntimeRendering;
public sealed class FarmerPartsTests
{
    [Fact]
    public void CombinedPartsAcceptWhitespaceAndPreserveSeparateEyes()
    {
        var parts=FarmerParts.Parse(" Head + hAiR + Hat ");
        Assert.True(parts.HasFlag(FarmerPart.Head));
        Assert.True(parts.HasFlag(FarmerPart.Hair));
        Assert.False(parts.HasFlag(FarmerPart.Eyes));
        Assert.False(parts.HasFlag(FarmerPart.Body));
    }
    [Fact]
    public void FullIncludesShoesAndBaseExpandsToBothSkinRegions()
    {
        Assert.True(FarmerParts.Parse("Full").HasFlag(FarmerPart.Shoes));
        Assert.Equal(FarmerPart.Head|FarmerPart.Body,FarmerParts.Parse("Base"));
    }
    [Theory]
    [InlineData("")]
    [InlineData("Head+Wings")]
    [InlineData("Head++Hair")]
    public void InvalidPartsRejectInsteadOfSilentlyDrawingFull(string input)
        =>Assert.Throws<ArgumentException>(()=>FarmerParts.Parse(input));
    [Fact]
    public void HeadAndBodySplitAfterInclusiveNeckRowWithoutMovingPixels()
    {
        Assert.Equal((0,17),FarmerCanvas.BaseRows(16,true));
        Assert.Equal((17,15),FarmerCanvas.BaseRows(16,false));
        Assert.Equal((0,12),FarmerCanvas.BaseRows(11,true));
        Assert.Equal((12,20),FarmerCanvas.BaseRows(11,false));
    }
    [Fact]
    public void DownsampleTakesTopLeftPixelOfEachFourByFourBlock()
    {
        var pixels=new uint[128*192];
        pixels[4*128+8]=0x80402010u;
        pixels[4*128+9]=0xFFFFFFFFu;
        var result=FarmerCanvas.Downsample(new(128,192,pixels));
        Assert.Equal(32,result.Width);
        Assert.Equal(48,result.Height);
        Assert.Equal(0x80402010u,result.Pixels[1*32+2]);
        Assert.Equal(0u,result.Pixels[0]);
    }
}
