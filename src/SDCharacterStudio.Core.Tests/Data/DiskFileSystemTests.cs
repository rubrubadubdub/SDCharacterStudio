using System.Buffers.Binary;
using SDCharacterStudio.Core.Loading;
using Xunit;

namespace SDCharacterStudio.Core.Tests.Data;
public class DiskFileSystemTests
{
    [Fact] public void DiskAdapterListsOnlyMatchingPackFilesAndReadsPngDimensions()
    {
        var root=Path.Combine(Path.GetTempPath(),"sdcs-data-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,"art"));Directory.CreateDirectory(Path.Combine(root,"cutscenes"));
        try
        {
            File.WriteAllText(Path.Combine(root,"cutscenes","talk.json"),"{}");File.WriteAllText(Path.Combine(root,"cutscenes","ignore.txt"),"note");
            var header=new byte[24];new byte[]{137,80,78,71,13,10,26,10}.CopyTo(header,0);BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(8),13);new byte[]{73,72,68,82}.CopyTo(header,12);BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(16),16);BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(20),32);
            File.WriteAllBytes(Path.Combine(root,"art","body.png"),header);var fs=new DiskPackFileSystem(root);
            Assert.Equal(new[]{"cutscenes/talk.json"},fs.ListFiles("cutscenes/*.json"));Assert.Equal(new ImageSize(16,32),fs.GetImageSize("art/body.png"));
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(8),12);File.WriteAllBytes(Path.Combine(root,"art","bad.png"),header);
            Assert.Null(fs.GetImageSize("art/bad.png"));Assert.Throws<ArgumentException>(()=>fs.ReadText("../outside.json"));
        }
        finally { Directory.Delete(root,true); }
    }
}
