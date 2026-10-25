using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using SDCharacterStudio.Core.Loading;
using SDCharacterStudio.Core.Models;

namespace SDCharacterStudio.Editor.Project;

/// <summary>Copies a PNG sequence into videos/&lt;id&gt;/ as frame-001.png… with a video.json (doc 03 §14).</summary>
public static class VideoImporter
{
    private static readonly Regex Number = new(@"\d+", RegexOptions.CultureInvariant);

    /// <summary>PNG files in natural order (frame2 before frame10).</summary>
    public static IReadOnlyList<string> FramesIn(string folder) =>
        Directory.EnumerateFiles(folder, "*.png").OrderBy(f => Number.Replace(Path.GetFileNameWithoutExtension(f), m => m.Value.PadLeft(10, '0')), StringComparer.OrdinalIgnoreCase).ToArray();

    public static VideoEntry Import(PackDocument document, IReadOnlyList<string> frames, string id, double fps, IReadOnlyDictionary<int, string>? sounds = null)
    {
        id = PackDocument.ToContentId(id);
        if (frames.Count == 0) throw new ArgumentException("Pick at least one PNG frame.");
        if (fps <= 0 || fps > 60) throw new ArgumentException("Frames per second must be between 1 and 60.");
        var probe = new DiskPackFileSystem(Path.GetDirectoryName(frames[0])!);
        var size = probe.GetImageSize(Path.GetFileName(frames[0])) ?? throw new ArgumentException($"{Path.GetFileName(frames[0])} isn't a PNG.");
        foreach (var frame in frames)
        {
            var other = new DiskPackFileSystem(Path.GetDirectoryName(frame)!).GetImageSize(Path.GetFileName(frame));
            if (other != size) throw new ArgumentException($"Every frame must be {size.Width}×{size.Height}; {Path.GetFileName(frame)} isn't.");
        }
        string folder = Path.Combine(document.PackFolder, "videos", id);
        if (frames.Any(f => Path.GetFullPath(Path.GetDirectoryName(f)!).Equals(Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Those frames are already this pack's video. Pick the original frames folder instead.");
        var settings = new VideoSettings { Fps = fps, Sounds = (sounds ?? new Dictionary<int, string>()).Where(s => !string.IsNullOrWhiteSpace(s.Value)).ToDictionary(s => s.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), s => s.Value.Trim()) };
        var entry = new VideoEntry { Id = id, FrameCount = frames.Count, Settings = settings, FirstFrame = "videos/" + id + "/frame-001.png", Width = size.Width, Height = size.Height };
        document.Edit("Import video", () => { document.StageVideo(id, frames, settings); document.Videos.RemoveAll(v => v.Id.Equals(id, StringComparison.OrdinalIgnoreCase)); document.Videos.Add(entry); });
        return entry;
    }
}
