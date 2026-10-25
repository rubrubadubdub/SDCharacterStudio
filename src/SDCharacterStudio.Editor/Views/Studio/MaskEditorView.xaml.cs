using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Views.Studio;

public partial class MaskEditorView : UserControl
{
    public MaskEditorView() => InitializeComponent();

    private MaskDocumentViewModel? Mask => DataContext as MaskDocumentViewModel;

    private void Canvas_PixelDown(object? sender, PixelPointerEventArgs e) =>
        Mask?.PointerDown(e.X, e.Y, e.Button == MouseButton.Right ? PointerButton.Secondary : PointerButton.Primary, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
    private void Canvas_PixelMove(object? sender, PixelPointerEventArgs e) => Mask?.PointerMove(e.X, e.Y);
    private void Canvas_PixelUp(object? sender, PixelPointerEventArgs e) => Mask?.PointerUp();
    private void ZoomIn_Click(object sender, RoutedEventArgs e) { if (Mask is { } m) m.Zoom = Math.Min(48, Math.Round(m.Zoom) + 2); }
    private void ZoomOut_Click(object sender, RoutedEventArgs e) { if (Mask is { } m) m.Zoom = Math.Max(2, Math.Round(m.Zoom) - 2); }
}
