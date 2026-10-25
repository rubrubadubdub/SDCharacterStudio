using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Views.Studio;

public partial class ExpressionEditorView : UserControl
{
    public ExpressionEditorView() => InitializeComponent();

    private ExpressionDocumentViewModel? Expression => DataContext as ExpressionDocumentViewModel;

    private void Canvas_PixelDown(object? sender, PixelPointerEventArgs e) =>
        Expression?.PointerDown(e.X, e.Y, e.Button == MouseButton.Right ? PointerButton.Secondary : PointerButton.Primary, Keyboard.Modifiers.HasFlag(ModifierKeys.Alt));
    private void Canvas_PixelMove(object? sender, PixelPointerEventArgs e) => Expression?.PointerMove(e.X, e.Y);
    private void Canvas_PixelUp(object? sender, PixelPointerEventArgs e) => Expression?.PointerUp();
    private void Frames_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Expression is { } x) x.PickedFrames = FramesList.SelectedItems.Cast<ExpressionFrameItem>().Select(f => f.Index).ToList();
    }
    private void Frames_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && Expression is { } x) { x.DeleteFrameCommand.Execute(null); e.Handled = true; }
    }
    private void ZoomIn_Click(object sender, RoutedEventArgs e) { if (Expression is { } x) x.Zoom = Math.Min(48, Math.Round(x.Zoom) + 2); }
    private void ZoomOut_Click(object sender, RoutedEventArgs e) { if (Expression is { } x) x.Zoom = Math.Max(2, Math.Round(x.Zoom) - 2); }
}
