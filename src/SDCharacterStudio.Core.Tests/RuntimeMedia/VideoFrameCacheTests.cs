using SDCharacterStudio.Runtime.Media;
using Xunit;

namespace SDCharacterStudio.Core.Tests.RuntimeMedia;

public sealed class VideoFrameCacheTests
{
    [Fact]
    public void ReplayingVideoReusesFramesUntilThePackIsCleared()
    {
        var cache = new VideoFrameCache<Frame>();
        var allocated = new List<Frame>();
        Frame Load(string _) { var frame = new Frame(); allocated.Add(frame); return frame; }
        var first = cache.GetOrLoad("Pack.One", "intro", new[] { "1", "2" }, Load, _ => { });
        var replay = cache.GetOrLoad("pack.one", "INTRO", new[] { "1", "2" }, Load, _ => { });
        Assert.Same(first, replay);
        Assert.Equal(2, allocated.Count);
        Assert.All(first, frame => Assert.Equal(0, frame.DisposeCount));
        cache.Clear("PACK.ONE");
        Assert.All(first, frame => Assert.Equal(1, frame.DisposeCount));
        cache.Clear("Pack.One");
        Assert.All(first, frame => Assert.Equal(1, frame.DisposeCount));
        var refreshed = cache.GetOrLoad("Pack.One", "intro", new[] { "1", "2" }, Load, _ => { });
        Assert.NotSame(first, refreshed);
        Assert.All(refreshed, frame => Assert.Equal(0, frame.DisposeCount));
    }

    [Fact]
    public void ClearingOnePackPreservesOthersAndGlobalClearDisposesTheRest()
    {
        var cache = new VideoFrameCache<Frame>();
        var first = cache.GetOrLoad("A", "intro", new[] { "1" }, _ => new Frame(), _ => { });
        var otherVideo = cache.GetOrLoad("A", "outro", new[] { "1" }, _ => new Frame(), _ => { });
        var otherPack = cache.GetOrLoad("B", "intro", new[] { "1" }, _ => new Frame(), _ => { });
        cache.Clear("a");
        Assert.Equal(1, first[0].DisposeCount);
        Assert.Equal(1, otherVideo[0].DisposeCount);
        Assert.Equal(0, otherPack[0].DisposeCount);
        cache.Clear();
        cache.Clear();
        Assert.Equal(1, first[0].DisposeCount);
        Assert.Equal(1, otherPack[0].DisposeCount);
    }

    [Fact]
    public void PartialLoadFailureDisposesAllocatedFramesAndAllowsRetry()
    {
        var cache = new VideoFrameCache<Frame>();
        var partial = new Frame();
        Assert.Throws<IOException>(() => cache.GetOrLoad("A", "intro", new[] { "1", "bad" },
            path => path == "bad" ? throw new IOException("broken PNG") : partial, _ => { }));
        Assert.Equal(1, partial.DisposeCount);
        var retry = cache.GetOrLoad("A", "intro", new[] { "1" }, _ => new Frame(), _ => { });
        Assert.NotSame(partial, retry[0]);
        Assert.Equal(0, retry[0].DisposeCount);
        cache.Clear();
        Assert.Equal(1, retry[0].DisposeCount);
        Assert.Equal(1, partial.DisposeCount);
    }

    [Fact]
    public void InvalidDimensionsDisposeAllLoadedFramesAndDoNotPublishCacheEntry()
    {
        var cache = new VideoFrameCache<Frame>();
        var allocated = new List<Frame>();
        Frame Load(string path) { var frame = new Frame { Width = path == "wide" ? 2 : 1 }; allocated.Add(frame); return frame; }
        Assert.Throws<InvalidDataException>(() => cache.GetOrLoad("A", "intro", new[] { "1", "wide" }, Load,
            frames => { if (frames.Any(frame => frame.Width != frames[0].Width)) throw new InvalidDataException("dimensions"); }));
        Assert.Equal(2, allocated.Count);
        Assert.All(allocated, frame => Assert.Equal(1, frame.DisposeCount));
        var valid = cache.GetOrLoad("A", "intro", new[] { "1" }, Load, _ => { });
        Assert.Single(valid);
        Assert.Equal(0, valid[0].DisposeCount);
        cache.Clear();
        Assert.All(allocated, frame => Assert.Equal(1, frame.DisposeCount));
    }

    [Fact]
    public void ClearAttemptsEveryDisposalEvenWhenOneResourceThrows()
    {
        var cache = new VideoFrameCache<Frame>();
        var frames = cache.GetOrLoad("A", "intro", new[] { "throws", "normal" },
            path => new Frame { ThrowOnDispose = path == "throws" }, _ => { });
        Assert.Throws<AggregateException>(() => cache.Clear());
        Assert.All(frames, frame => Assert.Equal(1, frame.DisposeCount));
        cache.Clear();
        Assert.All(frames, frame => Assert.Equal(1, frame.DisposeCount));
    }

    private sealed class Frame : IDisposable
    {
        public int Width { get; init; } = 1;
        public bool ThrowOnDispose { get; init; }
        public int DisposeCount { get; private set; }
        public void Dispose()
        {
            DisposeCount++;
            if (ThrowOnDispose) throw new InvalidOperationException("dispose failed");
        }
    }
}
