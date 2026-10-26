using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SDCharacterStudio.Editor.Infrastructure;

/// <summary>
/// The mouse wheel over a closed dropdown scrolls the panel it's in, as in a browser. WPF changes the dropdown's choice
/// instead (when it has focus), so scrolling a panel past a recolour or a field quietly changed it.
/// </summary>
public static class DropdownWheel
{
    private static bool registered;

    public static void Register()
    {
        if (registered) return;
        registered = true;
        EventManager.RegisterClassHandler(typeof(ComboBox), UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnPreviewMouseWheel));
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ComboBox combo || combo.IsDropDownOpen || e.Handled) return;
        e.Handled = true;
        if (VisualTreeHelper.GetParent(combo) is UIElement parent)
            parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = combo });
    }
}
