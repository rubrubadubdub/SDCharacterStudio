using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using SDCharacterStudio.Core.Audio;
using SDCharacterStudio.Editor.Project;

namespace SDCharacterStudio.Editor.Audio;

/// <summary>A content-addressed audition catalog, including unsaved pack audio.</summary>
public static class PackMusicPreview
{
    public static string Prepare(PackDocument document, string reference)
    {
        if (!PackMusic.TryGetId(reference, out var id) || document.Studio.Music == null || !document.Studio.Music.TryGetValue(id, out var track) || track?.File == null)
            throw new InvalidDataException("The imported music is missing from this pack.");
        string extension = Path.GetExtension(track.File).ToLowerInvariant();
        if (extension is not (".wav" or ".ogg")) throw new InvalidDataException("Music preview supports WAV and OGG files.");
        byte[] bytes = document.ReadBinaryAsset(track.File);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hash + reference + track.Loop)));
        string root = Path.Combine(document.ProjectPath + ".media", "audio-preview", key);
        Directory.CreateDirectory(root);
        string file = "music" + extension;
        if (!File.Exists(Path.Combine(root, file))) File.WriteAllBytes(Path.Combine(root, file), bytes);
        var catalog = new AudioPreviewCatalog
        {
            Format = 1,
            Cues = new(StringComparer.OrdinalIgnoreCase)
            {
                [reference] = new AudioPreviewCue { Category = "Music", Looped = track.Loop, Variants = new() { new() { File = file } } }
            }
        };
        PackWriter.WriteIfChanged(Path.Combine(root, "sound-previews.json"), JsonConvert.SerializeObject(catalog));
        return root;
    }
}
