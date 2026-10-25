using SDCharacterStudio.Core.Models;

namespace SDCharacterStudio.Core.Animation;

public static class ExpressionTimeline
{
    /// <summary>
    /// Selects a frame at elapsed expression time. Zero duration holds indefinitely; otherwise
    /// animation loops when requested or holds its final frame. Callers control speed and expiry.
    /// </summary>
    public static int SelectFrame(Expression expression, double elapsedMilliseconds, bool loop = true)
    {
        ArgumentNullException.ThrowIfNull(expression);
        if (!double.IsFinite(elapsedMilliseconds) || elapsedMilliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedMilliseconds));
        if (expression.Frames is not { Count: > 0 })
            throw new ArgumentException("An expression must contain at least one frame.", nameof(expression));

        long totalDuration = 0;
        var holds = false;
        foreach (var frame in expression.Frames)
        {
            if (frame is null || frame.Duration < 0)
                throw new ArgumentException("Expression frame durations must be nonnegative.", nameof(expression));
            totalDuration += frame.Duration;
            holds |= frame.Duration == 0;
        }
        var remaining = loop && !holds ? elapsedMilliseconds % totalDuration : elapsedMilliseconds;
        for (var i = 0; i < expression.Frames.Count; i++)
        {
            var duration = expression.Frames[i].Duration;
            if (duration == 0 || remaining < duration)
                return i;
            remaining -= duration;
        }
        return expression.Frames.Count - 1;
    }
}
