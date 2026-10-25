using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Project;
using SDCharacterStudio.Editor.Schema;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Tests;

public sealed class HardeningStateTests
{
    [Fact]
    public void SessionViewStateDoesNotCreateHistoryAndSurvivesContentUndo()
    {
        using var h = new EditorHarness(); var doc = h.NewProject();
        doc.Edit("second scene", () => doc.Cutscenes.Add(new("cutscenes/second.json", PackDocument.NewCutscene("second", "Town"))));
        doc.Save(); doc = PackDocument.Open(doc.ProjectPath); h.Shell.Attach(doc);
        h.Shell.CurrentCutsceneItem = h.Shell.Cutscenes.Single(c => c.Id == "second");
        doc.Project.StageZoom = 2; doc.Project.ShowGrid = false; doc.Project.StoryboardGraph = true;
        doc.Project.DockLayoutXml = "layout one";
        doc.Commit("view state");
        Assert.False(doc.History.CanUndo); Assert.False(doc.IsDirty);
        doc.Edit("title", () => doc.Cutscenes[0].Scene.Title = "edited");
        doc.Project.StageZoom = 4; doc.Project.DockLayoutXml = "layout two";
        Assert.True(doc.Undo());
        Assert.Equal(4, doc.Project.StageZoom); Assert.Equal("layout two", doc.Project.DockLayoutXml);
        Assert.Equal("second", doc.Project.OpenCutscene); Assert.True(doc.Project.StoryboardGraph);
        Assert.False(doc.Project.ShowGrid);
        Assert.NotEqual("edited", doc.Cutscenes[0].Scene.Title);
    }

    [Fact]
    public void ClosingACleanDocumentCommitsTheFocusedFieldBeforeDecidingToDiscard()
    {
        using var h = new EditorHarness(); var doc = h.NewProject();
        Assert.False(doc.IsDirty);
        h.Ui.AskAnswer = null;
        h.Ui.PendingFlush = () => { h.Ui.PendingFlush = null; doc.Edit("pending field", () => doc.Cutscenes[0].Scene.Title = "typed"); };
        Assert.False(h.Shell.ConfirmDiscard());
        Assert.True(doc.IsDirty); Assert.Equal("typed", doc.Cutscenes[0].Scene.Title);
    }

    [Fact]
    public void EveryDependencyUseNavigatesToItsActorOrBeatAndPreservesDialogEdits()
    {
        using var h = new EditorHarness(); var doc = h.NewProject(location: "ScienceHouse");
        h.Shell.Stage.DropCharacter("Sophia", 12, 10);
        h.Shell.Storyboard.Insert(LaneNames.Main, 0, BeatCatalog.Kind("Costume"), beat =>
        { var costume = (CostumeBeat)beat; costume.Actor = "Sophia"; costume.Set = null; costume.Sprite = new() { Asset = "Characters/Robin_Summer" }; });
        var dialog = new DependenciesViewModel(h.Shell);
        bool? accepted = null; dialog.CloseRequested += result => accepted = result;
        var sve = dialog.Providers.Single(p => p.ModId == "FlashShifter.StardewValleyExpandedCP");
        sve.OnlySomeScenes = true;
        sve.UseLinks.First(l => l.Description.StartsWith("NPC Sophia")).Go.Execute(null);
        Assert.True(accepted); Assert.Equal(new ActorSelection(h.Shell.Scene!.Id!, 1), h.Shell.Selection);
        Assert.Equal(DependencyMode.Optional, doc.Project.ModeFor(sve.ModId));
        dialog = new DependenciesViewModel(h.Shell);
        var scc = dialog.Providers.Single(p => p.ModId == "Poltergeister.SeasonalCuteCharacters");
        scc.UseLinks.Single().Go.Execute(null);
        Assert.Equal(new BeatSelection(h.Shell.Scene!.Id!, LaneNames.Main, 0), h.Shell.Selection);
    }
}
