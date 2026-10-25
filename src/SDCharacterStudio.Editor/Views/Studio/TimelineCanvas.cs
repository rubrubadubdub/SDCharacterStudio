using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Views.Studio;

/// <summary>
/// The clip timeline (doc 07 §7.2): a ruler, one row per track of each actor or role, keys as diamonds coloured by track,
/// the playhead and the length handle. Click scrubs, drag a key to move it, double-click a row to add a key there.
/// </summary>
public sealed class TimelineCanvas : FrameworkElement
{
    public const double HeaderWidth = 150, RulerHeight = 24, RowHeight = 22;
    private static readonly Typeface Face = new("Segoe UI");
    private static readonly Brush Text = Frozen(new SolidColorBrush(Color.FromRgb(0xD8, 0xDB, 0xE0)));
    private static readonly Brush Faint = Frozen(new SolidColorBrush(Color.FromRgb(0x8F, 0x9B, 0xAA)));
    private static readonly Brush RowA = Frozen(new SolidColorBrush(Color.FromRgb(0x22, 0x25, 0x2A)));
    private static readonly Brush RowB = Frozen(new SolidColorBrush(Color.FromRgb(0x1E, 0x20, 0x24)));
    private static readonly Brush OwnerBand = Frozen(new SolidColorBrush(Color.FromRgb(0x2A, 0x2D, 0x33)));
    private static readonly Brush Beyond = Frozen(new SolidColorBrush(Color.FromArgb(0x70, 0x0F, 0x10, 0x12)));
    private static readonly Pen TickPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x38, 0x3C, 0x44))), 1));
    private static readonly Pen PlayheadPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0xE0, 0x5D, 0x5D))), 2));
    private static readonly Pen EndPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0xEF, 0xCB, 0x4A))), 2));
    private static readonly Pen KeyOutline = Frozen(new Pen(Brushes.Black, 1));
    private static readonly Pen SelectedOutline = Frozen(new Pen(Brushes.White, 2));
    private static readonly Pen SpanPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0x80, 0x8F, 0x9B, 0xAA))), 2));

    private enum Drag { None, Scrub, Key, Length }
    private Drag drag;
    private TimelineKey? dragKey;
    private double dragTime;

    public TimelineCanvas()
    {
        Focusable = true; FocusVisualStyle = null; ClipToBounds = true;
    }

    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
    private static FrameworkPropertyMetadata Visual(object? value) => new(value, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure);

    public static readonly DependencyProperty RowsProperty = DependencyProperty.Register(nameof(Rows), typeof(IEnumerable), typeof(TimelineCanvas), Visual(null));
    public static readonly DependencyProperty DurationProperty = DependencyProperty.Register(nameof(Duration), typeof(int), typeof(TimelineCanvas), Visual(1000));
    public static readonly DependencyProperty PlayheadProperty = DependencyProperty.Register(nameof(Playhead), typeof(double), typeof(TimelineCanvas), Visual(0.0));
    public static readonly DependencyProperty ScaleProperty = DependencyProperty.Register(nameof(Scale), typeof(double), typeof(TimelineCanvas), Visual(0.12));
    public static readonly DependencyProperty VersionProperty = DependencyProperty.Register(nameof(Version), typeof(int), typeof(TimelineCanvas), Visual(0));
    public static readonly DependencyProperty StepFpsProperty = DependencyProperty.Register(nameof(StepFps), typeof(int), typeof(TimelineCanvas), Visual(0));
    public IEnumerable? Rows { get => (IEnumerable?)GetValue(RowsProperty); set => SetValue(RowsProperty, value); }
    public int Duration { get => (int)GetValue(DurationProperty); set => SetValue(DurationProperty, value); }
    public double Playhead { get => (double)GetValue(PlayheadProperty); set => SetValue(PlayheadProperty, value); }
    public double Scale { get => (double)GetValue(ScaleProperty); set => SetValue(ScaleProperty, value); }
    /// <summary>Bumped by the view model when keys change, so the timeline redraws.</summary>
    public int Version { get => (int)GetValue(VersionProperty); set => SetValue(VersionProperty, value); }
    public int StepFps { get => (int)GetValue(StepFpsProperty); set => SetValue(StepFpsProperty, value); }

    public event Action<double>? Scrubbed;
    public event Action<TimelineKey>? KeyClicked;
    public event Action<TimelineKey, double>? KeyMoved;
    public event Action<TimelineRow, double>? RowDoubleClicked;
    public event Action<double>? LengthChanged;

    private IReadOnlyList<TimelineRow> RowList => Rows?.OfType<TimelineRow>().ToList() ?? new List<TimelineRow>();
    private double X(double ms) => HeaderWidth + 8 + ms * Scale;
    private double Time(double x) => Math.Max(0, (x - HeaderWidth - 8) / Scale);

    protected override Size MeasureOverride(Size available) => new(X(Math.Max(Duration, 1000) * 1.25) + 40, RulerHeight + RowList.Count * RowHeight + 4);

    public static Color TrackColor(TrackKind track) => track switch
    {
        TrackKind.Position => Color.FromRgb(0x45, 0xC0, 0x8A), TrackKind.Facing => Color.FromRgb(0x5B, 0x9B, 0xF0), TrackKind.Pose => Color.FromRgb(0xF0, 0xA0, 0x48),
        TrackKind.Visible => Color.FromRgb(0x8F, 0x9B, 0xAA), TrackKind.Parts => Color.FromRgb(0xB4, 0x8C, 0xF2), TrackKind.Expression => Color.FromRgb(0xF2, 0x8C, 0xC8),
        TrackKind.Emote => Color.FromRgb(0xEF, 0xCB, 0x4A), TrackKind.Sound => Color.FromRgb(0x4C, 0xC9, 0xD6), TrackKind.Effect => Color.FromRgb(0xE0, 0x7B, 0x5D),
        _ => Color.FromRgb(0xD8, 0xDB, 0xE0)
    };

    protected override void OnRender(DrawingContext dc)
    {
        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        FormattedText Label(string text, Brush brush, double size = 11) => new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, size, brush, dip);
        var rows = RowList;
        double width = ActualWidth, height = ActualHeight;
        dc.DrawRectangle(RowB, null, new Rect(0, 0, width, height));

        // Ruler: a tick every 100 ms, a label every second (every 500 ms when zoomed in).
        double labelEvery = Scale >= 0.3 ? 500 : Scale >= 0.08 ? 1000 : 2000;
        for (double t = 0; X(t) < width; t += 100)
        {
            bool major = Math.Abs(t % labelEvery) < 0.5;
            dc.DrawLine(TickPen, new Point(X(t) + 0.5, major ? 6 : 14), new Point(X(t) + 0.5, RulerHeight));
            if (major) dc.DrawText(Label($"{t / 1000:0.#} s", Faint, 10), new Point(X(t) + 3, 2));
        }
        // StepFps: faint marks where steps fall.
        if (StepFps > 0 && Scale * 1000.0 / StepFps >= 6)
            for (double t = 0; t <= Duration; t += 1000.0 / StepFps) dc.DrawRectangle(Faint, null, new Rect(X(t), RulerHeight - 3, 1, 3));

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            double y = RulerHeight + i * RowHeight;
            dc.DrawRectangle(i % 2 == 0 ? RowA : RowB, null, new Rect(0, y, width, RowHeight));
            if (row.IsFirstOfOwner)
            {
                dc.DrawRectangle(OwnerBand, null, new Rect(0, y, HeaderWidth, RowHeight));
                dc.DrawText(Label(row.Owner.Label, Text, 11.5), new Point(6, y + 3));
            }
            else dc.DrawText(Label(row.Label, Faint), new Point(18, y + 4));
            if (row.IsFirstOfOwner) dc.DrawText(Label(row.Label, Faint), new Point(HeaderWidth - Label(row.Label, Faint).Width - 6, y + 4));
            var color = TrackColor(row.Track);
            var brush = Frozen(new SolidColorBrush(color));
            var keys = row.Keys.OrderBy(k => k.Key.At).ToList();
            // Spans: an expression with a duration, a pose that holds until the next key.
            for (int k = 0; k < keys.Count; k++)
            {
                double at = keys[k].Key.At;
                double end = keys[k].Key is Core.Models.ExpressionKey { Duration: { } d } ? at + d : k + 1 < keys.Count ? keys[k + 1].Key.At : at;
                if (row.Track is TrackKind.Pose or TrackKind.Expression or TrackKind.Position && end > at)
                    dc.DrawLine(SpanPen, new Point(X(at), y + RowHeight / 2), new Point(X(end), y + RowHeight / 2));
            }
            foreach (var key in keys)
            {
                double at = drag == Drag.Key && ReferenceEquals(key, dragKey) ? dragTime : key.Key.At;
                double cx = X(at), cy = y + RowHeight / 2, r = 6;
                var diamond = new StreamGeometry();
                using (var g = diamond.Open())
                {
                    g.BeginFigure(new Point(cx, cy - r), true, true);
                    g.LineTo(new Point(cx + r, cy), true, false); g.LineTo(new Point(cx, cy + r), true, false); g.LineTo(new Point(cx - r, cy), true, false);
                }
                diamond.Freeze();
                dc.DrawGeometry(brush, key.IsSelected ? SelectedOutline : KeyOutline, diamond);
                if (key.Label is { Length: > 0 } text && Scale >= 0.06)
                {
                    var label = Label(text.Length > 22 ? text[..21] + "…" : text, Text, 10);
                    dc.DrawText(label, new Point(cx + r + 2, cy - label.Height / 2));
                }
            }
        }
        // Beyond the end, the playhead and the length handle.
        dc.DrawRectangle(Beyond, null, new Rect(X(Duration), RulerHeight, Math.Max(0, width - X(Duration)), Math.Max(0, height - RulerHeight)));
        dc.DrawLine(EndPen, new Point(X(Duration), 0), new Point(X(Duration), height));
        dc.DrawRectangle(Frozen(new SolidColorBrush(Color.FromRgb(0xEF, 0xCB, 0x4A))), null, new Rect(X(Duration) - 4, 0, 8, 10));
        dc.DrawLine(PlayheadPen, new Point(X(Playhead), 0), new Point(X(Playhead), height));
        dc.DrawLine(TickPen, new Point(HeaderWidth + 0.5, 0), new Point(HeaderWidth + 0.5, height));
    }

    private (TimelineRow? Row, TimelineKey? Key) HitTest(Point p)
    {
        var rows = RowList;
        int index = (int)Math.Floor((p.Y - RulerHeight) / RowHeight);
        if (p.Y < RulerHeight || index < 0 || index >= rows.Count) return (null, null);
        var row = rows[index];
        var key = row.Keys.Where(k => Math.Abs(X(k.Key.At) - p.X) <= 7).OrderBy(k => Math.Abs(X(k.Key.At) - p.X)).FirstOrDefault();
        return (row, key);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        var p = e.GetPosition(this);
        if (p.X < HeaderWidth) return;
        if (e.ClickCount == 2 && HitTest(p) is { Row: { } row, Key: null }) { RowDoubleClicked?.Invoke(row, Time(p.X)); e.Handled = true; return; }
        if (Math.Abs(p.X - X(Duration)) <= 5 && p.Y < RulerHeight) drag = Drag.Length;
        else if (HitTest(p) is { Key: { } key }) { drag = Drag.Key; dragKey = key; dragTime = key.Key.At; KeyClicked?.Invoke(key); }
        else { drag = Drag.Scrub; Scrubbed?.Invoke(Math.Min(Time(p.X), Duration)); }
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.GetPosition(this);
        Cursor = drag == Drag.Length || (Math.Abs(p.X - X(Duration)) <= 5 && p.Y < RulerHeight) ? Cursors.SizeWE : HitTest(p).Key != null ? Cursors.Hand : null;
        switch (drag)
        {
            case Drag.Scrub: Scrubbed?.Invoke(Math.Min(Time(p.X), Duration)); break;
            case Drag.Key: dragTime = Math.Clamp(Math.Round(Time(p.X) / 10) * 10, 0, Duration); InvalidateVisual(); break;
            case Drag.Length: InvalidateVisual(); break;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var p = e.GetPosition(this);
        var finished = drag;
        drag = Drag.None;
        ReleaseMouseCapture();
        if (finished == Drag.Key && dragKey != null && Math.Abs(dragTime - dragKey.Key.At) >= 10) KeyMoved?.Invoke(dragKey, dragTime);
        if (finished == Drag.Length) LengthChanged?.Invoke(Time(p.X));
        dragKey = null;
        InvalidateVisual();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e) { base.OnLostMouseCapture(e); if (drag != Drag.None) { drag = Drag.None; dragKey = null; InvalidateVisual(); } }
}
