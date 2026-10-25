using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Studio;
using SDCharacterStudio.Runtime.Animation;

namespace SDCharacterStudio.Editor.ViewModels;

/// <summary>
/// Animating with confidence (D-49 phase 3):
/// <list type="bullet">
/// <item>the play order in plain words (loop everything, or play the first frames once and loop the rest), kept right when frames are added, moved or deleted;</item>
/// <item>a preview that keeps playing beside the canvas while you edit (Aseprite's preview window, Pixelorama's drawing while it plays);</item>
/// <item>several frames picked at once, so a move, transform, flip or clear applies to all of them (Animate's edit multiple frames);</item>
/// <item>a ready-made idle loop made from her own sprite.</item>
/// </list>
/// </summary>
public partial class PoseDocumentViewModel
{
    // ---------------- the play order ----------------

    /// <summary>
    /// The frame the loop starts at, when the order is the simple kind: 0 loops every frame; k plays frames 1..k once and
    /// loops the rest. Null: a custom order (made under Advanced).
    /// </summary>
    public int? LoopFrom => SimpleLoopFrom(Model);

    private static int? SimpleLoopFrom(Pose pose)
    {
        int n = pose.Frames.Count;
        if (pose.Intro.Length == 0 && (pose.Loop.Length == 0 || pose.Loop.SequenceEqual(Enumerable.Range(0, n)))) return 0;
        int k = pose.Intro.Length;
        return k < n && pose.Intro.SequenceEqual(Enumerable.Range(0, k)) && pose.Loop.SequenceEqual(Enumerable.Range(k, n - k)) ? k : null;
    }

    /// <summary>Make the order simple: frames before <paramref name="k"/> play once, the rest loop (0: loop them all).</summary>
    private static void SetLoopFrom(Pose pose, int k)
    {
        int n = pose.Frames.Count;
        k = Math.Clamp(k, 0, Math.Max(0, n - 1));
        pose.Intro = Enumerable.Range(0, k).ToArray();
        pose.Loop = k == 0 ? Array.Empty<int>() : Enumerable.Range(k, n - k).ToArray();
    }

    public bool LoopsAll { get => LoopFrom == 0; set { if (value) SetPlayOrder(0); } }
    public bool PlaysOnceThenLoops { get => LoopFrom > 0 && LoopFrom < Model.Frames.Count - 1; set { if (value) SetPlayOrder(Math.Max(1, Math.Min(LoopFromNumber - 1, Model.Frames.Count - 1))); } }
    public bool HoldsLast { get => Model.Frames.Count > 1 && LoopFrom == Model.Frames.Count - 1; set { if (value) SetPlayOrder(Model.Frames.Count - 1); } }
    public bool HasCustomOrder => LoopFrom == null;
    /// <summary>The intro/loop/outro chips: under Advanced, or whenever the order is a custom one.</summary>
    public bool ShowsSequenceChips => !IsPortrait && (Advanced || HasCustomOrder);
    /// <summary>The first looping frame as people count (1-based), for the options row.</summary>
    public int LoopFromNumber
    {
        get => LoopFrom is > 0 and var k ? k + 1 : 2;
        set { if (value >= 2 && value <= Model.Frames.Count) SetPlayOrder(value - 1); }
    }
    public string PlayOrderText => LoopFrom switch
    {
        null => "A custom order (see Advanced)",
        0 => Model.Frames.Count == 1 ? "One frame: it holds" : $"Loops all {Model.Frames.Count} frames",
        var k when k == Model.Frames.Count - 1 => k == 1 ? "Plays frame 1, then holds frame 2" : $"Plays frames 1–{k}, then holds frame {k + 1}",
        var k => $"Plays {(k == 1 ? "frame 1" : $"frames 1–{k}")} once, then loops {k + 1}–{Model.Frames.Count}",
    };

    [RelayCommand]
    private void SetPlayOrder(int k)
    {
        if (Model.Frames.Count == 0) return;
        Save("Play order", pose => SetLoopFrom(pose, k));
        NotifyPlayOrder();
    }

    private void NotifyPlayOrder()
    {
        foreach (var name in new[] { nameof(LoopFrom), nameof(LoopsAll), nameof(PlaysOnceThenLoops), nameof(HoldsLast), nameof(HasCustomOrder), nameof(LoopFromNumber), nameof(PlayOrderText), nameof(ShowsSequenceChips) }) OnPropertyChanged(name);
        foreach (var frame in Frames) frame.Role = RoleOf(frame.Index);
        RestartPreview();
    }

