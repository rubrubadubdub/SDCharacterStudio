using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SDCharacterStudio.Editor.Studio;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Views.Studio;

/// <summary>Where the pointer is, in frame pixels.</summary>
public sealed class PixelPointerEventArgs : RoutedEventArgs
{
    public PixelPointerEventArgs(RoutedEvent e, int x, int y, MouseButton? button, double fx = double.NaN, double fy = double.NaN) : base(e) { X = x; Y = y; Button = button; FX = fx; FY = fy; }
    public int X { get; }
    public int Y { get; }
    /// <summary>The pointer in fractional frame pixels (for transform handles).</summary>
    public double FX { get; }
    public double FY { get; }
    public MouseButton? Button { get; }
}

/// <summary>
/// A zoomed pixel canvas (doc 07 §6): checkerboard, reference ghost, onion skin, the frame, a pixel grid, the pivot,
/// anchors and the selection. Integer zoom only, nearest-neighbour; pointer events report frame pixels.
/// </summary>
public sealed class PixelCanvas : FrameworkElement
{
    private static readonly Brush CheckLight = Frozen(new SolidColorBrush(Color.FromRgb(0x3A, 0x3D, 0x44)));
    private static readonly Brush CheckDark = Frozen(new SolidColorBrush(Color.FromRgb(0x30, 0x33, 0x39)));
    private static readonly Pen GridPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF))), 1));
    private static readonly Pen FramePen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF))), 1));
    private static readonly Pen PivotPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x5B, 0x9B, 0xF0))), 2));
    private static readonly Brush AnchorBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xF0, 0xA0, 0x48)));
    private static readonly Pen AnchorPen = Frozen(new Pen(Brushes.Black, 1));
    private static readonly Brush HoverFill = Frozen(new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)));
    private static readonly Pen HoverPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF))), 1));
    private static readonly Pen SelectionPen = Frozen(new Pen(Brushes.White, 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) });
    private static readonly Pen SelectionShadow = Frozen(new Pen(Brushes.Black, 1));
    private static readonly Pen BoxPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x5B, 0x9B, 0xF0))), 1.5));
    private static readonly Pen BoxShadow = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0xC0, 0, 0, 0))), 3));
    private static readonly Brush PivotFill = Frozen(new SolidColorBrush(Color.FromRgb(0x5B, 0x9B, 0xF0)));
    private static readonly Typeface LabelFace = new("Segoe UI");

    private (int X, int Y)? hover;
    private (int X, int Y)? lastMoved;
    private MouseButton? pressed;

    public PixelCanvas()
    {
        Focusable = true;
        FocusVisualStyle = null;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        Cursor = Cursors.Cross;
        Unloaded += (_, _) => CancelGesture();
    }

    private static T Frozen<T>(T freezable) where T : Freezable { freezable.Freeze(); return freezable; }

    // Painting changes the picture, not its extent. Re-measuring every stroke point makes large canvases lag.
    private static FrameworkPropertyMetadata Visual(object? value) => new(value, FrameworkPropertyMetadataOptions.AffectsRender);
    private static FrameworkPropertyMetadata Extent(object value) => new(value, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure);

    public static readonly DependencyProperty ImageProperty = DependencyProperty.Register(nameof(Image), typeof(BitmapSource), typeof(PixelCanvas), Visual(null));
    public static readonly DependencyProperty GhostProperty = DependencyProperty.Register(nameof(Ghost), typeof(BitmapSource), typeof(PixelCanvas), Visual(null));
    public static readonly DependencyProperty OnionPreviousProperty = DependencyProperty.Register(nameof(OnionPrevious), typeof(BitmapSource), typeof(PixelCanvas), Visual(null));
    public static readonly DependencyProperty OnionNextProperty = DependencyProperty.Register(nameof(OnionNext), typeof(BitmapSource), typeof(PixelCanvas), Visual(null));
    public static readonly DependencyProperty OnionOpacityProperty = DependencyProperty.Register(nameof(OnionOpacity), typeof(double), typeof(PixelCanvas), Visual(0.3));
    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(nameof(Zoom), typeof(double), typeof(PixelCanvas), Extent(8.0));
    public static readonly DependencyProperty PixelWidthProperty = DependencyProperty.Register(nameof(PixelWidth), typeof(int), typeof(PixelCanvas), Extent(16));
    public static readonly DependencyProperty PixelHeightProperty = DependencyProperty.Register(nameof(PixelHeight), typeof(int), typeof(PixelCanvas), Extent(32));
    public static readonly DependencyProperty ShowGridProperty = DependencyProperty.Register(nameof(ShowGrid), typeof(bool), typeof(PixelCanvas), Visual(true));
    public static readonly DependencyProperty PivotProperty = DependencyProperty.Register(nameof(Pivot), typeof(int[]), typeof(PixelCanvas), Visual(null));
    public static readonly DependencyProperty AnchorsProperty = DependencyProperty.Register(nameof(Anchors), typeof(IEnumerable), typeof(PixelCanvas), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, e) => ((PixelCanvas)d).WatchAnchors(e)));
    public static readonly DependencyProperty SelectionProperty = DependencyProperty.Register(nameof(Selection), typeof(PixelRect?), typeof(PixelCanvas), Visual(null));
    /// <summary>A selection of any shape: outlined along its edges (the box when null).</summary>
    public static readonly DependencyProperty SelectionShapeProperty = DependencyProperty.Register(nameof(SelectionShape), typeof(SDCharacterStudio.Editor.Studio.SelectionMask), typeof(PixelCanvas), Visual(null));
    /// <summary>A floating piece's box: corners, scale handles, the rotation handle and the pivot.</summary>
    public static readonly DependencyProperty TransformBoxProperty = DependencyProperty.Register(nameof(TransformBox), typeof(TransformBox), typeof(PixelCanvas), Visual(null));
    /// <summary>A mask drawn over the frame (mask editor), tinted.</summary>
    public static readonly DependencyProperty OverlayProperty = DependencyProperty.Register(nameof(Overlay), typeof(BitmapSource), typeof(PixelCanvas), Visual(null));
    public static readonly DependencyProperty OverlayOpacityProperty = DependencyProperty.Register(nameof(OverlayOpacity), typeof(double), typeof(PixelCanvas), Visual(0.5));

    public BitmapSource? Image { get => (BitmapSource?)GetValue(ImageProperty); set => SetValue(ImageProperty, value); }
    public BitmapSource? Ghost { get => (BitmapSource?)GetValue(GhostProperty); set => SetValue(GhostProperty, value); }
    public BitmapSource? OnionPrevious { get => (BitmapSource?)GetValue(OnionPreviousProperty); set => SetValue(OnionPreviousProperty, value); }
    public BitmapSource? OnionNext { get => (BitmapSource?)GetValue(OnionNextProperty); set => SetValue(OnionNextProperty, value); }
    public double OnionOpacity { get => (double)GetValue(OnionOpacityProperty); set => SetValue(OnionOpacityProperty, value); }
    public double Zoom { get => (double)GetValue(ZoomProperty); set => SetValue(ZoomProperty, value); }
    public int PixelWidth { get => (int)GetValue(PixelWidthProperty); set => SetValue(PixelWidthProperty, value); }
    public int PixelHeight { get => (int)GetValue(PixelHeightProperty); set => SetValue(PixelHeightProperty, value); }
    public bool ShowGrid { get => (bool)GetValue(ShowGridProperty); set => SetValue(ShowGridProperty, value); }
    public int[]? Pivot { get => (int[]?)GetValue(PivotProperty); set => SetValue(PivotProperty, value); }
    public IEnumerable? Anchors { get => (IEnumerable?)GetValue(AnchorsProperty); set => SetValue(AnchorsProperty, value); }
    public PixelRect? Selection { get => (PixelRect?)GetValue(SelectionProperty); set => SetValue(SelectionProperty, value); }
    public SDCharacterStudio.Editor.Studio.SelectionMask? SelectionShape { get => (SDCharacterStudio.Editor.Studio.SelectionMask?)GetValue(SelectionShapeProperty); set => SetValue(SelectionShapeProperty, value); }
    public TransformBox? TransformBox { get => (TransformBox?)GetValue(TransformBoxProperty); set => SetValue(TransformBoxProperty, value); }
    public BitmapSource? Overlay { get => (BitmapSource?)GetValue(OverlayProperty); set => SetValue(OverlayProperty, value); }
    public double OverlayOpacity { get => (double)GetValue(OverlayOpacityProperty); set => SetValue(OverlayOpacityProperty, value); }

    public static readonly RoutedEvent PixelDownEvent = EventManager.RegisterRoutedEvent(nameof(PixelDown), RoutingStrategy.Bubble, typeof(EventHandler<PixelPointerEventArgs>), typeof(PixelCanvas));
    public static readonly RoutedEvent PixelMoveEvent = EventManager.RegisterRoutedEvent(nameof(PixelMove), RoutingStrategy.Bubble, typeof(EventHandler<PixelPointerEventArgs>), typeof(PixelCanvas));
    public static readonly RoutedEvent PixelUpEvent = EventManager.RegisterRoutedEvent(nameof(PixelUp), RoutingStrategy.Bubble, typeof(EventHandler<PixelPointerEventArgs>), typeof(PixelCanvas));
    public static readonly RoutedEvent PixelCancelEvent = EventManager.RegisterRoutedEvent(nameof(PixelCancel), RoutingStrategy.Bubble, typeof(EventHandler<PixelPointerEventArgs>), typeof(PixelCanvas));
    public event EventHandler<PixelPointerEventArgs> PixelDown { add => AddHandler(PixelDownEvent, value); remove => RemoveHandler(PixelDownEvent, value); }
    public event EventHandler<PixelPointerEventArgs> PixelMove { add => AddHandler(PixelMoveEvent, value); remove => RemoveHandler(PixelMoveEvent, value); }
    public event EventHandler<PixelPointerEventArgs> PixelUp { add => AddHandler(PixelUpEvent, value); remove => RemoveHandler(PixelUpEvent, value); }
    public event EventHandler<PixelPointerEventArgs> PixelCancel { add => AddHandler(PixelCancelEvent, value); remove => RemoveHandler(PixelCancelEvent, value); }

    /// <summary>Abort this pointer gesture before releasing capture; a later button-up must do nothing.</summary>
    public void CancelGesture()
    {
        var button = pressed;
        pressed = null;
        lastMoved = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (button != null)
        {
            var (x, y) = hover ?? (0, 0);
            RaiseEvent(new PixelPointerEventArgs(PixelCancelEvent, x, y, button));
        }
    }

    private void WatchAnchors(DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is System.Collections.Specialized.INotifyCollectionChanged old) old.CollectionChanged -= AnchorsChanged;
        if (e.NewValue is System.Collections.Specialized.INotifyCollectionChanged now) now.CollectionChanged += AnchorsChanged;
    }
    private void AnchorsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    private double Scale => Math.Max(1, Math.Round(Zoom));
    private const double Gutter = 24;

    protected override Size MeasureOverride(Size availableSize) => new(PixelWidth * Scale + Gutter * 2, PixelHeight * Scale + Gutter * 2);

    /// <summary>Top-left of the frame inside the control (centred when there's room).</summary>
    private Point Origin => new(Math.Max(Gutter, Math.Round((ActualWidth - PixelWidth * Scale) / 2)), Math.Max(Gutter, Math.Round((ActualHeight - PixelHeight * Scale) / 2)));

    private (int X, int Y) ToPixel(Point point)
    {
        var origin = Origin;
        return ((int)Math.Floor((point.X - origin.X) / Scale), (int)Math.Floor((point.Y - origin.Y) / Scale));
    }

    /// <summary>A point of this control in frame pixels, fractional (zooming keeps the pixel under the cursor still).</summary>
    public Point ToFramePoint(Point point) { var origin = Origin; return new((point.X - origin.X) / Scale, (point.Y - origin.Y) / Scale); }

    /// <summary>A frame pixel position in this control's coordinates, at the current zoom and layout.</summary>
    public Point FromFramePoint(Point frame) { var origin = Origin; return new(origin.X + frame.X * Scale, origin.Y + frame.Y * Scale); }

    /// <summary>What's behind the frame instead of the checkerboard: a picture (the floor where the scene plays) or a colour.</summary>
    public static readonly DependencyProperty BackdropImageProperty = DependencyProperty.Register(nameof(BackdropImage), typeof(BitmapSource), typeof(PixelCanvas), Visual(null));
    public BitmapSource? BackdropImage { get => (BitmapSource?)GetValue(BackdropImageProperty); set => SetValue(BackdropImageProperty, value); }
    public static readonly DependencyProperty BackdropBrushProperty = DependencyProperty.Register(nameof(BackdropBrush), typeof(Brush), typeof(PixelCanvas), Visual(null));
    public Brush? BackdropBrush { get => (Brush?)GetValue(BackdropBrushProperty); set => SetValue(BackdropBrushProperty, value); }

    /// <summary>The brush outline drawn at the pointer (its size in pixels; 0 hides it).</summary>
    public static readonly DependencyProperty BrushSizeProperty = DependencyProperty.Register(nameof(BrushSize), typeof(int), typeof(PixelCanvas), Visual(1));
    public int BrushSize { get => (int)GetValue(BrushSizeProperty); set => SetValue(BrushSizeProperty, value); }
    /// <summary>Mirror the brush outline across the frame's centre (symmetry).</summary>
    public static readonly DependencyProperty SymmetryProperty = DependencyProperty.Register(nameof(Symmetry), typeof(bool), typeof(PixelCanvas), Visual(false));
    public bool Symmetry { get => (bool)GetValue(SymmetryProperty); set => SetValue(SymmetryProperty, value); }
    private static readonly Pen SymmetryPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0x80, 0x5B, 0x9B, 0xF0))), 1) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) });

    protected override void OnRender(DrawingContext dc)
    {
        double s = Scale;
        var o = Origin;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));   // hit testing everywhere
        var frame = new Rect(o.X, o.Y, PixelWidth * s, PixelHeight * s);
        // Checkerboard: 4×4 screen cells per 2 pixels reads well at every zoom.
        double cell = Math.Max(4, s);
        if (BackdropImage is { } floor) dc.DrawImage(floor, frame);
        else if (BackdropBrush is { } fill) dc.DrawRectangle(fill, null, frame);
        else
        {
            dc.DrawRectangle(CheckDark, null, frame);
            dc.PushClip(new RectangleGeometry(frame));
            for (double y = 0; y < frame.Height; y += cell)
                for (double x = ((int)(y / cell) % 2) * cell; x < frame.Width; x += cell * 2)
                    dc.DrawRectangle(CheckLight, null, new Rect(o.X + x, o.Y + y, cell, cell));
            dc.Pop();
        }

        void Draw(BitmapSource? image, double opacity)
        {
            if (image == null || opacity <= 0) return;
            if (opacity < 1) dc.PushOpacity(opacity);
            dc.DrawImage(image, new Rect(o.X, o.Y, image.PixelWidth * s, image.PixelHeight * s));
            if (opacity < 1) dc.Pop();
        }
        Draw(Ghost, 0.35);
        Draw(OnionPrevious, OnionOpacity);
        Draw(OnionNext, OnionOpacity * 0.7);
        Draw(Image, 1);
        Draw(Overlay, OverlayOpacity);

        if (ShowGrid && s >= 4)
        {
            for (int x = 1; x < PixelWidth; x++) dc.DrawLine(GridPen, new Point(o.X + x * s + 0.5, o.Y), new Point(o.X + x * s + 0.5, frame.Bottom));
            for (int y = 1; y < PixelHeight; y++) dc.DrawLine(GridPen, new Point(o.X, o.Y + y * s + 0.5), new Point(frame.Right, o.Y + y * s + 0.5));
        }
        dc.DrawRectangle(null, FramePen, new Rect(frame.X - 0.5, frame.Y - 0.5, frame.Width + 1, frame.Height + 1));

        if (Pivot is { Length: 2 } pivot)
        {
            var c = new Point(o.X + (pivot[0] + 0.5) * s, o.Y + (pivot[1] + 0.5) * s);
            double r = Math.Max(4, s * 0.6);
            dc.DrawLine(PivotPen, new Point(c.X - r, c.Y), new Point(c.X + r, c.Y));
            dc.DrawLine(PivotPen, new Point(c.X, c.Y - r), new Point(c.X, c.Y + r));
        }

        foreach (var anchor in Anchors?.OfType<AnchorMarker>() ?? Enumerable.Empty<AnchorMarker>())
        {
            var c = new Point(o.X + (anchor.X + 0.5) * s, o.Y + (anchor.Y + 0.5) * s);
            dc.DrawEllipse(AnchorBrush, AnchorPen, c, Math.Max(3, s * 0.35), Math.Max(3, s * 0.35));
            var label = new FormattedText(anchor.Name, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, LabelFace, 11, AnchorBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(label, new Point(c.X + Math.Max(5, s * 0.5), c.Y - label.Height / 2));
        }

        if (SelectionShape is { IsEmpty: false, IsRectangle: false } shape && shape.Width == PixelWidth && shape.Height == PixelHeight)
        {
            // Marching ants along the shape's edges: a segment wherever a selected pixel meets an unselected one.
            var edges = new StreamGeometry();
            using (var g = edges.Open())
                foreach (var (x, y) in shape.Cells())
                {
                    double left = o.X + x * s + 0.5, top = o.Y + y * s + 0.5, right = left + s - 1, bottom = top + s - 1;
                    void Edge(double x0, double y0, double x1, double y1) { g.BeginFigure(new Point(x0, y0), false, false); g.LineTo(new Point(x1, y1), true, false); }
                    if (!shape.Contains(x, y - 1)) Edge(left, top, right + 1, top);
                    if (!shape.Contains(x, y + 1)) Edge(left, bottom, right + 1, bottom);
                    if (!shape.Contains(x - 1, y)) Edge(left, top, left, bottom + 1);
                    if (!shape.Contains(x + 1, y)) Edge(right, top, right, bottom + 1);
                }
            edges.Freeze();
            dc.DrawGeometry(null, SelectionShadow, edges);
            dc.DrawGeometry(null, SelectionPen, edges);
        }
        else if (Selection is { } sel && !sel.IsEmpty)
        {
            var rect = new Rect(o.X + sel.X * s + 0.5, o.Y + sel.Y * s + 0.5, sel.Width * s - 1, sel.Height * s - 1);
            dc.DrawRectangle(null, SelectionShadow, rect);
            dc.DrawRectangle(null, SelectionPen, rect);
        }

        if (TransformBox is { } box)
        {
            Point P((double X, double Y) p) => new(o.X + p.X * s, o.Y + p.Y * s);
            var corners = box.Corners.Select(P).ToArray();
            var outline = new StreamGeometry();
            using (var g = outline.Open()) { g.BeginFigure(corners[0], false, true); g.PolyLineTo(corners.Skip(1).ToList(), true, false); }
            outline.Freeze();
            dc.DrawGeometry(null, BoxShadow, outline);
            dc.DrawGeometry(null, BoxPen, outline);
            var top = new Point((corners[0].X + corners[1].X) / 2, (corners[0].Y + corners[1].Y) / 2);
            var rotate = P(box.RotateHandle);
            dc.DrawLine(BoxPen, top, rotate);
            void Handle(Point at) { dc.DrawRectangle(Brushes.White, BoxShadow, new Rect(at.X - 4, at.Y - 4, 8, 8)); }
            for (int i = 0; i < 4; i++) { Handle(corners[i]); Handle(new Point((corners[i].X + corners[(i + 1) % 4].X) / 2, (corners[i].Y + corners[(i + 1) % 4].Y) / 2)); }
            dc.DrawEllipse(PivotFill, BoxShadow, rotate, 5.5, 5.5);
            var centre = P(box.Pivot);
            dc.DrawEllipse(null, BoxShadow, centre, 5, 5);
            dc.DrawEllipse(null, PivotPen, centre, 5, 5);
            dc.DrawLine(PivotPen, new Point(centre.X - 8, centre.Y), new Point(centre.X + 8, centre.Y));
            dc.DrawLine(PivotPen, new Point(centre.X, centre.Y - 8), new Point(centre.X, centre.Y + 8));
        }

        if (Symmetry)
        {
            double axis = o.X + PixelWidth * s / 2;
            dc.DrawLine(SymmetryPen, new Point(axis, frame.Top - 6), new Point(axis, frame.Bottom + 6));
        }

        if (hover is { } h && h.X >= 0 && h.Y >= 0 && h.X < PixelWidth && h.Y < PixelHeight)
        {
            // The brush's footprint (and its mirror), like Aseprite's brush preview.
            var cells = SDCharacterStudio.Editor.Studio.PixelTools.Brush(h.X, h.Y, Math.Max(1, BrushSize)).ToList();
            if (Symmetry) cells.AddRange(cells.Select(c => (PixelWidth - 1 - c.X, c.Y)).ToList());
            bool single = cells.Distinct().Count() == 1;
            foreach (var (cx, cy) in cells.Distinct())
                if (cx >= 0 && cy >= 0 && cx < PixelWidth && cy < PixelHeight)
                    dc.DrawRectangle(single ? null : HoverFill, single ? HoverPen : null, new Rect(o.X + cx * s + 0.5, o.Y + cy * s + 0.5, s - 1, s - 1));
        }
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.ChangedButton is not (MouseButton.Left or MouseButton.Right) || pressed != null) return;
        pressed = e.ChangedButton;
        lastMoved = null;
        if (!CaptureMouse()) { pressed = null; return; }
        var at = e.GetPosition(this);
        var (x, y) = ToPixel(at);
        var f = ToFramePoint(at);
        RaiseEvent(new PixelPointerEventArgs(PixelDownEvent, x, y, e.ChangedButton, f.X, f.Y));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var at = e.GetPosition(this);
        var point = ToPixel(at);
        var f = ToFramePoint(at);
        if (hover != point) { hover = point; InvalidateVisual(); }
        // Handles need sub-pixel moves; everything else only hears about whole pixels.
        if (hover == point && (TransformBox != null || lastMoved != point)) { lastMoved = point; RaiseEvent(new PixelPointerEventArgs(PixelMoveEvent, point.X, point.Y, pressed, f.X, f.Y)); }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (pressed != e.ChangedButton) return;
        pressed = null;
        ReleaseMouseCapture();
        var at = e.GetPosition(this);
        var (x, y) = ToPixel(at);
        var f = ToFramePoint(at);
        RaiseEvent(new PixelPointerEventArgs(PixelUpEvent, x, y, e.ChangedButton, f.X, f.Y));
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        CancelGesture();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (pressed == null) { hover = null; InvalidateVisual(); }
    }
}
