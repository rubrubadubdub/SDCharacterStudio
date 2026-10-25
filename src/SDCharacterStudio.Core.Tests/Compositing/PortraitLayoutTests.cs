using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Serialization;
using Xunit;

namespace SDCharacterStudio.Core.Tests.Compositing;

public sealed class PortraitLayoutTests
{
    // Wrong index ordering would put a valid composite into the wrong vanilla emotion cell.
    [Fact]
    public void NamedAndNumericEmotionsOccupyNativeIndicesRegardlessOfDictionaryOrder()
    {
        var portrait = Portrait();
        foreach (var name in new[] { "9", "angry", "LOVE", "unique", "sad", "happy" })
            portrait.Emotions[name] = Frame(name);
        var sheet = PortraitLayout.Create(portrait);
        Assert.Equal("sheet", sheet.Kind);
        Assert.Equal("robin", sheet.For);
        Assert.Equal(new[] { 64, 64 }, sheet.FrameSize);
        Assert.Equal(new[] { 128, 320 }, sheet.SheetSize);
        Assert.Equal(10, sheet.Frames.Count);
        Assert.Equal(new[] { "neutral", "happy", "sad", "unique", "LOVE", "angry", "neutral", "neutral", "neutral", "9" },
            sheet.Frames.Select(f => Assert.IsType<ImageLayer>(f.Layers[0]).Image));
    }

    // Missing cells must reuse the neutral source frame, not accidentally sample the missing emotion.
    [Fact]
    public void SparseNumericEmotionsFillGapsAndTheLastPhysicalCellWithNeutralComposite()
    {
        var portrait = Portrait();
        portrait.Emotions["neutral"] = new() { Layers = new() { Live("Portrait") } };
        portrait.Emotions["6"] = new() { Layers = new() { Live("PortraitSheet") } };
        var sheet = PortraitLayout.Create(portrait);
        Assert.Equal(new[] { 128, 256 }, sheet.SheetSize);
        Assert.Equal(8, sheet.Frames.Count);
        Assert.Equal(new int?[] { 0, 0, 0, 0, 0, 0, 6, 0 },
            sheet.Frames.Select(f => Assert.IsType<LiveLayer>(f.Layers[0]).SourceFrame!.Index));
    }

    [Fact]
    public void NeutralOnlyStillSupportsEveryNamedVanillaEmotion()
    {
        var sheet = PortraitLayout.Create(Portrait());
        Assert.Equal(new[] { 128, 192 }, sheet.SheetSize);
        Assert.Equal(6, sheet.Frames.Count);
        Assert.All(sheet.Frames, f => Assert.Equal("neutral", Assert.IsType<ImageLayer>(f.Layers.Single()).Image));
    }

    // Setting defaults must preserve explicit frame/facing and leave nonportrait live sources alone.
    [Fact]
    public void PortraitSourceDefaultsPreserveExplicitIndexFacingAndOtherSourceKinds()
    {
        var portrait = Portrait();
        var missingIndex = Live("pOrTrAiT");
        missingIndex.SourceFrame = new() { Facing = "left", Stand = true };
        var explicitIndex = Live("PortraitSheet");
        explicitIndex.SourceFrame = new() { Index = 8, Facing = "right" };
        var npc = Live("Npc");
        portrait.Emotions["sad"] = new() { Layers = new() { missingIndex, explicitIndex, npc } };
        var before = PackJson.Serialize(portrait);
        var sheet = PortraitLayout.Create(portrait);
        var first = Assert.IsType<LiveLayer>(sheet.Frames[2].Layers[0]);
        Assert.Equal(2, first.SourceFrame!.Index);
        Assert.Equal("left", first.SourceFrame.Facing);
        Assert.True(first.SourceFrame.Stand);
        var second = Assert.IsType<LiveLayer>(sheet.Frames[2].Layers[1]);
        Assert.Equal(8, second.SourceFrame!.Index);
        Assert.Equal("right", second.SourceFrame.Facing);
        Assert.Null(Assert.IsType<LiveLayer>(sheet.Frames[2].Layers[2]).SourceFrame);
        Assert.Equal(before, PackJson.Serialize(portrait));
    }

