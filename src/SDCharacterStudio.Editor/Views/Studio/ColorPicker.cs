using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SDCharacterStudio.Editor.Studio;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Views.Studio;

/// <summary>
/// A hue, saturation and value colour picker (D-49): a saturation/value square, a hue bar and a hex box. Its
/// <see cref="Color"/> is ARGB like the palette's.
/// </summary>
public sealed class ColorPicker : Control
{
    private const double SquareWidth = 180, SquareHeight = 120, HueHeight = 14, Gap = 8;
    private double hue, saturation = 1, value = 1;
    private bool updating;
    private int dragging;   // 1: the square, 2: the hue bar
    private readonly TextBox hex = new() { Width = 94, Margin = new Thickness(0, 0, 6, 0), VerticalContentAlignment = VerticalAlignment.Center };

    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(nameof(Color), typeof(uint), typeof(ColorPicker),
        new FrameworkPropertyMetadata(0xFFFFFFFFu, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender, (d, e) => ((ColorPicker)d).ColorChanged()));
    public uint Color { get => (uint)GetValue(ColorProperty); set => SetValue(ColorProperty, value); }

    public ColorPicker()
    {
        Focusable = true;
        Width = SquareWidth; Height = SquareHeight + Gap + HueHeight + Gap + 30;
        hex.LostFocus += (_, _) => ApplyHex();
        hex.KeyDown += (_, e) => { if (e.Key == Key.Enter) { ApplyHex(); e.Handled = true; } };
        AddVisualChild(hex); AddLogicalChild(hex);
        ColorChanged();
    }

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => hex;
    protected override Size MeasureOverride(Size constraint) { hex.Measure(new Size(94, 30)); return new Size(Width, Height); }
    protected override Size ArrangeOverride(Size size) { hex.Arrange(new Rect(0, SquareHeight + Gap + HueHeight + Gap, 94, 28)); return size; }

    private void ColorChanged()
    {
        if (updating) return;
        var c = StudioImages.ToColor(Color | 0xFF000000);
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0, max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        if (d > 0) hue = (max == r ? (g - b) / d % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4) * 60;
        if (hue < 0) hue += 360;
        saturation = max == 0 ? 0 : d / max; value = max;
        hex.Text = StudioImages.Hex(Color);
        InvalidateVisual();
    }

    private void ApplyHex()
    {
        if (PaletteViewModel.Parse(hex.Text) is { } parsed) Color = parsed | 0xFF000000;
        else hex.Text = StudioImages.Hex(Color);
    }

    public static uint FromHsv(double h, double s, double v)
    {
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        var (r, g, b) = (h % 360) switch { < 60 => (c, x, 0.0), < 120 => (x, c, 0.0), < 180 => (0.0, c, x), < 240 => (0.0, x, c), < 300 => (x, 0.0, c), _ => (c, 0.0, x) };
        return 0xFF000000 | (uint)Math.Round((r + m) * 255) << 16 | (uint)Math.Round((g + m) * 255) << 8 | (uint)Math.Round((b + m) * 255);
    }

    private void SetFromHsv()
    {
        updating = true;
        try { Color = FromHsv(hue, saturation, value); hex.Text = StudioImages.Hex(Color); }
        finally { updating = false; }
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var square = new Rect(0, 0, SquareWidth, SquareHeight);
        dc.DrawRectangle(new SolidColorBrush(StudioImages.ToColor(FromHsv(hue, 1, 1))), null, square);
        dc.DrawRectangle(new LinearGradientBrush(Colors.White, System.Windows.Media.Color.FromArgb(0, 255, 255, 255), 0), null, square);
        dc.DrawRectangle(new LinearGradientBrush(System.Windows.Media.Color.FromArgb(0, 0, 0, 0), Colors.Black, 90), null, square);
        var thumb = new Point(saturation * SquareWidth, (1 - value) * SquareHeight);
        dc.DrawEllipse(null, new Pen(Brushes.Black, 3), thumb, 5, 5);
        dc.DrawEllipse(null, new Pen(Brushes.White, 1.5), thumb, 5, 5);

        var bar = new Rect(0, SquareHeight + Gap, SquareWidth, HueHeight);
        var rainbow = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        for (int i = 0; i <= 6; i++) rainbow.GradientStops.Add(new GradientStop(StudioImages.ToColor(FromHsv(i * 60 % 360 + (i == 6 ? 359.9 : 0), 1, 1)), i / 6.0));
        dc.DrawRoundedRectangle(rainbow, null, bar, 3, 3);
        double hx = hue / 360 * SquareWidth;
        dc.DrawRectangle(null, new Pen(Brushes.White, 2), new Rect(hx - 2, bar.Top - 2, 4, bar.Height + 4));

        var swatch = new Rect(102, SquareHeight + Gap + HueHeight + Gap, SquareWidth - 102, 28);
        dc.DrawRoundedRectangle(new SolidColorBrush(StudioImages.ToColor(Color | 0xFF000000)), new Pen(new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x88, 255, 255, 255)), 1), swatch, 4, 4);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        var p = e.GetPosition(this);
        dragging = p.Y <= SquareHeight ? 1 : p.Y <= SquareHeight + Gap + HueHeight + 2 ? 2 : 0;
        if (dragging == 0) return;
        CaptureMouse();
        Drag(p);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (dragging != 0) Drag(e.GetPosition(this)); }
    protected override void OnMouseUp(MouseButtonEventArgs e) { base.OnMouseUp(e); if (dragging != 0) { dragging = 0; ReleaseMouseCapture(); } }

    private void Drag(Point p)
    {
        if (dragging == 1) { saturation = Math.Clamp(p.X / SquareWidth, 0, 1); value = Math.Clamp(1 - p.Y / SquareHeight, 0, 1); }
        else hue = Math.Clamp(p.X / SquareWidth, 0, 0.9999) * 360;
        SetFromHsv();
    }
}
