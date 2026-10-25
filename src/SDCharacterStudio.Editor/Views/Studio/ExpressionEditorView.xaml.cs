using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Views.Studio;

public partial class ExpressionEditorView : UserControl
{
    private CanvasNavigation? navigation;
    private bool spaceHeld;
    private Window? ownerWindow;
    private bool changingDocument;
    public ExpressionEditorView()
    {
        InitializeComponent();
        Canvas.PixelCancel += Canvas_PixelCancel;
        DataContextChanged += (_, e) =>
        {
            changingDocument = true;
            try { if (e.OldValue is ExpressionDocumentViewModel old) old.CancelStroke(); ResetNavigation(); }
            finally { changingDocument = false; }
        };
        Loaded += (_, _) =>
        {
            navigation = new(CanvasScroller, Canvas, () => Expression?.Zoom ?? 8, z => { if (Expression != null) Expression.Zoom = z; }, () => Expression?.Width ?? 0, () => Expression?.Height ?? 0, () => Expression?.CancelStroke());
            AttachWindow(Window.GetWindow(this));
        };
        Unloaded += (_, _) => { ResetNavigation(); AttachWindow(null); };
        LostKeyboardFocus += (_, _) => { if (!Canvas.IsKeyboardFocused) ResetNavigation(); };
    }

    private void AttachWindow(Window? window)
    {
        if (ownerWindow == window) return;
        if (ownerWindow != null) ownerWindow.Deactivated -= Window_Deactivated;
        ownerWindow = window;
        if (ownerWindow != null) ownerWindow.Deactivated += Window_Deactivated;
    }
    private void ResetNavigation() { spaceHeld = false; navigation?.Reset(); Canvas.Cursor = Cursors.Cross; Canvas.CancelGesture(); if (!changingDocument) Expression?.CancelStroke(); }

    private ExpressionDocumentViewModel? Expression => DataContext as ExpressionDocumentViewModel;
    private void Canvas_PixelCancel(object? sender, PixelPointerEventArgs e) => ResetNavigation();
    private void Window_Deactivated(object? sender, EventArgs e) => ResetNavigation();

    private void Canvas_PixelDown(object? sender, PixelPointerEventArgs e) =>
        Expression?.PointerDown(e.X, e.Y, e.Button == MouseButton.Right ? PointerButton.Secondary : PointerButton.Primary, Keyboard.Modifiers.HasFlag(ModifierKeys.Alt));
    private void Canvas_PixelMove(object? sender, PixelPointerEventArgs e) => Expression?.PointerMove(e.X, e.Y);
    private void Canvas_PixelUp(object? sender, PixelPointerEventArgs e) => Expression?.PointerUp();
    private void Canvas_MouseWheel(object sender, MouseWheelEventArgs e) => navigation?.Wheel(e);
    private void Scroller_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        bool held = spaceHeld;
        if (e.ChangedButton != MouseButton.Middle && !(e.ChangedButton == MouseButton.Left && held)) return;
        navigation?.BeginPan(e, held);
        spaceHeld = held;
    }
    private void Scroller_PreviewMouseMove(object sender, MouseEventArgs e) => navigation?.MovePan(e);
    private void Scroller_PreviewMouseUp(object sender, MouseButtonEventArgs e) { if (navigation?.EndPan(e) == true && spaceHeld) Canvas.Cursor = Cursors.SizeAll; }
    private void Scroller_LostMouseCapture(object sender, MouseEventArgs e) { if (e.OriginalSource == CanvasScroller && navigation?.IsPanning == true) ResetNavigation(); }
    private void Fit_Click(object sender, RoutedEventArgs e) => navigation?.Fit();
    private void View_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            if (CanvasNavigation.IsInputControl(e) && !Canvas.IsMouseCaptured && navigation?.IsPanning != true) return;
            ResetNavigation(); e.Handled = true; return;
        }
        if (CanvasNavigation.IsInputControl(e)) return;
        if (key == Key.Space)
        {
            if (!e.IsRepeat) { spaceHeld = true; Canvas.Cursor = Cursors.SizeAll; }
            e.Handled = true; return;
        }
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && key is Key.D0 or Key.NumPad0) { navigation?.Fit(); e.Handled = true; return; }
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && key is Key.OemPlus or Key.Add or Key.OemMinus or Key.Subtract)
        {
            navigation?.ZoomAt(new Point(CanvasScroller.ViewportWidth / 2, CanvasScroller.ViewportHeight / 2), key is Key.OemPlus or Key.Add ? 1 : -1);
            e.Handled = true; return;
        }
        if (Keyboard.Modifiers != ModifierKeys.None || Expression is not { } expression) return;
        switch (key)
        {
            case Key.B: expression.Tool = StudioTool.Pencil; break;
            case Key.E: expression.Tool = StudioTool.Eraser; break;
            case Key.G: expression.Tool = StudioTool.Fill; break;
            case Key.I: expression.Tool = StudioTool.Picker; break;
            case Key.X: expression.Palette.SwapCommand.Execute(null); break;
            default: return;
        }
        e.Handled = true;
    }
    private void View_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || !spaceHeld) return;
        spaceHeld = false; Canvas.Cursor = Cursors.Cross; e.Handled = true;
    }
    private void Frames_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Expression is { } x) x.PickedFrames = FramesList.SelectedItems.Cast<ExpressionFrameItem>().Select(f => f.Index).ToList();
    }
    private void Frames_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && Expression is { } x) { x.DeleteFrameCommand.Execute(null); e.Handled = true; }
    }
    private void ZoomIn_Click(object sender, RoutedEventArgs e) { if (Expression is { } x) x.Zoom = Math.Min(64, Math.Round(x.Zoom) + 1); }
    private void ZoomOut_Click(object sender, RoutedEventArgs e) { if (Expression is { } x) x.Zoom = Math.Max(1, Math.Round(x.Zoom) - 1); }
}
