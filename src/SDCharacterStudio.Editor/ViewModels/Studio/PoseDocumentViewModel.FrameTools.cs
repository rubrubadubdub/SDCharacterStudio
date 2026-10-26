using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Studio;

namespace SDCharacterStudio.Editor.ViewModels;

/// <summary>
/// Frame tools after Pixelorama: linked frames (frames that share one picture: draw on one and all of them change),
/// reversing picked frames, a back-and-forth play order, and a mirrored view of the canvas to check proportions.
/// </summary>
public partial class PoseDocumentViewModel
{
    // ---------------- linked frames ----------------

    /// <summary>The picture a frame is (its paint image), or null for a frame that isn't one picture.</summary>
    private static string? PictureOf(PoseFrame? frame) => FlatOf(frame)?.Paint.Image;

    /// <summary>The other frames that share this frame's picture.</summary>
    public IReadOnlyList<int> LinkedWith(int index)
    {
        if (PictureOf(Model.Frames.ElementAtOrDefault(index)) is not { } picture) return Array.Empty<int>();
        return Enumerable.Range(0, Model.Frames.Count).Where(i => i != index && string.Equals(PictureOf(Model.Frames[i]), picture, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public bool IsLinked => LinkedWith(FrameIndex).Count > 0;
    public string? LinkedText => LinkedWith(FrameIndex) is { Count: > 0 } others
        ? $"Linked with frame{(others.Count == 1 ? "" : "s")} {string.Join(", ", others.Select(i => i + 1))}: drawing on one draws on all"
        : null;
    /// <summary>Several frames picked, and not all of them one picture already.</summary>
    public bool CanLink => HasSeveralPicked && PickedFrames.Select(i => PictureOf(Model.Frames.ElementAtOrDefault(i))).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;

    /// <summary>
    /// The picked frames share this frame's picture from now on (Pixelorama's linked cels): a hold, a blink's open
    /// frames. Their own pictures are replaced; Unlink gives a frame its own copy again. One undo step.
    /// </summary>
    [RelayCommand]
    public void LinkPicked()
    {
        if (Flat is not { } source) { Ui.Toast("One picture only", "Linking works on frames that are one picture.", ToastKind.Info); return; }
        var others = PickedFrames.Where(i => i != FrameIndex && FlatOf(Model.Frames.ElementAtOrDefault(i)) != null
            && !string.Equals(PictureOf(Model.Frames[i]), source.Paint.Image, StringComparison.OrdinalIgnoreCase)).ToList();
        if (others.Count == 0) { Ui.Toast("Nothing to link", "Pick other frames (Shift or Ctrl+click in the strip), then link them to this one.", ToastKind.Info); return; }
        var replaced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Save(others.Count == 1 ? "Link a frame" : $"Link {others.Count} frames", pose =>
        {
            var from = FlatOf(pose.Frames[FrameIndex])!;
            foreach (int index in others)
            {
                var flat = FlatOf(pose.Frames[index])!;
                replaced.Add(flat.Live.Map!); replaced.Add(flat.Paint.Image!);
                flat.Live.Source = from.Live.Source == null ? null : PackWriter.Clone(from.Live.Source);
                flat.Live.SourceFrame = from.Live.SourceFrame == null ? null : PackWriter.Clone(from.Live.SourceFrame);
                flat.Live.Map = from.Live.Map;
                flat.Paint.Image = from.Paint.Image;
                flat.Paint.ColorSlots = from.Paint.ColorSlots == null ? null : new(from.Paint.ColorSlots, StringComparer.OrdinalIgnoreCase);
            }
        }, lib =>
        {
            // The pictures they had go, unless another frame still shows them.
            var used = Model.Frames.SelectMany(f => f.Layers).SelectMany(l => new[] { (l as PaintLayer)?.Image, (l as LiveLayer)?.Map, (l as ImageLayer)?.Image })
                .OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var path in replaced.Where(p => !used.Contains(p))) lib.RemoveImage(path);
        });
        Ui.Toast("Frames linked", $"Frame{(others.Count == 1 ? "" : "s")} {string.Join(", ", others.Select(i => i + 1))} now share frame {FrameIndex + 1}'s picture. Unlink one to change it on its own.", ToastKind.Info);
    }

    /// <summary>This frame gets its own copy of the picture it shares, to change on its own. One undo step.</summary>
    [RelayCommand]
    public void UnlinkFrame()
    {
        if (!IsLinked) return;
        Save("Unlink frame", _ => { }, lib => CopyFrameImages(lib, Model, FrameIndex));
    }

    private static readonly string[] LinkColors = { "#E0A030", "#40B0E0", "#C060E0", "#60C060", "#E06070", "#D0D040" };

    /// <summary>A colour for each group of linked frames in the strip.</summary>
    private void RefreshLinks()
    {
        var groups = Enumerable.Range(0, Model.Frames.Count).Select(i => (Index: i, Picture: PictureOf(Model.Frames[i])))
            .Where(f => f.Picture != null).GroupBy(f => f.Picture!, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).ToList();
        foreach (var frame in Frames) { frame.LinkColor = null; frame.LinkText = null; }
        for (int g = 0; g < groups.Count; g++)
            foreach (var (index, _) in groups[g])
                if (Frames.ElementAtOrDefault(index) is { } item)
                {
                    item.LinkColor = LinkColors[g % LinkColors.Length];
                    item.LinkText = "Linked with " + string.Join(", ", groups[g].Where(f => f.Index != index).Select(f => f.Index + 1));
                }
        OnPropertyChanged(nameof(IsLinked)); OnPropertyChanged(nameof(LinkedText)); OnPropertyChanged(nameof(CanLink));
    }

    // ---------------- reverse picked frames ----------------

    /// <summary>The picked frames swap end for end, in their places (a gesture played backwards). One undo step.</summary>
    [RelayCommand]
    public void ReversePicked()
    {
        var picked = PickedFrames.Distinct().OrderBy(i => i).ToList();
        if (picked.Count < 2) { Ui.Toast("Pick some frames", "Pick the frames to reverse (Shift or Ctrl+click in the strip).", ToastKind.Info); return; }
        if (FramesStayInPlace()) return;
        Save($"Reverse {picked.Count} frames", pose => KeepingOrder(pose, p =>
        {
            var frames = picked.Select(i => p.Frames[i]).Reverse().ToList();
            for (int k = 0; k < picked.Count; k++) p.Frames[picked[k]] = frames[k];
        }));
        RenderThumbnails();
    }

    // ---------------- back and forth ----------------

    /// <summary>Every frame forwards, then back again without repeating the ends: 1 2 3 4 3 2, 1 2 3 4 3 2...</summary>
    public static int[] BackAndForthOrder(int count) =>
        count < 3 ? Enumerable.Range(0, Math.Max(count, 0)).ToArray() : Enumerable.Range(0, count).Concat(Enumerable.Range(1, count - 2).Reverse()).ToArray();

    private static bool IsBackAndForth(Pose pose) => pose.Frames.Count >= 3 && pose.Intro.Length == 0 && pose.Loop.SequenceEqual(BackAndForthOrder(pose.Frames.Count));

    /// <summary>Play forwards then backwards (Pixelorama's ping-pong): a wave, a sway, a breath.</summary>
    public bool PlaysBackAndForth
    {
        get => IsBackAndForth(Model);
        set
        {
            if (!value || IsBackAndForth(Model)) return;
            if (Model.Frames.Count < 3) { Ui.Toast("Three frames or more", "Back and forth needs at least three frames to turn round.", ToastKind.Info); NotifyPlayOrder(); return; }
            Save("Play order", pose => { pose.Intro = Array.Empty<int>(); pose.Loop = BackAndForthOrder(pose.Frames.Count); pose.Outro = Array.Empty<int>(); });
            NotifyPlayOrder();
        }
    }

    // ---------------- mirror view ----------------

    /// <summary>The canvas shown flipped (only the view): a fresh look at proportions, as artists flip their canvas.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ViewScaleX))] private bool mirrorView;
    public double ViewScaleX => MirrorView ? -1 : 1;
    [RelayCommand] private void ToggleMirrorView() => MirrorView = !MirrorView;
}
