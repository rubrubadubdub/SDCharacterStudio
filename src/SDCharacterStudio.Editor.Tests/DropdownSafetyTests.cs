using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SDCharacterStudio.Editor.ViewModels;
using SDCharacterStudio.Editor.Views.Studio;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>
/// Dropdowns whose choice rebuilds the list they sit in (a recolour row, a flipbook step, a properties field): picking
/// one mustn't crash ("Can only change SelectedItems collection in multiple selection modes", the owner's crash), and
/// the mouse wheel over a closed dropdown scrolls the panel instead of changing it.
/// </summary>
public sealed class DropdownSafetyTests
{
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var deeper in Descendants<T>(child)) yield return deeper;
        }
    }

    private static (EditorHarness Editor, CharacterStudioViewModel Studio, PoseDocumentViewModel Pose, Window Window) Open()
    {
        var h = new EditorHarness();
        h.NewProject();
        h.Shell.IsCharacterStudio = true;
        var studio = h.Shell.CharacterStudio;
        studio.Settings.AdvancedPoseTools = false;
        studio.Character = studio.Characters.First(c => c.Key == "Robin");
        studio.NewPose("character", "Recolour me", "down");
        var pose = Assert.IsType<PoseDocumentViewModel>(studio.ActiveDocument);
        var window = new Window { Content = new CharacterStudioView { DataContext = studio }, Width = 1400, Height = 900, ShowActivated = false, Left = -4000 };
        window.Show(); UiSnapshotTests.Pump(150);
        return (h, studio, pose, window);
    }

    /// <summary>A real pick: the list opens, and the mouse comes up on an item.</summary>
    private static void Click(ComboBox combo, int index)
    {
        combo.IsDropDownOpen = true;
        UiSnapshotTests.Pump(150);
        var item = (ComboBoxItem)combo.ItemContainerGenerator.ContainerFromIndex(index);
        item.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
        UiSnapshotTests.Pump(150);
    }

    [Fact]
    public void PickingTheColourToRecolourDoesntCrash() => EditorUiThread.Run(() =>
    {
        var (editor, _, pose, window) = Open();
        using var _ = editor;
        try
        {
            pose.AddRecolor();
            UiSnapshotTests.Pump(150);
            var combo = Descendants<ComboBox>(window).First(c => c.DataContext is RecolorRowViewModel && c.ItemsSource is IReadOnlyList<ChoiceItem>
                && ReferenceEquals(c.ItemsSource, ((RecolorRowViewModel)c.DataContext).Sources));
            string before = pose.Recolors.Single().From!;
            Click(combo, combo.SelectedIndex == 0 ? 1 : 0);   // open the list, click another colour
            Assert.NotEqual(before, pose.Recolors.Single().From);

            var range = Descendants<ComboBox>(window).First(c => c.DataContext is RecolorRowViewModel && ReferenceEquals(c.ItemsSource, RecolorRowViewModel.Ranges));
            Click(range, 2);
            Assert.Equal(40, pose.Recolors.Single().Range);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void TheWheelOverAClosedDropdownScrollsInsteadOfChangingIt() => EditorUiThread.Run(() =>
    {
        var (editor, _, pose, window) = Open();
        using var _ = editor;
        try
        {
            pose.AddRecolor();
            UiSnapshotTests.Pump(150);
            var range = Descendants<ComboBox>(window).First(c => c.DataContext is RecolorRowViewModel && ReferenceEquals(c.ItemsSource, RecolorRowViewModel.Ranges));
            range.Focus();
            int index = range.SelectedIndex;
            // Like the input system: the preview, then the same event bubbling (handled or not).
            var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent };
            range.RaiseEvent(wheel);
            range.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = UIElement.MouseWheelEvent, Handled = wheel.Handled });
            UiSnapshotTests.Pump(100);
            Assert.Equal(index, range.SelectedIndex);
        }
        finally { window.Close(); }
    });
}
