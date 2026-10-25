using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Studio;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Tests;

public sealed class AdvancedPainterSelectionTests
{
    private const uint Red = 0xFFD02020, Green = 0xFF20B040, Blue = 0xFF2040D0;

    private static uint At(CharacterStudioViewModel studio, PoseDocumentViewModel pose, int x, int y) =>
        PixelTools.Get(studio.Context.RenderFrame(pose.Model, pose.FrameIndex).Image, x, y);

    private static void Select(PoseDocumentViewModel pose, params (int X, int Y)[] cells)
    {
        pose.Selection = null;
        pose.Tool = StudioTool.Select;
        foreach (var (x, y) in cells)
        {
            pose.PointerDown(x, y, PointerButton.Primary, shift: true);
            pose.PointerUp(x, y);
        }
        Assert.Equal(cells.OrderBy(p => p), pose.SelectionShape!.Cells().OrderBy(p => p));
    }

    private static (EditorHarness Editor, CharacterStudioViewModel Studio, PoseDocumentViewModel Pose) PaintFixture(bool flip)
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        var pose = CharacterStudioTests.NewPose(studio, layered: true);
        pose.Frame!.Layers[0].Visible = false;
        pose.AddPaintLayerCommand.Execute(null);
        var layer = Assert.IsType<PaintLayer>(pose.SelectedLayer);
        layer.Offset = new[] { 2, 3 };
        layer.Flip = flip;
        pose.Tool = StudioTool.Pencil;
        foreach (var (x, y, color) in new[] { (3, 4, Red), (6, 6, Green), (4, 5, Blue), (5, 4, Blue) })
        {
            studio.Palette.Primary = color;
            pose.PointerDown(x, y, PointerButton.Primary); pose.PointerUp(x, y);
        }
        Select(pose, (3, 4), (6, 6));
        return (editor, studio, pose);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CopyAndPasteExcludeUnselectedPixelsInsideTheBounds(bool flip)
    {
        var (editor, studio, pose) = PaintFixture(flip);
        using var _ = editor;
        pose.CopySelectionCommand.Execute(null);
        pose.AddPaintLayerCommand.Execute(null);
        pose.Selection = new PixelRect(9, 10, 4, 3);

        pose.PasteSelectionCommand.Execute(null);

        Assert.Equal(Red, At(studio, pose, 9, 10));
        Assert.Equal(Green, At(studio, pose, 12, 12));
        Assert.Equal(0u, At(studio, pose, 10, 11));
        Assert.Equal(Blue, At(studio, pose, 4, 5));
    }

