using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Studio;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>The Character Studio for a first-time modder: friendly names, the way back to the scene, only what applies.</summary>
public sealed class StudioUxTests
{
    [Fact]
    public void ItemsAreCalledByTheirNamesNotTheirIds()
    {
        Assert.Equal("Wave", LibraryNames.Friendly("abigail-wave", "abigail", "Abigail"));
        Assert.Equal("Blush left", LibraryNames.Friendly("abigail-blush-left", "abigail"));
        Assert.Equal("Greet", LibraryNames.Friendly("greet", "abigail"));

        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio, "wave");
        Assert.Equal("Wave", pose.Title);
        var item = studio.Library!.Items(LibraryKind.Pose).Single(i => i.Id == "robin-wave");
        Assert.Equal("Wave", item.Title);
        Assert.Equal("1 frame · Robin", item.Detail);
    }

    [Fact]
    public void UseInTheScenePlaysAPoseOnItsCharacterAndAddsThemWhenMissing()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio("Sophia");
        using var _ = editor;
        var scene = editor.Shell.Scene!;
        Assert.DoesNotContain(scene.Actors, a => a.Npc == "Sophia");
        var pose = CharacterStudioTests.NewPose(studio, "wave");
        Assert.True(studio.CanUseActiveInScene);

        studio.UseActiveInSceneCommand.Execute(null);

        Assert.False(editor.Shell.IsCharacterStudio);
        var actor = Assert.Single(scene.Actors, a => a.Npc == "Sophia");
        var beat = Assert.IsType<PoseBeat>(scene.Beats.Last());
        Assert.Equal(actor.Id, beat.Actor);
        Assert.Equal(pose.ItemId, beat.Pose);
        Assert.Equal(0, editor.Shell.ValidateNow().Errors);
    }

    [Fact]
    public void UseInTheSceneDressesThemInAnOutfitAndShowsAPortraitInALine()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        string outfit = studio.NewOutfit("raincoat")!;
        Assert.True(studio.UseItemInScene(LibraryKind.Pose, outfit));
        var costume = Assert.IsType<CostumeBeat>(editor.Shell.Scene!.Beats.Last());
        Assert.Equal(outfit, costume.Sprite!.Pose);
        Assert.Null(costume.Set);

        editor.Shell.IsCharacterStudio = true;
        string portrait = studio.NewPortrait("raincoat")!;
        Assert.True(studio.UseItemInScene(LibraryKind.Portrait, portrait));
        var say = Assert.IsType<SayBeat>(editor.Shell.Scene!.Beats.Last());
        Assert.Equal(portrait, say.Portrait);
        Assert.Equal(costume.Actor, say.Actor);
    }

    [Fact]
    public void APoseOfOneFrameHasNoPlayOrderToPick()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio);
        Assert.False(pose.ShowsPlayOrderRow);
        pose.AddFrameCommand.Execute(null);
        Assert.True(pose.ShowsPlayOrderRow);
        pose.AddFrameCommand.Execute(null);   // back and forth needs a middle frame: 1 2 3 2 1...
        Assert.Equal("loop", pose.PlayOrderChoice);
        pose.PlayOrderChoice = "backforth";
        Assert.True(pose.PlaysBackAndForth);
        pose.PlayOrderChoice = "hold";
        Assert.True(pose.HoldsLast);
    }

    [Fact]
    public void APortraitShowsNoSpriteTools()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        studio.NewPortrait("winter");
        var portrait = Assert.IsType<PortraitDocumentViewModel>(studio.ActiveDocument);
        Assert.False(portrait.ShowsReadyMade);
        Assert.False(portrait.ShowsSpriteSections);
        Assert.False(portrait.ShowsBodyParts);
        Assert.False(portrait.ShowsPlayOrderRow);
        Assert.StartsWith("Neutral · 1/", portrait.FrameCaption);
    }

    [Fact]
    public void ASimpleExpressionAsksOnlyWhatItNeeds()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        studio.NewExpression("blush");
        var expression = Assert.IsType<ExpressionDocumentViewModel>(studio.ActiveDocument);
        var labels = expression.Fields.Select(f => f.Label).ToList();
        Assert.Contains("Name", labels);
        Assert.DoesNotContain("For (rig)", labels);
        Assert.DoesNotContain("Nudge x", labels);
        Assert.DoesNotContain(expression.Fields, f => f is ListField);
    }

    [Fact]
    public void ClickingTheColourOpensThePickerAndYouPaintWithWhatYouMix()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        CharacterStudioTests.NewPose(studio);
        var palette = studio.Palette;
        uint skin = palette.Primary;   // her live skin colour

        palette.EditPrimaryCommand.Execute(null);
        Assert.True(palette.IsPickerOpen);
        Assert.Equal(skin, palette.Primary);   // just opening it keeps her live skin
        palette.PickerColor = 0xFF2E8B57;
        Assert.Equal(0xFF2E8B57u, palette.Primary);   // mixing paints with it right away
        palette.IsPickerOpen = false;
        Assert.Equal(0xFF2E8B57u, palette.Recent.First().Color);

        palette.EditSecondaryCommand.Execute(null);
        palette.PickerColor = 0xFF102030;
        Assert.Equal(0xFF102030u, palette.Secondary);
        Assert.Equal(0xFF2E8B57u, palette.Primary);
        palette.IsPickerOpen = false;

        var swatch = palette.InPicture.First(s => s.Color >> 24 != 0);
        palette.EditSwatchCommand.Execute(swatch);
        Assert.Equal(swatch.Color, palette.Primary);
        Assert.True(palette.IsPickerOpen);
        Assert.False(palette.PickingSecondary);
    }

    [Fact]
    public void ClickingARecoloursNewColourMixesItRightThere()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio);
        pose.AddRecolor();
        var row = pose.Recolors.Single();
        row.PickCommand.Execute(null);
        Assert.True(row.IsPicking);
        Assert.Equal(row.To | 0xFF000000, row.PickColor);
        row.PickColor = 0xFF3366CC;
        row.UsePickCommand.Execute(null);
        Assert.Equal(0xFF3366CCu, pose.Recolors.Single().To | 0xFF000000);
        Assert.Equal("Change recolour", editor.Shell.Document!.History.UndoLabel);
    }
}
