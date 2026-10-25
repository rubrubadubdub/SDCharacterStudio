using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using StardewModdingAPI;
using StardewValley.GameData;

namespace SDCharacterStudio.Bridge.Export;

/// <summary>Optional offline audition sources. The compatibility cue-name list stays unchanged.</summary>
internal static class SoundPreviewExporter
{
    public static void Schedule(ExportSession session)
    {
        var cues = new JObject();
        foreach (var (id, data) in session.Helper.GameContent.Load<Dictionary<string, AudioCueData>>("Data/AudioChanges").OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var variants = new JArray();
            var entry = new JObject { ["Category"] = data.Category, ["Looped"] = data.Looped, ["UseReverb"] = data.UseReverb, ["StreamedVorbis"] = data.StreamedVorbis, ["Variants"] = variants };
            cues[id] = entry;
            if (data.FilePaths == null || data.FilePaths.Count == 0) continue;
            session.Queue("audio sources " + id, () =>
            {
                try
                {
                    string folder = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))).ToLowerInvariant();
                    foreach (string source in data.FilePaths)
                    {
                        string path = Path.IsPathRooted(source) ? source : Path.Combine(Constants.GamePath, "Content", source);
                        string extension = Path.GetExtension(path).ToLowerInvariant();
                        if (extension is not (".wav" or ".ogg")) throw new InvalidDataException("Offline audition supports WAV and OGG sources; this cue uses " + extension + ".");
                        var info = new FileInfo(path);
                        if (!info.Exists) throw new FileNotFoundException("Audio source was not found: " + source);
                        if (info.Length > 64 * 1024 * 1024) throw new InvalidDataException("Audio source exceeds the 64 MiB audition cache limit.");
                        string relative = $"audio/{folder}/{variants.Count}{extension}";
                        session.Bytes(relative, File.ReadAllBytes(path));
                        variants.Add(new JObject { ["File"] = relative });
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                { entry["Error"] = ex.Message; }
            });
        }
        session.Queue("sound preview metadata", () => session.Json("sound-previews.json", new JObject { ["Format"] = 1, ["GameDirectory"] = Constants.GamePath, ["Cues"] = cues }));
        session.Catalog["SoundPreviews"] = "sound-previews.json";
    }
}
