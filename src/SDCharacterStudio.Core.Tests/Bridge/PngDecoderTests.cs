using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core.Bridge;
using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Palette;
using Xunit;

namespace SDCharacterStudio.Core.Tests.Bridge;

/// <summary>The editor's PNG decoder must be exact: reserved slot colours (doc 05 §4.1) only work bit for bit.</summary>
public sealed class PngDecoderTests
{
    private static string Folder => Path.Combine(AppContext.BaseDirectory, "TestData", "Png");

    public static IEnumerable<object[]> Fixtures() =>
        JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "Png", "expected.json"))).Properties().Select(p => new object[] { p.Name });

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void DecodesEveryColourTypeDepthAndInterlace(string name)
    {
        var expected = (JObject)JObject.Parse(File.ReadAllText(Path.Combine(Folder, "expected.json")))[name]!;
        var image = PngDecoder.Decode(File.ReadAllBytes(Path.Combine(Folder, name)));
        Assert.Equal((int)expected["Width"]!, image.Width);
        Assert.Equal((int)expected["Height"]!, image.Height);
        Assert.Equal(expected["Pixels"]!.Values<long>().Select(v => (uint)v).ToArray(), image.Pixels);
    }

    [Fact]
    public void RoundTripsTheEncoderIncludingSlotColours()
    {
        var pixels = new uint[] { ColorSlots.SkinDark, ColorSlots.SkinMid, ColorSlots.Erase, 0, 0x80FF0000, ColorSlots.Outline };
        var image = new RgbaImage(3, 2, pixels);
        var decoded = PngDecoder.Decode(PngEncoder.Encode(image));
        Assert.Equal(pixels, decoded.Pixels);
    }

    [Fact]
    public void RejectsNonPngData()
    {
        Assert.False(PngDecoder.IsPng(new byte[] { 1, 2, 3 }));
        Assert.Throws<InvalidDataException>(() => PngDecoder.Decode(new byte[] { 0x89, 0x50, 0x4E }));
    }
}
