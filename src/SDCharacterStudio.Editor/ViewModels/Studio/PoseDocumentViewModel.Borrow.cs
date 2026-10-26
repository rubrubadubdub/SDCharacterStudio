using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Studio;

namespace SDCharacterStudio.Editor.ViewModels;

/// <summary>
/// Pixels from her other frames (D-49): copy and paste carry which of her sprite frames each live pixel comes from (a
/// map cell's blue channel), so a raised arm from frame 41 pasted into a standing frame is still frame 41's arm and
/// follows sprite mods. Pastes float (Aseprite): drag, turn or flip them, then Enter. "Copy part of her frame" shows
/// one of her sheet frames on the canvas to select from.
/// </summary>
public partial class PoseDocumentViewModel
{
    /// <summary>A copied piece: frame positions, map cells naming their sheet frame, paint, and the colour shown.</summary>
    private sealed record FlatClip(PixelRect From, List<(int X, int Y, uint Map, uint Paint, uint Shown)> Cells);
    private static FlatClip? flatClip;

    /// <summary>One of her sheet frames shown on the canvas to copy part of it (null: editing the pose).</summary>
    [ObservableProperty] private int? peekFrame;
    public bool IsPeeking => PeekFrame != null;
    public string PeekText => PeekFrame is { } index ? $"Copying from game frame {index}: select what you want, then copy it into this frame. It stays their live sprite." : "";
    partial void OnPeekFrameChanged(int? value) { OnPropertyChanged(nameof(IsPeeking)); OnPropertyChanged(nameof(PeekText)); OnPropertyChanged(nameof(ToolHint)); Render(); }

    /// <summary>The sheet frame a flat frame's own map cells refer to.</summary>
    private int? OwnSourceIndex(PoseFrame? frame)
    {
        if (FlatOf(frame) is not { } flat) return null;
        if (flat.Live.SourceFrame?.Index is { } index) return index;
        string facing = flat.Live.SourceFrame?.Facing ?? Model.Facing ?? "down";
        if (string.Equals(flat.Live.Source?.Kind, "Farmer", StringComparison.OrdinalIgnoreCase))
            return FarmerExport.FacingIndex(facing) switch { 0 => 12, 1 or 3 => 6, _ => 0 };
        var rig = PoseCharacter() is { } who ? Library?.RigFor(who.Key) : null;
        return rig != null && rig.Frames.TryGetValue(facing, out var frames) ? frames.Stand : null;
    }

    /// <summary>A map cell made to name its sheet frame (so it means the same in any frame).</summary>
    private static uint Absolute(uint cell, int? own) => cell >> 24 == 0 || (cell & 0xFF) != 0 || own is not { } o ? cell : cell | (uint)((o + 1) & 0xFF);
    /// <summary>A cell naming this frame's own sheet frame goes back to meaning "my own frame".</summary>
    private static uint Relative(uint cell, int? own) => cell >> 24 != 0 && own is { } o && (cell & 0xFF) == (uint)((o + 1) & 0xFF) ? cell & 0xFFFFFF00 : cell;

    private bool FlatCopy()
    {
        if (FlatImages() is not { } images) return false;
        var shape = SelectionShape ?? SelectionMask.FromRect(Width, Height, new PixelRect(0, 0, Width, Height));
        var shown = Context.RenderFrame(Model, FrameIndex).Image;
        int? own = OwnSourceIndex(Frame);
        var cells = new List<(int X, int Y, uint Map, uint Paint, uint Shown)>();
        foreach (var (x, y) in shape.Cells())
        {
            int i = y * Width + x;
            if (shown.Pixels[i] >> 24 != 0) cells.Add((x, y, Absolute(images.Map.Pixels[i], own), images.Paint.Pixels[i], shown.Pixels[i]));
        }
        flatClip = cells.Count == 0 ? flatClip : new FlatClip(shape.Bounds, cells);
        return true;
    }

    /// <summary>Paste as a floating piece: where the selection is, else where it was copied from.</summary>
    private bool FlatPaste()
    {
        if (!IsFlat) return false;
        if (flatClip is not { } clip) return true;
        var at = Selection is { IsEmpty: false } selection ? (selection.X, selection.Y) : (clip.From.X, clip.From.Y);
        int dx = at.Item1 - clip.From.X, dy = at.Item2 - clip.From.Y;
        BeginFloating(clip.Cells.Select(c => (c.X + dx, c.Y + dy, c.Map, c.Paint, c.Shown)).ToList(), "Paste");
        return true;
    }

