using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Views;

/// <summary>
/// A drop-down for long lists of <see cref="ChoiceItem"/>s (534 locations in a modded game): a search box that matches
/// every word anywhere in the name, the mod or the value, a list with a visible scrollbar, and keyboard picking. Bind
/// either <see cref="SelectedItem"/> or <see cref="SelectedValue"/>; both stay in step.
/// </summary>
public partial class SearchablePicker : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(SearchablePicker),
        new PropertyMetadata(null, (d, _) => ((SearchablePicker)d).OnItemsChanged()));
    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(nameof(SelectedItem), typeof(object), typeof(SearchablePicker),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((SearchablePicker)d).OnSelectedItemChanged()));
    public static readonly DependencyProperty SelectedValueProperty = DependencyProperty.Register(nameof(SelectedValue), typeof(string), typeof(SearchablePicker),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((SearchablePicker)d).OnSelectedValueChanged()));
    /// <summary>What the list holds, for the search hint ("Search 534 locations…").</summary>
    public static readonly DependencyProperty NounProperty = DependencyProperty.Register(nameof(Noun), typeof(string), typeof(SearchablePicker), new PropertyMetadata("items"));

    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public object? SelectedItem { get => GetValue(SelectedItemProperty); set => SetValue(SelectedItemProperty, value); }
    public string? SelectedValue { get => (string?)GetValue(SelectedValueProperty); set => SetValue(SelectedValueProperty, value); }
    public string Noun { get => (string)GetValue(NounProperty); set => SetValue(NounProperty, value); }

    private ListCollectionView? view;

    public SearchablePicker()
    {
        InitializeComponent();
        Toggle.PreviewKeyDown += (_, e) => { if (e.Key is Key.Down or Key.Enter or Key.Space or Key.F4 && !Popup.IsOpen) { Popup.IsOpen = true; e.Handled = true; } };
    }

    public bool IsOpen { get => Popup.IsOpen; set => Popup.IsOpen = value; }
    /// <summary>The items the search shows now.</summary>
    public IEnumerable<object> Shown => view?.Cast<object>() ?? Enumerable.Empty<object>();
    public string Query { get => Search.Text; set => Search.Text = value; }

    /// <summary>Every word of the query appears in the item's name, mod (detail) or value; case and underscores don't matter.</summary>
    public static bool Matches(object? item, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        string text = item is ChoiceItem c ? $"{c.Label} {c.Detail} {c.Value}" : item?.ToString() ?? "";
        text = text.Replace('_', ' ');
        return query.Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .All(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Pick the highlighted item (or the only match) and close.</summary>
    public void PickHighlighted()
    {
        var item = List.SelectedItem ?? (view is { Count: 1 } ? view.GetItemAt(0) : null);
        if (item != null) Pick(item);
    }

    private void Pick(object item)
    {
        SelectedItem = item;
        Popup.IsOpen = false;
        Toggle.Focus();
    }

    private System.Collections.Specialized.INotifyCollectionChanged? observed;

    private void OnItemsChanged()
    {
        if (observed != null) observed.CollectionChanged -= OnCollectionChanged;
        observed = ItemsSource as System.Collections.Specialized.INotifyCollectionChanged;
        if (observed != null) observed.CollectionChanged += OnCollectionChanged;
        // A view of our own, so filtering here never filters another control bound to the same list.
        view = ItemsSource is IList list ? new ListCollectionView(list) { Filter = item => Matches(item, Search.Text) } : null;
        List.ItemsSource = view;
        if (SelectedItem == null && SelectedValue != null) OnSelectedValueChanged(); else ShowSelection();
        UpdateCount();
    }

    private void OnCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (SelectedItem == null && SelectedValue != null) OnSelectedValueChanged();
        UpdateCount();
    }

    private void OnSelectedItemChanged()
    {
        string? value = (SelectedItem as ChoiceItem)?.Value ?? SelectedItem?.ToString();
        if (SelectedItem != null && !string.Equals(SelectedValue, value, StringComparison.Ordinal)) SelectedValue = value;
        ShowSelection();
    }

    private void OnSelectedValueChanged()
    {
        var items = ItemsSource?.Cast<object>();
        var match = items?.FirstOrDefault(i => string.Equals((i as ChoiceItem)?.Value ?? i.ToString(), SelectedValue, StringComparison.Ordinal));
        if (match != null && !Equals(match, SelectedItem)) SelectedItem = match;
        ShowSelection();
    }

    private void ShowSelection()
    {
        var item = SelectedItem;
        SelectedLabel.Text = item is ChoiceItem c ? c.Label : item?.ToString() ?? SelectedValue ?? "";
        SelectedDetail.Text = item is ChoiceItem { Detail: { Length: > 0 } detail } ? "  ·  " + detail : "";
    }

    private void UpdateCount()
    {
        int total = (ItemsSource as ICollection)?.Count ?? 0, shown = view?.Count ?? 0;
        Search.PlaceholderText = $"Search {total} {Noun}…";
        Count.Text = shown == 0 ? "No match. Try part of the name, or the mod's name." : shown == total ? $"{total} {Noun}" : $"{shown} of {total} {Noun}";
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        Search.Text = "";
        view?.Refresh();
        List.SelectedItem = SelectedItem;
        if (SelectedItem != null) List.ScrollIntoView(SelectedItem);
        UpdateCount();
        Dispatcher.BeginInvoke(() => { Search.Focus(); Keyboard.Focus(Search); }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void OnClosed(object? sender, EventArgs e) => Search.Text = "";

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (view == null) return;
        view.Refresh();
        if (view.Count > 0 && (List.SelectedItem == null || !view.Contains(List.SelectedItem))) List.SelectedIndex = 0;
        if (List.SelectedItem != null) List.ScrollIntoView(List.SelectedItem);
        UpdateCount();
    }

    private void OnSearchKey(object sender, KeyEventArgs e)
    {
        if (view == null) return;
        switch (e.Key)
        {
            case Key.Down: Move(1); break;
            case Key.Up: Move(-1); break;
            case Key.PageDown: Move(10); break;
            case Key.PageUp: Move(-10); break;
            case Key.Enter: PickHighlighted(); break;
            case Key.Escape: Popup.IsOpen = false; Toggle.Focus(); break;
            default: return;
        }
        e.Handled = true;
    }

    private void Move(int by)
    {
        if (view == null || view.Count == 0) return;
        List.SelectedIndex = Math.Clamp(List.SelectedIndex + by, 0, view.Count - 1);
        List.ScrollIntoView(List.SelectedItem);
    }

    private void OnListClick(object sender, MouseButtonEventArgs e)
    {
        // Only a click on an item picks (not the scrollbar).
        for (var current = e.OriginalSource as DependencyObject; current != null && current != List;
             current = current is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
            if (current is ListBoxItem { DataContext: { } item }) { Pick(item); e.Handled = true; return; }
    }
}
