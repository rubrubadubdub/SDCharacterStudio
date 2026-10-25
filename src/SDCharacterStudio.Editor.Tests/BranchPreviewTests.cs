using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Preview;
using Xunit;

namespace SDCharacterStudio.Editor.Tests;

public sealed class BranchPreviewTests
{
    [Fact]
    public void FromBeatFollowsNestedBranchBackToCaller()
    {
        var scene = new Cutscene { Id = "nested", Beats = { new ChoiceBeat { Options = { new ChoiceOption { Then = "outer" } } }, new SayBeat { Text = "after" } } };
        scene.Branches["outer"] = new() { new ChoiceBeat { Options = { new ChoiceOption { Then = "inner" } } } };
        scene.Branches["inner"] = new() { new SayBeat { Text = "chosen" } };
        var result = PreviewService.FromBeat(scene, "inner", 0);
        Assert.Equal(new[] { "chosen", "after" }, result.Beats.OfType<SayBeat>().Select(b => b.Text!.Literal));
        Assert.IsType<EndBeat>(result.Beats[^1]);
    }

    [Fact]
    public void FromBeatRespectsGotoAndExplicitEnd()
    {
        var scene = new Cutscene { Id = "goto", Beats = { new GotoBeat { Branch = "branch" }, new SayBeat { Text = "unreachable" } } };
        scene.Branches["branch"] = new() { new EndBeat() };
        var result = PreviewService.FromBeat(scene, "branch", 0);
        Assert.Single(result.Beats);
        Assert.IsType<EndBeat>(result.Beats[0]);
    }

    [Fact]
    public void TailGotoRetainsTheChoiceCallerAndFutureChoicesStayInteractive()
    {
        var scene = new Cutscene { Id = "tail", Beats = { new ChoiceBeat { Options = { new ChoiceOption { Then = "outer" } } }, new WaitBeat { Duration = 999 } } };
        scene.Branches["outer"] = new() { new GotoBeat { Branch = "leaf" } };
        scene.Branches["leaf"] = new() { new SayBeat { Text = "start" }, new ChoiceBeat { Options = { new ChoiceOption { Then = "a" }, new ChoiceOption { Then = "b" } } }, new WaitBeat { Duration = 333 } };
        scene.Branches["a"] = new() { new SayBeat { Text = "answer a" } };
        scene.Branches["b"] = new() { new SayBeat { Text = "answer b" } };
        var result = PreviewService.FromBeat(scene, "leaf", 0);
        Assert.IsType<SayBeat>(result.Beats[0]);
        Assert.Equal(2, Assert.IsType<ChoiceBeat>(result.Beats[1]).Options.Count);
        Assert.Equal(new[] { 333, 999 }, result.Beats.OfType<WaitBeat>().Select(b => b.Duration));
        Assert.Single(result.Beats.OfType<SayBeat>());
    }

    [Fact]
    public void EmptyAndUnreachableBranchesHaveDefinedBehaviorAndCyclesAreBounded()
    {
        var scene = new Cutscene { Id = "cycle", Beats = { new GotoBeat { Branch = "loop" } } };
        scene.Branches["loop"] = new() { new GotoBeat { Branch = "loop" } };
        scene.Branches["unused"] = new() { new SayBeat { Text = "standalone" } };
        scene.Branches["empty"] = new();
        Assert.IsType<SayBeat>(PreviewService.FromBeat(scene, "unused", 0).Beats[0]);
        Assert.Throws<ArgumentException>(() => PreviewService.FromBeat(scene, "empty", 0));
        Assert.IsType<GotoBeat>(PreviewService.FromBeat(scene, "loop", 0).Beats[0]);
    }

    [Fact]
    public void FromBeatResolvesMixedCaseGotoAndPreservesCallerContinuation()
    {
        var scene = new Cutscene { Id = "mixed", Beats = { new ChoiceBeat { Options = { new ChoiceOption { Then = "OUTER" } } }, new SayBeat { Text = "after" } } };
        scene.Branches = new Dictionary<string, List<Beat>>(StringComparer.Ordinal)
        {
            ["Outer"] = new() { new GotoBeat { Branch = "LEAF" } },
            ["Leaf"] = new() { new SayBeat { Text = "chosen" } }
        };
        var result = PreviewService.FromBeat(scene, "leaf", 0);
        Assert.Equal(new[] { "chosen", "after" }, result.Beats.OfType<SayBeat>().Select(b => b.Text!.Literal));
    }
}
