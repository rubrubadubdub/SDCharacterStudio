using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Storyboard;

namespace SDCharacterStudio.Editor.Tests;

public sealed class StoryboardGraphTests
{
    [Fact]
    public void BuildsChoiceGotoAndSequentialEdgesWithLabels()
    {
        var scene = new Cutscene { Id = "s" };
        scene.Beats.Add(new ChoiceBeat { Options = { new ChoiceOption { Text = "Yes", Then = "yes" }, new ChoiceOption { Text = "No", Then = "missing" } } });
        scene.Beats.Add(new SayBeat()); scene.Branches["yes"] = new() { new GotoBeat { Branch = "main" } };
        var graph = BranchGraphBuilder.Build(scene);
        Assert.Contains(graph.Edges, e => e.Kind == GraphEdgeKind.Choice && e.Label == "Yes");
        Assert.Contains(graph.Edges, e => e.Kind == GraphEdgeKind.Choice && e.Missing && e.Label == "No");
        Assert.DoesNotContain(graph.Edges, e => e.From == "@main:0" && e.To == "@main:1");
        Assert.Contains(graph.Nodes, n => n.Missing && n.Label.Contains("missing"));
    }

    [Fact]
    public void ReportsEmptyAndMissingBranchesWithoutCreatingInvalidNodes()
    {
        var scene = new Cutscene { Id = "s", Beats = { new GotoBeat { Branch = "absent" } } };
        scene.Branches["empty"] = new();
        var graph = BranchGraphBuilder.Build(scene);
        Assert.Contains(graph.Diagnostics, d => d.Contains("absent"));
        Assert.Contains(graph.Diagnostics, d => d.Contains("empty"));
        Assert.Null(graph.Find("absent", 0));
    }

    [Fact]
    public void AddsBranchReturnToContinuationAfterChoice()
    {
        var scene = new Cutscene { Id = "s" };
        scene.Beats.Add(new ChoiceBeat { Options = { new ChoiceOption { Then = "branch" } } });
        scene.Beats.Add(new EndBeat()); scene.Branches["branch"] = new() { new SayBeat() };
        var graph = BranchGraphBuilder.Build(scene);
        Assert.Contains(graph.Edges, e => e.Kind == GraphEdgeKind.BranchReturn && e.To == "@main:1");
    }

    [Fact]
    public void PreservesBoundedCyclesAsFiniteGraph()
    {
        var scene = new Cutscene { Id = "s", Beats = { new GotoBeat { Branch = "a" } } };
        scene.Branches["a"] = new() { new GotoBeat { Branch = "a" } };
        var graph = BranchGraphBuilder.Build(scene);
        Assert.Equal(2, graph.Nodes.Count);
        Assert.Contains(graph.Edges, e => e.Kind == GraphEdgeKind.Goto && !e.Missing);
    }

    [Fact]
    public void NestedChoiceAndTailGotoReturnToTheirCallersAndEmptyBranchesAreRealTargets()
    {
        var scene = new Cutscene { Beats = { new ChoiceBeat { Options = { new ChoiceOption { Then = "outer" }, new ChoiceOption { Then = "empty" } } }, new EndBeat() } };
        scene.Branches["outer"] = new() { new ChoiceBeat { Options = { new ChoiceOption { Then = "inner" } } }, new WaitBeat() };
        scene.Branches["inner"] = new() { new GotoBeat { Branch = "leaf" } };
        scene.Branches["leaf"] = new() { new WaitBeat() };
        scene.Branches["empty"] = new();
        var graph = BranchGraphBuilder.Build(scene);
        Assert.Contains(graph.Edges, e => e.From == "leaf:0" && e.To == "outer:1" && e.Kind == GraphEdgeKind.BranchReturn);
        Assert.Contains(graph.Edges, e => e.From == "outer:1" && e.To == "@main:1" && e.Kind == GraphEdgeKind.BranchReturn);
        Assert.Contains(graph.Edges, e => e.From == "empty:-1" && e.To == "@main:1" && e.Kind == GraphEdgeKind.BranchReturn);
        Assert.Contains(graph.Edges, e => e.From == "@main:0" && e.To == "empty:-1" && !e.Missing);
    }

    [Fact]
    public void EndAndNonreturningCyclesNeverAcquireReturnEdges()
    {
        var scene = new Cutscene { Beats = { new ChoiceBeat { Options = { new ChoiceOption { Then = "loop" }, new ChoiceOption { Then = "end" } } }, new WaitBeat() } };
        scene.Branches["loop"] = new() { new GotoBeat { Branch = "loop" } };
        scene.Branches["end"] = new() { new EndBeat(), new WaitBeat() };
        Assert.DoesNotContain(BranchGraphBuilder.Build(scene).Edges, e => e.Kind == GraphEdgeKind.BranchReturn);
    }

    [Fact]
    public void ResolvesMixedCaseBranchReferenceWithCaseSensitiveBranchMap()
    {
        var scene = new Cutscene { Beats = { new ChoiceBeat { Options = { new ChoiceOption { Then = "YES" } } }, new EndBeat() } };
        scene.Branches = new Dictionary<string, List<Beat>>(StringComparer.Ordinal) { ["Yes"] = new() { new SayBeat() } };
        var graph = BranchGraphBuilder.Build(scene);
        Assert.Contains(graph.Edges, e => e.Kind == GraphEdgeKind.Choice && !e.Missing && e.To == "Yes:0");
    }
}
