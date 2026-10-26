using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using SDCharacterStudio.Editor.ViewModels;
using SDCharacterStudio.Editor.Views;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>Long lists (534 locations in the owner's game) are searched, not scrolled through.</summary>
public sealed class SearchablePickerTests
{
    private static ObservableCollection<ChoiceItem> Locations()
    {
        var items = new ObservableCollection<ChoiceItem>
        {
            new("Town", "Town"), new("ScienceHouse", "ScienceHouse"),
            new("Custom_SakuraVineyard", "Custom_SakuraVineyard", "Stardew Valley Expanded"),
            new("Custom_SakuraVineyardCellar", "Custom_SakuraVineyardCellar", "Stardew Valley Expanded"),
            new("Custom_Ridgeside_SummitFarm", "Custom_Ridgeside_SummitFarm", "Ridgeside Village (Content Patcher component)"),
        };
        for (int i = 0; i < 529; i++) items.Add(new($"Custom_Place{i:000}", $"Custom_Place{i:000}", "Some Mod"));
        return items;
    }

    [Theory]
    [InlineData("sakura cellar", "Custom_SakuraVineyardCellar", true)]   // words anywhere, in any order
    [InlineData("cellar sakura", "Custom_SakuraVineyardCellar", true)]
    [InlineData("ridgeside", "Custom_SakuraVineyardCellar", false)]
    [InlineData("expanded", "Custom_SakuraVineyardCellar", true)]       // the mod's name finds its places
    [InlineData("summit_farm", "Custom_Ridgeside_SummitFarm", true)]    // underscores are spaces; each word is found inside SummitFarm
    [InlineData("summitfarm", "Custom_Ridgeside_SummitFarm", true)]
    public void SearchMatchesEveryWordAnywhere(string query, string id, bool expected)
    {
        var item = Locations().Single(i => i.Value == id);
        Assert.Equal(expected, SearchablePicker.Matches(item, query));
    }

    [Fact]
    public void TypingNarrowsTheListAndEnterPicks() => EditorUiThread.Run(() =>
    {
        var holder = new Holder { Location = "Town" };
        var picker = new SearchablePicker { ItemsSource = Locations(), Noun = "locations", DataContext = holder };
        picker.SetBinding(SearchablePicker.SelectedValueProperty, new Binding(nameof(Holder.Location)) { Mode = BindingMode.TwoWay });
        Assert.Equal("Town", (picker.SelectedItem as ChoiceItem)?.Value);   // a bound value shows its item
        Assert.Equal(534, picker.Shown.Count());

        picker.Query = "sakura";
        Assert.Equal(2, picker.Shown.Count());
        picker.Query = "sakura cellar";
        Assert.Single(picker.Shown);
        picker.PickHighlighted();
        Assert.Equal("Custom_SakuraVineyardCellar", holder.Location);   // the binding got the pick

        picker.Query = "nothing like this";
        Assert.Empty(picker.Shown);
        picker.PickHighlighted();                                       // no match: nothing changes
        Assert.Equal("Custom_SakuraVineyardCellar", holder.Location);
    });

    [Fact]
    public void ItBindsTheItemToo() => EditorUiThread.Run(() =>
    {
        var items = Locations();
        var picker = new SearchablePicker { ItemsSource = items, SelectedItem = items[1] };
        Assert.Equal("ScienceHouse", picker.SelectedValue);
        picker.SelectedValue = "Town";
        Assert.Same(items[0], picker.SelectedItem);
    });

    [Fact]
    public void LongListPopupKeepsItsScrollViewerBoundedAndShowsScrollbar() => EditorUiThread.Run(() =>
    {
        var picker = new SearchablePicker { ItemsSource = Locations(), Noun = "locations", Width = 420 };
        var window = new Window { Content = picker, Width = 460, Height = 120, ShowActivated = false,
            WindowStyle = WindowStyle.None, Left = SystemParameters.VirtualScreenLeft - 4000, Top = 0 };
        try
        {
            window.Show();
            picker.IsOpen = true;
            EditorUiThread.PumpUntil(() => picker.IsOpen && picker.ActualWidth > 0);
            var list = (ListBox)picker.FindName("List")!;
            EditorUiThread.PumpUntil(() => FindDescendant<ScrollViewer>(list)?.ActualHeight > 0);

            var scrollViewer = FindDescendant<ScrollViewer>(list);
            Assert.NotNull(scrollViewer);
            Assert.True(scrollViewer!.ScrollableHeight > 0);
            Assert.True(scrollViewer.ActualHeight <= 380);
            Assert.Equal(Visibility.Visible, scrollViewer.ComputedVerticalScrollBarVisibility);
            Assert.NotNull(FindDescendant<ScrollBar>(scrollViewer));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void ThemeBoundsRegularComboBoxDropDown()
    {
        EditorUiThread.Run(() =>
        {
            var combo = new ComboBox();
            var resources = new ResourceDictionary { Source = new Uri("/SDCharacterStudio.Editor;component/Themes/Studio.xaml", UriKind.Relative) };
            combo.Resources.MergedDictionaries.Add(resources);
            combo.ApplyTemplate();
            Assert.True(combo.MaxDropDownHeight <= 380);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThemedEditablePopupHasVisibleThumbAndCanScroll(bool docked) => EditorUiThread.Run(() =>
    {
        using var editor = new EditorHarness(); editor.NewProject(); editor.Shell.Settings.WindowMaximized = false;
        var combo = new ComboBox { IsEditable = true, Width = 420, ItemsSource = Enumerable.Range(0, 200).Select(i => "Music cue " + i).ToArray() };
        Window window;
        if (docked)
        {
            var main = new MainWindow { ShowActivated = false, WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.None, WindowStartupLocation = WindowStartupLocation.Manual };
            main.Attach(editor.Shell, new Infrastructure.WpfUserInterface(main));
            var field = editor.Field<ChoiceField>("Music");
            foreach (int i in Enumerable.Range(0, 200)) field.Options.Add(new("cue" + i, "Music cue " + i));
            window = main;
        }
        else window = new Window { Content = combo, Width = 460, Height = 100, ShowActivated = false, WindowStyle = WindowStyle.None };
        window.Left = SystemParameters.VirtualScreenLeft - 4000; window.Top = 0;
        bool rendered = false;
        window.ContentRendered += (_, _) => rendered = true;
        try
        {
            window.Show(); window.UpdateLayout();
            EditorUiThread.PumpUntil(() => rendered);
            if (docked) combo = Descendants(window).OfType<ComboBox>().Single(c => c.DataContext is ChoiceField { Label: "Music" });
            combo.ApplyTemplate(); combo.IsDropDownOpen = true;
            EditorUiThread.PumpUntil(() => FindDescendant<Popup>(combo)?.Child is FrameworkElement { ActualHeight: > 0 });
            var popup = FindDescendant<Popup>(combo)!;
            var viewer = FindDescendant<ScrollViewer>(popup.Child)!;
            Assert.NotNull(viewer);
            if (Environment.GetEnvironmentVariable("SDCS_UI_SNAPSHOTS") is { Length: > 0 } diagnostics)
                System.IO.File.WriteAllText(System.IO.Path.Combine(diagnostics, "popup-viewer.xaml"), System.Windows.Markup.XamlWriter.Save(viewer.Template));
            Assert.True(viewer.ScrollableHeight > 0);
            Assert.True(((FrameworkElement)popup.Child).ActualHeight <= 420);
            var bar = FindDescendant<ScrollBar>(viewer)!;
            Assert.NotNull(bar);
            Assert.True(bar.IsVisible && bar.ActualWidth >= 10);
            var thumb = FindDescendant<Thumb>(bar)!;
            Assert.NotNull(thumb);
            Assert.True(thumb.IsVisible && thumb.Opacity > 0 && thumb.ActualWidth > 0);
            for (DependencyObject? node = thumb; node != null; node = VisualTreeHelper.GetParent(node))
                if (node is UIElement element) Assert.True(element.Opacity > 0, node.GetType().FullName + " hides the scrollbar");
            viewer.ScrollToEnd(); viewer.UpdateLayout();
            EditorUiThread.PumpUntil(() => viewer.VerticalOffset > 0);
            if (Environment.GetEnvironmentVariable("SDCS_UI_SNAPSHOTS") is { Length: > 0 } output)
            {
                var child = (FrameworkElement)popup.Child;
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(child.ActualWidth), (int)Math.Ceiling(child.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(child);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using var file = System.IO.File.Create(System.IO.Path.Combine(output, docked ? "music-dropdown-docked.png" : "music-dropdown.png")); encoder.Save(file);
            }
        }
        finally { combo.IsDropDownOpen = false; window.Close(); }
    });

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } nested) return nested;
        }
        return null;
    }

    /// <summary>The open picker, rendered for review (only with SDCS_UI_SNAPSHOTS set to a folder).</summary>
    [Fact]
    public void RenderOpenPicker()
    {
        if (Environment.GetEnvironmentVariable("SDCS_UI_SNAPSHOTS") is not { Length: > 0 } output) return;
        EditorUiThread.Run(() =>
        {
            var picker = new SearchablePicker { ItemsSource = Locations(), Noun = "locations", SelectedValue = "Custom_SakuraVineyard", Width = 420 };
            var window = new System.Windows.Window { Content = picker, Width = 460, Height = 120, ShowActivated = false, WindowStyle = System.Windows.WindowStyle.None,
                Left = System.Windows.SystemParameters.VirtualScreenLeft - 4000, Top = 0, Background = System.Windows.Media.Brushes.Black };
            window.Show();
            foreach (var (name, query) in new[] { ("30-picker-open", ""), ("31-picker-search", "sakura") })
            {
                picker.IsOpen = true;
                EditorUiThread.PumpUntil(() => picker.IsOpen);
                picker.Query = query;
                var until = DateTime.UtcNow.AddMilliseconds(400);
                EditorUiThread.PumpUntil(() => DateTime.UtcNow >= until);
                var popup = (System.Windows.Controls.Primitives.Popup)picker.FindName("Popup");
                var child = (System.Windows.FrameworkElement)popup.Child;
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)child.ActualWidth, (int)child.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(child);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using (var file = System.IO.File.Create(System.IO.Path.Combine(output, name + ".png"))) encoder.Save(file);
                picker.IsOpen = false;
            }
            window.Close();
        });
    }

    private sealed class Holder : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        private string? location;
        public string? Location { get => location; set => SetProperty(ref location, value); }
    }
}
