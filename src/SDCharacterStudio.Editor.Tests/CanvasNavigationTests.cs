using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using SDCharacterStudio.Editor.Views.Studio;
using SDCharacterStudio.Editor.ViewModels;
using SDCharacterStudio.Editor.Studio;

namespace SDCharacterStudio.Editor.Tests;

public sealed class CanvasNavigationTests
{
    private static ScrollViewer Scroller(PixelCanvas canvas) => new()
    {
        Content = canvas, BorderThickness = new Thickness(3), Padding = new Thickness(5),
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };
    private static Window Show(FrameworkElement content)
    {
        var window = new Window { Content = content, Width = 300, Height = 260, ShowActivated = false, Left = -4000 };
        window.Show(); UiSnapshotTests.Pump(20); window.UpdateLayout(); return window;
    }
    private static MouseButtonEventArgs MouseButtonArgs(MouseButton button) => new(Mouse.PrimaryDevice, 0, button) { RoutedEvent = UIElement.MouseDownEvent };
    private static CanvasNavigation Navigation(ScrollViewer scroll, PixelCanvas canvas, Action? cancel = null) => new(scroll, canvas, () => canvas.Zoom, z => canvas.Zoom = z, () => canvas.PixelWidth, () => canvas.PixelHeight, cancel);

    [Fact]
    public void FitUsesWholeRealCanvasViewportAndCentersExtent() => EditorUiThread.Run(() =>
    {
        var canvas = new PixelCanvas { PixelWidth = 16, PixelHeight = 16, Zoom = 8 };
        var scroll = Scroller(canvas); var window = Show(scroll);
        try
        {
            Navigation(scroll, canvas).Fit(); scroll.UpdateLayout();
            Assert.Equal(Math.Floor(canvas.Zoom), canvas.Zoom);
            Assert.InRange(canvas.ActualWidth, 1, scroll.ViewportWidth);
            Assert.InRange(canvas.ActualHeight, 1, scroll.ViewportHeight);
            Assert.Equal(0, scroll.HorizontalOffset); Assert.Equal(0, scroll.VerticalOffset);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void PanCancelsCanvasCaptureAndEndsOnlyForInitiatingButton() => EditorUiThread.Run(() =>
    {
        var canvas = new PixelCanvas { PixelWidth = 64, PixelHeight = 64, Zoom = 8 };
        var scroll = Scroller(canvas); var window = Show(scroll);
        try
        {
            Assert.True(canvas.CaptureMouse());
            bool cancelled = false;
            var navigation = Navigation(scroll, canvas, () => { cancelled = true; Assert.NotSame(canvas, Mouse.Captured); });
            Assert.True(navigation.BeginPan(MouseButtonArgs(MouseButton.Middle), false));
            Assert.True(cancelled); Assert.True(navigation.IsPanning); Assert.Same(scroll, Mouse.Captured);
            Assert.False(navigation.EndPan(MouseButtonArgs(MouseButton.Left))); Assert.True(navigation.IsPanning);
            Assert.True(navigation.EndPan(MouseButtonArgs(MouseButton.Middle))); Assert.False(navigation.IsPanning);
            Assert.NotSame(scroll, Mouse.Captured);
        }
        finally { Mouse.Capture(null); window.Close(); }
    });

    [Fact]
    public void FailedCaptureCannotLeavePanActive() => EditorUiThread.Run(() =>
    {
        var canvas = new PixelCanvas(); var scroll = Scroller(canvas);
        var navigation = Navigation(scroll, canvas);
        Assert.False(navigation.BeginPan(MouseButtonArgs(MouseButton.Middle), false));
        Assert.False(navigation.IsPanning);
    });

    [Theory]
    [InlineData(1)] [InlineData(-1)]
    public void ZoomKeepsOffCenterAnchorWithinOneScreenPixelWithScrolledAutoBars(int delta) => EditorUiThread.Run(() =>
    {
        var canvas = new PixelCanvas { PixelWidth = 128, PixelHeight = 96, Zoom = 5 };
        var scroll = Scroller(canvas); var window = Show(scroll);
        try
        {
            // Leave room to scroll in both directions at both zoom levels. The app's
            // Window style has a minimum width, so a larger offset would hit the edge on zoom-out.
            scroll.ScrollToHorizontalOffset(130); scroll.ScrollToVerticalOffset(135); scroll.UpdateLayout();
            Assert.Equal(130, scroll.HorizontalOffset); Assert.Equal(135, scroll.VerticalOffset);
            var at = new Point(73, 119);
            var frame = canvas.ToFramePoint(scroll.TranslatePoint(at, canvas));
            Navigation(scroll, canvas).ZoomAt(at, delta); scroll.UpdateLayout();
            var after = canvas.TranslatePoint(canvas.FromFramePoint(frame), scroll);
            Assert.InRange(Math.Abs(after.X - at.X), 0, 1.0);
            Assert.InRange(Math.Abs(after.Y - at.Y), 0, 1.0);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(typeof(ExpressionEditorView))] [InlineData(typeof(MaskEditorView))]
    public void SpaceKeyUpIsUntouchedWithoutMatchingEditorKeyDown(Type viewType) => EditorUiThread.Run(() =>
    {
        var view = (UserControl)Activator.CreateInstance(viewType)!; var window = Show(view);
        try
        {
            var args = KeyArgs(view, Key.Space, UIElement.PreviewKeyUpEvent);
            viewType.GetMethod("View_PreviewKeyUp", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(view, new object[] { view, args });
            Assert.False(args.Handled);
        }
        finally { window.Close(); }
    });

    private static KeyEventArgs KeyArgs(Visual visual, Key key, RoutedEvent routedEvent) => new(Keyboard.PrimaryDevice, PresentationSource.FromVisual(visual)!, 0, key) { RoutedEvent = routedEvent };

    [Theory]
    [InlineData(typeof(PoseEditorView))] [InlineData(typeof(ExpressionEditorView))] [InlineData(typeof(MaskEditorView))]
    public void ChangingDocumentContextCancelsOldDocumentsPendingStroke(Type type) => EditorUiThread.Run(() =>
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio(); using var _ = editor;
        StudioDocumentViewModel document; int x = 3, y = 4;
        if (type == typeof(PoseEditorView)) document = CharacterStudioTests.NewPose(studio);
        else if (type == typeof(ExpressionEditorView))
        {
            studio.NewExpression("context-switch");
            var expression = Assert.IsType<ExpressionDocumentViewModel>(studio.ActiveDocument);
            document = expression; x = expression.Box[0]; y = expression.Box[1];
        }
        else { studio.MakeRig(); studio.OpenItem(LibraryKind.Mask, "robin-head-left"); document = studio.ActiveDocument!; x = 2; y = 20; }
        studio.Palette.Primary = 0xFFCC2244;
        var view = (UserControl)Activator.CreateInstance(type)!; view.DataContext = document; var window = Show(view);
        try
        {
            var before = studio.Context.Overrides.Keys.OrderBy(k => k).ToArray();
            int history = editor.Shell.Document!.History.UndoCount;
            var canvas = (PixelCanvas)view.FindName("Canvas");
            canvas.RaiseEvent(new PixelPointerEventArgs(PixelCanvas.PixelDownEvent, x, y, MouseButton.Left));
            if (document is PoseDocumentViewModel pose) Assert.True(pose.IsStroking);
            else Assert.NotEqual(before, studio.Context.Overrides.Keys.OrderBy(k => k).ToArray());
            view.DataContext = null;
            if (document is PoseDocumentViewModel oldPose) Assert.False(oldPose.IsStroking);
            Assert.Equal(before, studio.Context.Overrides.Keys.OrderBy(k => k).ToArray());
            canvas.RaiseEvent(new PixelPointerEventArgs(PixelCanvas.PixelUpEvent, x, y, MouseButton.Left));
            Assert.Equal(history, editor.Shell.Document.History.UndoCount);
        }
        finally { window.Close(); }
    });

    private static KeyEventArgs SendKey(UserControl view, Key key, bool up = false, object? source = null)
    {
        var args = KeyArgs(view, key, up ? UIElement.PreviewKeyUpEvent : UIElement.PreviewKeyDownEvent);
        args.Source = source ?? view.FindName("Canvas");
        var method = view is PoseEditorView ? (up ? "OnPreviewKeyUp" : "OnPreviewKeyDown") : (up ? "View_PreviewKeyUp" : "View_PreviewKeyDown");
        view.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)!.Invoke(view, new object[] { view, args });
        return args;
    }

    [Theory]
    [InlineData(typeof(PoseEditorView))] [InlineData(typeof(ExpressionEditorView))] [InlineData(typeof(MaskEditorView))]
    public void SpaceLeavesTextComboAndButtonInputUntouched(Type type) => EditorUiThread.Run(() =>
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio(); using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio);
        var view = (UserControl)Activator.CreateInstance(type)!; view.DataContext = pose;
        var window = Show(view);
        try
        {
            foreach (var input in new Control[] { new TextBox(), new ComboBox(), new Button() })
            {
                Assert.False(SendKey(view, Key.Space, source: input).Handled);
                Assert.False(SendKey(view, Key.Space, up: true, source: input).Handled);
                Assert.False(pose.IsPlaying);
            }
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(typeof(PoseEditorView))] [InlineData(typeof(ExpressionEditorView))] [InlineData(typeof(MaskEditorView))]
    public void IdleEscapeLeavesTextAndComboInputUntouched(Type type) => EditorUiThread.Run(() =>
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio(); using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio);
        var view = (UserControl)Activator.CreateInstance(type)!; view.DataContext = pose; var window = Show(view);
        try
        {
            Assert.False(SendKey(view, Key.Escape, source: new ComboBox()).Handled);
            Assert.False(SendKey(view, Key.Escape, source: new TextBox()).Handled);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void ReplacingContextDuringStrokeDoesNotCancelNewDocumentsFloatingTransform() => EditorUiThread.Run(() =>
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio(); using var _ = editor;
        var first = CharacterStudioTests.NewPose(studio, "old-view-document");
        var next = CharacterStudioTests.NewPose(studio, "next-view-document");
        Assert.True(next.BeginTransform()); next.TransformAngle = 30;
        var view = new PoseEditorView { DataContext = first }; var window = Show(view);
        try
        {
            var canvas = (PixelCanvas)view.FindName("Canvas");
            canvas.RaiseEvent(new PixelPointerEventArgs(PixelCanvas.PixelDownEvent, 3, 4, MouseButton.Left));
            Assert.True(first.IsStroking);
            // This is the same notification CancelGesture raises after clearing its
            // capture. Deliver it synchronously during the context-change cleanup.
            canvas.LostMouseCapture += (_, _) => canvas.RaiseEvent(new PixelPointerEventArgs(PixelCanvas.PixelCancelEvent, 3, 4, MouseButton.Left));
            Assert.True(canvas.CaptureMouse());
            view.DataContext = next;
            Assert.False(first.IsStroking); Assert.True(next.IsTransforming); Assert.Equal(30, next.TransformAngle);
        }
        finally { Mouse.Capture(null); window.Close(); }
    });

    [Fact]
    public void PoseSpaceTapPlaysButSpacePanNeverPlays() => EditorUiThread.Run(() =>
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio(); using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio);
        var view = new PoseEditorView { DataContext = pose }; var window = Show(view);
        try
        {
            Assert.True(SendKey(view, Key.Space).Handled); Assert.False(pose.IsPlaying);
            SendKey(view, Key.Space, up: true); Assert.True(pose.IsPlaying); pose.IsPlaying = false;
            SendKey(view, Key.Space);
            var scroll = (ScrollViewer)view.FindName("CanvasScroller");
            scroll.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseDownEvent });
            Assert.Same(scroll, Mouse.Captured);
            scroll.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseUpEvent });
            SendKey(view, Key.Space, up: true); Assert.False(pose.IsPlaying);
        }
        finally { Mouse.Capture(null); window.Close(); }
    });

    [Theory]
    [InlineData(typeof(PoseEditorView))] [InlineData(typeof(ExpressionEditorView))] [InlineData(typeof(MaskEditorView))]
    public void HoldingSpaceSupportsTwoConsecutivePanDragsWithoutPainting(Type type) => EditorUiThread.Run(() =>
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio(); using var _ = editor;
        StudioDocumentViewModel document;
        if (type == typeof(PoseEditorView)) document = CharacterStudioTests.NewPose(studio);
        else if (type == typeof(ExpressionEditorView)) { studio.NewExpression("repeat-pan"); document = studio.ActiveDocument!; }
        else { studio.MakeRig(); studio.OpenItem(LibraryKind.Mask, "robin-head-left"); document = studio.ActiveDocument!; }
        var view = (UserControl)Activator.CreateInstance(type)!; view.DataContext = document; var window = Show(view);
        try
        {
            var scroll = (ScrollViewer)view.FindName("CanvasScroller");
            int history = editor.Shell.Document!.History.UndoCount;
            SendKey(view, Key.Space);
            for (int drag = 0; drag < 2; drag++)
            {
                var down = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseDownEvent };
                scroll.RaiseEvent(down); Assert.True(down.Handled); Assert.Same(scroll, Mouse.Captured);
                scroll.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseUpEvent });
                Assert.NotSame(scroll, Mouse.Captured); Assert.Same(Cursors.SizeAll, ((PixelCanvas)view.FindName("Canvas")).Cursor);
            }
            SendKey(view, Key.Space, up: true);
            Assert.Equal(history, editor.Shell.Document.History.UndoCount); Assert.Empty(studio.Context.Overrides);
            if (document is PoseDocumentViewModel pose) Assert.False(pose.IsPlaying);
        }
        finally { Mouse.Capture(null); window.Close(); }
    });

    [Fact]
    public void FloatingTransformSurvivesOptionsFocusAndPanningThenApplies() => EditorUiThread.Run(() =>
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio(); using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio);
        var view = new PoseEditorView { DataContext = pose }; var window = Show(view);
        try
        {
            var canvas = (PixelCanvas)view.FindName("Canvas"); var scroll = (ScrollViewer)view.FindName("CanvasScroller");
            Assert.True(pose.BeginTransform()); window.UpdateLayout();
            var angle = PixelWorkspaceTests.Descendants<TextBox>(view).Single(t => BindingOperations.GetBinding(t, TextBox.TextProperty)?.Path.Path == nameof(pose.TransformAngle));
            var apply = PixelWorkspaceTests.Descendants<ButtonBase>(view).Single(b => ReferenceEquals(b.Command, pose.ApplyTransformCommand));
            angle.Focus();
            canvas.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, canvas, angle) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
            Assert.True(pose.IsTransforming);
            angle.Text = "30"; angle.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Assert.Equal(30, pose.TransformAngle);
            var transform = pose.CurrentTransform;
            scroll.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Middle) { RoutedEvent = UIElement.PreviewMouseDownEvent });
            Assert.Same(scroll, Mouse.Captured); Assert.True(pose.IsTransforming);
            scroll.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Middle) { RoutedEvent = UIElement.PreviewMouseUpEvent });
            ((CanvasNavigation)typeof(PoseEditorView).GetField("navigation", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view)!).ZoomAt(new Point(73, 119), 1);
            Assert.Equal(transform, pose.CurrentTransform);
            apply.Focus();
            canvas.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, angle, apply) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
            Assert.True(pose.IsTransforming);
            int history = editor.Shell.Document!.History.UndoCount;
            apply.Command.Execute(null);
            Assert.False(pose.IsTransforming); Assert.Equal(history + 1, editor.Shell.Document.History.UndoCount);
        }
        finally { Mouse.Capture(null); window.Close(); }
    });

    [Fact]
    public void PoseEscapeKeepsIdleDeselectPeekAndShortcutSemantics() => EditorUiThread.Run(() =>
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio(); using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio); var view = new PoseEditorView { DataContext = pose }; var window = Show(view);
        try
        {
            pose.Selection = new PixelRect(1, 2, 3, 4); SendKey(view, Key.Escape); Assert.Null(pose.Selection);
            pose.PeekFrame = 0; Assert.True(pose.IsPeeking); SendKey(view, Key.Escape); Assert.False(pose.IsPeeking);
            Assert.True(pose.BeginTransform()); SendKey(view, Key.Escape); Assert.False(pose.IsTransforming);
            var selected = new PixelRect(1, 2, 3, 4); pose.Selection = selected; pose.IsShortcutsOpen = true;
            SendKey(view, Key.Escape); Assert.False(pose.IsShortcutsOpen); Assert.Equal(selected, pose.Selection);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void PoseEscapeCancelsActiveSelectionWithoutDeselectingRestoredSelection() => EditorUiThread.Run(() =>
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio(); using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio); var view = new PoseEditorView { DataContext = pose }; var window = Show(view);
        try
        {
            pose.Tool = StudioTool.Select; var selected = new PixelRect(1, 2, 3, 4); pose.Selection = selected;
            pose.PointerDown(10, 12, PointerButton.Primary); pose.PointerMove(12, 14);
            Assert.NotEqual(selected, pose.Selection); Assert.True(pose.IsStroking);
            SendKey(view, Key.Escape); Assert.False(pose.IsStroking); Assert.Equal(selected, pose.Selection);
            pose.PointerUp(12, 14); Assert.Equal(selected, pose.Selection);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(typeof(PoseEditorView))] [InlineData(typeof(ExpressionEditorView))] [InlineData(typeof(MaskEditorView))]
    public void DeactivationResetsCursorAndStopsAffectingUnloadedView(Type type) => EditorUiThread.Run(() =>
    {
        var view = (UserControl)Activator.CreateInstance(type)!; var window = Show(view);
        var canvas = (PixelCanvas)view.FindName("Canvas");
        void Deactivate() => typeof(Window).GetMethod("OnDeactivated", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, new object[] { EventArgs.Empty });
        try
        {
            canvas.Cursor = Cursors.SizeAll; Deactivate(); Assert.Same(Cursors.Cross, canvas.Cursor);
            window.Content = null; UiSnapshotTests.Pump(20);
            canvas.Cursor = Cursors.SizeAll; Deactivate(); Assert.Same(Cursors.SizeAll, canvas.Cursor);
            window.Content = view; UiSnapshotTests.Pump(20);
            canvas.Cursor = Cursors.SizeAll; Deactivate(); Assert.Same(Cursors.Cross, canvas.Cursor);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void ExpressionAndMaskToolKeysUseOnlyTheirAvailableTools() => EditorUiThread.Run(() =>
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio(); using var _ = editor;
        studio.NewExpression("keys"); var expression = Assert.IsType<ExpressionDocumentViewModel>(studio.ActiveDocument);
        var expressionView = new ExpressionEditorView { DataContext = expression }; var window = Show(expressionView);
        try
        {
            foreach (var (key, expected) in new[] { (Key.E, StudioTool.Eraser), (Key.G, StudioTool.Fill), (Key.I, StudioTool.Picker), (Key.B, StudioTool.Pencil) })
            { Assert.True(SendKey(expressionView, key).Handled); Assert.Equal(expected, expression.Tool); }
            expression.Palette.Primary = 0xFFFF0000; expression.Palette.Secondary = 0xFF00FF00;
            SendKey(expressionView, Key.X); Assert.Equal(0xFF00FF00, expression.Palette.Primary); Assert.Equal(0xFFFF0000, expression.Palette.Secondary);
            studio.MakeRig(); studio.OpenItem(LibraryKind.Mask, "robin-head-left");
            var mask = Assert.IsType<MaskDocumentViewModel>(studio.ActiveDocument);
            var maskView = new MaskEditorView { DataContext = mask }; window.Content = maskView; UiSnapshotTests.Pump(20);
            foreach (var (key, expected) in new[] { (Key.E, MaskTool.Eraser), (Key.W, MaskTool.Wand), (Key.B, MaskTool.Brush) })
            { Assert.True(SendKey(maskView, key).Handled); Assert.Equal(expected, mask.Tool); }
        }
        finally { window.Close(); }
    });
}
