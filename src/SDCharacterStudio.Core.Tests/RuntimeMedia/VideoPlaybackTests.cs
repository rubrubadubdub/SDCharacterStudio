using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Runtime.Media;
using Xunit;

namespace SDCharacterStudio.Core.Tests.RuntimeMedia;

public sealed class VideoPlaybackTests
{
    [Fact]
    public void NumberedFramesSortNumericallyAndIgnoreUnrelatedImages()
    {
        var paths = new[] { "frame_10.png", "poster.png", "frame_2.PNG", "frame-001.png", "frame_000.png", "frame_3.jpg" };
        Assert.Equal(new[] { "frame_000.png", "frame-001.png", "frame_2.PNG", "frame_10.png" }, VideoPlayback.OrderFramePaths(paths));
    }

    [Fact]
    public void ReachedSoundsFireOnceEvenWhenAnUpdateCrossesSeveralFrames()
    {
        var playback = new VideoPlayback(4, new VideoSettings { Fps = 10, Sounds = new() { ["1"] = "first", ["2"] = "second", ["4"] = "last" } });
        Assert.Equal(new[] { "first" }, playback.Advance(0));
        Assert.Empty(playback.Advance(50));
        Assert.Equal(new[] { "second", "last" }, playback.Advance(260));
        Assert.Equal(3, playback.FrameIndex);
        Assert.Empty(playback.Advance(100));
        Assert.False(playback.IsFinished);
        Assert.Empty(playback.Advance(290));
        Assert.True(playback.IsFinished);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidFpsUsesTwelveFramesPerSecond(double fps)
    {
        var playback = new VideoPlayback(12, new VideoSettings { Fps = fps });
        playback.Advance(999);
        Assert.Equal(11, playback.FrameIndex);
        Assert.False(playback.IsFinished);
        playback.Advance(301);
        Assert.True(playback.IsFinished);
    }

    [Fact]
    public void MetadataIgnoresInvalidOneBasedSoundKeysAndEmptyCues()
    {
        var playback = new VideoPlayback(2, new VideoSettings { Fps = 10, Sounds = new()
        {
            ["0"] = "zero", ["-1"] = "negative", ["bad"] = "bad", ["3"] = "outside", ["1"] = " ", ["2"] = "valid"
        } });
        Assert.Equal(new[] { "valid" }, playback.Advance(200));
    }

    [Fact]
    public void SkipKeepsFadeOutAndCanBeRepeatedWithoutExtendingPlayback()
    {
        var playback = new VideoPlayback(20, new VideoSettings { Fps = 10 });
        playback.SkipToEnd();
        Assert.Equal(19, playback.FrameIndex);
        Assert.Equal(1f, playback.Fade);
        playback.Advance(150);
        Assert.Equal(0.5f, playback.Fade);
        playback.SkipToEnd();
        playback.Advance(150);
        Assert.True(playback.IsFinished);
        Assert.Equal(0f, playback.Fade);
    }

    [Fact]
    public void ShortVideosFadeOutAtTheEndAndBadTimeDoesNotRewindThem()
    {
        var playback = new VideoPlayback(1, new VideoSettings { Fps = 10 });
        playback.Advance(250);
        Assert.Equal(0.5f, playback.Fade);
        playback.Advance(-100);
        playback.Advance(double.NaN);
        Assert.Equal(0.5f, playback.Fade);
        playback.Advance(150);
        Assert.True(playback.IsFinished);
    }
}
