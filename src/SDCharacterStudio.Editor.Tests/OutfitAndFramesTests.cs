using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Catalog;
using SDCharacterStudio.Editor.Studio;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>D-49 phase 4: her outfits (seasons, festivals, mods) and her real frames to start from.</summary>
public sealed class OutfitAndFramesTests
{
    private static RgbaImage Composite(CharacterStudioViewModel studio, PoseDocumentViewModel pose, int frame) => studio.Context.RenderFrame(pose.Model, frame).Image;

    [Fact]
    public void OutfitsAreNamedBySeasonFestivalAndMod()
    {
        Assert.Equal("Flower Dance", CharacterStudioViewModel.OutfitLabel(new AppearanceEntry { Id = "X.AbigailFlowerDance", Condition = "IS_EVENT festival_spring24, true", Sprite = "Characters/Abigail_FlowerDance" }, "Abigail"));
        Assert.Equal("Spirit's Eve", CharacterStudioViewModel.OutfitLabel(new AppearanceEntry { Condition = "IS_EVENT festival_fall27, true" }, "Abigail"));
        Assert.Equal("Winter (indoors)", CharacterStudioViewModel.OutfitLabel(new AppearanceEntry { Season = "winter", Indoors = true, Outdoors = false }, "Abigail"));
        Assert.Equal("Ginger Island", CharacterStudioViewModel.OutfitLabel(new AppearanceEntry { IsIslandAttire = true }, "Abigail"));
        Assert.Equal("Jellies", CharacterStudioViewModel.OutfitLabel(new AppearanceEntry { Id = "Poltergeister.SeasonalCuteCharacters.AbigailJellies" }, "Abigail"));

        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        Assert.Equal(new[] { "As in the game now", "Everyday", "Summer" }, studio.Outfits.Select(o => o.Label));
        Assert.NotNull(studio.Outfits.Single(o => o.Label == "Summer").Detail);   // who adds it
    }

    [Fact]
    public void PickingAnOutfitPreviewsItEverywhereAndAPoseCanAlwaysWearIt()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio);
        var everyday = Composite(studio, pose, 0);
        var card = studio.CharacterFrames.ToList();

        studio.Outfit = studio.Outfits.Single(o => o.Label == "Summer");
        var summer = Composite(studio, pose, 0);
        Assert.NotEqual(everyday.Pixels, summer.Pixels);
        Assert.NotEqual(card, studio.CharacterFrames.ToList());
        Assert.Null(pose.PinnedOutfit);   // only a preview: the pose still wears what the game gives
        Assert.True(pose.CanPinOutfit);

        pose.PinShownOutfitCommand.Execute(null);
        Assert.Equal("Characters/Robin_Summer", pose.PinnedOutfit);
        Assert.All(pose.Model.Frames.SelectMany(f => f.Layers).OfType<LiveLayer>(), l => Assert.Equal("NpcSheet", l.Source!.Kind));
        studio.Outfit = studio.Outfits[0];
        Assert.Equal(summer.Pixels, Composite(studio, pose, 0).Pixels);   // pinned: summer whatever is shown
        Assert.Contains("Summer", pose.OutfitText);
        var validation = editor.Shell.ValidateNow();
        Assert.True(validation.Errors == 0, string.Join(" | ", validation.Problems.Select(p => p.Message)));

        pose.UnpinOutfitCommand.Execute(null);
        Assert.Null(pose.PinnedOutfit);
        Assert.Equal(everyday.Pixels, Composite(studio, pose, 0).Pixels);
    }

    [Fact]
    public void HerFramesStartAFrameOrAddOne()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio);
        pose.ShowSheetFrames = true;
        Assert.True(pose.SheetFrames.Count >= 16);
        Assert.Contains("walking", pose.SheetFrames[0].Label);
        var sheet = studio.Context.NpcTexture("Robin", false)!;

        pose.StartFromSheetFrameCommand.Execute(4);
        Assert.Equal(4, ((LiveLayer)pose.Frame!.Layers[0]).SourceFrame!.Index);
        Assert.Equal(StudioImages.Frame(sheet, 16, 32, 4)!.Pixels, Composite(studio, pose, 0).Pixels);
        Assert.True(pose.IsFlat);
        Assert.Equal("Start from game frame 4", editor.Shell.Document!.History.UndoLabel);

        pose.AddSheetFrameCommand.Execute(8);
        Assert.Equal(2, pose.Model.Frames.Count);
        Assert.Equal(1, pose.FrameIndex);
        Assert.Equal(StudioImages.Frame(sheet, 16, 32, 8)!.Pixels, Composite(studio, pose, 1).Pixels);
        Assert.Equal(StudioImages.Frame(sheet, 16, 32, 4)!.Pixels, Composite(studio, pose, 0).Pixels);   // the first is untouched
        Assert.True(pose.LoopsAll);
        Assert.Equal(0, editor.Shell.ValidateNow().Errors);

        // Still live: a sprite mod shows through the new frames.
        using (studio.Context.SheetOverride("Robin", "Characters/Robin_Summer"))
            Assert.Equal(StudioImages.Frame(studio.Context.AssetTexture("Characters/Robin_Summer")!, 16, 32, 8)!.Pixels, Composite(studio, pose, 1).Pixels);
    }
}

