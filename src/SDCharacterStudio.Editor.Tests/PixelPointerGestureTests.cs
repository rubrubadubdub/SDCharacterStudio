using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SDCharacterStudio.Editor.Studio;
using SDCharacterStudio.Editor.ViewModels;
using SDCharacterStudio.Editor.Views.Studio;

namespace SDCharacterStudio.Editor.Tests;

public sealed partial class PointerGestureTests
{
    [PointerFact]
    public void PoseBrushCancellationSpacePanAndPanelToggleUseRealInput() => EditorUiThread.Run(() =>
    {
        var (h, studio) = CharacterStudioTests.OpenStudio();
        using var cleanup = h;
        var pose = CharacterStudioTests.NewPose(studio, "pointer-pose");
        pose.Tool = StudioTool.Pencil; pose.BrushSize = 1; pose.Zoom = 8;
        studio.Palette.Primary = 0xFF22CC44;
        var view = new CharacterStudioView { DataContext = studio };
        WithWindow(view, window =>
        {
            var canvas = PixelWorkspaceTests.Descendants<PixelCanvas>(view).Single();
            var editor = PixelWorkspaceTests.Descendants<PoseEditorView>(view).Single();
            var scroll = (ScrollViewer)editor.FindName("CanvasScroller");
            Point Pixel(double x, double y) => canvas.PointToScreen(canvas.FromFramePoint(new Point(x, y)));
            uint[] Picture() => studio.Context.RenderFrame(pose.Model, pose.FrameIndex).Image.Pixels.ToArray();
            var before = Picture();
            int history = h.Shell.Document!.History.UndoCount;
            AssertHit(window, Pixel(3.5, 4.5), e => ReferenceEquals(e, canvas));
            Gesture(Pixel(3.5, 4.5), Pixel(8.5, 6.5));
            Assert.Equal(history + 1, h.Shell.Document.History.UndoCount);
            Assert.NotEqual(before, Picture());
            Assert.True(h.Shell.Document.Undo()); Pump();
            Assert.Equal(before, Picture());

            Gesture(Pixel(3.5, 4.5), Pixel(8.5, 6.5), escape: true);
            Assert.Equal(before, Picture());
            Assert.Equal(history, h.Shell.Document.History.UndoCount);
            Assert.Null(Mouse.Captured);
            Gesture(Pixel(3.5, 4.5), Pixel(8.5, 6.5), loseCapture: () => Mouse.Capture(null));
            Assert.Equal(before, Picture());
            Assert.Equal(history, h.Shell.Document.History.UndoCount);

            pose.Zoom = 40; Pump();
            scroll.ScrollToHorizontalOffset(80); scroll.ScrollToVerticalOffset(100); Pump();
            double initialOffset = scroll.VerticalOffset;
            var p = scroll.PointToScreen(new Point(scroll.ViewportWidth / 2, scroll.ViewportHeight / 2));
            var input = Task.Run(() =>
            {
                try
                {
                    NativeInput.KeyDown(0x20);
                    NativeInput.Move(p); NativeInput.LeftDown();
                    NativeInput.Move(p + new Vector(-30, -40)); NativeInput.LeftUp();
                    // Keep Space held for a second pan: releasing the mouse must not switch back to painting.
                    NativeInput.Move(p); NativeInput.LeftDown();
                    NativeInput.Move(p + new Vector(-60, -80)); NativeInput.LeftUp();
                }
                finally { NativeInput.LeftUp(); NativeInput.KeyUp(0x20); }
            });
            EditorUiThread.PumpUntil(() => input.IsCompleted); input.GetAwaiter().GetResult(); Pump();
            Assert.False(pose.IsPlaying);
            Assert.True(scroll.VerticalOffset > initialOffset + 80);
            Assert.Equal(before, Picture());
            Assert.Equal(history, h.Shell.Document.History.UndoCount);

            canvas.Focus();
            SendKey(0x09);
            Assert.True(StudioWorkspace.GetFocusCanvas(view));
            SendKey(0x09);
            Assert.False(StudioWorkspace.GetFocusCanvas(view));
        });
    });

    [PointerFact]
    public void ExpressionAndMaskBrushesCancelThroughTheirViews() => EditorUiThread.Run(() =>
    {
        var (h, studio) = CharacterStudioTests.OpenStudio();
        using var cleanup = h;
        studio.NewExpression("pointer-expression");
        var expression = Assert.IsType<ExpressionDocumentViewModel>(studio.ActiveDocument);
        expression.Tool = StudioTool.Pencil; expression.Zoom = 8; studio.Palette.Primary = 0xFFCC2244;
        WithWindow(new ExpressionEditorView { DataContext = expression }, window =>
        {
            var canvas = PixelWorkspaceTests.Descendants<PixelCanvas>(window).Single();
            Point P(int x, int y) => canvas.PointToScreen(canvas.FromFramePoint(new Point(x + 0.5, y + 0.5)));
            string before = h.Shell.Document!.CaptureState();
            int count = h.Shell.Document.History.UndoCount;
            Gesture(P(3, 4), P(6, 5), escape: true);
            Assert.Equal(before, h.Shell.Document.CaptureState());
            Assert.Equal(count, h.Shell.Document.History.UndoCount);
            Assert.Empty(studio.Context.Overrides);
            Assert.Null(Mouse.Captured);
        });

        studio.OpenItem(LibraryKind.Mask, "robin-head-left");
        var mask = Assert.IsType<MaskDocumentViewModel>(studio.ActiveDocument);
        mask.Tool = MaskTool.Brush; mask.Zoom = 8;
        WithWindow(new MaskEditorView { DataContext = mask }, window =>
        {
            var canvas = PixelWorkspaceTests.Descendants<PixelCanvas>(window).Single();
            Point P(int x, int y) => canvas.PointToScreen(canvas.FromFramePoint(new Point(x + 0.5, y + 0.5)));
            var before = studio.Context.PackImage(mask.CurrentPath)!.Pixels.ToArray();
            int count = h.Shell.Document!.History.UndoCount;
            Gesture(P(3, 20), P(6, 22), loseCapture: () => Mouse.Capture(null));
            Assert.Equal(before, studio.Context.PackImage(mask.CurrentPath)!.Pixels);
            Assert.Equal(count, h.Shell.Document.History.UndoCount);
            Assert.Empty(studio.Context.Overrides);
            Assert.Null(Mouse.Captured);
        });
    });

    private static void SendKey(ushort key)
    {
        var input = Task.Run(() => NativeInput.Key(key));
        EditorUiThread.PumpUntil(() => input.IsCompleted); input.GetAwaiter().GetResult(); Pump();
    }
}