    [Theory]
    [InlineData(false, "delete")]
    [InlineData(true, "delete")]
    [InlineData(false, "mirror")]
    [InlineData(true, "mirror")]
    [InlineData(false, "flip")]
    [InlineData(true, "flip")]
    [InlineData(false, "nudge")]
    [InlineData(true, "nudge")]
    public void PixelCommandsOnlyEditSelectedPixelsInCanvasCoordinates(bool flip, string operation)
    {
        var (editor, studio, pose) = PaintFixture(flip);
        using var _ = editor;
        switch (operation)
        {
            case "delete": pose.DeleteSelectionCommand.Execute(null); break;
            case "mirror": pose.MirrorSelectionCommand.Execute(null); break;
            case "flip": pose.FlipSelectionVerticalCommand.Execute(null); break;
            default: pose.NudgeSelection(2, 2); break;
        }

        Assert.Equal(Blue, At(studio, pose, 4, 5));
        Assert.Equal(Blue, At(studio, pose, 5, 4));
        Assert.Equal(0u, At(studio, pose, 3, 4));
        Assert.Equal(0u, At(studio, pose, 6, 6));
        switch (operation)
        {
            case "mirror": Assert.Equal(Red, At(studio, pose, 6, 4)); Assert.Equal(Green, At(studio, pose, 3, 6)); break;
            case "flip": Assert.Equal(Red, At(studio, pose, 3, 6)); Assert.Equal(Green, At(studio, pose, 6, 4)); break;
            case "nudge": Assert.Equal(Red, At(studio, pose, 5, 6)); Assert.Equal(Green, At(studio, pose, 8, 8)); break;
        }
        var selected = operation switch
        {
            "mirror" or "flip" => new[] { (3, 6), (6, 4) },
            "nudge" => new[] { (5, 6), (8, 8) },
            _ => new[] { (3, 4), (6, 6) },
        };
        Assert.Equal(selected.OrderBy(p => p), pose.SelectionShape!.Cells().OrderBy(p => p));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EraserKeepsTheSelectionWhenItTargetsAnotherPaintLayer(bool flip)
    {
        var (editor, studio, pose) = PaintFixture(flip);
        using var _ = editor;
        pose.LayerIndex = 0;
        Select(pose, (3, 4), (6, 6));
        pose.Tool = StudioTool.Eraser;

        pose.PointerDown(3, 4, PointerButton.Primary); pose.PointerMove(6, 6); pose.PointerUp(6, 6);

        Assert.Equal(Blue, At(studio, pose, 4, 5));
        Assert.Equal(0u, At(studio, pose, 3, 4));
        Assert.Equal(0u, At(studio, pose, 6, 6));
        Assert.Equal(2, pose.SelectionShape!.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DraggingPaintOnlyLiftsSelectedPixels(bool flip)
    {
        var (editor, studio, pose) = PaintFixture(flip);
        using var _ = editor;
        pose.Tool = StudioTool.Move;

        pose.PointerDown(3, 4, PointerButton.Primary); pose.PointerMove(5, 6); pose.PointerUp(5, 6);

        Assert.Equal(Blue, At(studio, pose, 4, 5));
        Assert.Equal(Red, At(studio, pose, 5, 6));
        Assert.Equal(Green, At(studio, pose, 8, 8));
        Assert.Equal(0u, At(studio, pose, 6, 7));
    }

    [Theory]
    [InlineData("erase")]
    [InlineData("move")]
    public void DirectLiveEditsLeaveUnselectedPixelsInsideTheBounds(string operation)
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio, layered: true);
        var before = studio.Context.RenderFrame(pose.Model, pose.FrameIndex).Image;
        int y = Enumerable.Range(0, pose.Height - 5).First(row => Enumerable.Range(0, pose.Width).Count(x => PixelTools.Get(before, x, row) >> 24 != 0) >= 3);
        var xs = Enumerable.Range(0, pose.Width).Where(x => PixelTools.Get(before, x, y) >> 24 != 0).Take(3).ToArray();
        Select(pose, (xs[0], y), (xs[2], y));
        pose.Tool = operation == "erase" ? StudioTool.Eraser : StudioTool.Move;

        pose.PointerDown(xs[0], y, PointerButton.Primary);
        if (operation == "erase") pose.PointerMove(xs[2], y);
        else pose.PointerMove(xs[0], y + 5);
        pose.PointerUp(xs[0], y + 5);

        Assert.Equal(PixelTools.Get(before, xs[1], y), At(studio, pose, xs[1], y));
        Assert.Equal(0u, At(studio, pose, xs[0], y));
        Assert.Equal(0u, At(studio, pose, xs[2], y));
        if (operation == "move")
        {
            Assert.Equal(PixelTools.Get(before, xs[0], y), At(studio, pose, xs[0], y + 5));
            Assert.Equal(PixelTools.Get(before, xs[2], y), At(studio, pose, xs[2], y + 5));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellingAClippedMoveRestoresTheExactOriginalShape(bool layered)
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio, layered: layered);
        if (layered) pose.AddPaintLayerCommand.Execute(null);
        pose.Tool = StudioTool.Pencil;
        studio.Palette.Primary = Red;
        pose.PointerDown(3, 4, PointerButton.Primary); pose.PointerUp(3, 4);
        pose.PointerDown(6, 6, PointerButton.Primary); pose.PointerUp(6, 6);
        Select(pose, (3, 4), (6, 6));
        var original = pose.SelectionShape;
        pose.Tool = StudioTool.Move;
        pose.PointerDown(3, 4, PointerButton.Primary); pose.PointerMove(-2, 4);

        pose.CancelStroke();

        Assert.Equal(original!.Cells().OrderBy(p => p), pose.SelectionShape!.Cells().OrderBy(p => p));
        Assert.Equal(Red, At(studio, pose, 3, 4));
        Assert.Equal(Red, At(studio, pose, 6, 6));
    }
}
