using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace SDCharacterStudio.Editor.Views.Studio;

/// <summary>Shared pixel editor navigation: integer cursor anchored zoom and safe middle/Space panning.</summary>
internal sealed class CanvasNavigation
{
    private readonly ScrollViewer scroller;
    private readonly PixelCanvas canvas;
    private readonly Func<double> getZoom;
    private readonly Action<double> setZoom;
    private readonly Func<int> width;
    private readonly Func<int> height;
    private readonly Action cancel;
    private Point? panFrom;
    private MouseButton panButton;
    private Point panOffset;

    public CanvasNavigation(ScrollViewer scroller, PixelCanvas canvas, Func<double> getZoom, Action<double> setZoom, Func<int> width, Func<int> height, Action? cancel = null)
    { this.scroller = scroller; this.canvas = canvas; this.getZoom = getZoom; this.setZoom = setZoom; this.width = width; this.height = height; this.cancel = cancel ?? (() => { }); }

    public bool IsPanning => panFrom.HasValue;
    public void Wheel(MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) { scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset - e.Delta); e.Handled = true; return; }
        ZoomAt(e.GetPosition(scroller), e.Delta > 0 ? 1 : -1);
        e.Handled = true;
    }
    internal void ZoomAt(Point at, int delta)
    {
        var frame = canvas.ToFramePoint(scroller.TranslatePoint(at, canvas));
        setZoom(Math.Clamp(Math.Round(getZoom()) + delta, 1, 64));
        scroller.UpdateLayout();
        // Translate back through the content presenter: its border/padding inset and the
        // existing scroll offset are part of the viewport coordinate system.
        var after = canvas.TranslatePoint(canvas.FromFramePoint(frame), scroller);
        scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset + after.X - at.X);
        scroller.ScrollToVerticalOffset(scroller.VerticalOffset + after.Y - at.Y);
    }
    public void Fit()
    {
        if (width() <= 0 || height() <= 0) return;
        double room = Math.Min((scroller.ViewportWidth - 56) / width(), (scroller.ViewportHeight - 56) / height());
        setZoom(Math.Clamp(Math.Floor(room), 1, 64));
        scroller.UpdateLayout();
        scroller.ScrollToHorizontalOffset(Math.Max(0, (scroller.ExtentWidth - scroller.ViewportWidth) / 2));
        scroller.ScrollToVerticalOffset(Math.Max(0, (scroller.ExtentHeight - scroller.ViewportHeight) / 2));
    }
    public bool BeginPan(MouseButtonEventArgs e, bool spaceHeld)
    {
        if (e.ChangedButton != MouseButton.Middle && !(e.ChangedButton == MouseButton.Left && spaceHeld)) return false;
        canvas.CancelGesture();
        cancel();
        Reset();
        // Consume the attempted pan even when capture fails, so it cannot start painting.
        e.Handled = true;
        if (!scroller.CaptureMouse()) return false;
        panButton = e.ChangedButton; panFrom = e.GetPosition(scroller); panOffset = new Point(scroller.HorizontalOffset, scroller.VerticalOffset);
        canvas.Cursor = Cursors.SizeAll;
        return true;
    }
    public bool MovePan(MouseEventArgs e)
    {
        if (panFrom is not { } from) return false;
        var now = e.GetPosition(scroller); scroller.ScrollToHorizontalOffset(panOffset.X - now.X + from.X); scroller.ScrollToVerticalOffset(panOffset.Y - now.Y + from.Y); e.Handled = true; return true;
    }
    public bool EndPan(MouseButtonEventArgs e)
    {
        if (!panFrom.HasValue || e.ChangedButton != panButton) return false;
        Reset(); e.Handled = true; return true;
    }
    public void Reset() { panFrom = null; canvas.Cursor = Cursors.Cross; if (Mouse.Captured == scroller) scroller.ReleaseMouseCapture(); }

    internal static bool IsInputControl(KeyEventArgs e) => IsInputControl(e.OriginalSource as DependencyObject) || IsInputControl(Keyboard.FocusedElement as DependencyObject);
    private static bool IsInputControl(DependencyObject? node)
    {
        for (; node != null; node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (node is TextBoxBase or ComboBox or ComboBoxItem or ButtonBase) return true;
        return false;
    }
}
