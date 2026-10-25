using System.Windows;
using System.Windows.Controls;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Views.Studio;

public partial class RigEditorView : UserControl
{
    public RigEditorView() => InitializeComponent();

    private RigDocumentViewModel? Rig => DataContext as RigDocumentViewModel;

    private void Canvas_PixelDown(object? sender, PixelPointerEventArgs e) => Rig?.PointerDown(e.X, e.Y);
    private void Canvas_PixelMove(object? sender, PixelPointerEventArgs e) => Rig?.PointerMove(e.X, e.Y);
    private void Canvas_PixelUp(object? sender, PixelPointerEventArgs e) => Rig?.PointerUp();
    private void Facing_Click(object sender, RoutedEventArgs e) { if (Rig is { } rig && (sender as FrameworkElement)?.Tag is string facing) rig.Facing = facing; }
    private void ZoomIn_Click(object sender, RoutedEventArgs e) { if (Rig is { } r) r.Zoom = Math.Min(48, Math.Round(r.Zoom) + 2); }
    private void ZoomOut_Click(object sender, RoutedEventArgs e) { if (Rig is { } r) r.Zoom = Math.Max(2, Math.Round(r.Zoom) - 2); }
}
