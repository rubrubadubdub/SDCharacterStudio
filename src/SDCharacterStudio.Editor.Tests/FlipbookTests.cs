using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Studio;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>D-48 stage 3: flipbook animations, a row of steps that regenerate the animation's tracks.</summary>
public sealed class FlipbookTests
{
    [Fact]
    public void ANewAnimationInTheSimpleEditorIsAFlipbookOfTheirPose()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        CharacterStudioTests.NewPose(studio, "bow");
        studio.NewAnimation("greet");
        var doc = Assert.IsType<AnimationDocumentViewModel>(studio.ActiveDocument);
        Assert.True(doc.IsFlipbook);
        var step = Assert.Single(doc.Steps);
        Assert.Equal("Pose", step.Do);
        Assert.Equal("robin-bow", step.Pose);
        var track = Assert.Single(doc.LibraryModel!.Tracks);
        Assert.Equal("robin-bow", Assert.Single(track.Pose!).Pose);
        Assert.Equal(1000, doc.Duration);
        Assert.Equal(0, editor.Shell.ValidateNow().Errors);
    }

    [Fact]
    public void AWalkStepWalksWithTheirLiveWalkingFrames()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        CharacterStudioTests.NewPose(studio, "bow");
        studio.NewAnimation("greet");
        var doc = (AnimationDocumentViewModel)studio.ActiveDocument!;

        doc.AddStepCommand.Execute(null);
        doc.Steps[1].Do = "Walk";               // one tile left, half a second
        doc.Steps[1].Distance = 3;
        Assert.Equal("Change step", editor.Shell.Document!.History.UndoLabel);

        var track = Assert.Single(doc.LibraryModel!.Tracks);
        Assert.Equal(new[] { 0, 1000, 1500 }, track.Position!.Select(k => k.At));
        Assert.Equal(new[] { -3, 0 }, track.Position![^1].Tile);
        Assert.Equal("left", Assert.Single(track.Facing!).Facing);
        var walk = track.Pose!.Single(k => k.At == 1000);
        Assert.Equal("robin-walk-left", walk.Pose);
        Assert.Equal("loop", walk.Play);
        var pose = studio.Library!.Pose("robin-walk-left")!;
        Assert.All(pose.Frames, f => Assert.IsType<LiveLayer>(Assert.Single(f.Layers)));   // her real walking frames
        Assert.True(pose.Frames.Count >= 2);
        Assert.Equal(1500, doc.Duration);
        Assert.Equal(0, editor.Shell.ValidateNow().Errors);

        editor.Shell.Document.Undo();           // the distance
        Assert.Equal(new[] { -1, 0 }, Flipbook.Steps(studio.Library!.Clip("greet")!)[1].Tiles);
    }

    [Fact]
    public void ATimelineEditMakesItATimelineAnimationAgain()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        studio.NewAnimation("wave");
        var doc = (AnimationDocumentViewModel)studio.ActiveDocument!;
        Assert.True(doc.IsFlipbook);
        doc.Save("Timeline edit", () => doc.LibraryModel!.Tracks[0].Emote = new() { new EmoteKey { At = 0, Emote = 32 } });
        doc.Reload();
        Assert.False(doc.IsFlipbook);
        Assert.True(doc.IsTimelineInSimple);
        doc.StartFlipbookCommand.Execute(null);   // no keys to lose beyond the emote: asks first (the test UI says yes)
        Assert.True(doc.IsFlipbook);
    }
}
