using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Views.Studio;

public partial class PoseEditorView : UserControl
{
    private readonly DispatcherTimer timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch clock = new();
    /// <summary>Space is down: a drag pans (Aseprite, Photoshop); a tap without a drag plays.</summary>
    private bool spaceHeld, spacePanned;
    private Point? panFrom;
    private Point panOffset;
    /// <summary>The preview keeps playing while you edit (D-49).</summary>
    private readonly DispatcherTimer previewTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch previewClock = new();
    private bool syncingFrames;
    private Point? frameDragStart;
    private int frameDragIndex = -1;
    private const string FrameFormat = "SDCS.PoseFrame";

    public PoseEditorView()
    {
        InitializeComponent();
        timer.Tick += (_, _) =>
        {
            if (Pose is not { IsPlaying: true } pose) { timer.Stop(); return; }
            double elapsed = clock.Elapsed.TotalMilliseconds;
            clock.Restart();
            pose.Tick(elapsed);
        };
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is PoseDocumentViewModel old) old.PropertyChanged -= Pose_PropertyChanged;
            if (e.NewValue is PoseDocumentViewModel now) now.PropertyChanged += Pose_PropertyChanged;
            Dispatcher.BeginInvoke(SyncFrames, DispatcherPriority.Loaded);
        };
        previewTimer.Tick += (_, _) => { double ms = previewClock.Elapsed.TotalMilliseconds; previewClock.Restart(); Pose?.TickPreview(Math.Min(ms, 250)); };
        Loaded += (_, _) => { previewClock.Restart(); previewTimer.Start(); SyncFrames(); };
        Unloaded += (_, _) => { timer.Stop(); previewTimer.Stop(); };
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;
    }

    private PoseDocumentViewModel? Pose => DataContext as PoseDocumentViewModel;

    private void Pose_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PoseDocumentViewModel.IsPlaying) && Pose?.IsPlaying == true) { clock.Restart(); timer.Start(); }
        if (e.PropertyName == nameof(PoseDocumentViewModel.FrameIndex)) SyncFrames();
    }

    private static PointerButton ButtonOf(MouseButton? button) => button == MouseButton.Right ? PointerButton.Secondary : PointerButton.Primary;

    private void Canvas_PixelDown(object? sender, PixelPointerEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        Pose?.PointerDown(e.X, e.Y, ButtonOf(e.Button), modifiers.HasFlag(ModifierKeys.Shift), modifiers.HasFlag(ModifierKeys.Control), modifiers.HasFlag(ModifierKeys.Alt), e.FX, e.FY);
    }

    private void Canvas_PixelMove(object? sender, PixelPointerEventArgs e) => Pose?.PointerMove(e.X, e.Y, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), e.FX, e.FY);

    private void Canvas_PixelUp(object? sender, PixelPointerEventArgs e) => Pose?.PointerUp(e.X, e.Y);

    // ---------------- zoom and pan ----------------

    /// <summary>The wheel zooms at the pointer (Aseprite); Shift+wheel scrolls sideways.</summary>
    private void Canvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Pose is not { } pose) return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) { CanvasScroller.ScrollToHorizontalOffset(CanvasScroller.HorizontalOffset - e.Delta); e.Handled = true; return; }
        ZoomAt(e.GetPosition(CanvasScroller), () => { if (e.Delta > 0) pose.ZoomInCommand.Execute(null); else pose.ZoomOutCommand.Execute(null); });
        e.Handled = true;
    }

    /// <summary>Change the zoom keeping the frame pixel under <paramref name="at"/> (viewport coordinates) where it is.</summary>
    private void ZoomAt(Point at, Action zoom)
    {
        var frame = Canvas.ToFramePoint(CanvasScroller.TranslatePoint(at, Canvas));
        zoom();
        CanvasScroller.UpdateLayout();
        var after = Canvas.FromFramePoint(frame);
        CanvasScroller.ScrollToHorizontalOffset(Math.Max(0, after.X - at.X));
        CanvasScroller.ScrollToVerticalOffset(Math.Max(0, after.Y - at.Y));
    }

    private void Fit_Click(object sender, RoutedEventArgs e) => Fit();

    /// <summary>The biggest whole zoom that shows the whole frame.</summary>
    private void Fit()
    {
        if (Pose is not { } pose || pose.Width <= 0 || pose.Height <= 0) return;
        double room = Math.Min((CanvasScroller.ViewportWidth - 56) / pose.Width, (CanvasScroller.ViewportHeight - 56) / pose.Height);
        pose.Zoom = Math.Clamp(Math.Floor(room), 1, 64);
        CanvasScroller.UpdateLayout();
        CanvasScroller.ScrollToHorizontalOffset((CanvasScroller.ExtentWidth - CanvasScroller.ViewportWidth) / 2);
        CanvasScroller.ScrollToVerticalOffset((CanvasScroller.ExtentHeight - CanvasScroller.ViewportHeight) / 2);
    }

    private void Scroller_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!(e.ChangedButton == MouseButton.Middle || (e.ChangedButton == MouseButton.Left && spaceHeld))) return;
        panFrom = e.GetPosition(CanvasScroller);
        panOffset = new Point(CanvasScroller.HorizontalOffset, CanvasScroller.VerticalOffset);
        spacePanned = true;
        CanvasScroller.CaptureMouse();
        e.Handled = true;
    }

    private void Scroller_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (panFrom is not { } from) return;
        var now = e.GetPosition(CanvasScroller);
        CanvasScroller.ScrollToHorizontalOffset(panOffset.X - (now.X - from.X));
        CanvasScroller.ScrollToVerticalOffset(panOffset.Y - (now.Y - from.Y));
        e.Handled = true;
    }

    private void Scroller_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (panFrom == null || e.ChangedButton is not (MouseButton.Middle or MouseButton.Left)) return;
        panFrom = null;
        CanvasScroller.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void Scroller_LostMouseCapture(object sender, MouseEventArgs e) => panFrom = null;

    private void Shortcuts_MouseDown(object sender, MouseButtonEventArgs e) { if (Pose is { } pose) pose.IsShortcutsOpen = false; e.Handled = true; }

    private void AddLayer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { ContextMenu: { } menu } button)
        {
            menu.DataContext = button.DataContext;
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    // ---------------- the frame strip: pick several, drag to reorder ----------------

    /// <summary>The strip shows the frame on the canvas as selected (and keeps a multi-pick that includes it).</summary>
    private void SyncFrames()
    {
        if (Pose is not { } pose || FramesList == null) return;
        if (pose.Frames.ElementAtOrDefault(pose.FrameIndex) is not { } current || FramesList.SelectedItems.Contains(current)) return;
        syncingFrames = true;
        try { FramesList.SelectedItems.Clear(); FramesList.SelectedItem = current; FramesList.ScrollIntoView(current); }
        finally { syncingFrames = false; }
        pose.PickFrames(new[] { pose.FrameIndex });
    }

    private void Frames_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (syncingFrames || Pose is not { } pose) return;
        var picked = FramesList.SelectedItems.Cast<PoseFrameItem>().Select(f => f.Index).ToList();
        if (picked.Count == 0) { SyncFrames(); return; }   // never nothing: the canvas always shows a frame
        int? current = e.AddedItems.Count > 0 && e.AddedItems[^1] is PoseFrameItem added ? added.Index : null;
        if (current == null && !picked.Contains(pose.FrameIndex)) current = picked[0];
        pose.PickFrames(picked, current);
    }

    private int FrameIndexAt(object source)
    {
        for (var node = source as DependencyObject; node != null && node != FramesList; node = System.Windows.Media.VisualTreeHelper.GetParent(node))
            if (node is ListBoxItem item) return FramesList.ItemContainerGenerator.IndexFromContainer(item);
        return -1;
    }

    private void Frames_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        frameDragStart = null; frameDragIndex = -1;
        for (var node = e.OriginalSource as DependencyObject; node != null; node = System.Windows.Media.VisualTreeHelper.GetParent(node))
            if (node is TextBox) return;   // editing a duration
        frameDragIndex = FrameIndexAt(e.OriginalSource);
        if (frameDragIndex >= 0) frameDragStart = e.GetPosition(FramesList);
    }

    private void Frames_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || frameDragStart is not { } start || frameDragIndex < 0) return;
        var now = e.GetPosition(FramesList);
        if (Math.Abs(now.X - start.X) < SystemParameters.MinimumHorizontalDragDistance * 3) return;
        frameDragStart = null;
        DragDrop.DoDragDrop(FramesList, new DataObject(FrameFormat, frameDragIndex), DragDropEffects.Move | DragDropEffects.Copy);
    }

    private void Frames_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = !e.Data.GetDataPresent(FrameFormat) ? DragDropEffects.None : e.KeyStates.HasFlag(DragDropKeyStates.ControlKey) ? DragDropEffects.Copy : DragDropEffects.Move;
        e.Handled = true;
    }

    /// <summary>Drop a dragged frame before or after the frame under the pointer (Ctrl: a copy).</summary>
    private void Frames_Drop(object sender, DragEventArgs e)
    {
        if (Pose is not { } pose || e.Data.GetData(FrameFormat) is not int from) return;
        int over = FrameIndexAt(e.OriginalSource), to = pose.Frames.Count;
        if (over >= 0 && FramesList.ItemContainerGenerator.ContainerFromIndex(over) is FrameworkElement item)
            to = e.GetPosition(item).X > item.ActualWidth / 2 ? over + 1 : over;
        pose.MoveFrameTo(from, to, e.KeyStates.HasFlag(DragDropKeyStates.ControlKey));
        e.Handled = true;
    }

    // ---------------- ready-made poses ----------------

    private void Preset_MouseEnter(object sender, MouseEventArgs e) { if (sender is FrameworkElement { Tag: string id }) Pose?.PreviewPreset(id); }
    private void Preset_MouseLeave(object sender, MouseEventArgs e) => Pose?.PreviewPreset(null);

    // ---------------- keys ----------------

    private static bool Typing(KeyEventArgs e) => e.OriginalSource is TextBox or ComboBox or ComboBoxItem || Keyboard.FocusedElement is TextBox;

    /// <summary>Single-key tools while the canvas (not a text box) has the keyboard.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Pose is not { } pose || Typing(e)) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        if (key == Key.Space)
        {
            if (!e.IsRepeat && !spaceHeld) { spaceHeld = true; spacePanned = false; Canvas.Cursor = Cursors.SizeAll; }
            e.Handled = true;
            return;
        }
        // Alt alone is the eyedropper on the canvas: keep the window's menu from taking the keyboard.
        if (key is Key.LeftAlt or Key.RightAlt) { if (Canvas.IsKeyboardFocused) e.Handled = true; return; }
        // Delete in the frame strip deletes the frames picked there (Shift or Ctrl+click to pick several).
        if (key == Key.Delete && FramesList.IsKeyboardFocusWithin) { pose.DeleteFrameCommand.Execute(null); e.Handled = true; return; }
        if (key == Key.Escape && pose.IsShortcutsOpen) { pose.IsShortcutsOpen = false; e.Handled = true; return; }
        if (modifiers.HasFlag(ModifierKeys.Control) && key is Key.OemPlus or Key.Add) { pose.ZoomInCommand.Execute(null); e.Handled = true; return; }
        if (modifiers.HasFlag(ModifierKeys.Control) && key is Key.OemMinus or Key.Subtract) { pose.ZoomOutCommand.Execute(null); e.Handled = true; return; }
        if (modifiers.HasFlag(ModifierKeys.Control) && key is Key.D0 or Key.NumPad0) { Fit(); e.Handled = true; return; }
        if (key == Key.P && modifiers == ModifierKeys.None) { pose.RawSlots = !pose.RawSlots; e.Handled = true; return; }
        if (modifiers.HasFlag(ModifierKeys.Alt)) return;
        // Ctrl+Z/Y/S stay with the window.
        if (modifiers.HasFlag(ModifierKeys.Control) && key is Key.Z or Key.Y or Key.S or Key.E or Key.N or Key.O) return;
        if (pose.HandleKey(key.ToString(), modifiers.HasFlag(ModifierKeys.Control), modifiers.HasFlag(ModifierKeys.Shift))) e.Handled = true;
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftAlt or Key.RightAlt && Canvas.IsKeyboardFocused) { e.Handled = true; return; }
        if (key != Key.Space || !spaceHeld) return;
        spaceHeld = false;
        Canvas.Cursor = Cursors.Cross;
        if (!spacePanned && Pose is { } pose && !Typing(e)) pose.HandleKey("Space", false, false);   // a tap plays
        e.Handled = true;
    }
}
