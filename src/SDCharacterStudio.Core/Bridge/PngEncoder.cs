using SDCharacterStudio.Core.Compositing;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace SDCharacterStudio.Core.Bridge;

public static class PngEncoder
{
    public static byte[] Encode(RgbaImage image)
    {
        using var output = new MemoryStream();
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, image.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), image.Height);
        header[8] = 8; header[9] = 6;
        Chunk(output, "IHDR", header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, true))
        {
            var row = new byte[checked(1 + image.Width * 4)];
            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    uint pixel = image.Pixels[y * image.Width + x]; int start = 1 + x * 4;
                    row[start] = (byte)(pixel >> 16); row[start + 1] = (byte)(pixel >> 8);
                    row[start + 2] = (byte)pixel; row[start + 3] = (byte)(pixel >> 24);
                }
                zlib.Write(row);
            }
        }
        Chunk(output, "IDAT", compressed.ToArray());
        Chunk(output, "IEND", Array.Empty<byte>());
        return output.ToArray();
    }

    private static void Chunk(Stream output, string name, byte[] data)
    {
        var kind = Encoding.ASCII.GetBytes(name);
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length); output.Write(number);
        output.Write(kind); output.Write(data);
        uint crc = uint.MaxValue;
        foreach (byte value in kind.Concat(data))
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? crc >> 1 ^ 0xEDB88320u : crc >> 1;
        }
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc); output.Write(number);
    }
}