/// <summary>D-49: pixels from her other frames, copied in live, and pastes that keep naming the frame they came from.</summary>
public sealed class BorrowedPixelTests
{
    private static RgbaImage Composite(CharacterStudioViewModel studio, PoseDocumentViewModel pose, int frame) => studio.Context.RenderFrame(pose.Model, frame).Image;

    [Fact]
    public void CopyingPartOfHerOtherFrameStaysLive()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio);   // Robin standing, facing left
        var sheet = studio.Context.NpcTexture("Robin", false)!;
        var eight = StudioImages.Frame(sheet, 16, 32, 8)!;

        pose.PeekSheetFrameCommand.Execute(8);
        Assert.True(pose.IsPeeking);
        Assert.Contains("frame 8", pose.PeekText);
        pose.Tool = StudioTool.Select;
        pose.PointerDown(0, 20, PointerButton.Primary); pose.PointerMove(15, 31); pose.PointerUp(15, 31);   // her legs in frame 8
        Assert.True(pose.HandleKey("Return", false, false));   // copy into this frame: it floats
        Assert.False(pose.IsPeeking);
        Assert.True(pose.IsTransforming);
        Assert.True(pose.HandleKey("Return", false, false));   // apply
        Assert.Equal("Copy from game frame 8", editor.Shell.Document!.History.UndoLabel);

        var after = Composite(studio, pose, 0);
        for (int y = 20; y < 32; y++)
            for (int x = 0; x < 16; x++)
                if (PixelTools.Get(eight, x, y) >> 24 != 0) Assert.Equal(PixelTools.Get(eight, x, y), PixelTools.Get(after, x, y));
        Assert.True(StudioImages.IsEmpty(studio.Context.PackImage(pose.Flat!.Paint.Image)!));   // live, not painted

        // A sprite mod changes her frame 8: the borrowed legs follow.
        using (studio.Context.SheetOverride("Robin", "Characters/Robin_Summer"))
        {
            var summerEight = StudioImages.Frame(studio.Context.AssetTexture("Characters/Robin_Summer")!, 16, 32, 8)!;
            var summer = Composite(studio, pose, 0);
            for (int y = 20; y < 32; y++)
                for (int x = 0; x < 16; x++)
                    if (PixelTools.Get(eight, x, y) >> 24 != 0 && PixelTools.Get(summerEight, x, y) >> 24 != 0) Assert.Equal(PixelTools.Get(summerEight, x, y), PixelTools.Get(summer, x, y));
        }
        Assert.Equal(0, editor.Shell.ValidateNow().Errors);
    }

    [Fact]
    public void APasteIntoAFrameMadeFromAnotherSpriteFrameShowsTheSamePixels()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio);
        var standing = Composite(studio, pose, 0);
        pose.AddSheetFrameCommand.Execute(4);   // frame 2 is her sprite frame 4
        pose.FrameIndex = 0;
        pose.Selection = new PixelRect(0, 0, 16, 12);
        pose.CopySelectionCommand.Execute(null);   // her standing head
        pose.FrameIndex = 1;
        pose.Selection = new PixelRect(0, 0, 16, 12);
        pose.PasteSelectionCommand.Execute(null);
        pose.ApplyTransformCommand.Execute(null);
        var pasted = Composite(studio, pose, 1);
        for (int y = 0; y < 12; y++)
            for (int x = 0; x < 16; x++)
                if (PixelTools.Get(standing, x, y) >> 24 != 0) Assert.Equal(PixelTools.Get(standing, x, y), PixelTools.Get(pasted, x, y));   // her standing head, not frame 4's pixels
        Assert.Equal("Paste", editor.Shell.Document!.History.UndoLabel);
    }
}
