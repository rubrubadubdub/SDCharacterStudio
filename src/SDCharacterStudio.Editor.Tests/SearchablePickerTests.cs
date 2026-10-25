using System.Collections.ObjectModel;
using System.Windows.Data;
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
