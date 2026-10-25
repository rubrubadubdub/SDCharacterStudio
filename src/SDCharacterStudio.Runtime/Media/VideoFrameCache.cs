using System;
using System.Collections.Generic;
using System.Linq;

namespace SDCharacterStudio.Runtime.Media;

/// <summary>Own disposable video frames across menu lifetimes.</summary>
internal sealed class VideoFrameCache<T> where T : class, IDisposable
{
    private readonly Dictionary<string, Dictionary<string, T[]>> packs = new(StringComparer.OrdinalIgnoreCase);

    public T[] GetOrLoad(string packId, string videoId, IEnumerable<string> paths, Func<string, T> load, Action<IReadOnlyList<T>> validate)
    {
        if (packs.TryGetValue(packId, out var videos) && videos.TryGetValue(videoId, out var cached)) return cached;

        var allocated = new List<T>();
        try
        {
            foreach (string path in paths) allocated.Add(load(path));
            T[] frames = allocated.ToArray();
            validate(frames);
            if (videos == null) packs.Add(packId, videos = new Dictionary<string, T[]>(StringComparer.OrdinalIgnoreCase));
            videos.Add(videoId, frames);
            return frames;
        }
        catch (Exception loadError)
        {
            try { DisposeFrames(allocated); }
            catch (Exception disposeError) { throw new AggregateException(loadError, disposeError); }
            throw;
        }
    }

    /// <summary>Remove cache entries before disposing their frames. Call only after menus release their references.</summary>
    public void Clear(string? packId = null)
    {
        var removed = new List<T[]>();
        if (packId == null)
        {
            foreach (var videos in packs.Values) removed.AddRange(videos.Values);
            packs.Clear();
        }
        else if (packs.Remove(packId, out var videos)) removed.AddRange(videos.Values);
        DisposeFrames(removed.SelectMany(frames => frames));
    }

    private static void DisposeFrames(IEnumerable<T> frames)
    {
        var disposed = new HashSet<T>(ReferenceEqualityComparer.Instance);
        var errors = new List<Exception>();
        foreach (T frame in frames)
        {
            if (!disposed.Add(frame)) continue;
            try { frame.Dispose(); }
            catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count > 0) throw new AggregateException("One or more video frames could not be disposed.", errors);
    }
}
