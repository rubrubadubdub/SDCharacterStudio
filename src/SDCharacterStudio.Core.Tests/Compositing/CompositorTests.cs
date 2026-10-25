using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Palette;
using Xunit;

namespace SDCharacterStudio.Core.Tests.Compositing;

public sealed class CompositorTests
{
    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(1, -1, 0)]
    [InlineData(2, 2, 3)]
    [InlineData(int.MaxValue, 2, 0)]
    public void ImageRejectsInvalidDimensionsAndPixelCounts(int width, int height, int count)
        => Assert.ThrowsAny<ArgumentException>(() => new RgbaImage(width, height, new uint[count]));

    [Theory]
    [InlineData(MaskMode.Keep, 0xFF112233u, 0u)]
    [InlineData(MaskMode.Remove, 0u, 0xFF445566u)]
    public void MaskUsesAnyNonzeroAlphaRatherThanMaskRgb(MaskMode mode, uint left, uint right)
    {
        var image = Image(0xFF112233, 0xFF445566);
        Compositor.ApplyMask(image, Image(0x01000000, 0x00FFFFFF), mode);
        Assert.Equal(new[] { left, right }, image.Pixels);
    }

    [Fact]
    public void MaskRejectsMismatchedDimensions()
        => Assert.Throws<ArgumentException>(() => Compositor.ApplyMask(Image(1, 2), Image(1), MaskMode.Keep));

    [Theory]
    [InlineData("Skin", 0xFF7F007Fu, 0xFFBF00BFu, 0xFFFF00FFu)]
    [InlineData("Hair", 0xFF007F7Fu, 0xFF00BFBFu, 0xFF00FFFFu)]
    [InlineData("Shirt", 0xFF7F3F00u, 0xFFBF5F00u, 0xFFFF7F00u)]
    [InlineData("Pants", 0xFF003F7Fu, 0xFF005FBFu, 0xFF007FFFu)]
    [InlineData("Shoes", 0xFF3F7F00u, 0xFF5FBF00u, 0xFF7FFF00u)]
    [InlineData("Custom1", 0xFF7F0000u, 0xFFBF0000u, 0xFFFF0001u)]
    [InlineData("Custom2", 0xFF00007Fu, 0xFF0000BFu, 0xFF0000FEu)]
    public void EveryThreeShadeSlotUsesFixedColorRamp(string name, uint dark, uint mid, uint light)
    {
        var image = Image(dark, mid, light);
        Compositor.ReplaceSlots(image, ColorSlotTable.FromMappings(new Dictionary<string, string> { [name] = "#6496C8" }));
        Assert.Equal(new uint[] { 0xFF486C90, 0xFF6496C8, 0xFF9ABBDB }, image.Pixels);
    }

    [Fact]
    public void EyesUseDarkAndLightAndOutlineUsesSingleColor()
    {
        var image = Image(0xFF7F7F00, 0xFFFFFF00, 0xFF400040);
        var slots = ColorSlotTable.FromMappings(new Dictionary<string, string> { ["eyes"] = "#6496C8", ["OUTLINE"] = "#102030" });
        Compositor.ReplaceSlots(image, slots);
        Assert.Equal(new uint[] { 0xFF486C90, 0xFF9ABBDB, 0xFF102030 }, image.Pixels);
    }

    [Fact]
    public void ReplacementOnlyMatchesOpaqueExactColorsAndEraseBecomesTransparent()
    {
        var image = Image(0xFFFF00FF, 0x80FF00FF, 0x00FF00FF, 0xFFFF00FE, 0xFF00FF01, 0x8000FF01);
        Compositor.ReplaceSlots(image, ColorSlotTable.FromMappings(new Dictionary<string, string> { ["Skin"] = "#6496C8" }));
        Assert.Equal(new uint[] { 0xFF9ABBDB, 0x80FF00FF, 0x00FF00FF, 0xFFFF00FE, 0, 0x8000FF01 }, image.Pixels);
    }

    [Fact]
    public void UnmappedSlotsRemainAvailableAndResolvedRampsForceOpaqueOutput()
    {
        var image = Image(0xFF7F007F, 0xFFBF00BF, 0xFFFF00FF, 0xFF007F7F);
        var slots = ColorSlotTable.FromMappings(new Dictionary<string, string> { ["Skin"] = "Robin.Skin" }, _ => new ColorRamp(0x00112233, 0x80445566, 0x00778899));
        Compositor.ReplaceSlots(image, slots);
        Assert.Equal(new uint[] { 0xFF112233, 0xFF445566, 0xFF778899, 0xFF007F7F }, image.Pixels);
    }

    [Fact]
    public void StraightAlphaOverIncludesDestinationAlphaInColorChannels()
    {
        var canvas = Image(0x800000FF);
        Compositor.Blit(canvas, Image(0x80FF0000), 0, 0);
        Assert.Equal(0xC0AA0055u, canvas.Pixels[0]);
    }

    [Fact]
    public void OpacityMultipliesSourceAlphaBeforeOver()
    {
        var canvas = Image(0x800000FF);
        Compositor.Blit(canvas, Image(0x80FF0000), 0, 0, opacity: 0.5);
        Assert.Equal(0xA0660099u, canvas.Pixels[0]);
    }

    [Fact]
    public void BlitClipsNegativeAndExtremeCoordinatesAndMirrorsWithinSource()
    {
        var canvas = Image(0, 0);
        Compositor.Blit(canvas, Image(0xFFFF0000, 0xFF00FF00, 0xFF0000FF), -1, 0, flip: true);
        Assert.Equal(new uint[] { 0xFF00FF00, 0xFFFF0000 }, canvas.Pixels);
        Compositor.Blit(canvas, Image(0xFFFFFFFF), int.MaxValue, int.MaxValue);
        Compositor.Blit(canvas, Image(0xFFFFFFFF), int.MinValue, int.MinValue);
        Assert.Equal(new uint[] { 0xFF00FF00, 0xFFFF0000 }, canvas.Pixels);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void BlitRejectsInvalidOpacity(double opacity)
        => Assert.Throws<ArgumentOutOfRangeException>(() => Compositor.Blit(Image(0), Image(1), 0, 0, opacity: opacity));

    [Fact]
    public void CompositionResolvesVisibleLayersInOrderWithSeparateSlotMappings()
    {
        var first = new ImageLayer { Image = "first", ColorSlots = new() { ["Skin"] = "#FF0000" } };
        var second = new PaintLayer { Image = "second", Offset = new[] { 1, 0 }, ColorSlots = new() { ["Skin"] = "#00FF00" }, Opacity = 0.5 };
        var hidden = new ImageLayer { Image = "missing", Visible = false };
        var sources = new Sources();
        sources.Layers[first] = new(Image(0xFFBF00BF, 0xFFBF00BF));
        sources.Layers[second] = new(Image(0xFFBF00BF));
        var result = Compositor.Compose(2, 1, new PoseFrame { Layers = new() { first, hidden, second } }, sources);
        Assert.Equal(new uint[] { 0xFFFF0000, 0xFF808000 }, result.Pixels);
    }

    [Theory]
    [InlineData(7, 0xFF223344u, 0u)]
    [InlineData(8, 0u, 0xFF112233u)]
    public void PerFrameMaskOverrideUsesResolvedSourceFrameBeforeFlipping(int sourceFrame, uint left, uint right)
    {
        var layer = new LiveLayer { Mask = "head", Flip = true, Offset = new[] { 0, 0 } };
        var sources = new Sources();
        sources.Layers[layer] = new(Image(0xFF112233, 0xFF223344), sourceFrame);
        sources.Masks["head"] = new Mask { Image = "base", PerFrame = new() { ["7"] = "override" } };
        sources.Images["base"] = Image(0xFFFFFFFF, 0);
        sources.Images["override"] = Image(0, 0x01000000);
        Assert.Equal(new[] { left, right }, Compositor.Compose(2, 1, new PoseFrame { Layers = new() { layer } }, sources).Pixels);
    }

    [Theory]
    [InlineData("rig")]
    [InlineData("explicit")]
    public void FlipMirrorsSourceAnchorBeforePlacementThenAppliesBothOffsets(string kind)
    {
        var layer = new ImageLayer { Flip = true, Offset = new[] { 1, 1 }, AnchorTo = new() { Anchor = "neck", From = kind == "rig" ? new JValue("rig") : new JArray(0, 0) } };
        var sources = new Sources();
        sources.Layers[layer] = new(Image(0xFFFF0000, 0xFF00FF00, 0xFF0000FF), RigAnchor: new PixelPoint(0, 0));
        var frame = new PoseFrame { Layers = new() { layer }, Anchors = new() { ["Neck"] = new[] { 2, 0 } } };
        Assert.Equal(new uint[] { 0, 0, 0, 0, 0, 0xFF0000FF, 0xFF00FF00, 0xFFFF0000 }, Compositor.Compose(4, 2, frame, sources).Pixels);
    }

    [Fact]
    public void FinalFrameMirrorDoesNotMirrorIndividualLayerPlacement()
    {
        var layer = new ImageLayer { Offset = new[] { 1, 0 } };
        var sources = new Sources(); sources.Layers[layer] = new(Image(0xFF112233, 0xFF445566));
        Assert.Equal(new uint[] { 0, 0xFF445566, 0xFF112233, 0 }, Compositor.Compose(4, 1, new PoseFrame { Layers = new() { layer } }, sources, mirror: true).Pixels);
    }

    [Fact]
    public void RepeatedCompositionDoesNotMutateCachedSourcesMasksOrPreviousOutput()
    {
        var layer = new PaintLayer { Mask = "mask", ColorSlots = new() { ["Skin"] = "#6496C8" } };
        var source = Image(0xFF7F007F, 0xFFBF00BF, 0xFF00FF01);
        var mask = Image(0xFFFFFFFF, 0, 0xFFFFFFFF);
        var sources = new Sources(); sources.Layers[layer] = new(source);
        sources.Masks["mask"] = new Mask { Image = "mask.png" }; sources.Images["mask.png"] = mask;
        var frame = new PoseFrame { Layers = new() { layer } };
        var first = Compositor.Compose(3, 1, frame, sources);
        var second = Compositor.Compose(3, 1, frame, sources);
        Assert.Equal(new uint[] { 0xFF486C90, 0, 0 }, first.Pixels);
        Assert.Equal(first.Pixels, second.Pixels);
        Assert.Equal(new uint[] { 0xFF7F007F, 0xFFBF00BF, 0xFF00FF01 }, source.Pixels);
        Assert.Equal(new uint[] { 0xFFFFFFFF, 0, 0xFFFFFFFF }, mask.Pixels);
        second.Pixels[0] = 0;
        Assert.Equal(0xFF486C90u, first.Pixels[0]);
    }

    [Fact]
    public void ALiveMapRearrangesTheLiveSourceAndStaysLive()
    {
        // A 2x2 live source; a 3x1 map: source (1,0), nothing, source (0,1). Changing the source changes the output.
        var source = new RgbaImage(2, 2, new uint[] { 0xFF000001, 0xFF000002, 0xFF000003, 0xFF000004 });
        var live = new LiveLayer { Source = new LiveSource { Kind = "Npc", Npc = "Robin" }, Map = "map.png" };
        var sources = new Sources();
        sources.Layers[live] = new ResolvedLayerSource(source);
        sources.Images["map.png"] = new RgbaImage(3, 1, new[] { Compositor.MapCell(1, 0), 0u, Compositor.MapCell(0, 1) });
        var frame = new PoseFrame { Layers = { live } };
        Assert.Equal(new uint[] { 0xFF000002, 0, 0xFF000003 }, Compositor.Compose(3, 1, frame, sources).Pixels);
        source.Pixels[1] = 0xFF0000AA;   // a sprite mod changes that pixel: the pose follows
        Assert.Equal(new uint[] { 0xFF0000AA, 0, 0xFF000003 }, Compositor.Compose(3, 1, frame, sources).Pixels);
        // A cell pointing outside the source shows nothing.
        sources.Images["map.png"] = new RgbaImage(3, 1, new[] { Compositor.MapCell(5, 5), 0u, 0u });
        Assert.Equal(new uint[] { 0, 0, 0 }, Compositor.Compose(3, 1, frame, sources).Pixels);
    }

    [Fact]
    public void MapCellsCanShowHerOtherFramesAndStayLive()
    {
        // Own frame 2x1; frame 7 of the sheet 2x1. The map shows own (0,0), then frame 7's (1,0).
        var own = new RgbaImage(2, 1, new uint[] { 0xFF000001, 0xFF000002 });
        var seven = new RgbaImage(2, 1, new uint[] { 0xFF000070, 0xFF000071 });
        var live = new LiveLayer { Source = new LiveSource { Kind = "Npc", Npc = "Robin" }, Map = "map.png" };
        var sources = new Sources();
        sources.Layers[live] = new ResolvedLayerSource(own, 0);
        sources.Frames[7] = new ResolvedLayerSource(seven, 7);
        sources.Images["map.png"] = new RgbaImage(2, 1, new[] { Compositor.MapCell(0, 0), Compositor.MapCell(1, 0, frame: 7) });
        var frame = new PoseFrame { Layers = { live } };
        Assert.Equal(new uint[] { 0xFF000001, 0xFF000071 }, Compositor.Compose(2, 1, frame, sources).Pixels);
        seven.Pixels[1] = 0xFF0000BB;   // a sprite mod changes her frame 7: the copied pixel follows
        Assert.Equal(new uint[] { 0xFF000001, 0xFF0000BB }, Compositor.Compose(2, 1, frame, sources).Pixels);
        Assert.Equal((1, 0, (int?)7), Compositor.MapSource(Compositor.MapCell(1, 0, frame: 7)));
        Assert.Equal((1, 0, (int?)null), Compositor.MapSource(Compositor.MapCell(1, 0)));
    }

    private static RgbaImage Image(params uint[] pixels) => new(pixels.Length, 1, pixels);

    private sealed class Sources : ILayerSourceResolver
    {
        public Dictionary<Layer, ResolvedLayerSource> Layers { get; } = new();
        public Dictionary<string, Mask> Masks { get; } = new();
        public Dictionary<string, RgbaImage> Images { get; } = new();
        /// <summary>Sheet frames by index, for layers made on the fly (another frame of the same source).</summary>
        public Dictionary<int, ResolvedLayerSource> Frames { get; } = new();
        public ResolvedLayerSource Resolve(Layer layer) => Layers.TryGetValue(layer, out var known) ? known : Frames[((LiveLayer)layer).SourceFrame!.Index!.Value];
        public Mask GetMask(string id) => Masks[id];
        public RgbaImage ResolveMaskImage(string path) => Images[path];
    }
}
