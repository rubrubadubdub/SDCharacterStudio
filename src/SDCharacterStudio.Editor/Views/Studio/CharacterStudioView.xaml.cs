using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SDCharacterStudio.Editor.Views.Studio;

public partial class CharacterStudioView : UserControl
{
    public CharacterStudioView() => InitializeComponent();

    /// <summary>Library lists don't scroll by themselves: pass the wheel to the panel's scroller.</summary>
    private void List_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled) return;
        e.Handled = true;
        DependencyObject? parent = sender as DependencyObject;
        while (parent != null && parent is not ScrollViewer) parent = VisualTreeHelper.GetParent(parent);
        // The list's own (disabled) scroller comes first; the panel's is the next one up.
        parent = parent == null ? null : VisualTreeHelper.GetParent(parent);
        while (parent != null && parent is not ScrollViewer) parent = VisualTreeHelper.GetParent(parent);
        (parent as UIElement)?.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = MouseWheelEvent, Source = sender });
    }
}