    // Lost anchors or layer placement settings would visibly move and recolor the composed head.
    [Fact]
    public void PerEmotionAnchorsMasksAndColorSlotsReachTheExistingCompositor()
    {
        var portrait = Portrait();
        var layer = Live("Portrait");
        layer.Mask = "head";
        layer.ColorSlots = new() { ["Skin"] = "#FF0000" };
        layer.AnchorTo = new() { Anchor = "Neck", From = new JValue("rig") };
        layer.Offset = new[] { 1, 0 };
        portrait.Emotions["happy"] = new()
        {
            Layers = new() { layer }, Anchors = new() { ["Neck"] = new[] { 4, 5 } }
        };
        var sheet = PortraitLayout.Create(portrait);
        var pixels = Compositor.Compose(64, 64, sheet.Frames[1], new HeadSource());
        Assert.Equal(0xFFFF0000u, pixels.Pixels[5 * 64 + 4]);
        Assert.Equal(0u, pixels.Pixels[5 * 64 + 5]);
        Assert.Equal(new[] { 4, 5 }, portrait.Emotions["happy"].Anchors["Neck"]);
    }

    [Theory]
    [InlineData("confused")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("1.5")]
    [InlineData("2147483648")]
    [InlineData("128")]
    public void InvalidOrExcessiveEmotionIndicesFailBeforeSheetAllocation(string emotion)
    {
        var portrait = Portrait(); portrait.Emotions[emotion] = Frame(emotion);
        Assert.Throws<ArgumentException>(() => PortraitLayout.Create(portrait));
    }

    [Theory]
    [InlineData(32, 64)]
    [InlineData(64, 32)]
    [InlineData(0, 64)]
    public void FrameDimensionsMustMatchVanillaPortraitContract(int width, int height)
    {
        var portrait = Portrait(); portrait.FrameSize = new[] { width, height };
        Assert.Throws<ArgumentException>(() => PortraitLayout.Create(portrait));
    }

    [Fact]
    public void NumericZeroCanDefineTheRequiredNeutralComposite()
    {
        var portrait = Portrait(); portrait.Emotions.Clear(); portrait.Emotions["0"] = Frame("zero");
        Assert.Equal("zero", Assert.IsType<ImageLayer>(PortraitLayout.Create(portrait).Frames[5].Layers[0]).Image);
    }

    [Fact]
    public void MissingNeutralIsRejectedRatherThanSilentlyChoosingAnotherEmotion()
    {
        var portrait = Portrait(); portrait.Emotions.Clear(); portrait.Emotions["happy"] = Frame("happy");
        Assert.Throws<ArgumentException>(() => PortraitLayout.Create(portrait));
    }

    [Fact]
    public void AliasesForTheSameEmotionAreRejectedRatherThanDependingOnInsertionOrder()
    {
        var portrait = Portrait(); portrait.Emotions["0"] = Frame("duplicate");
        Assert.Throws<ArgumentException>(() => PortraitLayout.Create(portrait));
    }

    private static Portrait Portrait() => new() { Id = "test", For = "robin", Emotions = new() { ["neutral"] = Frame("neutral") } };
    private static PortraitFrame Frame(string image) => new() { Layers = new() { new ImageLayer { Image = image } } };
    private static LiveLayer Live(string kind) => new() { Source = new() { Kind = kind, Npc = "Robin" } };

    private sealed class HeadSource : ILayerSourceResolver
    {
        public ResolvedLayerSource Resolve(Layer layer) => new(new RgbaImage(2, 1, new uint[] { 0xFFBF00BF, 0xFFBF00BF }),
            Assert.IsType<LiveLayer>(layer).SourceFrame!.Index!.Value, new PixelPoint(1, 0));
        public Mask GetMask(string id) => new() { Image = "head.png" };
        public RgbaImage ResolveMaskImage(string path) => new(2, 1, new uint[] { 0xFFFFFFFF, 0 });
    }
}
