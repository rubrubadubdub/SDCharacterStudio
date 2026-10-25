using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Views.Studio;

/// <summary>
/// The animation preview stage (doc 07 §15): a plain tiled floor with the cast standing on it. Click an actor to select it;
/// drag it to move where it stands (or, while recording, to make a position key at the playhead). Alt drops on pixels.
/// </summary>
public sealed class ClipStage : FrameworkElement
{
    private static readonly Brush FloorA = Frozen(new SolidColorBrush(Color.FromRgb(0x5E, 0x4B, 0x36)));
    private static readonly Brush FloorB = Frozen(new SolidColorBrush(Color.FromRgb(0x56, 0x44, 0x31)));
    private static readonly Pen GridPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0))), 1));
    private static readonly Pen SelectPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x5B, 0x9B, 0xF0))), 2) { DashStyle = DashStyles.Dash });
    private static readonly Pen RecordPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0xE0, 0x5D, 0x5D))), 3));
    private static readonly Brush TagBrush = Frozen(new SolidColorBrush(Color.FromArgb(0xC0, 0x15, 0x17, 0x1A)));
    private static readonly Brush BubbleBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xEF, 0xCB, 0x4A)));
    private static readonly Typeface Face = new("Segoe UI");

    private StageActor? dragging;
    private Point dragOffset, dragAt;

    public ClipStage()
    {
        Focusable = true; FocusVisualStyle = null; ClipToBounds = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
    }

    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
    private static FrameworkPropertyMetadata Visual(object? value) => new(value, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure);

    public static readonly DependencyProperty ActorsProperty = DependencyProperty.Register(nameof(Actors), typeof(IEnumerable), typeof(ClipStage),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, e) => ((ClipStage)d).Watch(e)));
    public static readonly DependencyProperty TilesWideProperty = DependencyProperty.Register(nameof(TilesWide), typeof(int), typeof(ClipStage), Visual(12));
    public static readonly DependencyProperty TilesHighProperty = DependencyProperty.Register(nameof(TilesHigh), typeof(int), typeof(ClipStage), Visual(7));
    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(nameof(Zoom), typeof(double), typeof(ClipStage), Visual(3.0));
    public static readonly DependencyProperty RecordingProperty = DependencyProperty.Register(nameof(Recording), typeof(bool), typeof(ClipStage), Visual(false));
    public IEnumerable? Actors { get => (IEnumerable?)GetValue(ActorsProperty); set => SetValue(ActorsProperty, value); }
    public int TilesWide { get => (int)GetValue(TilesWideProperty); set => SetValue(TilesWideProperty, value); }
    public int TilesHigh { get => (int)GetValue(TilesHighProperty); set => SetValue(TilesHighProperty, value); }
    public double Zoom { get => (double)GetValue(ZoomProperty); set => SetValue(ZoomProperty, value); }
    public bool Recording { get => (bool)GetValue(RecordingProperty); set => SetValue(RecordingProperty, value); }

    public event Action<string?>? ActorClicked;
    public event Action<string>? DragStarted;
    /// <summary>Feet position in game pixels from the stage's top-left, and whether to keep pixel precision.</summary>
    public event Action<double, double, bool>? Dropped;
    public event Action? DragCancelled;

    private void Watch(DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyCollectionChanged old) old.CollectionChanged -= Changed;
        if (e.NewValue is INotifyCollectionChanged now) now.CollectionChanged += Changed;
    }
    private void Changed(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    private double S => Math.Max(1, Math.Round(Zoom));
    protected override Size MeasureOverride(Size available) => new(TilesWide * 16 * S, TilesHigh * 16 * S);

    private IEnumerable<StageActor> List => Actors?.OfType<StageActor>() ?? Enumerable.Empty<StageActor>();

    protected override void OnRender(DrawingContext dc)
    {
        double s = S, tile = 16 * s, dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        for (int y = 0; y < TilesHigh; y++)
            for (int x = 0; x < TilesWide; x++)
                dc.DrawRectangle((x + y) % 2 == 0 ? FloorA : FloorB, GridPen, new Rect(x * tile, y * tile, tile, tile));
        if (Recording) dc.DrawRectangle(null, RecordPen, new Rect(1.5, 1.5, TilesWide * tile - 3, TilesHigh * tile - 3));
        // Back to front by feet, like the game.
        foreach (var actor in List.OrderBy(a => a.FeetY))
        {
            bool moving = dragging != null && dragging.Id == actor.Id;
            double left = moving ? dragAt.X - dragOffset.X : actor.Left * s, top = moving ? dragAt.Y - dragOffset.Y : actor.Top * s;
            if (actor.Image is { } image)
            {
                dc.PushOpacity(actor.Visible ? 1 : 0.3);
                dc.DrawImage(image, new Rect(left, top, image.PixelWidth * s, image.PixelHeight * s));
                dc.Pop();
                if (actor.IsSelected) dc.DrawRectangle(null, SelectPen, new Rect(left - 2, top - 2, image.PixelWidth * s + 4, image.PixelHeight * s + 4));
            }
            var name = new FormattedText(actor.Label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, 11, Brushes.White, dip);
            double feetX = moving ? dragAt.X - dragOffset.X + (actor.FeetX - actor.Left) * s : actor.FeetX * s, feetY = moving ? dragAt.Y - dragOffset.Y + (actor.FeetY - actor.Top) * s : actor.FeetY * s;
            dc.DrawRoundedRectangle(TagBrush, null, new Rect(feetX - name.Width / 2 - 4, feetY + 4, name.Width + 8, name.Height + 2), 3, 3);
            dc.DrawText(name, new Point(feetX - name.Width / 2, feetY + 5));
            string? bubble = actor.Emote ?? (actor.Expression != null ? "expression: " + actor.Expression : null) ?? (actor.Effect != null ? "effect: " + actor.Effect : null);
            if (bubble != null && actor.Image != null)
            {
                var text = new FormattedText(bubble, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, 11, Brushes.Black, dip);
                dc.DrawRoundedRectangle(BubbleBrush, null, new Rect(feetX - text.Width / 2 - 5, top - text.Height - 8, text.Width + 10, text.Height + 4), 6, 6);
                dc.DrawText(text, new Point(feetX - text.Width / 2, top - text.Height - 6));
            }
        }
    }

    private StageActor? Hit(Point p)
    {
        double s = S;
        return List.OrderByDescending(a => a.FeetY).FirstOrDefault(a => a.Image != null
            && new Rect(a.Left * s, a.Top * s, a.Image.PixelWidth * s, a.Image.PixelHeight * s).Contains(p));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        var p = e.GetPosition(this);
        var actor = Hit(p);
        ActorClicked?.Invoke(actor?.Id);
        if (actor == null) return;
        dragging = actor;
        dragOffset = new Point(p.X - actor.Left * S, p.Y - actor.Top * S);
        dragAt = p;
        DragStarted?.Invoke(actor.Id);
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragging == null) { Cursor = Hit(e.GetPosition(this)) != null ? Cursors.SizeAll : null; return; }
        dragAt = e.GetPosition(this);
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (dragging is not { } actor) return;
        dragging = null;
        ReleaseMouseCapture();
        var p = e.GetPosition(this);
        double s = S;
        double feetX = (p.X - dragOffset.X) / s + (actor.FeetX - actor.Left), feetY = (p.Y - dragOffset.Y) / s + (actor.FeetY - actor.Top);
        if (Math.Abs(p.X - dragOffset.X - actor.Left * s) < 2 && Math.Abs(p.Y - dragOffset.Y - actor.Top * s) < 2) { DragCancelled?.Invoke(); InvalidateVisual(); return; }
        Dropped?.Invoke(feetX, feetY, Keyboard.Modifiers.HasFlag(ModifierKeys.Alt));
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (dragging == null) return;
        dragging = null;
        DragCancelled?.Invoke();
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && dragging != null) { ReleaseMouseCapture(); e.Handled = true; }
    }
}
