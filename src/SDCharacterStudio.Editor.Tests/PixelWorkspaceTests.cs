using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using SDCharacterStudio.Editor.Studio;
using SDCharacterStudio.Editor.ViewModels;
using SDCharacterStudio.Editor.Views.Studio;

namespace SDCharacterStudio.Editor.Tests;

public sealed class PixelWorkspaceTests
{
    [Fact]
    public void LosingCaptureCancelsInsteadOfCompletingTheStroke() => EditorUiThread.Run(() =>
    {
        var canvas = new PixelCanvas();
        var window = new Window { Content = canvas, Width = 320, Height = 320, ShowActivated = false, Left = -4000 };
        int completed = 0;
        int cancelled = 0;
        canvas.PixelUp += (_, _) => completed++;
        canvas.PixelCancel += (_, _) => cancelled++;
        try
        {
            window.Show();
            canvas.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent });
            Assert.Same(canvas, Mouse.Captured);
            Mouse.Capture(null);
            canvas.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent });
            Assert.Equal(0, completed);
            Assert.Equal(1, cancelled);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(1280, 800)]
    [InlineData(1280, 680)]
    [InlineData(1600, 1000)]
    public void PaintingControlsFitInsideTheirCanvasPanel(int width, int height) => EditorUiThread.Run(() =>
    {
        using var h = new EditorHarness();
        h.NewProject();
        h.Shell.IsCharacterStudio = true;
        var studio = h.Shell.CharacterStudio;
        studio.Character = studio.Characters.First(c => c.Key == "Robin");
        studio.NewPose("character", "Test pose", "down");
        var pose = Assert.IsType<PoseDocumentViewModel>(studio.ActiveDocument);
        var view = new CharacterStudioView { DataContext = studio };
        var window = new Window { Content = view, Width = width, Height = height, ShowActivated = false, Left = -4000 };
        try
        {
            window.Show(); UiSnapshotTests.Pump(120);
            var editor = Descendants<PoseEditorView>(view).Single();
            var scroller = (ScrollViewer)editor.FindName("CanvasScroller");
            var panel = (FrameworkElement)VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(scroller));
            if (Environment.GetEnvironmentVariable("SDCS_PIXEL_SNAPSHOTS") is { Length: > 0 } output)
            {
                Directory.CreateDirectory(output);
                UiSnapshotTests.Save(window, Path.Combine(output, $"pose-{width}.png"));
            }
            var controls = Descendants<ButtonBase>(panel).Where(b => b.IsVisible &&
                (b.Command == pose.TogglePlayCommand || b.Command == pose.ZoomInCommand || b.Command == pose.ZoomOutCommand)).ToArray();
            Assert.True(controls.Length >= 3);
            foreach (var control in controls)
            {
                var p = control.TranslatePoint(new Point(), panel);
                Assert.True(p.X >= -1 && p.X + control.ActualWidth <= panel.ActualWidth + 1,
                    $"{control.ToolTip}: bounds {p.X}..{p.X + control.ActualWidth} outside panel 0..{panel.ActualWidth}");
            }
            Assert.True(scroller.ViewportWidth >= 320, $"Canvas width {scroller.ViewportWidth}");
            // The shorter host models the space below the main window's menu/status bars.
            Assert.True(scroller.ViewportHeight >= (height < 750 ? 200 : 250), $"Canvas height {scroller.ViewportHeight}");

            // A splitter adjustment survives hiding the side panels and returning to the workspace.
            var layout = (Grid)editor.Content;
            layout.ColumnDefinitions[4].Width = new GridLength(260);
            window.UpdateLayout();
            double regularWidth = scroller.ViewportWidth;
            StudioWorkspace.SetFocusCanvas(view, true);
            window.UpdateLayout(); UiSnapshotTests.Pump(60);
            Assert.True(scroller.ViewportWidth > regularWidth + 500);
            Assert.True(((PixelCanvas)editor.FindName("Canvas")).IsVisible);
            if (Environment.GetEnvironmentVariable("SDCS_PIXEL_SNAPSHOTS") is { Length: > 0 } focusedOutput)
                UiSnapshotTests.Save(window, Path.Combine(focusedOutput, $"pose-focus-{width}.png"));
            StudioWorkspace.SetFocusCanvas(view, false);
            window.UpdateLayout(); UiSnapshotTests.Pump(60);
            Assert.Equal(new GridLength(260), layout.ColumnDefinitions[4].Width);
            Assert.InRange(scroller.ViewportWidth, regularWidth - 1, regularWidth + 1);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExpressionAndMaskPanelsSupportTheSameFocusWorkspace(bool expression) => EditorUiThread.Run(() =>
    {
        var (h, studio) = CharacterStudioTests.OpenStudio();
        using var cleanup = h;
        studio.MakeRig();
        if (expression) studio.NewExpression("layout-expression");
        else studio.OpenItem(LibraryKind.Mask, "robin-head-left");
        var view = new CharacterStudioView { DataContext = studio };
        var window = new Window { Content = view, Width = 1280, Height = 680, ShowActivated = false, Left = -4000 };
        try
        {
            window.Show(); UiSnapshotTests.Pump(100);
            var canvas = Descendants<PixelCanvas>(view).Single();
            var scroll = Descendants<ScrollViewer>(view).Single(s => s.Content == canvas);
            Assert.True(scroll.ViewportWidth >= 320);
            Assert.True(scroll.ViewportHeight >= 200);
            double width = scroll.ViewportWidth;
            if (Environment.GetEnvironmentVariable("SDCS_PIXEL_SNAPSHOTS") is { Length: > 0 } output)
                UiSnapshotTests.Save(window, Path.Combine(output, expression ? "expression-1280.png" : "mask-1280.png"));
            StudioWorkspace.SetFocusCanvas(view, true); window.UpdateLayout(); UiSnapshotTests.Pump(40);
            Assert.True(scroll.ViewportWidth >= width + 500);
            StudioWorkspace.SetFocusCanvas(view, false); window.UpdateLayout(); UiSnapshotTests.Pump(40);
            Assert.InRange(scroll.ViewportWidth, width - 1, width + 1);
        }
        finally { window.Close(); }
    });

    internal static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
