using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Palette;
using Xunit;

namespace SDCharacterStudio.Core.Tests.Compositing;

public sealed class ExpressionOverlayTests
{
    [Fact]
    public void EraseClearsHeadWhileTransparentPixelsPreserveHeadAndOverlaySlotsReplaceIndependently()
    {
        var head = Image(0xFF112233, 0xFF112233, 0xFF112233, 0xFF112233);
        var overlay = Image(ColorSlots.Erase, 0, 0xFFBF00BF, 0x80FF0000);
        var frame = new ExpressionFrame { Offset = new[] { -1, 0 } };
        var slots = new Dictionary<string, string> { ["Skin"] = "#00FF00" };

        ExpressionOverlay.Apply(head, overlay, new[] { 1, 0, 4, 1 }, frame, slots);

        Assert.Equal(new uint[] { 0, 0xFF112233, 0xFF00FF00, 0xFF881119 }, head.Pixels);
        Assert.Equal(new uint[] { ColorSlots.Erase, 0, 0xFFBF00BF, 0x80FF0000 }, overlay.Pixels);
        Assert.Equal(new[] { -1, 0 }, frame.Offset);
        Assert.Equal("#00FF00", slots["Skin"]);
    }

    [Fact]
    public void OverlayClipsToHeadCanvasRatherThanFaceBoxSize()
    {
        var head = new RgbaImage(2, 2, new uint[4]);
        var overlay = new RgbaImage(3, 2, new uint[] { 0xFF010101, 0xFF020202, 0xFF030303, 0xFF040404, 0xFF050505, 0xFF060606 });
        ExpressionOverlay.Apply(head, overlay, new[] { 0, 0, 1, 1 }, new() { Offset = new[] { -1, 0 } });
        Assert.Equal(new uint[] { 0xFF020202, 0xFF030303, 0xFF050505, 0xFF060606 }, head.Pixels);
    }

    [Theory]
    [InlineData(int.MaxValue, int.MaxValue)]
    [InlineData(int.MinValue, int.MinValue)]
    public void ExtremeCombinedOffsetsCannotWrapOntoHead(int faceX, int offsetX)
    {
        var head = Image(0xFF112233);
        ExpressionOverlay.Apply(head, Image(ColorSlots.Erase), new[] { faceX, 0, 1, 1 }, new() { Offset = new[] { offsetX, 0 } });
        Assert.Equal(0xFF112233u, head.Pixels[0]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidFaceBoxDimensionsAreRejected(int size)
        => Assert.Throws<ArgumentException>(() => ExpressionOverlay.Apply(Image(1), Image(2), new[] { 0, 0, size, 1 }, new()));

    [Fact]
    public void MalformedFaceBoxAndOffsetAreRejected()
    {
        Assert.Throws<ArgumentException>(() => ExpressionOverlay.Apply(Image(1), Image(2), new[] { 0, 0 }, new()));
        Assert.Throws<ArgumentException>(() => ExpressionOverlay.Apply(Image(1), Image(2), new[] { 0, 0, 1, 1 }, new() { Offset = new[] { 0 } }));
    }

    [Fact]
    public void CompositionTransformsClonedColoredSourceBeforeMaskAndLayerFlip()
    {
        var layer = new LiveLayer { Mask = "head", Flip = true, ColorSlots = new() { ["Skin"] = "#FF0000" } };
        var source = Image(0xFFBF00BF, 0xFFBF00BF, 0xFFBF00BF);
        var mask = Image(0xFFFFFFFF, 0xFFFFFFFF, 0);
        var overlay = Image(ColorSlots.Erase, 0xFFBF00BF, 0xFFBF00BF);
        var result = Compositor.Compose(3, 1, new() { Layers = new() { layer } }, new HeadSource(source, mask),
            transformSource: (actualLayer, head, sourceFrame) =>
            {
                Assert.Same(layer, actualLayer);
                Assert.Equal(7, sourceFrame);
                Assert.Equal(new uint[] { 0xFFFF0000, 0xFFFF0000, 0xFFFF0000 }, head.Pixels);
                ExpressionOverlay.Apply(head, overlay, new[] { 0, 0, 3, 1 }, new(), new Dictionary<string, string> { ["Skin"] = "#00FF00" });
            });
        Assert.Equal(new uint[] { 0, 0xFF00FF00, 0 }, result.Pixels);
        Assert.Equal(new uint[] { 0xFFBF00BF, 0xFFBF00BF, 0xFFBF00BF }, source.Pixels);
        Assert.Equal(new uint[] { 0xFFFFFFFF, 0xFFFFFFFF, 0 }, mask.Pixels);
    }

    private static RgbaImage Image(params uint[] pixels) => new(pixels.Length, 1, pixels);
    private sealed class HeadSource(RgbaImage image, RgbaImage mask) : ILayerSourceResolver
    {
        public ResolvedLayerSource Resolve(Layer layer) => new(image, 7);
        public Mask GetMask(string id) => new() { Image = "mask" };
        public RgbaImage ResolveMaskImage(string path) => mask;
    }
}
