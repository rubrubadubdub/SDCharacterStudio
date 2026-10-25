using SDCharacterStudio.Core.Bridge;
using SDCharacterStudio.Core.Compositing;
using Xunit;

namespace SDCharacterStudio.Core.Tests.Bridge;

public sealed class TextureCropTests
{
    [Fact]
    public void UndersizedBirchWaterShapeRepeatsTheRightEdgeWithoutChangingTheSource()
    {
        uint[] original = Enumerable.Range(0, 15 * 16).Select(index => 0x80400000u + (uint)index).ToArray();
        var source = new RgbaImage(15, 16, original.ToArray());
        var icon = TextureCrop.PointClamp(source, 0, 0, 16, 16);
        Assert.Equal(16, icon.Width); Assert.Equal(16, icon.Height);
        for (int row = 0; row < 16; row++)
        {
            Assert.Equal(original.Skip(row * 15).Take(15), icon.Pixels.Skip(row * 16).Take(15));
            Assert.Equal(original[row * 15 + 14], icon.Pixels[row * 16 + 15]);
        }
        Assert.Equal(original, source.Pixels);
    }

    [Fact]
    public void CoordinatesOutsideEveryEdgeClampToNativeEdgePixelsAndPreserveStraightAlpha()
    {
        var source = new RgbaImage(2, 2, new uint[] { 0x80F04020, 0, 0xFF123456, 0x40010203 });
        var icon = TextureCrop.PointClamp(source, -1, -1, 4, 4);
        Assert.Equal(new uint[]
        {
            0x80F04020, 0x80F04020, 0, 0,
            0x80F04020, 0x80F04020, 0, 0,
            0xFF123456, 0xFF123456, 0x40010203, 0x40010203,
            0xFF123456, 0xFF123456, 0x40010203, 0x40010203
        }, icon.Pixels);
        Assert.All(TextureCrop.PointClamp(source, int.MaxValue, int.MaxValue, 2, 2).Pixels, pixel => Assert.Equal(0x40010203u, pixel));
    }

    [Fact]
    public void InBoundsCropCopiesTheRequestedPixelsExactly()
    {
        var source = new RgbaImage(3, 2, new uint[] { 1, 2, 3, 4, 5, 6 });
        Assert.Equal(new uint[] { 2, 3, 5, 6 }, TextureCrop.PointClamp(source, 1, 0, 2, 2).Pixels);
    }

    [Theory]
    [InlineData(0, 16)]
    [InlineData(16, -1)]
    [InlineData(int.MaxValue, int.MaxValue)]
    [InlineData(4097, 4096)]
    public void RejectsInvalidOrExcessiveOutputDimensionsBeforeAllocating(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TextureCrop.PointClamp(new RgbaImage(1, 1, new uint[1]), 0, 0, width, height));
    }
}
