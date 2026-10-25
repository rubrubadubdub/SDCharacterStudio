using System.Windows;
using System.Windows.Controls;

namespace SDCharacterStudio.Editor.Views.Studio;

/// <summary>Temporarily hides a pixel editor's side panels without discarding splitter sizes.</summary>
public static class StudioWorkspace
{
    public static readonly DependencyProperty FocusCanvasProperty = DependencyProperty.RegisterAttached(
        "FocusCanvas", typeof(bool), typeof(StudioWorkspace),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits, FocusChanged));
    public static bool GetFocusCanvas(DependencyObject element) => (bool)element.GetValue(FocusCanvasProperty);
    public static void SetFocusCanvas(DependencyObject element, bool value) => element.SetValue(FocusCanvasProperty, value);

    public static readonly DependencyProperty FocusColumnsProperty = DependencyProperty.RegisterAttached(
        "FocusColumns", typeof(string), typeof(StudioWorkspace), new PropertyMetadata(null, ColumnsChanged));
    public static string? GetFocusColumns(DependencyObject element) => (string?)element.GetValue(FocusColumnsProperty);
    public static void SetFocusColumns(DependencyObject element, string value) => element.SetValue(FocusColumnsProperty, value);

    private sealed record ColumnState(ColumnDefinition Column, GridLength Width, double Minimum);
    private sealed record PanelState(UIElement Panel, Visibility Visibility);
    private sealed record LayoutState(ColumnState[] Columns, PanelState[] Panels);
    private static readonly DependencyProperty SavedLayoutProperty = DependencyProperty.RegisterAttached(
        "SavedLayout", typeof(LayoutState), typeof(StudioWorkspace));

    private static void ColumnsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Grid grid) return;
        grid.Loaded -= GridLoaded;
        if (e.NewValue != null) grid.Loaded += GridLoaded;
        if (grid.IsLoaded) Update(grid);
    }

    private static void GridLoaded(object sender, RoutedEventArgs e) => Update((Grid)sender);
    private static void FocusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    { if (d is Grid grid && GetFocusColumns(grid) != null) Update(grid); }

    private static void Update(Grid grid)
    {
        if (!GetFocusCanvas(grid))
        {
            if (grid.GetValue(SavedLayoutProperty) is not LayoutState saved) return;
            foreach (var c in saved.Columns) { c.Column.MinWidth = c.Minimum; c.Column.Width = c.Width; }
            foreach (var p in saved.Panels) p.Panel.SetCurrentValue(UIElement.VisibilityProperty, p.Visibility);
            grid.ClearValue(SavedLayoutProperty);
            return;
        }
        if (grid.GetValue(SavedLayoutProperty) != null || !grid.IsLoaded) return;
        var columns = (GetFocusColumns(grid) ?? "").Split(',').Select(s => int.TryParse(s, out int i) ? i : -1)
            .Where(i => i >= 0 && i < grid.ColumnDefinitions.Count).ToHashSet();
        var state = new LayoutState(
            columns.Select(i => grid.ColumnDefinitions[i]).Select(c => new ColumnState(c, c.Width, c.MinWidth)).ToArray(),
            grid.Children.Cast<UIElement>().Where(p => columns.Contains(Grid.GetColumn(p)))
                .Select(p => new PanelState(p, p.Visibility)).ToArray());
        grid.SetValue(SavedLayoutProperty, state);
        foreach (var p in state.Panels) p.Panel.SetCurrentValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        foreach (var c in state.Columns) { c.Column.MinWidth = 0; c.Column.Width = new GridLength(0); }
    }
}