    /// <summary>What a frame does in the order: "once", "loop", "end" (outro) or nothing.</summary>
    private string? RoleOf(int index) =>
        Model.Intro.Contains(index) ? "once" : Model.Loop.Contains(index) || (Model.Loop.Length == 0 && Model.Intro.Length == 0) ? "loop" : Model.Outro.Contains(index) ? "end" : null;

    /// <summary>Keep a simple order simple when frames change: <paramref name="change"/> mutates the pose, then the order is rebuilt.</summary>
    private void KeepingOrder(Pose pose, Action<Pose> change, Func<int, int>? newLoopFrom = null)
    {
        int? k = SimpleLoopFrom(pose);
        change(pose);
        if (k is { } before) SetLoopFrom(pose, newLoopFrom?.Invoke(before) ?? before);
    }

    /// <summary>Move a frame to another place in the strip (a drag), or copy it there (Ctrl+drag). One undo step.</summary>
    public void MoveFrameTo(int from, int to, bool copy)
    {
        int n = Model.Frames.Count;
        if (from < 0 || from >= n || to < 0 || to > n || (!copy && (to == from || to == from + 1))) return;
        if (FramesStayInPlace()) return;
        if (copy)
        {
            int at = to;
            Save("Copy frame", pose => KeepingOrder(pose, p => p.Frames.Insert(at, CopyFrame(p.Frames[from], at)), k => k > 0 && at <= k ? k + 1 : k),
                lib => CopyFrameImages(lib, pose: Model, frameIndex: at));
            FrameIndex = at;
            return;
        }
        int target = to > from ? to - 1 : to;
        Save("Move frame", pose =>
        {
            int? k = SimpleLoopFrom(pose);
            var frame = pose.Frames[from];
            pose.Frames.RemoveAt(from);
            pose.Frames.Insert(target, frame);
            if (k is { } loopFrom) { SetLoopFrom(pose, loopFrom); return; }
            int Map(int i) => i == from ? target : from < target ? (i > from && i <= target ? i - 1 : i) : (i >= target && i < from ? i + 1 : i);
            pose.Intro = pose.Intro.Select(Map).ToArray(); pose.Loop = pose.Loop.Select(Map).ToArray(); pose.Outro = pose.Outro.Select(Map).ToArray();
        });
        FrameIndex = target;
    }

    // ---------------- the preview ----------------

    [ObservableProperty] private BitmapSource? previewImage;
    [ObservableProperty] private bool previewPlaying = true;
    /// <summary>1, 2 or 4 (4 is how big the game draws it at zoom 1).</summary>
    [ObservableProperty] private int previewScale = 4;
    private PoseTimeline? previewTimeline;
    private int previewShown = -1;
    private bool previewDirty = true;
    private double previewRest;

    partial void OnPreviewPlayingChanged(bool value) { if (value) RestartPreview(); else { previewDirty = true; TickPreview(0); } }
    partial void OnPreviewScaleChanged(int value) { if (value is not (1 or 2 or 4)) PreviewScale = 4; }

    [RelayCommand] private void SetPreviewScale(int scale) => PreviewScale = scale;

    private void RestartPreview() { previewTimeline = null; previewDirty = true; previewRest = 0; }
    private double sheetWalkMs;

    /// <summary>Advance the preview (the view's timer calls this while it shows). It redraws on a new frame or after an edit.</summary>
    public void TickPreview(double ms)
    {
        if (IsPortrait || Model.Frames.Count == 0) return;
        if (TickPresetPreview(ms)) return;   // a ready-made pose plays there while it's hovered
        int frame = FrameIndex;
        if (IsSheet)
        {
            // An outfit walks: the four steps of the way this frame faces (her special frames just show).
            if (PreviewPlaying) sheetWalkMs += ms;
            if (FrameIndex < 16 && Model.Frames.Count >= 16) frame = FrameIndex / 4 * 4 + (int)(sheetWalkMs / 150) % 4;
        }
        else if (PreviewPlaying)
        {
            try
            {
                if (previewTimeline == null)
                {
                    previewTimeline = new PoseTimeline(Model, Model.Intro.Length > 0 ? "once-then-loop" : "loop");
                    previewDirty = true;
                }
                if (previewRest > 0) previewRest -= ms;   // a short rest at the end before it starts again
                else
                {
                    previewTimeline.Advance(ms);
                    if (previewTimeline.Finished) { previewTimeline = null; previewRest = 600; }
                }
                frame = previewTimeline?.Frame ?? previewShown;
            }
            catch (ArgumentException) { previewTimeline = null; frame = FrameIndex; }
        }
        if (frame < 0 || frame >= Model.Frames.Count) frame = FrameIndex;
        if (frame == previewShown && !previewDirty) return;
        previewShown = frame; previewDirty = false;
        PreviewImage = StudioImages.ToBitmap(Context.RenderFrame(ForRender(), frame).Image);
    }

