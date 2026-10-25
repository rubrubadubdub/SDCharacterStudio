using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Views.Studio;

public partial class AnimationEditorView : UserControl
{
    private readonly DispatcherTimer timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch clock = new();

    public AnimationEditorView()
    {
        InitializeComponent();
        timer.Tick += (_, _) =>
        {
            if (Document is not { IsPlaying: true } clip) { timer.Stop(); return; }
            double elapsed = clock.Elapsed.TotalMilliseconds;
            clock.Restart();
            clip.Tick(elapsed);
        };
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is AnimationDocumentViewModel old) old.PropertyChanged -= Clip_PropertyChanged;
            if (e.NewValue is AnimationDocumentViewModel now) now.PropertyChanged += Clip_PropertyChanged;
        };
        Unloaded += (_, _) => timer.Stop();
        Stage.ActorClicked += id => Document?.SelectActor(id);
        Stage.DragStarted += id => Document?.BeginDrag(id);
        Stage.Dropped += (x, y, pixels) => Document?.EndDrag(x, y, pixels);
        Stage.DragCancelled += () => Document?.CancelDrag();
        // Clicking the timeline outside a key also clears the key selection, which shows the clip's own properties.
        Timeline.Scrubbed += t => { if (Document is { } c) { c.IsPlaying = false; c.SelectedKey = null; c.Playhead = t; } };
        Timeline.KeyClicked += key => { if (Document is { } c) { c.SelectedKey = key; c.Playhead = key.Key.At; } };
        Timeline.KeyMoved += (key, t) => Document?.MoveKey(key, t);
        Timeline.RowDoubleClicked += (row, t) => Document?.AddKey(row, t);
        Timeline.LengthChanged += t => Document?.SetDuration(t);
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private AnimationDocumentViewModel? Document => DataContext as AnimationDocumentViewModel;

    private void Clip_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AnimationDocumentViewModel.IsPlaying) && Document?.IsPlaying == true) { clock.Restart(); timer.Start(); }
        if (e.PropertyName == nameof(AnimationDocumentViewModel.TimelineVersion)) Timeline.InvalidateMeasure();
    }

    private void StageZoomIn_Click(object sender, RoutedEventArgs e) { if (Document is { } c) c.StageZoom = Math.Min(8, Math.Round(c.StageZoom) + 1); }
    private void StageZoomOut_Click(object sender, RoutedEventArgs e) { if (Document is { } c) c.StageZoom = Math.Max(1, Math.Round(c.StageZoom) - 1); }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Document is not { } clip || e.OriginalSource is TextBox or ComboBox or ComboBoxItem || Keyboard.FocusedElement is TextBox) return;
        switch (e.Key)
        {
            case Key.Space: clip.TogglePlayCommand.Execute(null); e.Handled = true; break;
            case Key.Delete when clip.SelectedKey != null: clip.DeleteKey(); e.Handled = true; break;
            case Key.Home: clip.Playhead = 0; e.Handled = true; break;
            case Key.End: clip.Playhead = clip.Duration; e.Handled = true; break;
            case Key.Left when Keyboard.Modifiers == ModifierKeys.None: clip.Playhead = Math.Max(0, clip.Playhead - 100); e.Handled = true; break;
            case Key.Right when Keyboard.Modifiers == ModifierKeys.None: clip.Playhead = Math.Min(clip.Duration, clip.Playhead + 100); e.Handled = true; break;
            case Key.R when Keyboard.Modifiers == ModifierKeys.None: clip.IsRecording = !clip.IsRecording; e.Handled = true; break;
        }
    }
}
