using SDCharacterStudio.Editor.Studio;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>D-49 phase 5: backdrops, folding sections that remember, an empty frame that says what to do, the selection's size.</summary>
public sealed class EditorPolishTests
{
    [Fact]
    public void BackdropsAreRememberedAndTheSceneFloorExplainsItself()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio);
        studio.Settings.Backdrop = "checker";
        pose.Backdrop = "dark";
        Assert.NotNull(pose.BackdropBrush);
        Assert.Null(pose.BackdropImage);
        Assert.Equal("dark", studio.Settings.Backdrop);
        pose.Backdrop = "scene";   // the test export has no map pictures: it says why there's no floor
        Assert.Null(pose.BackdropImage);
        Assert.NotNull(pose.BackdropNote);
        pose.Backdrop = "checker";
        Assert.Null(pose.BackdropBrush);
        Assert.Contains(PoseDocumentViewModel.Backdrops, b => b.Value == "scene");
    }

    [Fact]
    public void SectionsFoldAndStayFolded()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        studio.Settings.OpenSections.Clear();
        var pose = CharacterStudioTests.NewPose(studio);
        Assert.True(pose.PresetsOpen);
        pose.BodyOpen = false;
        Assert.False(studio.Settings.OpenSections["body"]);
        var again = CharacterStudioTests.NewPose(studio, "wave");
        Assert.False(again.BodyOpen);   // remembered
        Assert.True(again.HasPresetPictures);
    }

    [Fact]
    public void AnEmptyFrameSaysSoAndTheSelectionShowsItsSize()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio);
        Assert.False(pose.IsFrameEmpty);
        Assert.Null(pose.SelectionText);
        pose.Selection = new PixelRect(0, 0, 16, 12);
        Assert.Equal("16×12 selected", pose.SelectionText);
        pose.HandleKey("A", true, false);
        pose.DeleteSelectionCommand.Execute(null);
        Assert.True(pose.IsFrameEmpty);
        pose.Tool = StudioTool.Wand;
        pose.HandleKey("D", true, false);
        Assert.Null(pose.SelectionText);
    }
}