    // ---------------- several frames at once ----------------

    /// <summary>The frames picked in the strip (Shift or Ctrl+click), the current one included.</summary>
    public IReadOnlyList<int> PickedFrames => Frames.Where(f => f.IsPicked || f.Index == FrameIndex).Select(f => f.Index).ToList();
    public bool HasSeveralPicked => PickedFrames.Count > 1;

    /// <summary>Pick the frames the strip has selected; <paramref name="current"/> becomes the one on the canvas.</summary>
    public void PickFrames(IEnumerable<int> picked, int? current = null)
    {
        var set = picked.ToHashSet();
        foreach (var frame in Frames) frame.IsPicked = set.Contains(frame.Index);
        if (current is { } index && index != FrameIndex && index >= 0 && index < Model.Frames.Count) FrameIndex = index;
        OnPropertyChanged(nameof(PickedFrames)); OnPropertyChanged(nameof(HasSeveralPicked)); OnPropertyChanged(nameof(PickedFramesText));
    }

    /// <summary>
    /// The same pixels on every other picked frame: each cell a stroke changed gets its new value there too (inside the
    /// current save). Paint the clothes once on all four steps of a walk.
    /// </summary>
    private void CopyChangesToOtherPicked(PackLibrary lib, (RgbaImage Before, RgbaImage After)? map, (RgbaImage Before, RgbaImage After)? paint)
    {
        static RgbaImage? Apply(RgbaImage target, (RgbaImage Before, RgbaImage After)? change)
        {
            if (change is not { } c || c.Before.Width != target.Width || c.Before.Height != target.Height) return null;
            RgbaImage? result = null;
            for (int i = 0; i < c.After.Pixels.Length; i++)
                if (c.After.Pixels[i] != c.Before.Pixels[i]) (result ??= StudioImages.Clone(target)).Pixels[i] = c.After.Pixels[i];
            return result;
        }
        foreach (var (_, flat, frameMap, framePaint) in OtherPickedFlatFrames().ToList())
        {
            if (Apply(frameMap, map) is { } newMap) lib.WriteImage(flat.Live.Map!, newMap);
            if (Apply(framePaint, paint) is { } newPaint) lib.WriteImage(flat.Paint.Image!, newPaint);
        }
    }

    /// <summary>"Draw" or "Draw on 4 frames".</summary>
    private string OnPicked(string label) => HasSeveralPicked && Flat != null ? $"{label} on {PickedFrames.Count} frames" : label;

    /// <summary>The other picked frames that are one picture, with their images.</summary>
    private IEnumerable<(int Frame, FlatFrame Flat, RgbaImage Map, RgbaImage Paint)> OtherPickedFlatFrames()
    {
        foreach (int index in PickedFrames)
        {
            if (index == FrameIndex || FlatOf(Model.Frames.ElementAtOrDefault(index)) is not { } flat) continue;
            if (Context.PackImage(flat.Live.Map) is not { } map || Context.PackImage(flat.Paint.Image) is not { } paint) continue;
            if (map.Width != Width || map.Height != Height || paint.Width != Width || paint.Height != Height) continue;
            yield return (index, flat, map, paint);
        }
    }

