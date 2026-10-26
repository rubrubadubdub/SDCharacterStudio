using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Runtime.Animation;
using Wpf.Ui.Controls;

namespace SDCharacterStudio.Editor.ViewModels;

public enum StudioTool { Pencil, Eraser, Fill, Line, Rectangle, Picker, Select, Anchor, Ellipse, Move, Lasso, Wand, Shade }

public sealed partial class PoseFrameItem : ObservableObject
{
    public PoseFrameItem(PoseDocumentViewModel document, int index) { Document = document; Index = index; SelectCommand = new RelayCommand(() => document.FrameIndex = index); }
    public PoseDocumentViewModel Document { get; }
    public int Index { get; }
    public int Number => Index + 1;
    /// <summary>Portrait emotions have no timing.</summary>
    public bool ShowsDuration => !Document.IsPortrait && !Document.IsSheet;   // the game times an outfit's frames itself
    [ObservableProperty] private BitmapSource? thumbnail;
    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private string? badges;
    /// <summary>Picked with Shift or Ctrl+click: edits apply to every picked frame (D-49).</summary>
    [ObservableProperty] private bool isPicked;
    /// <summary>What the frame does when the pose plays: "once", "loop", "end", or null (not played).</summary>
    [ObservableProperty] private string? role;
    /// <summary>Linked frames share one picture: the same colour marks each group in the strip.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsLinked))] private string? linkColor;
    [ObservableProperty] private string? linkText;
    public bool IsLinked => LinkColor != null;
    /// <summary>An outfit's frames in groups: "Walking down" over the first of each (its frames are the game's).</summary>
    [ObservableProperty] private string? group;
    public double Duration
    {
        get => Document.Model.Frames.ElementAtOrDefault(Index)?.Duration ?? 0;
        set { int ms = (int)Math.Round(value); if (ms > 0 && ms != (int)Duration) Document.SetFrameDuration(Index, ms); }
    }
    public IRelayCommand SelectCommand { get; }
}

public sealed record SequenceChip(string Sequence, int Position, int Frame)
{
    public string Text => (Frame + 1).ToString();
}

/// <summary>
/// The pose editor (doc 07 §3.2, §4, §6): one frame at a time on a zoomed canvas with onion skin and a reference ghost,
/// the frame strip with Intro/Loop/Outro sequences, the layer stack, pixel tools and anchors. Every change is one undo
/// step on the pack; paint strokes are drawn into a working copy and committed when the mouse is released.
/// </summary>
public partial class PoseDocumentViewModel : StudioDocumentViewModel
{
    private readonly HashSet<int> lockedLayers = new();
    private bool rebuildFieldsOnReload = true;
    private PoseTimeline? playback;
    private double playbackElapsed;

    public PoseDocumentViewModel(CharacterStudioViewModel studio, string id) : this(studio, LibraryKind.Pose, id) { }

    protected PoseDocumentViewModel(CharacterStudioViewModel studio, LibraryKind kind, string id) : base(studio, kind, id)
    {
        zoom = studio.Settings.PoseZoom; showGrid = studio.Settings.ShowGrid; showOnion = studio.Settings.ShowOnionSkin;
        showGhost = studio.Settings.ShowGhost; onionOpacity = studio.Settings.OnionOpacity;
        LoadOptions();
    }

    public override SymbolRegular Icon => SymbolRegular.Accessibility24;
    public Pose Model { get; protected set; } = new();
    /// <summary>Portraits edit their emotions as frames (doc 07 §3.3); see <see cref="PortraitDocumentViewModel"/>.</summary>
    public virtual bool IsPortrait => false;
    /// <summary>The item as a pose to edit (a portrait converts its emotions).</summary>
    protected virtual Pose? LoadModel() => Library?.Pose(ItemId);
    /// <summary>Write the edited pose back (a portrait converts back to emotions).</summary>
    protected virtual void Persist(PackLibrary library) => library.SavePose(Model);
    protected virtual bool CanDeleteFrame(int index) => true;
    public ObservableCollection<PoseFrameItem> Frames { get; } = new();
    public ObservableCollection<LayerRowViewModel> Layers { get; } = new();
    public ObservableCollection<SequenceChip> Intro { get; } = new();
    public ObservableCollection<SequenceChip> Loop { get; } = new();
    public ObservableCollection<SequenceChip> Outro { get; } = new();
    public ObservableCollection<AnchorMarker> Anchors { get; } = new();
    public PaletteViewModel Palette => Studio.Palette;

