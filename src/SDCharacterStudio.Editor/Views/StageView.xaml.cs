using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SDCharacterStudio.Editor.Infrastructure;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Views;

/// <summary>
/// Stage interactions (doc 07 §3.1): drag actors (snap to tiles, Alt for pixels), double-click to turn, drag the camera tab,
/// drop characters and locations from the asset browser, pick tiles for beat fields, Ctrl+wheel zoom and middle-drag pan.
/// </summary>
public partial class StageView : UserControl
{
    public static readonly IValueConverter FollowsText = new FuncConverter(v => v is true ? " (follows an actor)" : "");
    private const double Tile = StageViewModel.Tile;

    private StageViewModel? vm;
    private StageActorViewModel? dragActor;
    private StagePropViewModel? dragProp;
    private FrameworkElement? dragElement;
    private Point dragOrigin;
    private bool dragMoved;
    private Point panOrigin;
    private Point panOffsets;
    private bool panning;
    private Point cameraOrigin;
    private bool cameraDragging;
    private string? lastLocation;

    public StageView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (vm != null) vm.PropertyChanged -= OnStagePropertyChanged;
            vm = e.NewValue as StageViewModel;
            if (vm != null) vm.PropertyChanged += OnStagePropertyChanged;
        };
        Loaded += (_, _) => { if (vm != null) { vm.PropertyChanged -= OnStagePropertyChanged; vm.PropertyChanged += OnStagePropertyChanged; } };
        Unloaded += (_, _) => { if (vm != null) vm.PropertyChanged -= OnStagePropertyChanged; };
    }

    private void OnStagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StageViewModel.LocationName) && vm != null && vm.LocationName != lastLocation)
        {
            lastLocation = vm.LocationName;
            Dispatcher.BeginInvoke(CenterOnCamera, DispatcherPriority.Loaded);
        }
    }

    /// <summary>Scroll so the camera frame (what the player will see) is centred.</summary>
    public void CenterOnCamera()
    {
        if (vm == null) return;
        var rect = vm.CameraRect;
        double margin = Surface.Margin.Left;
        Scroller.ScrollToHorizontalOffset(margin + (rect.X + rect.Width / 2) * vm.Zoom - Scroller.ViewportWidth / 2);
        Scroller.ScrollToVerticalOffset(margin + (rect.Y + rect.Height / 2) * vm.Zoom - Scroller.ViewportHeight / 2);
    }

    private static (int X, int Y) TileAt(Point p) => ((int)Math.Floor(p.X / Tile), (int)Math.Floor(p.Y / Tile));

    private static StagePropViewModel? PropAt(object source, out FrameworkElement? element)
    {
        for (var d = source as DependencyObject; d != null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is FrameworkElement { Tag: "prop", DataContext: StagePropViewModel prop } fe) { element = fe; return prop; }
        element = null;
        return null;
    }

    private static StageActorViewModel? ActorAt(object source, out FrameworkElement? element)
    {
        for (var d = source as DependencyObject; d != null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is FrameworkElement { Tag: "actor", DataContext: StageActorViewModel actor } fe) { element = fe; return actor; }
        element = null;
        return null;
    }

    // ---------- actors ----------

    private void Surface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (vm == null) return;
        Focus();
        var position = e.GetPosition(Surface);
        var actor = ActorAt(e.OriginalSource, out var element);
        if (actor != null && !vm.IsPicking)
        {
            if (e.ClickCount == 2) { actor.TurnCommand.Execute(null); e.Handled = true; return; }
            actor.SelectCommand.Execute(null);
            // Freezables instantiated by a DataTemplate can be frozen/shared.
            // Each gesture needs its own mutable visual offset.
            if (element != null) element.RenderTransform = new TranslateTransform();
            dragActor = actor; dragElement = element; dragOrigin = position; dragMoved = false;
            Surface.CaptureMouse();
            e.Handled = true;
            return;
        }
        // A placed item: drag to move it, click to select the beat that placed it.
        if (PropAt(e.OriginalSource, out var propElement) is { } prop && !vm.IsPicking)
        {
            if (propElement != null) propElement.RenderTransform = new TranslateTransform();
            dragProp = prop; dragElement = propElement; dragOrigin = position; dragMoved = false;
            Surface.CaptureMouse();
            e.Handled = true;
            return;
        }
        var (x, y) = TileAt(position);
        if (vm.TileClicked(x, y)) { e.Handled = true; return; }
        // An empty spot on the stage is the scene itself: show its settings, whatever was selected.
        if (vm.Shell.Selection is not SceneSelection && vm.Shell.Scene is { } scene) vm.Shell.Select(new SceneSelection(scene.Id!));
    }

    private void Surface_MouseMove(object sender, MouseEventArgs e)
    {
        if (vm == null) return;
        var position = e.GetPosition(Surface);
        if (panning) { Pan(e); return; }
        if (dragProp != null && dragElement != null && (e.LeftButton == MouseButtonState.Pressed || Surface.IsMouseCaptured))
        {
            var delta = Snap(position - dragOrigin, pixels: false);
            if (delta.X != 0 || delta.Y != 0) dragMoved = true;
            if (dragElement.RenderTransform is TranslateTransform t) { t.X = delta.X; t.Y = delta.Y; }
            ShowHover(dragProp.TileX + (int)Math.Round(delta.X / Tile), dragProp.TileY + (int)Math.Round(delta.Y / Tile));
            return;
        }
        if (dragActor != null && dragElement != null && (e.LeftButton == MouseButtonState.Pressed || Surface.IsMouseCaptured))
        {
            var delta = Snap(position - dragOrigin, Keyboard.Modifiers.HasFlag(ModifierKeys.Alt));
            if (delta.X != 0 || delta.Y != 0) dragMoved = true;
            if (dragElement.RenderTransform is TranslateTransform t) { t.X = delta.X; t.Y = delta.Y; }
            ShowHover(dragActor.TileX + (int)Math.Round(delta.X / Tile), dragActor.TileY + (int)Math.Round(delta.Y / Tile));
            return;
        }
        var (x, y) = TileAt(position);
        ShowHover(x, y);
    }

    private static Vector Snap(Vector delta, bool pixels) => pixels
        ? new Vector(Math.Round(delta.X), Math.Round(delta.Y))
        : new Vector(Math.Round(delta.X / Tile) * Tile, Math.Round(delta.Y / Tile) * Tile);

    private void Surface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (dragProp != null && vm != null)
        {
            var prop = dragProp;
            var shift = Snap(e.GetPosition(Surface) - dragOrigin, pixels: false);
            bool movedProp = dragMoved;
            Surface.ReleaseMouseCapture();
            ResetActorDrag();
            if (movedProp) vm.MoveProp(prop, prop.TileX + (int)Math.Round(shift.X / Tile), prop.TileY + (int)Math.Round(shift.Y / Tile));
            else vm.SelectProp(prop);
            return;
        }
        if (dragActor == null || vm == null) return;
        var actor = dragActor;
        var element = dragElement;
        bool moved = dragMoved;
        var deltaPosition = e.GetPosition(Surface) - dragOrigin;
        Surface.ReleaseMouseCapture();
        dragActor = null; dragElement = null;
        if (element?.RenderTransform is TranslateTransform t) { t.X = 0; t.Y = 0; }
        if (!moved) return;
        bool pixels = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        var delta = Snap(deltaPosition, pixels);
        int tileX = actor.TileX, tileY = actor.TileY, pxX = actor.PxX, pxY = actor.PxY;
        if (pixels)
        {
            // Keep the tile (Move beats path from it) and carry whole tiles out of the pixel offset.
            pxX += (int)delta.X; pxY += (int)delta.Y;
            int carryX = pxX / (int)Tile, carryY = pxY / (int)Tile;
            tileX += carryX; tileY += carryY; pxX -= carryX * (int)Tile; pxY -= carryY * (int)Tile;
        }
        else { tileX += (int)Math.Round(delta.X / Tile); tileY += (int)Math.Round(delta.Y / Tile); }
        vm.MoveActor(actor.Index, Math.Max(0, tileX), Math.Max(0, tileY), pxX, pxY);
    }

    private void Surface_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (dragActor != null || dragProp != null) ResetActorDrag();
        if (panning) { panning = false; Cursor = null; }
    }

    private void ResetActorDrag()
    {
        if (dragElement?.RenderTransform is TranslateTransform t) { t.X = 0; t.Y = 0; }
        dragActor = null;
        dragProp = null;
        dragElement = null;
        dragMoved = false;
    }

    private void ShowHover(int x, int y, int width = (int)Tile, int height = (int)Tile)
    {
        if (vm == null) return;
        HoverTile.Visibility = Visibility.Visible;
        // An item being dropped shows its whole picture, rising from the bottom of the tile.
        HoverTile.Width = width; HoverTile.Height = height;
        Canvas.SetLeft(HoverTile, x * Tile); Canvas.SetTop(HoverTile, (y + 1) * Tile - height);
        vm.HoverText = $"Tile {x}, {y}";
    }

    private void Surface_MouseLeave(object sender, MouseEventArgs e)
    {
        if (dragActor != null || panning) return;
        HoverTile.Visibility = Visibility.Collapsed;
        if (vm != null) vm.HoverText = null;
    }

    private void Surface_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (vm?.Shell.Scene is not { } scene) return;
        var (x, y) = TileAt(e.GetPosition(Surface));
        var actor = ActorAt(e.OriginalSource, out _);
        var prop = actor == null ? PropAt(e.OriginalSource, out _) : null;
        var menu = new ContextMenu();
        void Add(string header, Action action, bool enabled = true) { var item = new MenuItem { Header = header, IsEnabled = enabled }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        if (prop != null)
        {
            Add($"Select the beat that places the {prop.Label}", () => vm.SelectProp(prop));
            Add($"Take the {prop.Label} away here (a new beat)", () => vm.TakeAway(prop));
            menu.Items.Add(new Separator());
        }
        if (actor != null)
        {
            Add($"Turn {actor.Label}", () => actor.TurnCommand.Execute(null));
            Add($"Select {actor.Label}", () => actor.SelectCommand.Execute(null));
            Add($"Remove {actor.Label}", () => vm.RemoveActor(actor.Index));
            menu.Items.Add(new Separator());
        }
        if (vm.Shell.Selection is ActorSelection selected && selected.Index < scene.Actors.Count && actor == null)
        {
            string who = scene.Actors[selected.Index].Id ?? "?";
            // A walk planned from where they are now (the stage's moment), or put them straight here.
            Add($"{who} walks here", () => vm.WalkHere(selected.Index, x, y));
            Add(vm.HasMoment ? $"Put {who} here" : $"Start {who} here", () => vm.MoveActor(selected.Index, x, y, 0, 0));
        }
        if (!scene.Actors.Any(a => a.Kind == "Farmer")) Add("Put the player here", () => vm.DropCharacter("farmer", x, y));
        Add("Centre the camera here", () => vm.MoveCamera(x, y));
        Add($"Copy tile ({x}, {y})", () => Clipboard.SetText($"{x}, {y}"));
        menu.PlacementTarget = Surface;
        menu.IsOpen = true;
        e.Handled = true;
    }

    // ---------- pan and zoom ----------

    private void Surface_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        panning = true;
        panOrigin = e.GetPosition(Scroller);
        panOffsets = new Point(Scroller.HorizontalOffset, Scroller.VerticalOffset);
        Surface.CaptureMouse();
        Cursor = Cursors.ScrollAll;
        e.Handled = true;
    }

    private void Pan(MouseEventArgs e)
    {
        var delta = e.GetPosition(Scroller) - panOrigin;
        Scroller.ScrollToHorizontalOffset(panOffsets.X - delta.X);
        Scroller.ScrollToVerticalOffset(panOffsets.Y - delta.Y);
    }

    private void Surface_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || !panning) return;
        panning = false;
        Surface.ReleaseMouseCapture();
        Cursor = null;
    }

    private void Scroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (vm == null || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        e.Handled = true;
        var inViewport = e.GetPosition(Scroller);
        var onSurface = e.GetPosition(Surface);
        double before = vm.Zoom;
        if (e.Delta > 0) vm.ZoomInCommand.Execute(null); else vm.ZoomOutCommand.Execute(null);
        if (vm.Zoom == before) return;
        Scroller.UpdateLayout();
        double margin = Surface.Margin.Left;
        Scroller.ScrollToHorizontalOffset(margin + onSurface.X * vm.Zoom - inViewport.X);
        Scroller.ScrollToVerticalOffset(margin + onSurface.Y * vm.Zoom - inViewport.Y);
    }

    // ---------- camera ----------

    private void CameraTab_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        cameraDragging = true;
        cameraOrigin = e.GetPosition(Surface);
        ((UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    private void CameraTab_MouseMove(object sender, MouseEventArgs e)
    {
        if (!cameraDragging) return;
        var delta = Snap(e.GetPosition(Surface) - cameraOrigin, pixels: false);
        CameraDrag.X = delta.X; CameraDrag.Y = delta.Y;
        e.Handled = true;
    }

    private void CameraTab_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!cameraDragging || vm == null) return;
        cameraDragging = false;
        ((UIElement)sender).ReleaseMouseCapture();
        var delta = Snap(e.GetPosition(Surface) - cameraOrigin, pixels: false);
        CameraDrag.X = 0; CameraDrag.Y = 0;
        e.Handled = true;
        if (delta.X == 0 && delta.Y == 0) return;
        var rect = vm.CameraRect;
        vm.MoveCamera((int)Math.Floor((rect.X + delta.X + rect.Width / 2) / Tile), (int)Math.Floor((rect.Y + delta.Y + rect.Height / 2) / Tile));
    }

    private void CameraTab_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (!cameraDragging) return;
        cameraDragging = false;
        CameraDrag.X = 0;
        CameraDrag.Y = 0;
    }

    // ---------- drag and drop from the asset browser ----------

    private static AssetItemViewModel? DraggedAsset(DragEventArgs e) => e.Data.GetDataPresent(AssetBrowserView.AssetFormat) ? e.Data.GetData(AssetBrowserView.AssetFormat) as AssetItemViewModel : null;

    private void Stage_DragOver(object sender, DragEventArgs e)
    {
        var asset = DraggedAsset(e);
        e.Effects = asset != null && vm?.Shell.Scene != null ? DragDropEffects.Copy : DragDropEffects.None;
        if (asset != null && vm != null)
        {
            var (x, y) = TileAt(e.GetPosition(Surface));
            var (width, height) = asset.Kind == AssetKind.Item ? vm.ItemSize(asset.Key) : ((int)Tile, (int)Tile);
            ShowHover(x, y, width, height);
            vm.HoverText = asset.Kind switch
            {
                AssetKind.Character => $"Place {asset.Name} at {x}, {y}",
                AssetKind.Location => $"Move the scene to {asset.Name}",
                AssetKind.Item => $"Place {asset.Name} here ({x}, {y}): it stays on the map until the scene ends",
                AssetKind.Sprite or AssetKind.Portrait => "Drop on an actor to change their outfit",
                _ => "Add to the storyboard"
            };
        }
        e.Handled = true;
    }

    private void Stage_DragLeave(object sender, DragEventArgs e) => HoverTile.Visibility = Visibility.Collapsed;

    private void Stage_Drop(object sender, DragEventArgs e)
    {
        var asset = DraggedAsset(e);
        HoverTile.Visibility = Visibility.Collapsed;   // the drop is done: no square left behind
        if (asset == null || vm == null) return;
        var (x, y) = TileAt(e.GetPosition(Surface));
        switch (asset.Kind)
        {
            case AssetKind.Character: vm.DropCharacter(asset.Key, Math.Max(0, x), Math.Max(0, y)); break;
            case AssetKind.Location: vm.SetLocation(asset.Key); break;
            case AssetKind.Item: vm.PlaceItem(asset.Key, Math.Max(0, x), Math.Max(0, y)); break;
            default:
                if (ActorAt(e.OriginalSource, out _) is { } actor) actor.SelectCommand.Execute(null);
                asset.UseCommand.Execute(null);
                break;
        }
        e.Handled = true;
    }

    // ---------- keys ----------

    private void Stage_KeyDown(object sender, KeyEventArgs e)
    {
        if (vm == null) return;
        if (e.Key == Key.Escape && dragActor != null) { Surface.ReleaseMouseCapture(); ResetActorDrag(); e.Handled = true; }
        else if (e.Key == Key.Escape && cameraDragging) { CameraTab.ReleaseMouseCapture(); cameraDragging = false; CameraDrag.X = 0; CameraDrag.Y = 0; e.Handled = true; }
        else if (e.Key == Key.Escape && vm.IsPicking) { vm.CancelPick(); e.Handled = true; }
        else if (e.Key == Key.Delete && vm.Shell.Selection is ActorSelection a) { vm.RemoveActor(a.Index); e.Handled = true; }
    }
}

/// <summary>A thin tile grid that stays one screen pixel wide at any zoom.</summary>
public sealed class TileGrid : FrameworkElement
{
    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(nameof(Zoom), typeof(double), typeof(TileGrid),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Zoom { get => (double)GetValue(ZoomProperty); set => SetValue(ZoomProperty, value); }

    private static readonly Brush Line = Frozen(new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush Major = Frozen(new SolidColorBrush(Color.FromArgb(0x45, 0xFF, 0xFF, 0xFF)));
    private static Brush Frozen(Brush b) { b.Freeze(); return b; }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight, t = StageViewModel.Tile, thickness = 1 / Math.Max(0.25, Zoom);
        var minor = new Pen(Line, thickness); minor.Freeze();
        var major = new Pen(Major, thickness); major.Freeze();
        for (int i = 0; i * t <= w; i++) dc.DrawLine(i % 10 == 0 ? major : minor, new Point(i * t, 0), new Point(i * t, h));
        for (int j = 0; j * t <= h; j++) dc.DrawLine(j % 10 == 0 ? major : minor, new Point(0, j * t), new Point(w, j * t));
    }
}
