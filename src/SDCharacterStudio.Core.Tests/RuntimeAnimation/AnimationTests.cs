using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Runtime.Animation;
using Xunit;

namespace SDCharacterStudio.Core.Tests.RuntimeAnimation;

public sealed class AnimationTests
{
    private static Pose Pose() => new() { FrameSize = new[] {16,32}, Intro = new[]{0}, Loop = new[]{1,2}, Outro = new[]{3}, Frames = Enumerable.Range(0,4).Select(_=>new PoseFrame{Duration=100}).ToList() };
    [Fact] public void PoseLoopsAfterIntroAndReleasePlaysOutroOnce()
    {
        var pose = new PoseTimeline(Pose(), "once-then-loop");
        Assert.Equal(0, pose.Frame);
        Assert.Equal(new[]{1,2,1}, pose.Advance(300));
        Assert.False(pose.Finished);
        pose.Release(); Assert.Equal(3, pose.Frame);
        pose.Advance(99); Assert.False(pose.Finished);
        pose.Advance(1); Assert.True(pose.Finished);
    }
    [Fact] public void OnceHoldsLastFrameAndSpeedScalesDuration()
    {
        var pose = new PoseTimeline(Pose(), "once", 2);
        Assert.Equal(new[]{1,2}, pose.Advance(149)); Assert.False(pose.Finished);
        pose.Advance(1); Assert.True(pose.Finished); Assert.Equal(2, pose.Frame);
        Assert.Empty(pose.Advance(999));
    }
    [Fact] public void HoldFirstDoesNotAdvanceAndEmptyOutroReleasesImmediately()
    {
        var definition = Pose(); definition.Outro = Array.Empty<int>();
        var pose = new PoseTimeline(definition,"hold-first");
        Assert.Empty(pose.Advance(10000)); Assert.Equal(0,pose.Frame);
        pose.Release(); Assert.True(pose.Finished);
    }
    [Fact] public void AdvanceReusesItsEnteredFrameBuffer()
    {
        var pose = new PoseTimeline(Pose(), "loop");
        var first = pose.Advance(100);
        var second = pose.Advance(0);
        Assert.Same(first, second);
        Assert.Empty(second);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    public void WarmTimelineUpdatesAllocateNoManagedMemory(int milliseconds)
    {
        var pose = new PoseTimeline(Pose(), "loop");
        for(int i=0;i<1000;i++)pose.Advance(milliseconds);
        long before=GC.GetAllocatedBytesForCurrentThread();
        int entered=0;
        for(int i=0;i<10000;i++)entered+=pose.Advance(milliseconds).Count;
        long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        Assert.Equal(0,allocated);
        Assert.Equal(milliseconds==0?0:1600,entered);
    }
    [Fact] public void QuantisationDoesNotQuantiseCueCrossingsOrLoseLoopKeys()
    {
        var clock = new ClipClock(1000,12,true);
        clock.Advance(130,false); Assert.Equal(1000d/12,clock.VisualTime,8);
        var keys = new[]{new SoundKey{At=0,Cue="a"},new SoundKey{At=120,Cue="b"}};
        Assert.Equal(new[]{"a","b"},ClipClock.Crossings(keys,-1,clock.Elapsed,1000,true).Select(x=>x.Cue));
        Assert.Equal(new[]{"a","b","a"},ClipClock.Crossings(keys,130,2100,1000,true).Select(x=>x.Cue));
        clock.Advance(500,true); Assert.Equal(130,clock.Elapsed);
    }
    [Fact] public void PositionUsesNextEaseRelativeOriginAndGameUnits()
    {
        var keys = new[]{new PositionKey{At=0,Tile=new[]{1,2}},new PositionKey{At=100,Px=new[]{8,4},Relative=true,Ease="step"}};
        var origin=new WorldPoint(100,200);
        Assert.Equal(new WorldPoint(64,128), AnimationMath.PositionAt(keys,50,origin));
        Assert.Equal(new WorldPoint(132,216), AnimationMath.PositionAt(keys,100,origin));
        keys[1].Ease="linear";
        Assert.Equal(new WorldPoint(98,172), AnimationMath.PositionAt(keys,50,origin));
        Assert.Equal(0.5,AnimationMath.Ease("inOut",.5),8);
    }
    [Fact] public void CameraClampsLargeMapsAndCentresSmallMaps()
    {
        Assert.Equal(new WorldPoint(0,0),AnimationMath.Camera(new WorldPoint(10,20),800,600,1600,1200));
        Assert.Equal(new WorldPoint(800,600),AnimationMath.Camera(new WorldPoint(9000,9000),800,600,1600,1200));
        Assert.Equal(new WorldPoint(-200,-150),AnimationMath.Camera(new WorldPoint(0,0),800,600,400,300));
    }
    [Fact] public void MirrorPivotKeepsFeetAndJumpMovesUp()
    {
        Assert.Equal(new WorldPoint(100,20),AnimationMath.StandInTopLeft(new WorldPoint(100,100),16,32,32,16,32,8,31,false,0));
        Assert.Equal(new WorldPoint(104,12),AnimationMath.StandInTopLeft(new WorldPoint(100,100),16,32,32,16,32,8,31,true,-8));
    }
    [Fact] public void EmotesFollowExplicitPivotAndHeadPlacement()
    {
        Assert.Equal(0,AnimationMath.EmoteOffset(31,0));
        Assert.Equal(24,AnimationMath.EmoteOffset(25,0));
        Assert.Equal(20,AnimationMath.EmoteOffset(31,5));
        Assert.Equal(-20,AnimationMath.EmoteOffset(40,4));
    }
    [Fact] public void SteppedClipAppliesExactFinalEndpoint()
    {
        var clock=new ClipClock(1050,12,false);
        clock.Advance(1049,false);Assert.Equal(1000,clock.VisualTime,8);
        clock.Advance(1,false);Assert.True(clock.Finished);Assert.Equal(1050,clock.VisualTime);
    }
    [Fact] public void ValidZeroDurationClipAppliesZeroKeysOnceAndCompletes()
    {
        var clock=new ClipClock(0,12,true);
        clock.Advance(16,false);Assert.True(clock.Finished);Assert.Equal(0,clock.VisualTime);
        var keys=new[]{new SoundKey{At=0,Cue="zero"}};
        Assert.Single(ClipClock.Crossings(keys,-1,0,0,true));
        Assert.Empty(ClipClock.Crossings(keys,0,16,0,true));
    }
}
