using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SDCharacterStudio.Core.Models;

namespace SDCharacterStudio.Runtime.Media;

/// <summary>Frame ordering and playback state without game dependencies.</summary>
internal sealed class VideoPlayback
{
    private const double FadeTime = 300;
    private static readonly Regex FrameName = new(@"^frame[-_](\d+)\.png$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private readonly int frameCount;
    private readonly double frameTime;
    private readonly double duration;
    private readonly Dictionary<int, string> sounds = new();
    private double elapsed;
    private int lastFrame = -1;

    public VideoPlayback(int frameCount, VideoSettings? settings)
    {
        if (frameCount < 1) throw new ArgumentOutOfRangeException(nameof(frameCount));
        this.frameCount = frameCount;
        double fps = settings?.Fps ?? 12;
        if (!double.IsFinite(fps) || fps <= 0 || !double.IsFinite(frameCount * (1000 / fps))) fps = 12;
        frameTime = 1000 / fps;
        duration = frameCount * frameTime;
        if (settings?.Sounds != null)
            foreach (var entry in settings.Sounds)
                if (int.TryParse(entry.Key, NumberStyles.None, CultureInfo.InvariantCulture, out int frame)
                    && frame >= 1 && frame <= frameCount && !string.IsNullOrWhiteSpace(entry.Value))
                    sounds[frame] = entry.Value;
    }

    /// <summary>Recognize numbered frame PNGs and order by frame number, then ordinal path.</summary>
    public static string[] OrderFramePaths(IEnumerable<string> paths) => paths
        .Select(path => (Path: path, Match: FrameName.Match(Path.GetFileName(path))))
        .Where(entry => entry.Match.Success)
        .Select(entry => (entry.Path, Number: long.TryParse(entry.Match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long number) ? number : long.MaxValue))
        .OrderBy(entry => entry.Number)
        .ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
        .ThenBy(entry => entry.Path, StringComparer.Ordinal)
        .Select(entry => entry.Path).ToArray();

    public int FrameIndex => (int)Math.Min(frameCount - 1, Math.Floor(elapsed / frameTime));
    public bool IsFinished => elapsed >= duration + FadeTime;
    public float Fade => elapsed >= duration
        ? (float)Math.Max(0, 1 - (elapsed - duration) / FadeTime)
        : (float)Math.Min(1, elapsed / FadeTime);

    /// <summary>Advance time and return every newly reached cue in playback order exactly once.</summary>
    public string[] Advance(double milliseconds)
    {
        if (double.IsFinite(milliseconds) && milliseconds > 0)
            elapsed = Math.Min(duration + FadeTime, elapsed + milliseconds);
        var reached = new List<string>();
        int frame = FrameIndex;
        while (lastFrame < frame)
            if (sounds.TryGetValue(++lastFrame + 1, out string? cue)) reached.Add(cue);
        return reached.ToArray();
    }

    public void SkipToEnd() => elapsed = Math.Max(elapsed, duration);
}
