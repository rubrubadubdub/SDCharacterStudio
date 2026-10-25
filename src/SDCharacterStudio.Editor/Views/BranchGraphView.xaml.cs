using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Storyboard;
using SDCharacterStudio.Editor.ViewModels;
using Path = System.Windows.Shapes.Path;

namespace SDCharacterStudio.Editor.Views;

/// <summary>Derived control flow. Node selection shares the editor's normal Properties selection.</summary>
public partial class BranchGraphView : UserControl
{
    private StoryboardViewModel? board;
    private double scale = 1;
    private Vector offset;
    private Point press;
    private bool panning, fitOnRender = true;
    private Size contentSize;
    private Dictionary<string, Point> nodePositions = new(StringComparer.Ordinal);

    public BranchGraphView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Subscribe();
        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) => { if (board != null) board.PropertyChanged -= Changed; board = null; };
        SizeChanged += (_, _) => { if (fitOnRender) Fit(); };
    }

    private void Subscribe()
    {
        if (board != null) board.PropertyChanged -= Changed;
        board = DataContext as StoryboardViewModel;
        if (board != null) board.PropertyChanged += Changed;
        Render();
    }

    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(StoryboardViewModel.Graph) or nameof(StoryboardViewModel.GraphMode) or "Selection") Render();
        if (e.PropertyName == nameof(StoryboardViewModel.GraphMode) && board?.GraphMode == true)
            Dispatcher.BeginInvoke(FocusSelection, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void Render()
    {
        GraphCanvas.Children.Clear();
        if (board == null) return;
        var graph = board.Graph;
        var positions = new Dictionary<string, Point>(StringComparer.Ordinal);
        nodePositions = positions;
        int column = 0;
        foreach (var lane in graph.Nodes.GroupBy(n => n.Lane))
        {
            AddText(LaneNames.Title(lane.Key), new Point(24 + column * 270, 12), Brushes.LightGray, 13);
            int row = 0;
            foreach (var node in lane)
                positions[node.Id] = new Point(24 + column * 270, 44 + row++ * 104);
            column++;
        }
        contentSize = positions.Count == 0 ? new(240, 140) : new(positions.Values.Max(p => p.X) + 260, positions.Values.Max(p => p.Y) + 120);
        GraphCanvas.Width = contentSize.Width; GraphCanvas.Height = contentSize.Height;
        foreach (var edge in graph.Edges)
        {
            if (!positions.TryGetValue(edge.From, out var from) || !positions.TryGetValue(edge.To, out var to)) continue;
            Point start, end, control1, control2;
            if (from.X == to.X && to.Y > from.Y)
            {
                start = new(from.X + 100, from.Y + 72); end = new(to.X + 100, to.Y);
                control1 = new(start.X, start.Y + 16); control2 = new(end.X, end.Y - 16);
            }
            else
            {
                start = new(from.X + 200, from.Y + 36); end = new(to.X, to.Y + 36);
                double bend = Math.Max(36, Math.Abs(end.X - start.X) / 2);
                if (from == to) { end = new(to.X + 200, to.Y + 14); control1 = new(start.X + 52, start.Y + 48); control2 = new(end.X + 52, end.Y - 48); }
                else { control1 = new(start.X + bend, start.Y); control2 = new(end.X - bend, end.Y); }
            }
            Brush color = edge.Missing ? Brushes.IndianRed : edge.Kind switch { GraphEdgeKind.Choice => Brushes.MediumPurple, GraphEdgeKind.Goto => Brushes.Orange, GraphEdgeKind.BranchReturn => Brushes.LightSeaGreen, _ => Brushes.SlateGray };
            DrawConnector(GraphCanvas, start, control1, control2, end, color, edge.Kind == GraphEdgeKind.BranchReturn);
            if (edge.Label is { Length: > 0 }) AddText(edge.Label, new Point((start.X + end.X) / 2, (start.Y + end.Y) / 2 - 14), color, 10);
        }
        foreach (var node in graph.Nodes)
        {
            bool selected = board.Shell.Selection is BeatSelection beat && beat.Lane == node.Lane && beat.Index == node.Index
                || board.Shell.Selection is BranchSelection branch && node.Index < 0 && branch.Lane == node.Lane;
            var card = new Border
            {
                Tag = node, Width = 200, Height = 72, Padding = new Thickness(8), CornerRadius = new CornerRadius(5),
                Background = selected ? Brushes.DarkGoldenrod : node.Missing ? Brushes.Maroon : Brushes.DarkSlateGray,
                BorderBrush = node.Beat is GotoBeat || BeatFlow.IsFork(node.Beat) ? Brushes.SteelBlue : Brushes.SlateGray, BorderThickness = new Thickness(1.5),
                ToolTip = node.Label,
                Child = new TextBlock { Text = node.Label, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis }
            };
            card.MouseLeftButtonDown += (_, e) =>
            {
                if (!node.Missing && board?.Shell.Scene?.Id is { } id)
                    board.Shell.Select(node.Index < 0 ? node.Lane == LaneNames.Main ? new SceneSelection(id) : new BranchSelection(id, node.Lane) : new BeatSelection(id, node.Lane, node.Index));
                e.Handled = true;
            };
            Canvas.SetLeft(card, positions[node.Id].X); Canvas.SetTop(card, positions[node.Id].Y);
            GraphCanvas.Children.Add(card);
        }
        Diagnostics.Text = string.Join("  ·  ", graph.Diagnostics);
        if (fitOnRender) Fit(); else ApplyTransform();
    }

    internal static void DrawConnector(Canvas canvas, Point start, Point c1, Point c2, Point end, Brush brush, bool dashed = false)
    {
        var figure = new PathFigure { StartPoint = start };
        figure.Segments.Add(new BezierSegment(c1, c2, end, true));
        canvas.Children.Add(new Path { Data = new PathGeometry(new[] { figure }), Stroke = brush, StrokeThickness = 1.7, StrokeDashArray = dashed ? new DoubleCollection { 4, 3 } : null, IsHitTestVisible = false });
        Vector direction = c2 - end;
        if (direction.Length < .1) direction = start - end;
        if (direction.Length < .1) return;
        direction.Normalize(); var perpendicular = new Vector(-direction.Y, direction.X);
        canvas.Children.Add(new Polygon { Points = new PointCollection { end, end + direction * 9 + perpendicular * 4, end + direction * 9 - perpendicular * 4 }, Fill = brush, IsHitTestVisible = false });
    }
    private void AddText(string text, Point position, Brush color, double size)
    {
        var label = new TextBlock { Text = text, Foreground = color, FontSize = size, MaxWidth = 240, TextTrimming = TextTrimming.CharacterEllipsis, Background = new SolidColorBrush(Color.FromRgb(16, 20, 27)), IsHitTestVisible = false };
        Canvas.SetLeft(label, position.X); Canvas.SetTop(label, position.Y); GraphCanvas.Children.Add(label);
    }
    private void ApplyTransform() => GraphCanvas.RenderTransform = new TransformGroup { Children = { new ScaleTransform(scale, scale), new TranslateTransform(offset.X, offset.Y) } };
    public void Fit()
    {
        if (Viewport.ActualWidth <= 0 || Viewport.ActualHeight <= 0 || contentSize.Width <= 0) return;
        scale = Math.Clamp(Math.Min(Viewport.ActualWidth / contentSize.Width, Viewport.ActualHeight / contentSize.Height), .1, 1.5);
        offset = new(8, 8); fitOnRender = false; ApplyTransform();
    }
    private void Fit_Click(object sender, RoutedEventArgs e) => Fit();
    public void FocusSelection()
    {
        if (board == null || Viewport.ActualWidth <= 0 || Viewport.ActualHeight <= 0) return;
        var node = board.Shell.Selection switch
        {
            BeatSelection beat => board.Graph.Find(beat.Lane, beat.Index),
            BranchSelection branch => board.Graph.Nodes.FirstOrDefault(n => n.Lane == branch.Lane && !n.Missing),
            _ => board.Graph.Nodes.FirstOrDefault(n => !n.Missing)
        };
        if (node == null || !nodePositions.TryGetValue(node.Id, out var point)) return;
        scale = Math.Min(1, Math.Max(.4, Viewport.ActualWidth / 260));
        offset = new Vector(Viewport.ActualWidth / 2, Viewport.ActualHeight / 2) - new Vector(point.X + 100, point.Y + 36) * scale;
        fitOnRender = false; ApplyTransform();
    }
    private void Selection_Click(object sender, RoutedEventArgs e) => FocusSelection();
    private void Canvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        Point anchor = e.GetPosition(Viewport);
        double next = Math.Clamp(scale * (e.Delta > 0 ? 1.15 : 1 / 1.15), .1, 3);
        offset = (Vector)anchor - ((Vector)anchor - offset) * (next / scale);
        scale = next; fitOnRender = false; ApplyTransform(); e.Handled = true;
    }
    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    { press = e.GetPosition(Viewport); panning = true; Viewport.CaptureMouse(); e.Handled = true; }
    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!panning) return;
        Point now = e.GetPosition(Viewport); offset += now - press; press = now; fitOnRender = false; ApplyTransform();
    }
    private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    { panning = false; Viewport.ReleaseMouseCapture(); }
    private void Viewport_LostMouseCapture(object sender, MouseEventArgs e) => panning = false;
}
