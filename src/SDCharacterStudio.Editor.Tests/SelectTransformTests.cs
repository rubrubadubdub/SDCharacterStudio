using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Editor.Studio;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>D-49 phase 2: shaped selections, isolate, and a free transform that keeps live pixels live.</summary>
public sealed class SelectTransformTests
{
    private const uint Red = 0xFFD02020, Green = 0xFF20B040, Blue = 0xFF2040D0;
    private static RgbaImage Composite(CharacterStudioViewModel studio, PoseDocumentViewModel pose) => studio.Context.RenderFrame(pose.Model, pose.FrameIndex).Image;
    private static uint At(CharacterStudioViewModel studio, PoseDocumentViewModel pose, int x, int y) => PixelTools.Get(Composite(studio, pose), x, y);
    private static List<(int X, int Y)> Coloured(RgbaImage image, uint color) =>
        Enumerable.Range(0, image.Height).SelectMany(y => Enumerable.Range(0, image.Width).Select(x => (x, y))).Where(p => PixelTools.Get(image, p.x, p.y) == color).ToList();

    private static (CharacterStudioViewModel Studio, PoseDocumentViewModel Pose, EditorHarness Editor) Open(bool blank = true)
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        var pose = CharacterStudioTests.NewPose(studio);
        pose.Zoom = 12;
        if (blank) { pose.HandleKey("A", true, false); pose.DeleteSelectionCommand.Execute(null); pose.Selection = null; }
        return (studio, pose, editor);
    }

    private static void Dot(CharacterStudioViewModel studio, PoseDocumentViewModel pose, int x, int y, uint color)
    {
        var keep = pose.SelectionShape; pose.Selection = null;
        studio.Palette.Primary = color; pose.Tool = StudioTool.Pencil; pose.BrushSize = 1;
        pose.PointerDown(x, y, PointerButton.Primary); pose.PointerUp(x, y);
        if (keep != null) pose.Selection = keep.Bounds;
    }

    private static void Block(CharacterStudioViewModel studio, PoseDocumentViewModel pose, int x0, int y0, int x1, int y1, uint color)
    {
        studio.Palette.Primary = color; pose.Tool = StudioTool.Rectangle; pose.FilledShapes = true;
        pose.PointerDown(x0, y0, PointerButton.Primary); pose.PointerMove(x1, y1); pose.PointerUp(x1, y1);
    }

    [Fact]
    public void AShapedSelectionMovesOnlyWhatItSelected()
    {
        var (studio, pose, editor) = Open();
        using var _ = editor;
        Dot(studio, pose, 2, 2, Red); Dot(studio, pose, 4, 4, Green); Dot(studio, pose, 3, 3, Blue);
        pose.Tool = StudioTool.Wand;
        pose.PointerDown(2, 2, PointerButton.Primary); pose.PointerUp(2, 2);
        pose.PointerDown(4, 4, PointerButton.Primary, shift: true); pose.PointerUp(4, 4);
        Assert.Equal(2, pose.SelectionShape!.Count);
        Assert.Equal(new PixelRect(2, 2, 3, 3), pose.Selection);

        pose.Tool = StudioTool.Select;
        pose.PointerDown(2, 2, PointerButton.Primary); pose.PointerMove(2, 7); pose.PointerUp(2, 7);
        Assert.Equal(Red, At(studio, pose, 2, 7));
        Assert.Equal(Green, At(studio, pose, 4, 9));
        Assert.Equal(Blue, At(studio, pose, 3, 3));   // inside the box, not in the selection: stays
        Assert.Equal(0u, At(studio, pose, 2, 2));
        Assert.True(pose.SelectionShape!.Contains(2, 7) && pose.SelectionShape.Contains(4, 9) && !pose.SelectionShape.Contains(3, 8));
        Assert.Equal("Move pixels", editor.Shell.Document!.History.UndoLabel);
    }

    [Fact]
    public void TheWandPicksAColourAreaAndModifiersCombine()
    {
        var (studio, pose, editor) = Open();
        using var _ = editor;
        Block(studio, pose, 2, 2, 3, 3, Red);
        Dot(studio, pose, 8, 8, Red);
        Assert.True(pose.HandleKey("W", false, false));
        pose.PointerDown(2, 2, PointerButton.Primary); pose.PointerUp(2, 2);
        Assert.Equal(4, pose.SelectionShape!.Count);
        pose.PointerDown(2, 2, PointerButton.Primary, ctrl: true); pose.PointerUp(2, 2);
        Assert.Equal(5, pose.SelectionShape!.Count);   // that colour everywhere
        pose.PointerDown(8, 8, PointerButton.Primary, alt: true); pose.PointerUp(8, 8);
        Assert.Equal(4, pose.SelectionShape!.Count);   // Alt takes away
    }

    [Fact]
    public void MarqueeModesInvertAndReselect()
    {
        var (studio, pose, editor) = Open();
        using var _ = editor;
        pose.Tool = StudioTool.Select;
        pose.PointerDown(0, 0, PointerButton.Primary); pose.PointerMove(3, 3); pose.PointerUp(3, 3);
        Assert.Equal(16, pose.SelectionShape!.Count);
        pose.PointerDown(6, 6, PointerButton.Primary, shift: true); pose.PointerMove(7, 7); pose.PointerUp(7, 7);
        Assert.Equal(20, pose.SelectionShape!.Count);
        pose.PointerDown(0, 0, PointerButton.Primary, alt: true); pose.PointerMove(1, 1); pose.PointerUp(1, 1);
        Assert.Equal(16, pose.SelectionShape!.Count);
        Assert.True(pose.HandleKey("I", true, true));   // Ctrl+Shift+I
        Assert.Equal(pose.Width * pose.Height - 16, pose.SelectionShape!.Count);
        Assert.True(pose.HandleKey("D", true, false));
        Assert.Null(pose.SelectionShape);
        Assert.True(pose.HandleKey("D", true, true));   // Ctrl+Shift+D brings it back
        Assert.Equal(pose.Width * pose.Height - 16, pose.SelectionShape!.Count);
    }

    [Fact]
    public void CancellingMarqueeRestoresPriorSelectionAndReselectHistory()
    {
        var (studio, pose, editor) = Open();
        using var _ = editor;
        pose.Tool = StudioTool.Select;
        pose.PointerDown(1, 1, PointerButton.Primary); pose.PointerMove(2, 2); pose.PointerUp(2, 2);
        var before = pose.SelectionShape;
        pose.PointerDown(6, 6, PointerButton.Primary); pose.PointerMove(8, 8);
        pose.CancelStroke();
        Assert.Equal(before!.Cells().OrderBy(p => p), pose.SelectionShape!.Cells().OrderBy(p => p));
        pose.Selection = null;
        Assert.True(pose.HandleKey("D", true, true));
        Assert.Equal(before.Cells().OrderBy(p => p), pose.SelectionShape!.Cells().OrderBy(p => p));
    }

    [Fact]
    public void CancellingLassoRestoresSelectionAndAllowsNextGesture()
    {
        var (studio, pose, editor) = Open();
        using var _ = editor;
        pose.Tool = StudioTool.Select;
        pose.PointerDown(1, 1, PointerButton.Primary); pose.PointerMove(2, 2); pose.PointerUp(2, 2);
        var before = pose.SelectionShape;
        pose.Tool = StudioTool.Lasso;
        pose.PointerDown(6, 6, PointerButton.Primary); pose.PointerMove(8, 6);
        Assert.True(pose.IsStroking);
        pose.CancelStroke();
        Assert.False(pose.IsStroking);
        Assert.Equal(before!.Cells().OrderBy(p => p), pose.SelectionShape!.Cells().OrderBy(p => p));
        pose.CancelStroke();
        pose.PointerDown(4, 4, PointerButton.Primary); pose.PointerMove(5, 5); pose.PointerUp(5, 5);
        Assert.NotEqual(before.Cells().OrderBy(p => p), pose.SelectionShape!.Cells().OrderBy(p => p));
    }

    [Fact]
    public void CancellingPaintDoesNotRestoreStaleSelectionSnapshot()
    {
        var (studio, pose, editor) = Open();
        using var _ = editor;
        pose.Tool = StudioTool.Select;
        pose.PointerDown(1, 1, PointerButton.Primary); pose.PointerMove(2, 2); pose.PointerUp(2, 2);
        var before = pose.SelectionShape;
        studio.Palette.Primary = 0xFFFF0000;
        pose.Tool = StudioTool.Pencil;
        pose.PointerDown(3, 3, PointerButton.Primary);
        pose.CancelStroke();
        Assert.Equal(before!.Cells().OrderBy(p => p), pose.SelectionShape!.Cells().OrderBy(p => p));
    }

    [Fact]
    public void PaintingStaysInsideAShapedSelection()
    {
        var (studio, pose, editor) = Open();
        using var _ = editor;
        pose.Tool = StudioTool.Select;
        pose.PointerDown(0, 0, PointerButton.Primary); pose.PointerMove(1, 1); pose.PointerUp(1, 1);
        pose.PointerDown(4, 4, PointerButton.Primary, shift: true); pose.PointerMove(5, 5); pose.PointerUp(5, 5);
        studio.Palette.Primary = Red; pose.Tool = StudioTool.Pencil; pose.BrushSize = 1;
        pose.PointerDown(0, 0, PointerButton.Primary); pose.PointerMove(5, 5); pose.PointerUp(5, 5);
        Assert.Equal(new[] { (0, 0), (1, 1), (4, 4), (5, 5) }, Coloured(Composite(studio, pose), Red).OrderBy(p => p.X).ToArray());
    }

    [Fact]
    public void TransformMovesScalesCancelsAndAppliesAsOneStep()
    {
        var (studio, pose, editor) = Open();
        using var _ = editor;
        Block(studio, pose, 2, 2, 5, 5, Red);
        var before = Composite(studio, pose);
        string? label = editor.Shell.Document!.History.UndoLabel;

        Assert.True(pose.HandleKey("T", true, false));   // Ctrl+T
        Assert.True(pose.IsTransforming);
        Assert.NotNull(pose.TransformBox);
        Assert.Equal("move", pose.HandleAt(3.2, 3.2));
        pose.PointerDown(3, 3, PointerButton.Primary, fx: 3.2, fy: 3.2); pose.PointerMove(3, 7, fx: 3.2, fy: 7.2); pose.PointerUp(3, 7);
        Assert.Equal(Red, At(studio, pose, 2, 6));
        Assert.Equal(0u, At(studio, pose, 2, 2));
        Assert.Equal(label, editor.Shell.Document.History.UndoLabel);   // nothing saved yet
        Assert.True(pose.HandleKey("Escape", false, false));
        Assert.False(pose.IsTransforming);
        Assert.Equal(before.Pixels, Composite(studio, pose).Pixels);

        // Scale twice as big by the bottom-right corner (the top-left stays put).
        pose.HandleKey("T", true, false);
        Assert.Equal("c2", pose.HandleAt(6, 6));
        pose.PointerDown(6, 6, PointerButton.Primary, fx: 6, fy: 6); pose.PointerMove(10, 10, fx: 10, fy: 10); pose.PointerUp(10, 10);
        Assert.Equal(8, pose.TransformWidth);
        Assert.True(pose.HandleKey("Return", false, false));
        Assert.False(pose.IsTransforming);
        var scaled = Composite(studio, pose);
        Assert.Equal(64, Coloured(scaled, Red).Count);
        Assert.Equal(Red, PixelTools.Get(scaled, 2, 2));
        Assert.Equal(Red, PixelTools.Get(scaled, 9, 9));
        Assert.Equal(0u, PixelTools.Get(scaled, 10, 10));
        Assert.Equal("Transform", editor.Shell.Document.History.UndoLabel);
        editor.Shell.Document.Undo();
        Assert.Equal(before.Pixels, Composite(studio, pose).Pixels);
    }

    [Fact]
    public void QuarterTurnsAndShapedFlips()
    {
        var (studio, pose, editor) = Open();
        using var _ = editor;
        // An L: a bar three tall with a foot.
        Dot(studio, pose, 2, 2, Red); Dot(studio, pose, 2, 3, Red); Dot(studio, pose, 2, 4, Red); Dot(studio, pose, 3, 4, Red);
        pose.Tool = StudioTool.Select;
        pose.PointerDown(2, 2, PointerButton.Primary); pose.PointerMove(3, 4); pose.PointerUp(3, 4);
        Assert.True(pose.HandleKey("R", false, true));   // Shift+R
        var turned = Coloured(Composite(studio, pose), Red);
        Assert.Equal(4, turned.Count);
        Assert.Equal(3, turned.Max(p => p.X) - turned.Min(p => p.X) + 1);   // now lying down: three wide
        Assert.Equal(2, turned.Max(p => p.Y) - turned.Min(p => p.Y) + 1);
        Assert.Equal("Rotate pixels", editor.Shell.Document!.History.UndoLabel);
        Assert.False(pose.IsTransforming);

        // A shaped flip swaps only the selected pixels; the one between them stays.
        editor.Shell.Document.Undo();
        pose.HandleKey("A", true, false); pose.DeleteSelectionCommand.Execute(null); pose.Selection = null;
        Dot(studio, pose, 2, 10, Red); Dot(studio, pose, 5, 10, Green); Dot(studio, pose, 3, 10, Blue);
        pose.Tool = StudioTool.Wand;
        pose.PointerDown(2, 10, PointerButton.Primary); pose.PointerUp(2, 10);
        pose.PointerDown(5, 10, PointerButton.Primary, shift: true); pose.PointerUp(5, 10);
        Assert.True(pose.HandleKey("H", false, false));
        Assert.Equal(Green, At(studio, pose, 2, 10));
        Assert.Equal(Red, At(studio, pose, 5, 10));
        Assert.Equal(Blue, At(studio, pose, 3, 10));
        Assert.Equal("Mirror pixels", editor.Shell.Document.History.UndoLabel);
    }

    [Fact]
    public void RotatingByAnyAngleKeepsHerPixelsLiveAndAddsNoColours()
    {
        var (studio, pose, editor) = Open(blank: false);
        using var _ = editor;
        var before = Composite(studio, pose);
        var colours = before.Pixels.Where(p => p >> 24 != 0).ToHashSet();
        pose.Selection = new PixelRect(0, 0, 16, 12);   // her head
        Assert.True(pose.BeginTransform());
        pose.TransformAngle = 30;
        pose.ApplyTransformCommand.Execute(null);
        var after = Composite(studio, pose);
        Assert.NotEqual(before.Pixels, after.Pixels);
        Assert.All(after.Pixels.Where(p => p >> 24 != 0), p => Assert.Contains(p, colours));   // RotSprite picks, never mixes
        Assert.True(StudioImages.IsEmpty(studio.Context.PackImage(pose.Flat!.Paint.Image)!));   // every pixel still names her sprite

        studio.Context.SheetOverrides["Robin"] = "Characters/Robin_Summer";
        var summer = Composite(studio, pose);
        studio.Context.SheetOverrides.Remove("Robin");
        Assert.NotEqual(after.Pixels, summer.Pixels);   // the rotated head follows her other outfit
    }

    [Fact]
    public void TheMoveToolMovesTheWholePictureWithNothingSelected()
    {
        var (studio, pose, editor) = Open(blank: false);
        using var _ = editor;
        var before = Composite(studio, pose);
        Assert.True(pose.HandleKey("V", false, false));
        Assert.Equal(StudioTool.Move, pose.Tool);
        pose.PointerDown(8, 16, PointerButton.Primary); pose.PointerMove(10, 16); pose.PointerUp(10, 16);
        var after = Composite(studio, pose);
        for (int y = 0; y < pose.Height; y++)
            for (int x = 0; x < pose.Width - 2; x++)
                if (PixelTools.Get(before, x, y) >> 24 != 0) Assert.Equal(PixelTools.Get(before, x, y), PixelTools.Get(after, x + 2, y));
        Assert.Null(pose.SelectionShape);
        Assert.Equal("Move pixels", editor.Shell.Document!.History.UndoLabel);
    }

    [Fact]
    public void IsolateDimsEverythingOutsideTheSelection()
    {
        var (studio, pose, editor) = Open(blank: false);
        using var _ = editor;
        Assert.True(pose.HandleKey("I", false, true));   // Shift+I with nothing selected: a hint, no isolate
        Assert.False(pose.Isolate);
        pose.Selection = new PixelRect(0, 0, 16, 12);
        pose.HandleKey("I", false, true);
        Assert.True(pose.Isolate);
        Assert.NotNull(pose.IsolateOverlay);
        pose.Selection = null;
        Assert.Null(pose.IsolateOverlay);
    }

    [Fact]
    public void AnotherToolAppliesAFloatingPieceAndUndoDropsOne()
    {
        var (studio, pose, editor) = Open();
        using var _ = editor;
        Block(studio, pose, 2, 2, 5, 5, Red);
        var before = Composite(studio, pose);
        pose.HandleKey("T", true, false);
        pose.NudgeSelection(0, 3);
        pose.Tool = StudioTool.Pencil;   // picking up the pencil applies it
        Assert.False(pose.IsTransforming);
        Assert.Equal("Transform", editor.Shell.Document!.History.UndoLabel);
        Assert.Equal(Red, At(studio, pose, 2, 8));

        pose.HandleKey("T", true, false);
        pose.NudgeSelection(1, 0);
        editor.Shell.Document.Undo();   // undo while floating: the piece goes, the last step is undone
        Assert.False(pose.IsTransforming);
        Assert.Equal(before.Pixels, Composite(studio, pose).Pixels);
    }
}
