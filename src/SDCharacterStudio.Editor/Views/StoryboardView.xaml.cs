using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using SDCharacterStudio.Editor.Storyboard;
using SDCharacterStudio.Editor.ViewModels;
using Wpf.Ui.Controls;
using MenuItem = System.Windows.Controls.MenuItem;

namespace SDCharacterStudio.Editor.Views;

/// <summary>Storyboard interactions (doc 07 §7.1): palette click/drag, card select/drag-reorder, insert menus and keys.</summary>
public partial class StoryboardView : UserControl
{
    public const string BeatFormat = "SDCS.Beat";
    public const string BeatKindFormat = "SDCS.BeatKind";

    private Point pressPoint;
    private object? pressed;
    private bool dragged;
    private InsertionAdorner? adorner;

    private int? connectionGeometry;
    public StoryboardView()
    {
        InitializeComponent();
        // Scenes grow sideways: the wheel scrolls the beats when there's nothing to scroll up and down (Shift always).
        LaneScroller.PreviewMouseWheel += (_, e) =>
        {
            if (LaneScroller.ScrollableWidth <= 0 || (LaneScroller.ScrollableHeight > 0 && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)) return;
            LaneScroller.ScrollToHorizontalOffset(LaneScroller.HorizontalOffset - e.Delta);
            e.Handled = true;
        };
        LayoutUpdated += (_, _) => DrawLaneConnections();
    }

    private void DrawLaneConnections()
    {
        if (!IsVisible || Board == null || Board.GraphMode) return;
        var cards = Descendants(LaneCanvasRoot).Where(e => Equals(e.Tag, "card") && e.DataContext is BeatCardViewModel).ToArray();
        var headers = Descendants(LaneCanvasRoot).Where(e => Equals(e.Tag, "lane-header") && e.DataContext is LaneViewModel)
            .GroupBy(e => ((LaneViewModel)e.DataContext).Name).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var hash = new HashCode(); hash.Add(Board.Graph);
        foreach (var element in cards.Concat(headers.Values))
        { hash.Add(element.TranslatePoint(new Point(), LaneCanvasRoot)); hash.Add(element.ActualWidth); hash.Add(element.ActualHeight); }
        int geometry = hash.ToHashCode();
        if (connectionGeometry == geometry) return;
        connectionGeometry = geometry;
        LaneConnections.Children.Clear();
        foreach (var source in cards)
        {
            var card = (BeatCardViewModel)source.DataContext;
            var node = Board.Graph.Find(card.Lane.Name, card.Index);
            if (node == null) continue;
            int parallel = 0;
            foreach (var edge in Board.Graph.Edges.Where(e => e.From == node.Id && e.Kind is GraphEdgeKind.Choice or GraphEdgeKind.Goto && !e.Missing))
            {
                var targetNode = Board.Graph.Nodes.FirstOrDefault(n => n.Id == edge.To);
                if (targetNode == null || !headers.TryGetValue(targetNode.Lane, out var target)) continue;
                Point start = source.TranslatePoint(new Point(source.ActualWidth - 3, source.ActualHeight - 6 - parallel++ * 6), LaneCanvasRoot);
                Point end = target.TranslatePoint(new Point(target.ActualWidth, Math.Min(target.ActualHeight - 6, 22 + parallel * 5)), LaneCanvasRoot);
                BranchGraphView.DrawConnector(LaneConnections, start, new Point(start.X + 35 + parallel * 8, start.Y), new Point(end.X + 35, end.Y), end,
                    edge.Kind == GraphEdgeKind.Choice ? Brushes.MediumPurple : Brushes.Orange);
            }
        }
    }

