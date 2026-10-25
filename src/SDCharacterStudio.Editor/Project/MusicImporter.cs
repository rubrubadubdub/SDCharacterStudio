using System.Buffers.Binary;
using System.IO;
using SDCharacterStudio.Core.Audio;
using SDCharacterStudio.Core.Models;

namespace SDCharacterStudio.Editor.Project;

/// <summary>Copies local music into the pack's undoable, portable asset store.</summary>
public static class MusicImporter
{
    public const int MaximumBytes = 64 * 1024 * 1024;

    public static string Import(PackDocument document, string source, Action<string> assign)
    {
        if (document.Studio.Music == null) throw new InvalidDataException("Fix the Music definitions reported in Problems before importing another track.");
        string extension = Path.GetExtension(source).ToLowerInvariant();
        if (extension is not (".wav" or ".ogg")) throw new InvalidDataException("Choose a WAV (16-bit PCM) or OGG Vorbis file. Convert MP3 files to one of these formats first.");
        using var stream = File.OpenRead(source);
        if (stream.Length == 0 || stream.Length > MaximumBytes) throw new InvalidDataException("Music files must be nonempty and no larger than 64 MiB.");
        byte[] bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes);
        Validate(bytes, extension);
        string name = Path.GetFileNameWithoutExtension(source);
        string stem = PackDocument.ToContentId(name);
        string displayName = name; int suffix = 2;
        while (document.Studio.Music.Values.Any(t => string.Equals(t?.Name, displayName, StringComparison.OrdinalIgnoreCase)))
            displayName = name + " (" + suffix++ + ")";
        string id = stem; int next = 2;
        var files = document.CreateOverlayFileSystem();
        while (document.Studio.Music.ContainsKey(id) || files.Exists("assets/audio/" + id + extension)) id = stem + "-" + next++;
        string path = "assets/audio/" + id + extension;
        string reference = PackMusic.Reference(id);
        document.Edit("Import music", () =>
        {
            document.SetBinaryAsset(path, bytes);
            document.Studio.Music.Add(id, new MusicTrack { Name = displayName, File = path });
            assign(reference);
        });
        return reference;
    }

    private static void Validate(byte[] buffer, string extension)
    {
        ReadOnlySpan<byte> bytes = buffer;
        if (extension == ".ogg")
        {
            // Verify complete Ogg pages and the Vorbis identification packet (Opus is not supported by the game).
            int at = 0; bool first = true, ended = false;
            while (at < bytes.Length)
            {
                if (bytes.Length - at < 27 || !bytes.Slice(at, 4).SequenceEqual("OggS"u8) || bytes[at + 4] != 0) throw Invalid();
                int segments = bytes[at + 26], body = at + 27 + segments;
                if (segments == 0 || body > bytes.Length) throw Invalid();
                int length = 0; for (int i = at + 27; i < body; i++) length += bytes[i];
                if (length > bytes.Length - body) throw Invalid();
                if (first && (length < 30 || !bytes.Slice(body, 7).SequenceEqual(new byte[] { 1, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s' }))) throw Invalid();
                ended = (bytes[at + 5] & 4) != 0;
                first = false; at = body + length;
            }
            if (first || !ended) throw Invalid();
            try
            {
                using var reader = new NVorbis.VorbisReader(new MemoryStream(buffer, writable: false), closeOnDispose: true);
                if (reader.Channels is not (1 or 2) || reader.SampleRate is < 8000 or > 192000 || reader.TotalSamples <= 0
                    || reader.ReadSamples(new float[4096], 0, 4096) == 0) throw Invalid();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            { throw new InvalidDataException("This OGG file cannot be decoded. Choose a complete mono/stereo OGG Vorbis file.", ex); }
            return;
        }
        if (bytes.Length < 44 || !bytes[..4].SequenceEqual("RIFF"u8) || !bytes.Slice(8, 4).SequenceEqual("WAVE"u8)
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(4, 4)) != bytes.Length - 8) throw Invalid();
        bool format = false, samples = false; int blockSize = 0, dataSize = 0;
        for (int at = 12; at < bytes.Length;)
        {
            if (bytes.Length - at < 8) throw Invalid();
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(at + 4, 4));
            if (length > bytes.Length - at - 8) throw Invalid();
            var chunk = bytes.Slice(at + 8, (int)length);
            if (bytes.Slice(at, 4).SequenceEqual("fmt "u8))
            {
                if (length < 16) throw Invalid();
                int channels = BinaryPrimitives.ReadUInt16LittleEndian(chunk.Slice(2, 2));
                uint rate = BinaryPrimitives.ReadUInt32LittleEndian(chunk.Slice(4, 4));
                blockSize = BinaryPrimitives.ReadUInt16LittleEndian(chunk.Slice(12, 2));
                if (BinaryPrimitives.ReadUInt16LittleEndian(chunk[..2]) != 1 || channels is not (1 or 2)
                    || BinaryPrimitives.ReadUInt16LittleEndian(chunk.Slice(14, 2)) != 16 || blockSize != channels * 2
                    || rate is < 8000 or > 192000 || BinaryPrimitives.ReadUInt32LittleEndian(chunk.Slice(8, 4)) != rate * blockSize) throw Invalid();
                format = true;
            }
            if (bytes.Slice(at, 4).SequenceEqual("data"u8)) { dataSize = (int)length; samples = dataSize > 0; }
            at = checked(at + 8 + (int)length + (int)(length % 2));
            if (at > bytes.Length) throw Invalid();
        }
        if (!format || !samples || dataSize % blockSize != 0) throw Invalid();
        static InvalidDataException Invalid() => new("This file isn't a complete supported audio file. Use WAV (16-bit PCM, mono/stereo) or OGG Vorbis.");
    }
}
