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
    private CanvasNavigation? navigation;
    private Window? ownerWindow;
    private bool changingDocument;
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
        Canvas.PixelCancel += (_, _) => { if (!changingDocument) Pose?.CancelStroke(); ResetNavigation(); };
        Loaded += (_, _) => navigation = new(CanvasScroller, Canvas, () => Pose?.Zoom ?? 8, z => { if (Pose != null) Pose.Zoom = z; }, () => Pose?.Width ?? 0, () => Pose?.Height ?? 0, CancelActiveStroke);
        timer.Tick += (_, _) =>
        {
            if (Pose is not { IsPlaying: true } pose) { timer.Stop(); return; }
            double elapsed = clock.Elapsed.TotalMilliseconds;
            clock.Restart();
            pose.Tick(elapsed);
        };
        DataContextChanged += (_, e) =>
        {
            changingDocument = true;
            try
            {
                if (e.OldValue is PoseDocumentViewModel old) { if (old.IsStroking || Canvas.IsMouseCaptured) old.CancelStroke(); old.PropertyChanged -= Pose_PropertyChanged; }
                // Capture still belongs to the old document. Its cancellation event
                // must not reach a floating piece in the newly assigned context.
                ResetNavigation();
            }
            finally { changingDocument = false; }
            if (e.NewValue is PoseDocumentViewModel now) now.PropertyChanged += Pose_PropertyChanged;
            Dispatcher.BeginInvoke(SyncFrames, DispatcherPriority.Loaded);
        };
        previewTimer.Tick += (_, _) => { double ms = previewClock.Elapsed.TotalMilliseconds; previewClock.Restart(); Pose?.TickPreview(Math.Min(ms, 250)); };
        Loaded += (_, _) => { previewClock.Restart(); previewTimer.Start(); SyncFrames(); };
        Unloaded += (_, _) => { timer.Stop(); previewTimer.Stop(); ResetNavigation(); AttachWindow(null); };
        Loaded += (_, _) => AttachWindow(Window.GetWindow(this));
        LostKeyboardFocus += (_, _) => { if (!Canvas.IsKeyboardFocused) ResetNavigation(); };
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;
    }

    private PoseDocumentViewModel? Pose => DataContext as PoseDocumentViewModel;
    private void AttachWindow(Window? window)
    {
        if (ownerWindow == window) return;
        if (ownerWindow != null) ownerWindow.Deactivated -= Window_Deactivated;
        ownerWindow = window;
        if (ownerWindow != null) ownerWindow.Deactivated += Window_Deactivated;
    }
    private void CancelActiveStroke() { if (!changingDocument && Pose is { IsStroking: true } pose) pose.CancelStroke(); }
    private void ResetNavigation() { spaceHeld = false; spacePanned = true; navigation?.Reset(); Canvas.Cursor = Cursors.Cross; Canvas.CancelGesture(); CancelActiveStroke(); }
    private void Window_Deactivated(object? sender, EventArgs e) => ResetNavigation();

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
        navigation?.Wheel(e);
    }

    private void Fit_Click(object sender, RoutedEventArgs e) => Fit();

    /// <summary>The biggest whole zoom that shows the whole frame.</summary>
    private void Fit() => navigation?.Fit();

    private void Scroller_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        bool held = spaceHeld;
        if (e.ChangedButton != MouseButton.Middle && !(e.ChangedButton == MouseButton.Left && held)) return;
        navigation?.BeginPan(e, held);
        // Cancelling the preceding canvas gesture may reset input state. The same
        // physical Space press still owns every pan until its matching key-up.
        spaceHeld = held;
        spacePanned = true;
    }

    private void Scroller_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        navigation?.MovePan(e);
    }

    private void Scroller_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (navigation?.EndPan(e) == true && spaceHeld) Canvas.Cursor = Cursors.SizeAll;
    }

    private void Scroller_LostMouseCapture(object sender, MouseEventArgs e) { if (e.OriginalSource == CanvasScroller && navigation?.IsPanning == true) ResetNavigation(); }

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

    private static bool Typing(KeyEventArgs e) => CanvasNavigation.IsInputControl(e);

    /// <summary>Single-key tools while the canvas (not a text box) has the keyboard.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Pose is not { } pose) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            bool pointerActive = Canvas.IsMouseCaptured || pose.IsStroking || navigation?.IsPanning == true;
            bool shortcutsOpen = pose.IsShortcutsOpen;
            ResetNavigation();
            if (shortcutsOpen) pose.IsShortcutsOpen = false;
            else if (!pointerActive && !Typing(e)) pose.HandleKey("Escape", false, false);
            e.Handled = pointerActive || shortcutsOpen || !Typing(e);
            return;
        }
        if (Typing(e)) return;
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