    private static IEnumerable<FrameworkElement> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement element) yield return element;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private StoryboardViewModel? Board => DataContext as StoryboardViewModel;

    // ---------- palette ----------

    private void Palette_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        pressed = (sender as FrameworkElement)?.DataContext as PaletteEntry;
        pressPoint = e.GetPosition(this);
        dragged = false;
        e.Handled = true;
    }

    private void Palette_MouseMove(object sender, MouseEventArgs e)
    {
        if (pressed is not PaletteEntry entry || e.LeftButton != MouseButtonState.Pressed || !Moved(e)) return;
        dragged = true; pressed = null;
        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(BeatKindFormat, entry), DragDropEffects.Copy);
        ClearAdorner();
    }

    private void Palette_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!dragged && pressed is PaletteEntry entry && ReferenceEquals((sender as FrameworkElement)?.DataContext, entry))
        {
            Board?.AddFromPaletteCommand.Execute(entry);
            LaneScroller.Focus();
        }
        pressed = null;
    }

    private bool Moved(MouseEventArgs e)
    {
        var delta = e.GetPosition(this) - pressPoint;
        return Math.Abs(delta.X) >= SystemParameters.MinimumHorizontalDragDistance || Math.Abs(delta.Y) >= SystemParameters.MinimumVerticalDragDistance;
    }

    // ---------- cards ----------

    private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not BeatCardViewModel card) return;
        LaneScroller.Focus();
        card.SelectCommand.Execute(null);
        pressed = card; pressPoint = e.GetPosition(this); dragged = false;
        e.Handled = true;
    }

    private void Card_MouseMove(object sender, MouseEventArgs e)
    {
        if (pressed is not BeatCardViewModel card || e.LeftButton != MouseButtonState.Pressed || !Moved(e)) return;
        dragged = true; pressed = null;
        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(BeatFormat, card), DragDropEffects.Move);
        ClearAdorner();
    }

    private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => pressed = null;

    private static object? Dragged(DragEventArgs e) =>
        e.Data.GetDataPresent(BeatFormat) ? e.Data.GetData(BeatFormat) : e.Data.GetDataPresent(BeatKindFormat) ? e.Data.GetData(BeatKindFormat)
        : e.Data.GetDataPresent(AssetBrowserView.AssetFormat) && e.Data.GetData(AssetBrowserView.AssetFormat) is AssetItemViewModel { Kind: AssetKind.Animation } animation ? animation : null;

    private void Card_DragOver(object sender, DragEventArgs e)
    {
        var payload = Dragged(e);
        if (payload == null || sender is not FrameworkElement element) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
        bool after = e.GetPosition(element).X > element.ActualWidth / 2;
        ShowAdorner(element, after);
        e.Effects = payload is BeatCardViewModel ? DragDropEffects.Move : DragDropEffects.Copy;
        e.Handled = true;
    }

    private void Card_DragLeave(object sender, DragEventArgs e) => ClearAdorner();

    private void Card_Drop(object sender, DragEventArgs e)
    {
        ClearAdorner();
        if (sender is not FrameworkElement { DataContext: BeatCardViewModel target } element || Board is not { } board) return;
        int index = target.Index + (e.GetPosition(element).X > element.ActualWidth / 2 ? 1 : 0);
        DropAt(board, Dragged(e), target.Lane.Name, index);
        e.Handled = true;
    }

    private void Lane_DragOver(object sender, DragEventArgs e)
    {
        var payload = Dragged(e);
        e.Effects = payload == null ? DragDropEffects.None : payload is BeatCardViewModel ? DragDropEffects.Move : DragDropEffects.Copy;
        e.Handled = true;
    }

    private void Lane_DragLeave(object sender, DragEventArgs e) => ClearAdorner();

    private void Lane_Drop(object sender, DragEventArgs e)
    {
        ClearAdorner();
        if ((sender as FrameworkElement)?.DataContext is not LaneViewModel lane || Board is not { } board) return;
        DropAt(board, Dragged(e), lane.Name, lane.Cards.Count);
        e.Handled = true;
    }

    private void DropAt(StoryboardViewModel board, object? payload, string lane, int index)
    {
        switch (payload)
        {
            case BeatCardViewModel card: board.Move(card.Lane.Name, card.Index, lane, index); break;
            case PaletteEntry entry: board.Insert(lane, index, entry.Kind); break;
            case AssetItemViewModel { Kind: AssetKind.Animation } animation: board.Shell.CharacterStudio.UseClipInScene(animation.Key, lane: lane, index: index); break;   // M8
        }
        LaneScroller.Focus();
    }

    private void ShowAdorner(UIElement element, bool after)
    {
        if (adorner?.AdornedElement == element && adorner.After == after) return;
        ClearAdorner();
        if (AdornerLayer.GetAdornerLayer(element) is not { } layer) return;
        adorner = new InsertionAdorner(element, after);
        layer.Add(adorner);
    }

    private void ClearAdorner()
    {
        if (adorner == null) return;
        AdornerLayer.GetAdornerLayer(adorner.AdornedElement)?.Remove(adorner);
        adorner = null;
    }

    // ---------- menus ----------

    private void AddToLane_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not LaneViewModel lane) return;
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        AddPaletteItems(menu.Items, entry => lane.AddCommand.Execute(entry), flat: true);
        menu.IsOpen = true;
    }

    private void Card_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: BeatCardViewModel card, ContextMenu: { } menu }) return;
        card.SelectCommand.Execute(null);
        menu.Items.Clear();
        var before = new MenuItem { Header = "Insert before", Icon = new SymbolIcon(SymbolRegular.ArrowCircleLeft24) };
        AddPaletteItems(before.Items, entry => card.InsertBeforeCommand.Execute(entry), flat: false);
        var after = new MenuItem { Header = "Insert after", Icon = new SymbolIcon(SymbolRegular.ArrowCircleRight24) };
        AddPaletteItems(after.Items, entry => card.InsertAfterCommand.Execute(entry), flat: false);
        menu.Items.Add(before);
        menu.Items.Add(after);
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Duplicate", "Ctrl+D", SymbolRegular.DocumentCopy24, () => card.DuplicateCommand.Execute(null)));
        menu.Items.Add(Item("Move earlier", "Ctrl+Left", null, () => card.MoveLeftCommand.Execute(null), card.Index > 0));
        menu.Items.Add(Item("Move later", "Ctrl+Right", null, () => card.MoveRightCommand.Execute(null), card.Index < card.Lane.Cards.Count - 1));
        menu.Items.Add(Item("Preview from here", "Shift+F5", SymbolRegular.Play24, () => card.PreviewFromHereCommand.Execute(null)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Delete", "Del", SymbolRegular.Delete24, () => card.DeleteCommand.Execute(null)));
    }

    private void Lane_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: LaneViewModel lane, ContextMenu: { } menu }) return;
        menu.Items.Clear();
        var add = new MenuItem { Header = "Add a beat", Icon = new SymbolIcon(SymbolRegular.Add24) };
        AddPaletteItems(add.Items, entry => lane.AddCommand.Execute(entry), flat: false);
        menu.Items.Add(add);
        if (lane.IsBranch)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Rename branch…", "F2", SymbolRegular.Rename24, () => lane.RenameCommand.Execute(null)));
            menu.Items.Add(Item("Delete branch", null, SymbolRegular.Delete24, () => lane.DeleteCommand.Execute(null)));
        }
    }

    private static MenuItem Item(string header, string? gesture, SymbolRegular? icon, Action action, bool enabled = true)
    {
        var item = new MenuItem { Header = header, InputGestureText = gesture ?? "", IsEnabled = enabled };
        if (icon is { } symbol) item.Icon = new SymbolIcon(symbol);
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>The beat palette as menu items: grouped submenus, or one flat list with category headers.</summary>
    private static void AddPaletteItems(ItemCollection items, Action<PaletteEntry> pick, bool flat)
    {
        foreach (var group in StoryboardViewModel.Palette)
        {
            var brush = Infrastructure.StudioColors.Brush(Infrastructure.StudioColors.For(group.Category));
            ItemCollection target = items;
            if (flat)
            {
                if (items.Count > 0) items.Add(new Separator());
                items.Add(new MenuItem { Header = group.Name, IsEnabled = false, Foreground = brush, FontSize = 11 });
            }
            else
            {
                var sub = new MenuItem { Header = group.Name, Icon = new SymbolIcon(group.Entries[0].Icon) { Foreground = brush } };
                items.Add(sub);
                target = sub.Items;
            }
            foreach (var entry in group.Entries)
            {
                var item = new MenuItem { Header = entry.Name, ToolTip = entry.Help, Icon = new SymbolIcon(entry.Icon) { Foreground = brush } };
                item.Click += (_, _) => pick(entry);
                target.Add(item);
            }
        }
    }

    // ---------- keys ----------

    private void Storyboard_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Board is not { } board || Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase or ComboBox || e.OriginalSource is System.Windows.Controls.Primitives.TextBoxBase) return;
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        switch (e.Key)
        {
            case Key.Delete: board.DeleteSelectedCommand.Execute(null); break;
            case Key.D when ctrl: board.DuplicateSelectedCommand.Execute(null); break;
            case Key.Left when ctrl: board.MoveSelectedLeftCommand.Execute(null); break;
            case Key.Right when ctrl: board.MoveSelectedRightCommand.Execute(null); break;
            case Key.Left: board.SelectPreviousCommand.Execute(null); break;
            case Key.Right: board.SelectNextCommand.Execute(null); break;
            case Key.F2 when board.Shell.Selection is BranchSelection b && board.Lanes.FirstOrDefault(l => l.Name == b.Lane) is { IsBranch: true } lane:
                lane.RenameCommand.Execute(null); break;
            default: return;
        }
        e.Handled = true;
    }
}

/// <summary>An accent bar at the left or right edge of a card while dragging over it.</summary>
public sealed class InsertionAdorner : Adorner
{
    public InsertionAdorner(UIElement adorned, bool after) : base(adorned) { After = after; IsHitTestVisible = false; }
    public bool After { get; }
    protected override void OnRender(DrawingContext dc)
    {
        var size = AdornedElement.RenderSize;
        var brush = (Brush?)TryFindResource("AccentFillColorDefaultBrush") ?? Brushes.LimeGreen;
        double x = After ? size.Width + 3 : -5;
        dc.DrawRoundedRectangle(brush, null, new Rect(x, -2, 3, size.Height + 4), 1.5, 1.5);
    }
}
