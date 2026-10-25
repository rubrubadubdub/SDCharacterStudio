using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Runtime.Animation;
using Xunit;
namespace SDCharacterStudio.Core.Tests;
public class ExpressionKeyTimelineTests
{
    [Fact]
    public void TemporaryKeyRevertsToPreviousStillActiveKeyAtExactExpiry()
    {
        var first=new ExpressionKey{At=0,Expression="a"};var temporary=new ExpressionKey{At=100,Expression="b",Duration=100};
        var keys=new[]{first,temporary};
        Assert.Same(temporary,ExpressionKeyTimeline.Select(keys,199));
        Assert.Same(first,ExpressionKeyTimeline.Select(keys,200));
    }
    [Fact]
    public void ExplicitClearPreventsExpiredKeysFromResurrectingEarlierValues()
    {
        var keys=new[]{new ExpressionKey{At=0,Expression="a"},new ExpressionKey{At=50},new ExpressionKey{At=100,Expression="b",Duration=20}};
        Assert.Null(ExpressionKeyTimeline.Select(keys,120));
    }
    [Fact]
    public void ExpiredPreviousKeysDoNotReturnAndDurationAdditionDoesNotOverflow()
    {
        var keys=new[]{new ExpressionKey{At=0,Expression="a",Duration=50},new ExpressionKey{At=100,Expression="b",Duration=20}};
        Assert.Null(ExpressionKeyTimeline.Select(keys,120));
        var late=new ExpressionKey{At=int.MaxValue-1,Expression="late",Duration=10};
        Assert.Same(late,ExpressionKeyTimeline.Select(new[]{late},int.MaxValue));
    }
}
