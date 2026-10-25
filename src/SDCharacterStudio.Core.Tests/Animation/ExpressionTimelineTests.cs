using SDCharacterStudio.Core.Animation;
using SDCharacterStudio.Core.Models;
using Xunit;

namespace SDCharacterStudio.Core.Tests.Animation;

public sealed class ExpressionTimelineTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(99.9, 0)]
    [InlineData(100, 1)]
    [InlineData(299.9, 1)]
    [InlineData(300, 0)]
    [InlineData(400, 1)]
    [InlineData(900, 0)]
    public void PositiveFramesAdvanceAtExactBoundariesAndLoop(double elapsed, int expected)
        => Assert.Equal(expected, ExpressionTimeline.SelectFrame(Expression(100, 200), elapsed));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 1)]
    [InlineData(300, 1)]
    [InlineData(100000, 1)]
    public void NonloopingAnimationHoldsFinalFrame(double elapsed, int expected)
        => Assert.Equal(expected, ExpressionTimeline.SelectFrame(Expression(100, 200), elapsed, loop: false));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 1)]
    [InlineData(100000, 1)]
    public void ZeroDurationFrameHoldsAndPreventsLaterFramesOrLoop(double elapsed, int expected)
        => Assert.Equal(expected, ExpressionTimeline.SelectFrame(Expression(100, 0, 200), elapsed));

    [Fact]
    public void SingleStaticFrameHoldsAtAnyTime()
        => Assert.Equal(0, ExpressionTimeline.SelectFrame(Expression(0), double.MaxValue));

    [Fact]
    public void DurationSumDoesNotOverflowAndModelsRemainUnchanged()
    {
        var expression = Expression(int.MaxValue, int.MaxValue);
        Assert.Equal(1, ExpressionTimeline.SelectFrame(expression, (double)int.MaxValue + 1));
        Assert.Equal(0, ExpressionTimeline.SelectFrame(expression, (double)int.MaxValue * 2));
        Assert.Equal(new[] { int.MaxValue, int.MaxValue }, expression.Frames.Select(frame => frame.Duration));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidElapsedTimeIsRejected(double elapsed)
        => Assert.Throws<ArgumentOutOfRangeException>(() => ExpressionTimeline.SelectFrame(Expression(0), elapsed));

    [Fact]
    public void EmptyOrNegativeDurationFramesAreRejectedEvenAfterStaticFrame()
    {
        Assert.Throws<ArgumentException>(() => ExpressionTimeline.SelectFrame(Expression(), 0));
        Assert.Throws<ArgumentException>(() => ExpressionTimeline.SelectFrame(Expression(0, -1), 0));
    }

    private static Expression Expression(params int[] durations)
        => new() { Frames = durations.Select(duration => new ExpressionFrame { Duration = duration }).ToList() };
}
