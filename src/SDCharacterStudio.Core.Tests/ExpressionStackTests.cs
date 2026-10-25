using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Runtime.Animation;
using Xunit;

namespace SDCharacterStudio.Core.Tests;

public class ExpressionStackTests
{
    private static Expression Blink(string id)=>new(){Id=id,Frames=new(){new(){Image="open.png",Duration=100},new(){Image="closed.png",Duration=100}}};
    [Fact]
    public void LineOverridesClipAndRestoresItsPausedTime()
    {
        var state=new ExpressionStack();var clip=new object();var line=new object();
        state.Set(clip,Blink("clip"),150,null,true);
        state.Set(line,Blink("line"));state.Advance(120);
        Assert.Equal("line",state.Active?.Definition.Id);Assert.Equal(1,state.Active?.Frame);
        state.Remove(line);Assert.Equal("clip",state.Active?.Definition.Id);Assert.Equal(150,state.Active?.Elapsed);
    }
    [Fact]
    public void ExpiryRevealsPreviousOwnerAndSeekingDoesNotResurrectExpiredEntries()
    {
        var state=new ExpressionStack();var old=new object();var current=new object();
        state.Set(old,Blink("old"));state.Set(current,Blink("temporary"),0,120,true);
        state.Seek(current,119);Assert.Equal("temporary",state.Active?.Definition.Id);
        state.Seek(current,120);Assert.Equal("old",state.Active?.Definition.Id);
        state.Seek(current,0);Assert.Equal("old",state.Active?.Definition.Id);
    }
    [Fact]
    public void HeldExpressionContinuesFromItsCurrentFrameAndExpires()
    {
        var state=new ExpressionStack();var owner=new object();state.Set(owner,Blink("held"),50,200,true);
        state.Advance(1000);Assert.Equal(50,state.Active?.Elapsed);
        state.ReleaseClock(owner);state.Advance(100);Assert.Equal(1,state.Active?.Frame);
        state.Advance(50);Assert.Null(state.Active);
    }
    [Fact]
    public void RepeatedCleanupDoesNotRemoveAnotherOwnersExpression()
    {
        var state=new ExpressionStack();var owner=new object();var later=new object();
        state.Set(owner,Blink("old"));state.Clear();state.Set(later,Blink("new"));state.Remove(owner);state.Remove(owner);
        Assert.Equal("new",state.Active?.Definition.Id);
    }
}
