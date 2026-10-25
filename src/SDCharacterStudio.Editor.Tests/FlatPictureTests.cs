using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Studio;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>
/// D-48, the owner's choice: a pose frame is one flat picture that stays live. Erasing, moving, filling, mirroring and
/// pasting change that picture (a pixel map of her real sprite, plus your own pixels); no layers ever appear, nothing
/// hidden is left behind, and each gesture is one undo step.
/// </summary>
public sealed class FlatPictureTests
{
    private static RgbaImage Composite(CharacterStudioViewModel studio, PoseDocumentViewModel pose) => studio.Context.RenderFrame(pose.Model, pose.FrameIndex).Image;
    private static bool Opaque(RgbaImage image, int x, int y) => PixelTools.Get(image, x, y) >> 24 != 0;
    private static bool PaintIsEmpty(CharacterStudioViewModel studio, PoseDocumentViewModel pose) => StudioImages.IsEmpty(studio.Context.PackImage(pose.Flat!.Paint.Image)!);

    [Fact]
    public void ErasingMakesAHoleAndNothingElseChanges()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio);
        Assert.True(pose.IsFlat);
        var before = Composite(studio, pose);
        var (x, y) = Enumerable.Range(0, pose.Height).SelectMany(row => Enumerable.Range(0, pose.Width).Select(col => (col, row))).First(p => Opaque(before, p.col, p.row) && Opaque(before, p.col + 1, p.row));

        pose.Tool = StudioTool.Eraser;
        pose.PointerDown(x, y, PointerButton.Primary); pose.PointerMove(x + 1, y); pose.PointerUp(x + 1, y);

        var after = Composite(studio, pose);
        for (int row = 0; row < pose.Height; row++)
            for (int col = 0; col < pose.Width; col++)
                if (row == y && (col == x || col == x + 1)) Assert.False(Opaque(after, col, row));
                else Assert.Equal(PixelTools.Get(before, col, row), PixelTools.Get(after, col, row));
        Assert.Equal(2, pose.Frame!.Layers.Count);   // still one picture: no layer appeared
        Assert.True(pose.IsFlat);
        Assert.Equal("Erase", editor.Shell.Document!.History.UndoLabel);
        Assert.Equal(0, editor.Shell.ValidateNow().Errors);

        editor.Shell.Document.Undo();
        Assert.Equal(before.Pixels, Composite(studio, pose).Pixels);
    }

    [Fact]
    public void MovingASelectionMovesWhatYouSeeAndItFollowsSpriteMods()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio);
        var before = Composite(studio, pose);
        studio.Context.SheetOverrides["Robin"] = "Characters/Robin_Summer";
        var summerBefore = Composite(studio, pose);
        studio.Context.SheetOverrides.Remove("Robin");
        Assert.NotEqual(before.Pixels, summerBefore.Pixels);

        pose.Tool = StudioTool.Select;
        pose.PointerDown(0, 0, PointerButton.Primary); pose.PointerMove(15, 11); pose.PointerUp(15, 11);   // her head
        pose.PointerDown(5, 5, PointerButton.Primary); pose.PointerMove(5, 8); pose.PointerUp(5, 8);       // three pixels down

        var after = Composite(studio, pose);
        int moved = 0;
        for (int row = 0; row < 12; row++)
            for (int col = 0; col < 16; col++)
                if (Opaque(before, col, row)) { Assert.Equal(PixelTools.Get(before, col, row), PixelTools.Get(after, col, row + 3)); moved++; }
        Assert.True(moved > 20, "The head should have pixels to move.");
        Assert.Equal(2, pose.Frame!.Layers.Count);
        Assert.True(PaintIsEmpty(studio, pose));   // every pixel still comes from her sprite
        Assert.Equal("Move pixels", editor.Shell.Document!.History.UndoLabel);
        Assert.Equal(new PixelRect(0, 3, 16, 12), pose.Selection);

        // A sprite mod (her summer outfit) shows through the moved head.
        studio.Context.SheetOverrides["Robin"] = "Characters/Robin_Summer";
        var summerAfter = Composite(studio, pose);
        studio.Context.SheetOverrides.Remove("Robin");
        for (int row = 0; row < 12; row++)
            for (int col = 0; col < 16; col++)
                if (Opaque(before, col, row)) Assert.Equal(PixelTools.Get(summerBefore, col, row), PixelTools.Get(summerAfter, col, row + 3));

        // Dragging again moves the same head; still no layers.
        pose.PointerDown(5, 6, PointerButton.Primary); pose.PointerMove(5, 7); pose.PointerUp(5, 7);
        Assert.Equal(2, pose.Frame.Layers.Count);
        Assert.Equal(PixelTools.Get(before, 5, 5), PixelTools.Get(Composite(studio, pose), 5, 9));
        Assert.Equal(0, editor.Shell.ValidateNow().Errors);

        editor.Shell.Document.Undo();
        editor.Shell.Document.Undo();
        Assert.Equal(before.Pixels, Composite(studio, pose).Pixels);
    }

    [Fact]
    public void CtrlDragCopiesAndTransparentCornersNeverWipeAnything()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio);
        var before = Composite(studio, pose);
        pose.Tool = StudioTool.Select;
        pose.PointerDown(0, 20, PointerButton.Primary); pose.PointerMove(15, 31); pose.PointerUp(15, 31);
        pose.PointerDown(5, 25, PointerButton.Primary, ctrl: true); pose.PointerMove(5, 22); pose.PointerUp(5, 22);
        var after = Composite(studio, pose);
        for (int row = 0; row < 17; row++)
            for (int col = 0; col < 16; col++)
                Assert.Equal(PixelTools.Get(before, col, row), PixelTools.Get(after, col, row));   // above the copy: untouched
        for (int row = 17; row < 32; row++)
            for (int col = 0; col < 16; col++)
            {
                // Where the copy has nothing to show, the original still shows; where it does, the copy wins.
                var expected = row + 3 < 32 && Opaque(before, col, row + 3) ? PixelTools.Get(before, col, row + 3) : PixelTools.Get(before, col, row);
                Assert.Equal(expected, PixelTools.Get(after, col, row));
            }
        Assert.Equal(2, pose.Frame!.Layers.Count);
    }

    [Fact]
    public void FillRecoloursWhatYouSee()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio);
        var before = Composite(studio, pose);
        var (x, y) = Enumerable.Range(0, pose.Height).SelectMany(row => Enumerable.Range(0, pose.Width).Select(col => (col, row))).First(p => Opaque(before, p.col, p.row));
        uint target = PixelTools.Get(before, x, y);
        const uint red = 0xFF2020D0;   // ABGR
        studio.Palette.Primary = red;
        pose.Tool = StudioTool.Fill;
        pose.PointerDown(x, y, PointerButton.Primary); pose.PointerUp(x, y);

        var after = Composite(studio, pose);
        var filled = PixelTools.Flood(pose.Width, pose.Height, x, y, (px, py) => PixelTools.Get(before, px, py) == target).ToHashSet();
        for (int row = 0; row < pose.Height; row++)
            for (int col = 0; col < pose.Width; col++)
                Assert.Equal(filled.Contains((col, row)) ? red : PixelTools.Get(before, col, row), PixelTools.Get(after, col, row));
        Assert.Equal(2, pose.Frame!.Layers.Count);
        Assert.Equal("Fill", editor.Shell.Document!.History.UndoLabel);
        editor.Shell.Document.Undo();
        Assert.Equal(before.Pixels, Composite(studio, pose).Pixels);
    }

    [Fact]
    public void MirrorCopyPasteAndNudgeWorkOnThePicture()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio);
        var before = Composite(studio, pose);

        Assert.True(pose.HandleKey("A", true, false));   // select all
        Assert.True(pose.HandleKey("H", false, false));  // mirror
        var mirrored = Composite(studio, pose);
        for (int row = 0; row < pose.Height; row++)
            for (int col = 0; col < pose.Width; col++)
                Assert.Equal(PixelTools.Get(before, pose.Width - 1 - col, row), PixelTools.Get(mirrored, col, row));
        Assert.Equal("Mirror pixels", editor.Shell.Document!.History.UndoLabel);
        editor.Shell.Document.Undo();

        // Copy her head, paste it lower down: only its visible pixels land.
        pose.Selection = new PixelRect(0, 0, 16, 12);
        pose.CopySelectionCommand.Execute(null);
        pose.Selection = new PixelRect(0, 16, 16, 12);
        pose.PasteSelectionCommand.Execute(null);
        Assert.True(pose.IsTransforming);   // a paste floats (Aseprite): place it, then Enter
        var pasted = Composite(studio, pose);
        for (int row = 0; row < 12; row++)
            for (int col = 0; col < 16; col++)
                Assert.Equal(Opaque(before, col, row) ? PixelTools.Get(before, col, row) : PixelTools.Get(before, col, row + 16), PixelTools.Get(pasted, col, row + 16));
        Assert.True(pose.HandleKey("Return", false, false));
        Assert.False(pose.IsTransforming);
        Assert.Equal("Paste", editor.Shell.Document!.History.UndoLabel);
        Assert.Equal(pasted.Pixels, Composite(studio, pose).Pixels);

        // Arrow keys nudge what the selection shows.
        var selected = pose.Selection!.Value;
        Assert.True(pose.HandleKey("Down", false, false));
        Assert.Equal(selected with { Y = selected.Y + 1 }, pose.Selection);
        Assert.Equal(PixelTools.Get(pasted, 5, 18), PixelTools.Get(Composite(studio, pose), 5, 19));
        Assert.Equal(2, pose.Frame!.Layers.Count);
        Assert.True(PaintIsEmpty(studio, pose));
    }

    [Fact]
    public void DrawingGoesOnThePictureAndTheEraserTakesItOff()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio);
        const uint blue = 0xFFD02020;
        studio.Palette.Primary = blue;
        pose.Tool = StudioTool.Pencil;
        pose.PointerDown(8, 16, PointerButton.Primary); pose.PointerUp(8, 16);
        Assert.Equal(blue, PixelTools.Get(Composite(studio, pose), 8, 16));
        Assert.Equal(2, pose.Frame!.Layers.Count);
        pose.Tool = StudioTool.Eraser;
        pose.PointerDown(8, 16, PointerButton.Primary); pose.PointerUp(8, 16);
        Assert.False(Opaque(Composite(studio, pose), 8, 16));   // your pixel and her pixel under it: both gone
    }

    [Fact]
    public void PaintingWithHerColourShowsHerRealColour()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio);
        var skin = Assert.Single(studio.Palette.VisibleSlots, s => s.Name == "Skin mid");   // shown as "Robin skin"
        studio.Palette.Primary = skin.Color;
        pose.Tool = StudioTool.Pencil;
        pose.PointerDown(8, 16, PointerButton.Primary); pose.PointerUp(8, 16);
        uint shown = PixelTools.Get(Composite(studio, pose), 8, 16);
        Assert.NotEqual(Core.Palette.ColorSlots.SkinMid, shown);   // not the reserved marker colour
        Assert.Equal(((System.Windows.Media.SolidColorBrush)skin.Display).Color, StudioImages.ToColor(shown));
        Assert.Equal(0, editor.Shell.ValidateNow().Errors);
    }

    [Fact]
    public void ALayeredPoseBecomesOnePictureThatLooksTheSameAndStaysLive()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio, layered: true);
        // Stage 1 edits: move her head down (a live cut-out) and paint a pixel.
        pose.Tool = StudioTool.Select;
        pose.PointerDown(0, 0, PointerButton.Primary); pose.PointerMove(15, 11); pose.PointerUp(15, 11);
        pose.PointerDown(5, 5, PointerButton.Primary); pose.PointerMove(5, 7); pose.PointerUp(5, 7);
        pose.Selection = null;
        pose.AddPaintLayerCommand.Execute(null);
        studio.Palette.Primary = 0xFF20D020;
        pose.Tool = StudioTool.Pencil;
        pose.PointerDown(1, 30, PointerButton.Primary); pose.PointerUp(1, 30);
        Assert.False(pose.IsFlat);
        Assert.True(pose.CanFlatten);
        var before = Composite(studio, pose);
        studio.Context.SheetOverrides["Robin"] = "Characters/Robin_Summer";
        var summerBefore = Composite(studio, pose);
        studio.Context.SheetOverrides.Remove("Robin");

        pose.MakeOnePictureCommand.Execute(null);

        Assert.True(pose.IsFlat);
        Assert.Equal(before.Pixels, Composite(studio, pose).Pixels);
        studio.Context.SheetOverrides["Robin"] = "Characters/Robin_Summer";
        Assert.Equal(summerBefore.Pixels, Composite(studio, pose).Pixels);   // still her live sprite
        studio.Context.SheetOverrides.Remove("Robin");
        Assert.DoesNotContain(studio.Library!.Masks().Masks, m => m.Tags.Contains(PoseDocumentViewModel.EditMaskTag));   // no leftovers
        Assert.Equal("Make it one picture", editor.Shell.Document!.History.UndoLabel);
        Assert.Equal(0, editor.Shell.ValidateNow().Errors);

        editor.Shell.Document.Undo();
        Assert.False(pose.IsFlat);
        Assert.Equal(before.Pixels, Composite(studio, pose).Pixels);
    }
}
