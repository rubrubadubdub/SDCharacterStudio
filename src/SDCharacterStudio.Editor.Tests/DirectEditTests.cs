using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Studio;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>
/// D-48: the eraser and moving a selection work on what you see. Live art stays live (it still follows sprite mods):
/// erasing hides its pixels with a keep mask; moving cuts a live copy, offset; each gesture is one undo step.
/// This is the layered model (Advanced); simple poses are one flat picture (<see cref="FlatPictureTests"/>).
/// </summary>
public sealed class DirectEditTests
{
    private static RgbaImage Composite(CharacterStudioViewModel studio, PoseDocumentViewModel pose) => studio.Context.RenderFrame(pose.Model, pose.FrameIndex).Image;
    private static bool Opaque(RgbaImage image, int x, int y) => PixelTools.Get(image, x, y) >> 24 != 0;

    [Fact]
    public void ErasingLiveArtHidesThosePixelsAndItStaysLive()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio, layered: true);
        var before = Composite(studio, pose);
        var (x, y) = Enumerable.Range(0, pose.Height).SelectMany(row => Enumerable.Range(0, pose.Width).Select(col => (col, row))).First(p => Opaque(before, p.col, p.row) && Opaque(before, p.col + 1, p.row));
        var elsewhere = Enumerable.Range(0, pose.Height).SelectMany(row => Enumerable.Range(0, pose.Width).Select(col => (col, row))).Last(p => Opaque(before, p.col, p.row));

        pose.Tool = StudioTool.Eraser;   // the paint layer is empty: the eraser still erases what you see
        pose.PointerDown(x, y, PointerButton.Primary);
        pose.PointerMove(x + 1, y);
        pose.PointerUp(x + 1, y);

        var after = Composite(studio, pose);
        Assert.False(Opaque(after, x, y));
        Assert.False(Opaque(after, x + 1, y));
        Assert.Equal(PixelTools.Get(before, elsewhere.col, elsewhere.row), PixelTools.Get(after, elsewhere.col, elsewhere.row));
        var live = Assert.IsType<LiveLayer>(Assert.Single(pose.Frame!.Layers));   // still the live sprite, not baked pixels
        var mask = studio.Context.FindMask(live.Mask);
        Assert.Contains(PoseDocumentViewModel.EditMaskTag, mask!.Tags);
        Assert.Equal("keep", mask.Mode);
        Assert.Equal("Erase", editor.Shell.Document!.History.UndoLabel);
        Assert.Equal(0, editor.Shell.ValidateNow().Errors);

        editor.Shell.Document.Undo();
        Assert.True(Opaque(Composite(studio, pose), x, y));
    }

    [Fact]
    public void DraggingASelectionMovesWhatYouSeeAndStaysLive()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio, layered: true);
        var before = Composite(studio, pose);
        pose.Tool = StudioTool.Select;
        pose.PointerDown(0, 0, PointerButton.Primary); pose.PointerMove(15, 11); pose.PointerUp(15, 11);   // her head
        Assert.Equal(new PixelRect(0, 0, 16, 12), pose.Selection);

        pose.PointerDown(5, 5, PointerButton.Primary); pose.PointerMove(5, 8); pose.PointerUp(5, 8);   // three pixels down

        var after = Composite(studio, pose);
        int moved = 0;
        for (int row = 0; row < 12; row++)
            for (int col = 0; col < 16; col++)
                if (Opaque(before, col, row)) { Assert.Equal(PixelTools.Get(before, col, row), PixelTools.Get(after, col, row + 3)); moved++; }
        Assert.True(moved > 20, "The head should have pixels to move.");
        Assert.All(pose.Frame!.Layers, l => Assert.IsType<LiveLayer>(l));   // both halves still follow sprite mods
        Assert.Equal(2, pose.Frame.Layers.Count);
        Assert.Equal(new[] { 0, 3 }, pose.Frame.Layers[1].Offset);
        Assert.Equal("Move pixels", editor.Shell.Document!.History.UndoLabel);
        Assert.Equal(new PixelRect(0, 3, 16, 12), pose.Selection);

        // Dragging the same selection again moves the same piece: no new cut.
        pose.PointerDown(5, 6, PointerButton.Primary); pose.PointerMove(5, 7); pose.PointerUp(5, 7);
        Assert.Equal(2, pose.Frame.Layers.Count);
        Assert.Equal(new[] { 0, 4 }, pose.Frame.Layers[1].Offset);
        Assert.Equal(0, editor.Shell.ValidateNow().Errors);

        editor.Shell.Document.Undo();
        editor.Shell.Document.Undo();
        Assert.Single(pose.Frame!.Layers);
        Assert.Equal(before.Pixels, Composite(studio, pose).Pixels);
    }

    [Fact]
    public void CtrlDragCopiesAndLeavesTheOriginal()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio, layered: true);
        var before = Composite(studio, pose);
        pose.Tool = StudioTool.Select;
        pose.PointerDown(0, 20, PointerButton.Primary); pose.PointerMove(15, 31); pose.PointerUp(15, 31);
        pose.PointerDown(5, 25, PointerButton.Primary, ctrl: true); pose.PointerMove(8, 25); pose.PointerUp(8, 25);
        Assert.Equal(2, pose.Frame!.Layers.Count);
        Assert.Null(pose.Frame.Layers[0].Mask);   // the original wasn't cut
        var after = Composite(studio, pose);
        for (int row = 0; row < 20; row++)
            for (int col = 0; col < 16; col++)
                Assert.Equal(PixelTools.Get(before, col, row), PixelTools.Get(after, col, row));
    }
}
