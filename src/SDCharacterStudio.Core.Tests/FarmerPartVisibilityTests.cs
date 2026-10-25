using SDCharacterStudio.Runtime.Animation;
using Xunit;

namespace SDCharacterStudio.Core.Tests;

public class FarmerPartVisibilityTests
{
    [Fact]
    public void ClipRestoresTemporaryKeysButRetainsEachPersistedValue()
    {
        var state=new FarmerPartVisibility();
        using(var scope=state.BeginScope())
        {
            scope.Apply(new(){{"Hat",false}},true);
            scope.Apply(new(){{"Hat",true},{"Arms",false}},false);
            scope.Apply(new(){{"Shirt",false}},true);
        }
        Assert.False(state.Visible("Hat"));
        Assert.False(state.Visible("Shirt"));
        Assert.True(state.Visible("Arms"));
    }

    [Fact]
    public void ClearInvalidatesOutstandingScopesAndIsIdempotent()
    {
        var state=new FarmerPartVisibility();
        var scope=state.BeginScope();
        scope.Apply(new(){{"Hair",false}},true);
        state.Clear();scope.Dispose();scope.Dispose();
        Assert.True(state.Visible("Hair"));
    }

    [Fact]
    public void InvalidPartDoesNotApplyEarlierValidValues()
    {
        var state=new FarmerPartVisibility();
        using var scope=state.BeginScope();
        Assert.Throws<ArgumentException>(()=>scope.Apply(new(){{"Hat",false},{"Unknown",false}},false));
        Assert.True(state.Visible("Hat"));
    }

    [Fact]
    public void NoOpChangesKeepTheSameVersion()
    {
        var state=new FarmerPartVisibility();
        long version=state.Version;
        using(var scope=state.BeginScope())scope.Apply(new(){{"Hat",true}},false);
        Assert.Equal(version,state.Version);
    }
    [Fact]
    public void HoldCommitsCurrentValuesOnlyWhenTheClipFinishes()
    {
        var state=new FarmerPartVisibility();
        using(var firstCycle=state.BeginScope())firstCycle.Apply(new(){{"Hair",false}},false);
        Assert.True(state.Visible("Hair"));
        using(var finalCycle=state.BeginScope())
        {
            finalCycle.Apply(new(){{"Hair",false}},false);
            finalCycle.KeepCurrent();
        }
        Assert.False(state.Visible("Hair"));
    }
}
