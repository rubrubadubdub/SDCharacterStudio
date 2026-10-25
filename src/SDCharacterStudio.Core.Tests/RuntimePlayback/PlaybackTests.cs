using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Runtime.Playback;
using Xunit;

namespace SDCharacterStudio.Core.Tests.RuntimePlayback;

public sealed class PlaybackTests
{
    [Fact]
    public void TwoLegMovementWaitsOnFirstLegAndUsesCurrentTile()
    {
        var beat = new MoveBeat { Actor = "Robin", To = new() { Tile = new[] { 17, 6 } }, Speed = "slow", Wait = false };
        Assert.Equal(new[] { "speed Robin 1", "move Robin -4 0 3", "move Robin 0 2 2 true" }, MovePlanner.Plan(beat, 21, 4));
        beat.Path = "y-first";
        Assert.Equal(new[] { "speed Robin 1", "move Robin 0 2 2", "move Robin -4 0 3 true" }, MovePlanner.Plan(beat, 21, 4));
    }

    [Fact]
    public void MovementSkipsZeroLegAndUsesNumericSpeed()
    {
        var beat = new MoveBeat { Actor = "farmer", To = new() { Tile = new[] { 4, 5 } }, Speed = new JValue(3) };
        Assert.Equal(new[] { "speed farmer 3", "move farmer 0 -3 0" }, MovePlanner.Plan(beat, 4, 8));
        Assert.Equal(new[] { "speed farmer 3" }, MovePlanner.Plan(beat, 4, 5));
    }

    [Fact]
    public void CommandStateCannotLeakAcrossEventsIndicesOrSwitchedScripts()
    {
        var slot = new CommandState<object>();
        var owner = new object(); var script = new object(); var value = new object();
        slot.Set(owner, script, 7, value);
        Assert.Same(value, slot.Get(owner, script, 7));
        Assert.Null(slot.Get(owner, script, 8));
        slot.Set(owner, script, 7, value);
        Assert.Null(slot.Get(new object(), script, 7));
        slot.Set(owner, script, 7, value);
        Assert.Null(slot.Get(owner, new object(), 7));
        slot.Set(owner, script, 7, value); slot.Clear();
        Assert.Null(slot.Get(owner, script, 7));
    }

    [Fact]
    public void GiftIsGrantedOnceAndMessageWaitsForInventoryAndHoldUp()
    {
        var state = new GiftProgress(100, "Received hardwood."); var grants = 0;
        state.GrantOnce(() => grants++); state.GrantOnce(() => grants++);
        Assert.Equal(1, grants);
        Assert.Equal("Received hardwood.", state.Message);
        Assert.False(state.TryShowMessage(2599, false, false));
        Assert.False(state.TryShowMessage(3000, true, false));
        Assert.False(state.TryShowMessage(3000, false, true));
        Assert.True(state.TryShowMessage(3000, false, false));
        Assert.True(state.MessageShown);
        Assert.False(state.TryShowMessage(4000, false, false));
    }

    [Fact]
    public void ResetOnlyRemovesFlagsBelongingToThatExactCutscene()
    {
        var flags = new[] { "SDCS.Player.Pack.scene.Paid", "SDCS.Player.Pack.scene2.Paid", "SDCS.Other.Pack.scene.Paid", "OtherMod_SomeFlag" };
        Assert.Equal(new[] { flags[0] }, ResetFlags.ForCutscene("Player.Pack.scene", flags));
    }

    [Fact]
    public void UnexpectedEndClosesOnlyItsOwnedMenuOnce()
    {
        var ownership = new MenuOwnership<object>(); var ours = new object(); var replacement = new object();
        var closed = new List<object>();
        ownership.Capture(ours); ownership.Release(replacement, closed.Add);
        Assert.Empty(closed);
        ownership.Capture(ours); ownership.Release(ours, closed.Add); ownership.Release(ours, closed.Add);
        Assert.Equal(new[] { ours }, closed);
    }

}