    /// <summary>
    /// The same lift and transform on another frame: its visible pixels inside <paramref name="shape"/> float and land
    /// by <paramref name="transform"/>. Returns the frame's new images.
    /// </summary>
    private (RgbaImage Map, RgbaImage Paint) TransformFrame(int frame, RgbaImage map, RgbaImage paint, SelectionMask shape, PieceTransform transform, bool copy)
    {
        var shown = Context.RenderFrame(Model, frame).Image;
        var baseMap = StudioImages.Clone(map); var basePaint = StudioImages.Clone(paint);
        var cells = new List<(uint Map, uint Paint, uint Shown)>();
        var lifted = new List<(int X, int Y)>();
        foreach (var (x, y) in shape.Cells())
        {
            int i = y * Width + x;
            if (shown.Pixels[i] >> 24 == 0) continue;
            cells.Add((map.Pixels[i], paint.Pixels[i], shown.Pixels[i]));
            lifted.Add((x, y));
            if (!copy) { baseMap.Pixels[i] = 0; basePaint.Pixels[i] = 0; }
        }
        if (cells.Count == 0) return (map, paint);
        var bounds = SelectionMask.FromCells(Width, Height, lifted).Bounds;
        var grid = Enumerable.Repeat(-1, bounds.Width * bounds.Height).ToArray();
        for (int k = 0; k < lifted.Count; k++) grid[(lifted[k].Y - bounds.Y) * bounds.Width + lifted[k].X - bounds.X] = k;
        var landed = PixelTransform.Land(grid, bounds, transform, Width, Height, i => cells[i].Shown);
        for (int i = 0; i < landed.Length; i++)
            if (landed[i] >= 0) { baseMap.Pixels[i] = cells[landed[i]].Map; basePaint.Pixels[i] = cells[landed[i]].Paint; }
        return (baseMap, basePaint);
    }

    /// <summary>Write the same lift and transform to every other picked frame (inside the current save).</summary>
    private void TransformOtherPicked(PackLibrary lib, SelectionMask shape, PieceTransform transform, bool copy)
    {
        foreach (var (frame, flat, map, paint) in OtherPickedFlatFrames().ToList())
        {
            var (newMap, newPaint) = TransformFrame(frame, map, paint, shape, transform, copy);
            lib.WriteImage(flat.Live.Map!, newMap); lib.WriteImage(flat.Paint.Image!, newPaint);
        }
    }

    /// <summary>The same pixel edit (clear, mirror, flip) on every other picked frame, inside the current save.</summary>
    private void EditOtherPicked(PackLibrary lib, PixelRect rect, Action<RgbaImage, PixelRect> edit)
    {
        foreach (var (_, flat, map, paint) in OtherPickedFlatFrames().ToList())
        {
            var newMap = StudioImages.Clone(map); var newPaint = StudioImages.Clone(paint);
            edit(newMap, rect); edit(newPaint, rect);
            lib.WriteImage(flat.Live.Map!, newMap); lib.WriteImage(flat.Paint.Image!, newPaint);
        }
    }

    // ---------------- a ready-made idle loop ----------------

    /// <summary>
    /// A breathing idle from her own sprite: a new frame after this one with the upper body a pixel lower (the legs stay),
    /// both frames slow, looping. Still live: only her pixels move.
    /// </summary>
    [RelayCommand]
    private void MakeIdleLoop()
    {
        if (Flat is null) { Ui.Toast("One picture only", "The idle loop works on a pose that is one picture.", ToastKind.Info); return; }
        var shown = Context.RenderFrame(Model, FrameIndex).Image;
        int top = -1, bottom = -1;
        for (int i = 0; i < shown.Pixels.Length; i++) if (shown.Pixels[i] >> 24 != 0) { int row = i / Width; if (top < 0) top = row; bottom = row; }
        if (top < 0) { Ui.Toast("Nothing to animate", "This frame is empty.", ToastKind.Info); return; }
        // The waist: about three fifths of the way down the figure. Everything above it breathes.
        int waist = top + (int)Math.Round((bottom - top) * 0.6);
        var upper = SelectionMask.FromRect(Width, Height, new PixelRect(0, 0, Width, waist + 1));
        int at = FrameIndex + 1;
        var source = FlatOf(Model.Frames[FrameIndex])!;
        var map = Context.PackImage(source.Live.Map)!; var paint = Context.PackImage(source.Paint.Image)!;
        var (breathMap, breathPaint) = TransformFrame(FrameIndex, map, paint, upper, new PieceTransform(0, 1, 1, 1, 0, Width / 2.0, Height / 2.0), copy: false);
        Save("Idle loop", pose =>
        {
            pose.Frames.Insert(at, CopyFrame(pose.Frames[FrameIndex], at));
            pose.Frames[FrameIndex].Duration = Math.Max(pose.Frames[FrameIndex].Duration, 600);
            pose.Frames[at].Duration = 600;
            SetLoopFrom(pose, 0);   // both breathe, round and round
        }, lib =>
        {
            CopyFrameImages(lib, Model, at);
            var copy = FlatOf(Model.Frames[at])!;
            lib.WriteImage(copy.Live.Map!, breathMap); lib.WriteImage(copy.Paint.Image!, breathPaint);
        });
        FrameIndex = at;
        NotifyPlayOrder();
        PreviewPlaying = true;
    }
}
