using SDCharacterStudio.Core.Loading;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Export;
using SDCharacterStudio.Editor.Preview;
using SDCharacterStudio.Editor.Storyboard;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Tests;

public sealed class LaneIdentityTests
{
    [Fact]
    public void LegalMainAndSkipBranchNamesStaySeparateFromSpecialLanes()
    {
        var scene = new Cutscene { Id = "named", Skippable = true,
            Beats = { new ChoiceBeat { Options = { new ChoiceOption { Then = "main" } } }, new SayBeat { Text = "after main" } },
            OnSkip = new() { new MailBeat { Add = "Skipped" } } };
        scene.Branches["main"] = new() { new SayBeat { Text = "branch main" }, new ChoiceBeat { Options = { new ChoiceOption { Then = "skip" } } } };
        scene.Branches["skip"] = new() { new SayBeat { Text = "branch skip" } };
        var graph = BranchGraphBuilder.Build(scene);
        Assert.Equal(graph.Nodes.Count, graph.Nodes.Select(n => n.Id).Distinct().Count());
        Assert.IsType<ChoiceBeat>(graph.Find(LaneNames.Main, 0)!.Beat);
        Assert.IsType<SayBeat>(graph.Find("main", 0)!.Beat);
        Assert.IsType<MailBeat>(graph.Find(LaneNames.Skip, 0)!.Beat);
        Assert.IsType<SayBeat>(graph.Find("skip", 0)!.Beat);
        Assert.Equal(new[] { "branch skip", "after main" }, PreviewService.FromBeat(scene, "skip", 0).Beats.OfType<SayBeat>().Select(b => b.Text!.Literal));
        var source = new[] { new SourceEntry<Cutscene>("cutscenes/named.json", scene) };
        Assert.Equal("main", ValidationService.Locate(source[0].File, "Branches.main[0].Text", source).Lane);
        Assert.Equal(LaneNames.Main, ValidationService.Locate(source[0].File, "Beats[0]", source).Lane);
        Assert.Equal(LaneNames.Skip, ValidationService.Locate(source[0].File, "OnSkip[0]", source).Lane);
    }

    [Fact]
    public void EditingMainNamedBranchDoesNotEditTheMainBeatList()
    {
        using var h = new EditorHarness(); var document = h.NewProject();
        h.Shell.Edit("branch", () => h.Shell.Scene!.Branches["main"] = new() { new WaitBeat { Duration = 100 } });
        h.Shell.Storyboard.Delete("main", 0);
        Assert.Empty(h.Shell.Scene!.Beats);
        Assert.Empty(h.Shell.Scene.Branches["main"]);
        Assert.True(document.Undo());
        Assert.Single(h.Shell.Scene!.Branches["main"]);
        Assert.Empty(h.Shell.Scene.Beats);
    }
}
