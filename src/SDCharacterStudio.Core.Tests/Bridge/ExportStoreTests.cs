using System.Buffers.Binary;
using System.IO.Compression;
using SDCharacterStudio.Core.Bridge;
using SDCharacterStudio.Core.Compositing;
using Xunit;

namespace SDCharacterStudio.Core.Tests.Bridge;

public sealed class ExportStoreTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "sdcs-export-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("../escape.png")]
    [InlineData("textures/../../escape.png")]
    [InlineData("C:/escape.png")]
    [InlineData("/escape.png")]
    [InlineData("textures/Robin:bad.png")]
    public void RejectsPathsOutsideExport(string relative) => Assert.Throws<ArgumentException>(() => new ExportStore(folder).PathFor(relative));

    [Fact]
    public void UnchangedBytesPreserveTimestampAndDeletedFilesAreRepaired()
    {
        var store = new ExportStore(folder);
        Assert.True(store.Write("textures/Robin.png", new byte[] { 1, 2, 3 }));
        string path = store.PathFor("textures/Robin.png");
        File.SetLastWriteTimeUtc(path, new DateTime(2020, 1, 1));
        Assert.False(store.Write("textures/Robin.png", new byte[] { 1, 2, 3 }));
        Assert.Equal(2020, File.GetLastWriteTimeUtc(path).Year);
        store.SaveState();
        File.Delete(path);
        Assert.True(new ExportStore(folder).Write("textures/Robin.png", new byte[] { 1, 2, 3 }));
    }

    [Fact]
    public void CorruptStateDoesNotPreventExportOrRewriteUnchangedFile()
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "export-state.json"), "{broken");
        File.WriteAllBytes(Path.Combine(folder, "data.json"), new byte[] { 4 });
        var store = new ExportStore(folder);
        Assert.False(store.Write("data.json", new byte[] { 4 }));
        Assert.True(store.Write("data.json", new byte[] { 5 }));
    }

    [Fact]
    public void PngPreservesStraightAlphaAndRgbaChannelOrder()
    {
        byte[] png = PngEncoder.Encode(new RgbaImage(2, 1, new uint[] { 0x80402010, 0x00000000 }));
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
        var idat = new MemoryStream();
        for (int pos = 8; pos < png.Length;)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(pos));
            string kind = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
            if (kind == "IHDR") { Assert.Equal(2, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(pos + 8))); Assert.Equal(6, png[pos + 17]); }
            if (kind == "IDAT") idat.Write(png, pos + 8, length);
            pos += 12 + length;
        }
        idat.Position = 0;
        using var zlib = new ZLibStream(idat, CompressionMode.Decompress);
        using var decoded = new MemoryStream(); zlib.CopyTo(decoded);
        Assert.Equal(new byte[] { 0, 0x40, 0x20, 0x10, 0x80, 0, 0, 0, 0 }, decoded.ToArray());
    }

    public void Dispose() { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
}