    [ObservableProperty] private int frameIndex;
    [ObservableProperty] private int layerIndex = -1;
    [ObservableProperty] private BitmapSource? composite;
    [ObservableProperty] private BitmapSource? onionPrevious;
    [ObservableProperty] private BitmapSource? onionNext;
    [ObservableProperty] private BitmapSource? ghost;
    [ObservableProperty] private double zoom;
    [ObservableProperty] private bool showGrid;
    [ObservableProperty] private bool showOnion;
    [ObservableProperty] private double onionOpacity;
    [ObservableProperty] private bool showGhost;
    [ObservableProperty] private bool rawSlots;
    [ObservableProperty] private bool pickFromLayer;
    [ObservableProperty] private bool filledShapes;
    [ObservableProperty] private bool applyToAllFrames;
    [ObservableProperty] private bool isPlaying;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ToolHint))] private StudioTool tool = StudioTool.Select;

    /// <summary>Layers, anchors, masks and the rig (D-48). Off: you edit what you see, and live art stays live by itself.</summary>
    public bool Advanced
    {
        get => Studio.Settings.AdvancedPoseTools;
        set
        {
            if (value == Studio.Settings.AdvancedPoseTools) return;
            Studio.Settings.AdvancedPoseTools = value; Studio.Settings.Save();
            Studio.Palette.Simple = !value;
            Studio.RefreshLibrary();
            RefreshAnchors();
            OnPropertyChanged(nameof(ShowsSequenceChips));
            if (!value && Tool == StudioTool.Anchor) Tool = StudioTool.Select;
            OnPropertyChanged(); OnPropertyChanged(nameof(ToolHint));
        }
    }

    /// <summary>What the current tool does, in plain words.</summary>
    public string ToolHint => IsPeeking
        ? "Select what you want from her frame (box, lasso or wand), then Copy into this frame (Enter or Ctrl+C). Esc goes back."
        : IsTransforming
        ? "Drag inside to move, a corner or edge to scale (Shift keeps proportions), the round handle to rotate (Shift: 15° steps). Enter applies, Esc cancels."
        : Tool switch
    {
        StudioTool.Move => "Drag to move what the selection shows, or the whole picture (Ctrl+drag copies). Arrows nudge.",
        StudioTool.Lasso => "Draw around what you want. Shift adds, Alt takes away. Then drag inside to move it, or Ctrl+T to transform.",
        StudioTool.Wand => "Click a colour to select that area (Ctrl: that colour everywhere). Shift adds, Alt takes away.",
        StudioTool.Select => "Drag a box around part of the character, then drag inside the box to move that part (Ctrl+drag copies). The rest still follows sprite mods.",
        StudioTool.Pencil => "Draw new pixels on top of the character. [ and ] change the brush; Alt+click picks a colour.",
        StudioTool.Eraser => "Erase what you see, even the character's own pixels. What's left still follows sprite mods.",
        StudioTool.Fill => "Fill an area of the picture with the colour (Ctrl+click: that colour everywhere).",
        StudioTool.Picker => "Click a colour, then you're back to the tool you were using. Holding Alt does the same with any tool.",
        StudioTool.Line => "Draw a straight line (Shift: 45°).",
        StudioTool.Rectangle => "Draw a rectangle (Shift: a square).",
        StudioTool.Ellipse => "Draw an ellipse (Shift: a circle).",
        StudioTool.Shade => "Left click shades darker, right click lighter: one step along her own colours (they stay live), your named colours, or by eye.",
        StudioTool.Anchor => "Place a point that other layers hang from: a head on the Neck, a prop in the Hand.",
        _ => "",
    };
    [ObservableProperty] private string anchorName = "Neck";
    [ObservableProperty] private PixelRect? selection;
    [ObservableProperty] private string? hoverText;

    public int Width => Model.FrameSize is { Length: 2 } s ? s[0] : 16;
    public int Height => Model.FrameSize is { Length: 2 } s ? s[1] : 32;
    public int[] Pivot => Model.GetPivot();
    public PoseFrame? Frame => Model.Frames.ElementAtOrDefault(FrameIndex);
    public Layer? SelectedLayer => Frame?.Layers.ElementAtOrDefault(LayerIndex);
    public bool IsSheet => Model.Kind == "sheet";
    /// <summary>A pose plays its frames in an order; an outfit's frames are the game's to pick.</summary>
    public bool ShowsPlayOrder => !IsPortrait && !IsSheet;
    /// <summary>An outfit keeps every frame in its place: no add, delete or move.</summary>
    public bool CanChangeFrames => !IsSheet;
    /// <summary>Ready-made poses make a pose; an outfit is clothes on all her frames.</summary>
    public bool ShowsReadyMade => !IsSheet;
    public string PickedFramesText => $"{PickedFrames.Count} frames picked: drawing, erasing and moving apply to all of them";
    public string SheetFramesHint => IsSheet ? "Click: start this frame again from it · Shift+click: copy part of it" : "Click: start from it · Ctrl+click: new frame · Shift+click: copy part of it";
    public string PinOutfitLabel => IsSheet ? "Make it from the outfit shown" : "Always wear the outfit shown";
    public string UnpinOutfitLabel => IsSheet ? "Make it from her own look" : "Wear what the game gives";
    public string FrameCaption => Frames.Count == 0 ? "" : IsPortrait ? $"Emotion {Badges(FrameIndex)} ({FrameIndex + 1} of {Frames.Count})"
        : IsSheet ? $"Frame {FrameIndex + 1} of {Frames.Count} · " + (FrameIndex < 16 ? $"walk {SheetFacing(FrameIndex)} {FrameIndex % 4 + 1}" : $"her frame {FrameIndex}")
        : $"Frame {FrameIndex + 1} of {Frames.Count}";

    partial void OnFrameIndexChanged(int value)
    {
        foreach (var frame in Frames) frame.IsSelected = frame.Index == value;
        if (!PreviewPlaying) previewDirty = true;   // a paused preview shows the frame you're on
        Selection = null;
        RebuildLayers();
        RefreshAnchors();
        Render();
        BuildFields();
        OnPropertyChanged(nameof(FrameCaption)); OnPropertyChanged(nameof(IsFlat)); OnPropertyChanged(nameof(CanFlatten));
        OnPropertyChanged(nameof(IsLinked)); OnPropertyChanged(nameof(LinkedText));
    }
    partial void OnLayerIndexChanged(int value) { foreach (var row in Layers) row.IsSelected = row.Index == value; Selection = null; BuildFields(); }
    partial void OnLayerIndexChanging(int value) { if (IsStroking && !IsTransforming) CancelStroke(); }
    partial void OnZoomChanged(double value) { Studio.Settings.PoseZoom = value; Studio.Settings.Save(); if (IsTransforming) NotifyTransform(); }
    partial void OnFrameIndexChanging(int value) { if (IsTransforming) ApplyTransform(); else if (IsStroking) CancelStroke(); }
    partial void OnShowGridChanged(bool value) { Studio.Settings.ShowGrid = value; Studio.Settings.Save(); }
    partial void OnShowOnionChanged(bool value) { Studio.Settings.ShowOnionSkin = value; Studio.Settings.Save(); Render(); }
    partial void OnOnionOpacityChanged(double value) { Studio.Settings.OnionOpacity = value; Studio.Settings.Save(); }
    partial void OnShowGhostChanged(bool value) { Studio.Settings.ShowGhost = value; Studio.Settings.Save(); Render(); }
    partial void OnRawSlotsChanged(bool value) => Render();
    partial void OnToolChanging(StudioTool value) { if (IsStroking && !IsTransforming) CancelStroke(); }
    partial void OnToolChanged(StudioTool value)
    {
        OnPropertyChanged(nameof(IsAnchorTool)); OnPropertyChanged(nameof(UsesBrush)); OnPropertyChanged(nameof(UsesShapes)); OnPropertyChanged(nameof(UsesPaint));
        OnPropertyChanged(nameof(IsSelectTool)); OnPropertyChanged(nameof(ToolName)); OnPropertyChanged(nameof(IsLassoTool)); OnPropertyChanged(nameof(IsWandTool)); OnPropertyChanged(nameof(ShowsSelectionActions));
        if (IsPaintTool(value)) lastPaintTool = value;
        // Picking up another kind of tool drops a floating piece where it is (Aseprite).
        if (IsTransforming && !(IsSelectionTool(value) || value == StudioTool.Move)) ApplyTransform();
    }
    public bool IsAnchorTool => Tool == StudioTool.Anchor;

    // ---------------- loading and drawing ----------------

    public override bool Reload()
    {
        if (!IsOwnCommit && IsStroking) CancelStroke();
        // After its own edit the document already shows the new state (and a portrait keeps its frames' emotion names).
        if (!IsOwnCommit && floating != null) { floating = null; EndPreview(); NotifyTransform(); }   // undo or an outside change: drop the floating piece
        if (!IsOwnCommit || Model.Id == null)
        {
            if (LoadModel() is not { } fresh) return false;
            Model = fresh;
        }
        Title = Model.Id ?? ItemId;
        RebuildAll(rebuildFieldsOnReload || !IsOwnCommit);
        return true;
    }

    public void RebuildAll(bool fields = true)
    {
        if (Frames.Count != Model.Frames.Count)
        {
            Frames.Clear();
            for (int i = 0; i < Model.Frames.Count; i++) Frames.Add(new PoseFrameItem(this, i));
        }
        int clamped = Math.Clamp(FrameIndex, 0, Math.Max(0, Model.Frames.Count - 1));
        if (clamped != FrameIndex) { FrameIndex = clamped; }
        foreach (var frame in Frames)
        {
            frame.IsSelected = frame.Index == FrameIndex; frame.Badges = Badges(frame.Index);
            frame.Group = !IsSheet ? null : frame.Index < 16 ? (frame.Index % 4 == 0 ? $"Walking {SheetFacing(frame.Index)}" : "") : frame.Index == 16 ? "Her other frames" : "";
        }
        RefreshLinks();
        RebuildSequences();
        NotifyPlayOrder();
        RefreshPresets();
        RefreshRecolors();
        RefreshBackdrop();
        RebuildLayers();
        RefreshAnchors();
        Render();
        RenderThumbnails();
        if (fields) BuildFields();
        OnPropertyChanged(nameof(Width)); OnPropertyChanged(nameof(Height)); OnPropertyChanged(nameof(Pivot)); OnPropertyChanged(nameof(IsSheet)); OnPropertyChanged(nameof(FrameCaption));
        OnPropertyChanged(nameof(IsFlat)); OnPropertyChanged(nameof(CanFlatten));
        foreach (var name in new[] { nameof(ShowsPlayOrder), nameof(CanChangeFrames), nameof(ShowsReadyMade), nameof(SheetFramesHint), nameof(PinOutfitLabel), nameof(UnpinOutfitLabel) }) OnPropertyChanged(name);
    }

    protected virtual string? Badges(int index)
    {
        var parts = new List<string>();
        if (Model.Intro.Contains(index)) parts.Add("intro");
        if (Model.Loop.Contains(index)) parts.Add("loop");
        if (Model.Outro.Contains(index)) parts.Add("outro");
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private void RebuildSequences()
    {
        void Fill(ObservableCollection<SequenceChip> target, int[] frames, string name)
        {
            target.Clear();
            for (int i = 0; i < frames.Length; i++) target.Add(new SequenceChip(name, i, frames[i]));
        }
        Fill(Intro, Model.Intro, "Intro"); Fill(Loop, Model.Loop, "Loop"); Fill(Outro, Model.Outro, "Outro");
    }

    private void RefreshAnchors()
    {
        Anchors.Clear();
        if (!Advanced) return;   // anchors are an Advanced idea (D-48): the simple canvas shows only the picture
        foreach (var (name, point) in Frame?.Anchors ?? new Dictionary<string, int[]>())
            if (point is { Length: 2 }) Anchors.Add(new AnchorMarker(name, point[0], point[1]));
    }

    /// <summary>The pose as drawn: with raw slot colours when asked.</summary>
    private Pose ForRender()
    {
        var source = preview ?? Model;   // a drag in progress shows its result before it's committed
        if (!RawSlots) return source;
        var copy = PackWriter.Clone(source);
        foreach (var layer in copy.Frames.SelectMany(f => f.Layers)) layer.ColorSlots = null;
        return copy;
    }

    public void Render()
    {
        if (PeekFrame is { } peek)
        {
            // Copying from one of her sheet frames: show that frame, nothing else.
            Composite = PeekImage(peek) is { } image ? StudioImages.ToBitmap(image) : null;
            OnionPrevious = null; OnionNext = null; Ghost = null; Problems = null; IsFrameEmpty = false;
            return;
        }
        var pose = ForRender();
        int index = IsPlaying && playback != null ? playback.Frame : FrameIndex;
        var result = Context.RenderFrame(pose, index);
        Composite = StudioImages.ToBitmap(result.Image);
        IsFrameEmpty = !IsPlaying && StudioImages.IsEmpty(result.Image);
        previewDirty = true;   // the preview shows edits as they happen
        if (ShowParts && partsWorking == null && !IsStroking) RefreshPartsOverlay();
        if (!IsStroking && !IsPlaying) Palette.ShowPictureColors(result.Image);
        Problems = result.Problems.Count == 0 ? null : string.Join("\n", result.Problems);
        var (before, after) = ShowOnion && !IsPlaying ? OnionImages(pose, index) : (null, null);
        OnionPrevious = before == null ? null : StudioImages.ToBitmap(before);
        OnionNext = after == null ? null : StudioImages.ToBitmap(after);
        Ghost = ShowGhost ? GhostImage() is { } ghost ? StudioImages.ToBitmap(ghost) : null : null;
    }

    public void RenderThumbnails()
    {
        var pose = ForRender();
        foreach (var frame in Frames) frame.Thumbnail = StudioImages.ToBitmap(Context.RenderFrame(pose, frame.Index).Image);
        foreach (var row in Layers) row.Refresh();
    }

    /// <summary>The character's real standing frame for this pose's facing, placed on the pose's pivot (doc 07 §3.2).</summary>
    protected virtual RgbaImage? GhostImage()
    {
        if (PoseCharacter() is not { } who) return null;
        string facing = IsSheet ? SheetFacing(FrameIndex) : Model.Facing ?? "down";
        var standing = Studio.StandingFrame(who, facing);
        if (standing == null) return null;
        // Bottom-centre the standing frame on the pivot, like the stand-in drawing (doc 05 §8).
        var canvas = StudioImages.Blank(Width, Height);
        var pivot = Pivot;
        int x = pivot[0] - standing.Width / 2, y = pivot[1] + 1 - standing.Height;
        if (who.IsFarmer) { x = pivot[0] - 16; y = pivot[1] + 1 - 48; }
        Compositor.Blit(canvas, standing, x, y);
        return canvas;
    }

    /// <summary>The way a walk sheet's frame faces (her special frames after the walking rows face down).</summary>
    public static string SheetFacing(int index) => index switch { < 4 => "down", < 8 => "right", < 12 => "up", < 16 => "left", _ => "down" };

    /// <summary>An outfit keeps every frame of her sprite sheet in its place (the game picks frames by their place).</summary>
    private bool FramesStayInPlace()
    {
        if (!IsSheet) return false;
        Ui.Toast("Frames stay in place", "An outfit has every frame of her sprite sheet, in the game's order, so frames can't be added, removed or moved. Start a frame again from one of her frames instead.", ToastKind.Info);
        return true;
    }

    /// <summary>Who the pose is for: its rig's NPC, or the player.</summary>
    public CharacterChoice? PoseCharacter()
    {
        var rig = Library?.Rig(Model.For);
        if (rig == null) return Studio.Character;
        if (string.Equals(rig.Kind, "Farmer", StringComparison.OrdinalIgnoreCase)) return Studio.Characters.FirstOrDefault(c => c.IsFarmer) ?? new CharacterChoice("farmer", "Player", null, true, null);
        return Studio.Characters.FirstOrDefault(c => string.Equals(c.Key, rig.Npc, StringComparison.OrdinalIgnoreCase)) ?? new CharacterChoice(rig.Npc ?? rig.Id!, rig.Npc ?? rig.Id!, null, false, null);
    }

    // ---------------- saving ----------------

    /// <summary>Change the pose and save it as one undo step (plus any image writes in <paramref name="extra"/>).</summary>
    public void Save(string label, Action<Pose> mutate, Action<PackLibrary>? extra = null, bool rebuildFields = true)
    {
        mutate(Model);
        rebuildFieldsOnReload = rebuildFields;
        try { Commit(label, lib => { extra?.Invoke(lib); Persist(lib); }); }
        finally { rebuildFieldsOnReload = true; }
    }

    public void SetFrameDuration(int index, int ms) => Save("Frame duration", p => p.Frames[index].Duration = ms, rebuildFields: false);

    // ---------------- frames ----------------

    [RelayCommand]
    private void AddFrame()
    {
        if (Frame == null || FramesStayInPlace()) return;
        int at = FrameIndex + 1;
        // A copy joins the part of the order its original is in (a frame copied in the "once" part plays once too).
        Save("Add frame", pose => KeepingOrder(pose, p => p.Frames.Insert(at, CopyFrame(Frame, at)), k => k > 0 && at <= k ? k + 1 : k),
            lib => CopyFrameImages(lib, pose: Model, frameIndex: at));
        FrameIndex = at;
    }

    [RelayCommand]
    private void DuplicateFrame() => AddFrame();

    /// <summary>A copy of a frame for position <paramref name="index"/>; its paint layers get their own images on save.</summary>
    private PoseFrame CopyFrame(PoseFrame frame, int index) => PackWriter.Clone(frame);

    /// <summary>Paint is per frame (doc 03 §10.1): give a new frame its own copies of the paint images.</summary>
    private void CopyFrameImages(PackLibrary library, Pose pose, int frameIndex)
    {
        foreach (var paint in pose.Frames[frameIndex].Layers.OfType<PaintLayer>())
            paint.Image = LibraryCopies.CopyImage(library, paint.Image, $"art/poses/{pose.Id}/", $"{System.IO.Path.GetFileNameWithoutExtension(paint.Image)?.Split('-')[0] ?? "paint"}-{frameIndex}");
        foreach (var live in pose.Frames[frameIndex].Layers.OfType<LiveLayer>().Where(l => l.Map != null))
            live.Map = LibraryCopies.CopyImage(library, live.Map, $"art/poses/{pose.Id}/", $"map-{frameIndex}");
    }

    [RelayCommand]
    private void DeleteFrame()
    {
        // Every frame picked in the strip (Shift or Ctrl+click), not just the one on the canvas. Picking them all keeps
        // frame 1: a pose needs at least one.
        if (FramesStayInPlace()) return;
        var doomed = PickedFrames.Where(CanDeleteFrame).Distinct().OrderBy(i => i).ToList();
        if (doomed.Count >= Model.Frames.Count) doomed.Remove(0);
        if (doomed.Count == 0) { if (Model.Frames.Count <= 1) Ui.Toast("Last frame", "A pose needs at least one frame.", ToastKind.Info); return; }
        int first = doomed[0];
        Save(doomed.Count == 1 ? "Delete frame" : $"Delete {doomed.Count} frames", pose =>
        {
            bool backAndForth = IsBackAndForth(pose);
            foreach (int index in doomed.OrderByDescending(i => i)) pose.Frames.RemoveAt(index);
            int Shift(int i) => i - doomed.Count(d => d < i);
            int[] Remap(int[] list) => list.Where(i => !doomed.Contains(i)).Select(Shift).ToArray();
            pose.Intro = Remap(pose.Intro); pose.Loop = Remap(pose.Loop); pose.Outro = Remap(pose.Outro);
            if (backAndForth) pose.Loop = BackAndForthOrder(pose.Frames.Count);
        });
        FrameIndex = Math.Min(first, Model.Frames.Count - 1);
        PickFrames(new[] { FrameIndex });
        if (doomed.Count > 1) Ui.Toast("Frames deleted", $"Deleted {doomed.Count} frames. Undo (Ctrl+Z) brings them back.", ToastKind.Info);
    }

    [RelayCommand] private void MoveFrameLeft() => MoveFrame(-1);
    [RelayCommand] private void MoveFrameRight() => MoveFrame(1);

    private void MoveFrame(int delta)
    {
        int from = FrameIndex, to = from + delta;
        if (to < 0 || to >= Model.Frames.Count || FramesStayInPlace()) return;
        Save("Move frame", pose =>
        {
            if (IsBackAndForth(pose)) { (pose.Frames[from], pose.Frames[to]) = (pose.Frames[to], pose.Frames[from]); return; }   // by position
            if (SimpleLoopFrom(pose) is { } k)
            {
                // A simple order goes by position (like Aseprite's tags): the frames trade places, the order stays.
                (pose.Frames[from], pose.Frames[to]) = (pose.Frames[to], pose.Frames[from]);
                SetLoopFrom(pose, k);
                return;
            }
            (pose.Frames[from], pose.Frames[to]) = (pose.Frames[to], pose.Frames[from]);
            int Swap(int i) => i == from ? to : i == to ? from : i;
            pose.Intro = pose.Intro.Select(Swap).ToArray(); pose.Loop = pose.Loop.Select(Swap).ToArray(); pose.Outro = pose.Outro.Select(Swap).ToArray();
        });
        FrameIndex = to;
    }

    [RelayCommand] private void NextFrame() { if (Model.Frames.Count > 0) FrameIndex = (FrameIndex + 1) % Model.Frames.Count; }
    [RelayCommand] private void PreviousFrame() { if (Model.Frames.Count > 0) FrameIndex = (FrameIndex - 1 + Model.Frames.Count) % Model.Frames.Count; }

    [RelayCommand] private void AddToIntro() => Save("Add to intro", p => p.Intro = p.Intro.Append(FrameIndex).ToArray());
    [RelayCommand] private void AddToLoop() => Save("Add to loop", p => p.Loop = p.Loop.Append(FrameIndex).ToArray());
    [RelayCommand] private void AddToOutro() => Save("Add to outro", p => p.Outro = p.Outro.Append(FrameIndex).ToArray());

    [RelayCommand]
    private void RemoveFromSequence(SequenceChip? chip)
    {
        if (chip == null) return;
        Save("Remove from " + chip.Sequence.ToLowerInvariant(), p =>
        {
            int[] Without(int[] list) => list.Where((_, i) => i != chip.Position).ToArray();
            switch (chip.Sequence) { case "Intro": p.Intro = Without(p.Intro); break; case "Loop": p.Loop = Without(p.Loop); break; default: p.Outro = Without(p.Outro); break; }
        });
    }

    [RelayCommand] private void ZoomIn() => Zoom = Math.Min(64, Math.Round(Zoom) + (Zoom >= 16 ? 4 : Zoom >= 8 ? 2 : 1));
    [RelayCommand] private void ZoomOut() => Zoom = Math.Max(1, Math.Round(Zoom) - (Zoom > 16 ? 4 : Zoom > 8 ? 2 : 1));

    // ---------------- playback ----------------

    [RelayCommand]
    private void TogglePlay()
    {
        if (IsPlaying) { StopPlayback(); return; }
        try
        {
            playback = new PoseTimeline(Model, Model.Intro.Length > 0 ? "once-then-loop" : "loop");
            playbackElapsed = 0;
            IsPlaying = true;
            Render();
        }
        catch (ArgumentException ex) { Ui.Toast("Can't play", ex.Message, ToastKind.Warning); }
    }

    public void StopPlayback() { IsPlaying = false; playback = null; Render(); }

    /// <summary>Advance playback (the view's timer calls this). Returns true when the shown frame changed.</summary>
    public bool Tick(double ms)
    {
        if (!IsPlaying || playback == null) return false;
        playbackElapsed += ms;
        var entered = playback.Advance(ms);
        if (playback.Finished) { StopPlayback(); return true; }
        if (entered.Count == 0) return false;
        Render();
        return true;
    }

    public override void Closing() { StopPlayback(); CancelStroke(); CloseExternalSessions(); }
}

public sealed record AnchorMarker(string Name, int X, int Y);
