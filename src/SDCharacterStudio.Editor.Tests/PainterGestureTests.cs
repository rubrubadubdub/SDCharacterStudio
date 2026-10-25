using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Studio;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Tests;

public sealed class PainterGestureTests
{
    private const uint Red = 0xFFD02020, Blue = 0xFF2040D0;

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void LayerFillStaysWithinSelectedPixelsAndMapsOffsetAndFlip(bool global, bool flip)
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio, layered: true);
        pose.AddPaintLayerCommand.Execute(null);
        var layer = Assert.IsType<PaintLayer>(pose.SelectedLayer);
        studio.Palette.Primary = Blue;
        pose.Tool = StudioTool.Fill;
        pose.PointerDown(0, 0, PointerButton.Primary);
        layer.Offset = new[] { 2, 3 };
        layer.Flip = flip;
        pose.Tool = StudioTool.Select;
        pose.PointerDown(3, 4, PointerButton.Primary); pose.PointerMove(4, 5); pose.PointerUp(4, 5);
        pose.PointerDown(7, 7, PointerButton.Primary, shift: true); pose.PointerMove(8, 8); pose.PointerUp(8, 8);
        Assert.Equal(8, pose.SelectionShape!.Count);
        studio.Palette.Primary = Red;
        pose.Tool = StudioTool.Fill;
        int history = editor.Shell.Document!.History.UndoCount;

        pose.PointerDown(3, 4, PointerButton.Primary, ctrl: global);
        pose.PointerUp(3, 4);

        var painted = studio.Library!.ReadImage(layer.Image)!;
        Assert.Equal(global ? 8 : 4, painted.Pixels.Count(p => p == Red));
        // Canvas (3,4) maps to local (1,1), or its mirrored source x=14.
        Assert.Equal(Red, PixelTools.Get(painted, flip ? 14 : 1, 1));
        Assert.Equal(global ? Red : Blue, PixelTools.Get(painted, flip ? 10 : 5, 4));
        Assert.Equal(Blue, PixelTools.Get(painted, flip ? 12 : 3, 3));
        Assert.Equal(history + 1, editor.Shell.Document.History.UndoCount);
        editor.Shell.Document.Undo();
        Assert.All(studio.Library.ReadImage(layer.Image)!.Pixels, p => Assert.Equal(Blue, p));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LayerFillOutsideSelectionDoesNotChangePixelsOrHistory(bool global)
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio, layered: true);
        pose.AddPaintLayerCommand.Execute(null);
        var layer = Assert.IsType<PaintLayer>(pose.SelectedLayer);
        pose.Selection = new PixelRect(3, 4, 2, 2);
        studio.Palette.Primary = Red;
        pose.Tool = StudioTool.Fill;
        int history = editor.Shell.Document!.History.UndoCount;

        pose.PointerDown(0, 0, PointerButton.Primary, ctrl: global);
        pose.PointerUp(0, 0);

        Assert.True(StudioImages.IsEmpty(studio.Library!.ReadImage(layer.Image)!));
        Assert.Equal(history, editor.Shell.Document.History.UndoCount);
        Assert.Empty(studio.Context.Overrides);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FlatFillHonorsTheSelectionShapeInsteadOfItsBoundingBox(bool global)
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio);
        studio.Palette.Primary = Blue;
        pose.Tool = StudioTool.Rectangle;
        pose.FilledShapes = true;
        pose.PointerDown(0, 0, PointerButton.Primary); pose.PointerMove(15, 31); pose.PointerUp(15, 31);
        pose.Tool = StudioTool.Select;
        pose.PointerDown(1, 1, PointerButton.Primary); pose.PointerMove(2, 2); pose.PointerUp(2, 2);
        pose.PointerDown(6, 6, PointerButton.Primary, shift: true); pose.PointerMove(7, 7); pose.PointerUp(7, 7);
        studio.Palette.Primary = Red;
        pose.Tool = StudioTool.Fill;

        pose.PointerDown(1, 1, PointerButton.Primary, ctrl: global);
        pose.PointerUp(1, 1);

        var painted = studio.Library!.ReadImage(pose.Flat!.Paint.Image)!;
        Assert.Equal(global ? 8 : 4, painted.Pixels.Count(p => p == Red));
        Assert.Equal(Red, PixelTools.Get(painted, 1, 1));
        Assert.Equal(global ? Red : Blue, PixelTools.Get(painted, 6, 6));
        Assert.Equal(Blue, PixelTools.Get(painted, 3, 3));
    }

    [Fact]
    public void ChangingExpressionFrameCancelsTheOriginalStrokeWithoutChangingEitherFrame()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        studio.NewExpression("frame-switch");
        var expression = Assert.IsType<ExpressionDocumentViewModel>(studio.ActiveDocument);
        expression.AddFrameCommand.Execute(null);
        expression.FrameIndex = 0;
        var original = expression.Frame!;
        var other = expression.Model.Frames[1];
        string? originalPath = original.Image, otherPath = other.Image;
        int[] originalOffset = original.Offset.ToArray(), otherOffset = other.Offset.ToArray();
        int history = editor.Shell.Document!.History.UndoCount;
        studio.Palette.Primary = Red;
        expression.PointerDown(1, 2, PointerButton.Primary, false);

        expression.FrameIndex = 1;
        expression.CancelStroke();
        expression.PointerUp();

        Assert.Empty(studio.Context.Overrides);
        Assert.Equal(originalPath, original.Image);
        Assert.Equal(originalOffset, original.Offset);
        Assert.Equal(otherPath, other.Image);
        Assert.Equal(otherOffset, other.Offset);
        Assert.Equal(history, editor.Shell.Document.History.UndoCount);
    }

    [Fact]
    public void ChangingExpressionToolCancelsTheStrokeBeforeContinuing()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        studio.NewExpression("tool-switch");
        var expression = Assert.IsType<ExpressionDocumentViewModel>(studio.ActiveDocument);
        studio.Palette.Primary = Red;
        int history = editor.Shell.Document!.History.UndoCount;
        expression.PointerDown(1, 2, PointerButton.Primary, false);

        expression.Tool = StudioTool.Eraser;
        expression.PointerUp();

        Assert.Empty(studio.Context.Overrides);
        Assert.Equal(history, editor.Shell.Document.History.UndoCount);
    }

    [Fact]
    public void ChangingMaskSourceFrameCancelsItsStroke()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        studio.MakeRig();
        studio.OpenItem(LibraryKind.Mask, "robin-head-left");
        var mask = Assert.IsType<MaskDocumentViewModel>(studio.ActiveDocument);
        mask.Tool = MaskTool.Eraser;
        int history = editor.Shell.Document!.History.UndoCount;
        mask.PointerDown(8, 10, PointerButton.Primary, false);

        mask.SourceFrame++;
        mask.PointerUp();

        Assert.Empty(studio.Context.Overrides);
        Assert.Equal(history, editor.Shell.Document.History.UndoCount);
    }

    [Fact]
    public void ChangingPoseToolCancelsAnUnfinishedLassoAndRestoresTheSelection()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio);
        pose.Selection = new PixelRect(1, 1, 2, 2);
        pose.Tool = StudioTool.Lasso;
        pose.PointerDown(5, 5, PointerButton.Primary); pose.PointerMove(7, 5);

        pose.Tool = StudioTool.Pencil;

        Assert.False(pose.IsStroking);
        Assert.Equal(new PixelRect(1, 1, 2, 2), pose.Selection);
        pose.Tool = StudioTool.Lasso;
        pose.PointerMove(8, 8);
        pose.PointerUp(8, 8);
        Assert.Equal(new PixelRect(1, 1, 2, 2), pose.Selection);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellingPixelMoveRestoresPixelsAndSelection(bool layered)
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio, layered: layered);
        if (layered) pose.AddPaintLayerCommand.Execute(null);
        studio.Palette.Primary = Red;
        pose.Tool = StudioTool.Pencil;
        pose.PointerDown(2, 2, PointerButton.Primary); pose.PointerUp(2, 2);
        pose.Selection = new PixelRect(2, 2, 2, 2);
        var before = studio.Context.RenderFrame(pose.Model, pose.FrameIndex).Image.Pixels.ToArray();
        int history = editor.Shell.Document!.History.UndoCount;
        pose.Tool = StudioTool.Move;
        pose.PointerDown(2, 2, PointerButton.Primary); pose.PointerMove(6, 6);

        pose.CancelStroke();
        pose.PointerUp(6, 6);

        Assert.False(pose.IsStroking);
        Assert.Empty(studio.Context.Overrides);
        Assert.Equal(new PixelRect(2, 2, 2, 2), pose.Selection);
        Assert.Equal(before, studio.Context.RenderFrame(pose.Model, pose.FrameIndex).Image.Pixels);
        Assert.Equal(history, editor.Shell.Document.History.UndoCount);
    }

    [Fact]
    public void CancellingBodyPartPaintingDoesNotSaveOnPointerRelease()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio);
        pose.ShowParts = true;
        int history = editor.Shell.Document!.History.UndoCount;
        pose.PointerDown(8, 10, PointerButton.Primary);

        pose.CancelStroke();
        pose.PointerUp(8, 10);

        Assert.Equal(history, editor.Shell.Document.History.UndoCount);
    }

    [Fact]
    public void UndoCancelsExpressionPreviewBeforeReloadingItsModel()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        studio.NewExpression("undo-expression");
        var expression = Assert.IsType<ExpressionDocumentViewModel>(studio.ActiveDocument);
        studio.Palette.Primary = Red;
        expression.PointerDown(1, 2, PointerButton.Primary, false); expression.PointerUp();
        expression.PointerDown(5, 6, PointerButton.Primary, false);

        editor.Shell.Document!.Undo();

        Assert.Empty(studio.Context.Overrides);
        int history = editor.Shell.Document.History.UndoCount;
        expression.PointerUp();
        Assert.Equal(history, editor.Shell.Document.History.UndoCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UndoCancelsPosePreviewBeforeReloadingItsModel(bool layered)
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio, layered: layered);
        if (layered) pose.AddPaintLayerCommand.Execute(null);
        studio.Palette.Primary = Red;
        pose.Tool = StudioTool.Pencil;
        pose.PointerDown(1, 2, PointerButton.Primary); pose.PointerUp(1, 2);
        pose.PointerDown(5, 6, PointerButton.Primary);

        editor.Shell.Document!.Undo();

        Assert.Empty(studio.Context.Overrides);
        Assert.False(pose.IsStroking);
        int history = editor.Shell.Document.History.UndoCount;
        pose.PointerUp(5, 6);
        Assert.Equal(history, editor.Shell.Document.History.UndoCount);
    }

    [Fact]
    public void UndoCancelsMaskPreviewBeforeReloadingItsModel()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        studio.MakeRig();
        studio.OpenItem(LibraryKind.Mask, "robin-head-left");
        var mask = Assert.IsType<MaskDocumentViewModel>(studio.ActiveDocument);
        mask.ClearMaskCommand.Execute(null);
        mask.PointerDown(8, 10, PointerButton.Primary, false);

        editor.Shell.Document!.Undo();

        Assert.Empty(studio.Context.Overrides);
        int history = editor.Shell.Document.History.UndoCount;
        mask.PointerUp();
        Assert.Equal(history, editor.Shell.Document.History.UndoCount);
    }
}