    /// <summary>
    /// Float pixels that aren't lifted from the picture (a paste, pixels from her other frames) over it, to place with
    /// the transform box. Nothing underneath is cleared.
    /// </summary>
    private bool BeginFloating(List<(int X, int Y, uint Map, uint Paint, uint Shown)> cells, string label)
    {
        if (IsTransforming) ApplyTransform();
        if (Flat is not { } flat || FlatImages() is not { } images || cells.Count == 0) return false;
        int? own = OwnSourceIndex(Frame);
        int left = cells.Min(c => c.X), top = cells.Min(c => c.Y), right = cells.Max(c => c.X), bottom = cells.Max(c => c.Y);
        var bounds = new PixelRect(left, top, right - left + 1, bottom - top + 1);
        var grid = Enumerable.Repeat(-1, bounds.Width * bounds.Height).ToArray();
        var pieces = new List<(uint Map, uint Paint, uint Shown)>();
        foreach (var (x, y, map, paint, shown) in cells)
        {
            grid[(y - top) * bounds.Width + x - left] = pieces.Count;
            pieces.Add((Relative(map, own), paint, shown));
        }
        if (!(IsSelectionTool(Tool) || Tool == StudioTool.Move)) Tool = StudioTool.Select;
        floating = new Floating
        {
            MapPath = flat.Live.Map!, PaintPath = flat.Paint.Image!, BaseMap = StudioImages.Clone(images.Map), BasePaint = StudioImages.Clone(images.Paint),
            Bounds = bounds, Grid = grid, Cells = pieces, Before = SelectionShape ?? SelectionMask.Empty(Width, Height),
            Transform = new PieceTransform(0, 0, 1, 1, 0, bounds.X + bounds.Width / 2.0, bounds.Y + bounds.Height / 2.0), Label = label, Pasted = true,
        };
        ApplyFloating();
        NotifyTransform();
        return true;
    }

    // ---------------- copying part of one of her frames ----------------

    /// <summary>Show one of her sheet frames on the canvas to select part of it (Shift+click in Her frames).</summary>
    [RelayCommand]
    public void PeekSheetFrame(int index)
    {
        if (Flat == null) { Ui.Toast("One picture only", "Copying from game frames works on a pose that is one picture.", ToastKind.Info); return; }
        if (IsTransforming) ApplyTransform();
        if (!IsSelectionTool(Tool)) Tool = StudioTool.Select;
        PeekFrame = index;
        SetShape(null);
    }

    [RelayCommand] public void EndPeek() { PeekFrame = null; SetShape(null); }

    /// <summary>Her sheet frame as the pose's live layer would draw it (in the outfit it wears or the studio shows).</summary>
    private RgbaImage? PeekImage(int index)
    {
        if (Flat is not { } flat) return null;
        var layer = new LiveLayer { Source = flat.Live.Source, Part = "Full", SourceFrame = new SourceFrame { Index = index } };
        var scratch = new Pose { Id = Model.Id, Kind = Model.Kind, For = Model.For, Facing = Model.Facing, FrameSize = Model.FrameSize, Frames = { new PoseFrame { Duration = 100, Layers = { layer } } } };
        return Context.RenderFrame(scratch, 0).Image;
    }

    /// <summary>Copy what's selected on her sheet frame into this frame, floating, still live. Back to the pose.</summary>
    [RelayCommand]
    public void CopyFromPeek()
    {
        if (PeekFrame is not { } index || PeekImage(index) is not { } image) return;
        var shape = SelectionShape ?? SelectionMask.FromRect(Width, Height, new PixelRect(0, 0, Width, Height));
        var cells = shape.Cells().Where(c => PixelTools.Get(image, c.X, c.Y) >> 24 != 0)
            .Select(c => (c.X, c.Y, Compositor.MapCell(c.X, c.Y, frame: index), 0u, PixelTools.Get(image, c.X, c.Y))).ToList();
        if (cells.Count == 0) { Ui.Toast("Nothing selected", "Select part of the game frame first (box, lasso or wand).", ToastKind.Info); return; }
        PeekFrame = null;
        // Copying from one of your outfits: the pose wears it, so the copied part looks the same in the game.
        if (!IsSheet && PinnedOutfit == null && WearableShownOutfit is { } shown && StudioContext.OutfitId(shown) != null) { WearOutfit(shown); SaidWearsOutfit(); }
        BeginFloating(cells, $"Copy from game frame {index}");
    }
}
