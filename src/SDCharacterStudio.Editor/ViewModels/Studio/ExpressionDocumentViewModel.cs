using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Palette;
using Wpf.Ui.Controls;

namespace SDCharacterStudio.Editor.ViewModels;

public sealed partial class ExpressionFrameItem : ObservableObject
{
    public ExpressionFrameItem(ExpressionDocumentViewModel document, int index) { Index = index; SelectCommand = new RelayCommand(() => document.FrameIndex = index); }
    public int Index { get; }
    public int Number => Index + 1;
    [ObservableProperty] private BitmapSource? thumbnail;
    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private string? caption;
    public IRelayCommand SelectCommand { get; }
}

/// <summary>
/// The expression editor (doc 05 §10, doc 07 §3.3): paint eyes, blushes and mouths over the live face, inside the rig's
/// face box. The Erase slot colour removes the face's own pixels; slot colours become the character's real colours.
/// </summary>
public sealed partial class ExpressionDocumentViewModel : StudioDocumentViewModel
{
    private PaintSession? stroke;
    /// <summary>The frame's offset and image before a stroke (a stroke paints on the whole canvas, then is trimmed).</summary>
    private (int[] Offset, string? Image) strokeBefore;
    /// <summary>The frames picked in the strip (Shift or Ctrl+click): Delete removes them all.</summary>
    public IReadOnlyList<int> PickedFrames { get; set; } = Array.Empty<int>();

    public ExpressionDocumentViewModel(CharacterStudioViewModel studio, string id) : base(studio, LibraryKind.Expression, id) { zoom = 16; }

    public override SymbolRegular Icon => SymbolRegular.EmojiSmileSlight24;
    public Expression Model { get; private set; } = new();
    public CharacterRig? Rig { get; private set; }
    public ObservableCollection<ExpressionFrameItem> Frames { get; } = new();
    public PaletteViewModel Palette => Studio.Palette;

    [ObservableProperty] private int frameIndex;
    [ObservableProperty] private double zoom;
    [ObservableProperty] private bool showGrid = true;
    [ObservableProperty] private bool rawSlots;
    [ObservableProperty] private StudioTool tool = StudioTool.Pencil;
    [ObservableProperty] private BitmapSource? composite;
    [ObservableProperty] private PixelRect? faceBox;
    [ObservableProperty] private string? hoverText;

    public bool IsPortrait => Model.Target == "Portrait";
    public int Width => IsPortrait ? Rig?.Portrait?.FrameSize[0] ?? 64 : Rig?.FrameSize[0] ?? 16;
    public int Height => IsPortrait ? Rig?.Portrait?.FrameSize[1] ?? 64 : Rig?.FrameSize[1] ?? 32;
    public ExpressionFrame? Frame => Model.Frames.ElementAtOrDefault(FrameIndex);
    public int[] Box => (IsPortrait ? Rig?.Portrait?.FaceBox : Rig?.FaceBox.GetValueOrDefault(Model.Facing ?? "down")) ?? new[] { 4, 8, 8, 6 };

    partial void OnFrameIndexChanging(int value) => CancelStroke();
    partial void OnFrameIndexChanged(int value) { foreach (var f in Frames) f.IsSelected = f.Index == value; Render(); BuildFields(); }
    partial void OnToolChanging(StudioTool value) => CancelStroke();
    partial void OnRawSlotsChanged(bool value) => Render();

    public override bool Reload()
    {
        if (!IsOwnCommit) CancelStroke();
        if (Library?.Expressions().Expressions.FirstOrDefault(e => string.Equals(e.Id, ItemId, StringComparison.OrdinalIgnoreCase)) is not { } fresh) return false;
        if (!IsOwnCommit) Model = fresh;
        Title = Model.Id ?? ItemId;
        Rig = Library.Rig(Model.For);
        if (Frames.Count != Model.Frames.Count) { Frames.Clear(); for (int i = 0; i < Model.Frames.Count; i++) Frames.Add(new ExpressionFrameItem(this, i)); }
        FrameIndex = Math.Clamp(FrameIndex, 0, Math.Max(0, Model.Frames.Count - 1));
        foreach (var f in Frames) f.IsSelected = f.Index == FrameIndex;
        Render();
        if (!IsOwnCommit) BuildFields();
        OnPropertyChanged(nameof(Width)); OnPropertyChanged(nameof(Height)); OnPropertyChanged(nameof(IsPortrait));
        return true;
    }

    /// <summary>The live face (standing frame for the facing, or neutral portrait) with this frame applied, like the runtime.</summary>
    private RgbaImage? Face()
    {
        if (Rig?.Npc is not { } npc) return null;
        if (IsPortrait) return Context.NpcTexture(npc, true) is { } sheet ? StudioImages.Frame(sheet, Width, Height, 0) : null;
        int stand = Rig.Frames.TryGetValue(Model.Facing ?? "down", out var f) ? f.Stand : 0;
        try { return new EditorLayerSourceResolver(Context, new Pose { FrameSize = Rig.FrameSize }, Model.Facing ?? "down", new List<string>()).SampleFrame(Rig, stand); }
        catch (ArgumentException) { return null; }
    }

    private RgbaImage FrameImage(ExpressionFrame frame) => Context.PackImage(frame.Image) ?? StudioImages.Blank(Box[2], Box[3]);

    public void Render()
    {
        // The whole face is paintable (the face box is only where the paint is anchored), so no box is drawn.
        FaceBox = null;
        var shown = Draw(FrameIndex);
        Composite = shown is { } image ? StudioImages.ToBitmap(image) : null;
        if (shown != null) Palette.ShowPictureColors(shown);   // "In this picture": her face's own colours
        Problems = Rig == null ? "Pick the rig this expression is for (For)." : Face() == null ? $"The game export has no {(IsPortrait ? "portrait" : "sprite")} for {Rig.Npc}." : null;
        foreach (var item in Frames) item.Thumbnail = Draw(item.Index) is { } thumb ? StudioImages.ToBitmap(thumb) : null;
        foreach (var item in Frames) item.Caption = Model.Frames[item.Index].Duration > 0 ? $"{Model.Frames[item.Index].Duration} ms" : "hold";
    }

    private RgbaImage? Draw(int index)
    {
        var face = Face();
        if (face == null || Model.Frames.ElementAtOrDefault(index) is not { } frame) return face;
        var head = StudioImages.Clone(face);
        var problems = new List<string>();
        var resolver = new EditorLayerSourceResolver(Context, new Pose { FrameSize = new[] { Width, Height } }, Model.Facing ?? "down", problems);
        try { ExpressionOverlay.Apply(head, FrameImage(frame), Box, frame, RawSlots ? null : Model.ColorSlots, Context.Colors(resolver, problems)); }
        catch (ArgumentException ex) { Problems = ex.Message; }
        return head;
    }

    // ---------------- painting ----------------

    public void PointerDown(int x, int y, PointerButton button, bool alt)
    {
        if (Frame is not { } frame) return;
        if (Tool == StudioTool.Picker || alt)
        {
            // What you see: her face with this frame on it (the overlay alone is mostly empty).
            if (Draw(FrameIndex) is { } shown && PixelTools.Get(shown, x, y) is var picked && picked >> 24 != 0)
            { if (button == PointerButton.Primary) Palette.Primary = picked; else Palette.Secondary = picked; }
            return;
        }
        uint color = Tool == StudioTool.Eraser ? 0u : button == PointerButton.Primary ? Palette.Primary : Palette.Secondary;
        if (Tool != StudioTool.Eraser && color >> 24 == 0)
        {
            Ui.Toast("Pick a colour first", "You're painting with Transparent, which changes nothing here. Click a swatch, or Alt+click her face to take a colour from it. To remove her own pixels, use Remove her pixels.", ToastKind.Info);
            return;
        }
        color = PaintColor(color);
        // Paint anywhere on the canvas: the stroke works on a canvas-sized copy (the face box is only the anchor), and
        // PointerUp trims it to what was painted.
        strokeBefore = ((int[])frame.Offset.Clone(), frame.Image);
        var working = WorkingCanvas(frame);
        frame.Image ??= Library?.UniquePath($"art/expressions/{Model.Id}-{FrameIndex}.png") ?? $"art/expressions/{Model.Id}-{FrameIndex}.png";
        frame.Offset = new[] { -Box[0], -Box[1] };
        stroke = PaintSession.Begin(Context, frame.Image, working, 0, 0, color);
        if (Tool == StudioTool.Fill) { FillAsSeen(stroke.Image, x, y, color); PointerUp(); return; }
        stroke.DrawTo(x, y);
        Render();
    }

    /// <summary>
    /// The colour a stroke puts in the overlay. A slot colour paints her real colour (what the swatch shows) unless this
    /// expression maps that slot; Erase stays Erase (it removes her own pixels).
    /// </summary>
    private uint PaintColor(uint color) =>
        PaletteViewModel.SlotGroup(color) is { } slot && Model.ColorSlots?.ContainsKey(slot) != true ? Palette.Resolved(color) : color;

    /// <summary>The frame's overlay on a canvas-sized image, at its place over the face.</summary>
    private RgbaImage WorkingCanvas(ExpressionFrame frame)
    {
        var canvas = StudioImages.Blank(Width, Height);
        var overlay = FrameImage(frame);
        int ox = Box[0] + frame.Offset[0], oy = Box[1] + frame.Offset[1];
        for (int y = 0; y < overlay.Height; y++)
            for (int x = 0; x < overlay.Width; x++)
                if (x + ox >= 0 && y + oy >= 0 && x + ox < Width && y + oy < Height)
                    canvas.Pixels[(y + oy) * Width + x + ox] = overlay.Pixels[y * overlay.Width + x];
        return canvas;
    }

    /// <summary>Fill the area of one colour as you see it (her face with this frame on it), in the overlay.</summary>
    private void FillAsSeen(RgbaImage overlay, int x, int y, uint color)
    {
        if (Draw(FrameIndex) is not { } shown || x < 0 || y < 0 || x >= shown.Width || y >= shown.Height) return;
        uint target = shown.Pixels[y * shown.Width + x];
        var seen = new bool[shown.Width * shown.Height];
        var todo = new Stack<(int X, int Y)>();
        todo.Push((x, y));
        while (todo.Count > 0)
        {
            var (px, py) = todo.Pop();
            if (px < 0 || py < 0 || px >= shown.Width || py >= shown.Height) continue;
            int i = py * shown.Width + px;
            if (seen[i] || shown.Pixels[i] != target) continue;
            seen[i] = true;
            if (px < overlay.Width && py < overlay.Height) overlay.Pixels[py * overlay.Width + px] = color;
            todo.Push((px + 1, py)); todo.Push((px - 1, py)); todo.Push((px, py + 1)); todo.Push((px, py - 1));
        }
    }

    /// <summary>Trim a canvas-sized overlay to what was painted: the image and its place on the canvas.</summary>
    private static (RgbaImage Image, int X, int Y) Trim(RgbaImage canvas)
    {
        int minX = canvas.Width, minY = canvas.Height, maxX = -1, maxY = -1;
        for (int y = 0; y < canvas.Height; y++)
            for (int x = 0; x < canvas.Width; x++)
                if (canvas.Pixels[y * canvas.Width + x] >> 24 != 0) { minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); }
        if (maxX < 0) return (StudioImages.Blank(1, 1), 0, 0);
        var image = StudioImages.Blank(maxX - minX + 1, maxY - minY + 1);
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
                image.Pixels[(y - minY) * image.Width + x - minX] = canvas.Pixels[y * canvas.Width + x];
        return (image, minX, minY);
    }

    /// <summary>Paint with Erase: it removes her own pixels (closed eyes start by removing the open ones).</summary>
    [RelayCommand] private void RemoveHerPixels() { Palette.Primary = ColorSlots.Erase; if (Tool is StudioTool.Eraser or StudioTool.Picker) Tool = StudioTool.Pencil; }

    public void PointerMove(int x, int y)
    {
        HoverText = x >= 0 && y >= 0 && x < Width && y < Height ? $"{x}, {y}" : null;
        if (stroke == null) return;
        stroke.DrawTo(x, y);
        Render();
    }

    public void PointerUp()
    {
        if (stroke is not { } done) return;
        stroke = null;
        var image = done.End();
        int index = FrameIndex;
        var frame = Model.Frames[index];
        if (!done.Changed)
        {
            frame.Offset = strokeBefore.Offset; frame.Image = strokeBefore.Image;
            Render();
            return;
        }
        var (trimmed, x, y) = Trim(image);
        string path = frame.Image!;
        Save(Tool == StudioTool.Eraser ? "Erase expression" : "Paint expression", lib =>
        {
            frame.Image = path;
            frame.Offset = new[] { x - Box[0], y - Box[1] };
            lib.WriteImage(path, trimmed);
        });
    }

    public void CancelStroke()
    {
        if (stroke is not { } active) return;
        active.Cancel();
        stroke = null;
        if (Frame is { } frame) { frame.Offset = strokeBefore.Offset; frame.Image = strokeBefore.Image; }
        Render();
    }

    private void Save(string label, Action<PackLibrary> apply)
    {
        Commit(label, lib => { apply(lib); lib.SaveExpression(Model); });
        Context.InvalidatePack();
        Render();
    }

    [RelayCommand]
    private void AddFrame()
    {
        int at = FrameIndex + 1;
        Save("Add expression frame", lib =>
        {
            var copy = new ExpressionFrame { Offset = (int[])(Frame?.Offset ?? new[] { 0, 0 }).Clone(), Duration = Math.Max(Frame?.Duration ?? 0, 120) };
            copy.Image = LibraryCopies.CopyImage(lib, Frame?.Image, "art/expressions/", $"{Model.Id}-{at}");
            Model.Frames.Insert(at, copy);
        });
        Reload();
        FrameIndex = at;
    }

    [RelayCommand]
    private void DeleteFrame()
    {
        // The picked frames (or this one); picking them all keeps frame 1.
        var doomed = (PickedFrames.Count > 1 ? PickedFrames : new[] { FrameIndex }).Distinct().Where(i => i >= 0 && i < Model.Frames.Count).OrderBy(i => i).ToList();
        if (doomed.Count >= Model.Frames.Count) doomed.Remove(0);
        if (doomed.Count == 0) { Ui.Toast("Last frame", "An expression needs at least one frame.", ToastKind.Info); return; }
        Save(doomed.Count == 1 ? "Delete expression frame" : $"Delete {doomed.Count} expression frames", _ =>
        {
            foreach (int index in doomed.OrderByDescending(i => i)) Model.Frames.RemoveAt(index);
        });
        PickedFrames = Array.Empty<int>();
        FrameIndex = Math.Min(doomed[0], Model.Frames.Count - 1);
        Reload();
    }

    // ---------------- properties ----------------

    public override void BuildFields()
    {
        Fields.Clear();
        var commit = FieldCommit(lib => lib.SaveExpression(Model));
        Fields.Add(new SectionField("Expression", "Painted over the face, anywhere. \"Remove her pixels\" clears the face's own pixels where you paint."));
        Fields.Add(new TextField("ID", "Id", () => Model.Id, v => Rename(v), (l, a, r) => a()) { AllowNull = false, Help = "Say beats and Expression keys use it. Renaming updates them." });
        var rigs = (Library?.Items(LibraryKind.Rig) ?? Enumerable.Empty<LibraryItem>()).Select(r => new ChoiceItem(r.Id, r.Id, r.Detail));
        Fields.Add(new ChoiceField("For (rig)", "For", rigs, () => Model.For, v => Model.For = v, commit) { Rebuild = true });
        Fields.Add(new ChoiceField("On", "Target", new[] { new ChoiceItem("Sprite", "The sprite's face"), new ChoiceItem("Portrait", "The portrait's face") }, () => Model.Target, v => Model.Target = v ?? "Sprite", commit) { Rebuild = true });
        if (!IsPortrait) Fields.Add(new ChoiceField("Facing", "Facing", StudioOptions.Facings(), () => Model.Facing, v => Model.Facing = v, commit) { Rebuild = true });
        if (Frame is { } frame)
        {
            Fields.Add(new SectionField($"Frame {FrameIndex + 1}"));
            Fields.Add(new NumberField("Shows for", "Duration", () => frame.Duration, v => frame.Duration = Math.Max(0, v ?? 0), commit) { Minimum = 0, Suffix = "ms", Help = "0 holds it. Several frames with times animate (a blink)." });
            Fields.Add(new NumberField("Nudge x", "Offset", () => frame.Offset[0], v => frame.Offset = new[] { v ?? 0, frame.Offset[1] }, commit) { Suffix = "px" });
            Fields.Add(new NumberField("Nudge y", "Offset", () => frame.Offset[1], v => frame.Offset = new[] { frame.Offset[0], v ?? 0 }, commit) { Suffix = "px" });
        }
        var slots = new ListField("Colour slots", "ColorSlots", "Add slot", () => commit("Add colour slot", () =>
        {
            Model.ColorSlots ??= new(StringComparer.OrdinalIgnoreCase);
            string slot = Core.Palette.ColorSlots.Names.FirstOrDefault(n => n != "Erase" && !Model.ColorSlots.ContainsKey(n)) ?? "Custom1";
            Model.ColorSlots[slot] = Rig != null && Rig.ColorSamples.ContainsKey(slot) ? $"{Rig.Id}.{slot}" : "#E07090";
        }, true)) { EmptyText = "Slot colours paint as they are. Add Skin to use the character's real skin." };
        foreach (var (slot, source) in (Model.ColorSlots ?? new()).ToArray())
        {
            string key = slot;
            var row = new ListRowViewModel(() => commit("Remove colour slot", () => Model.ColorSlots!.Remove(key), true)) { Caption = key };
            row.Cells.Add(new TextField("", $"ColorSlots.{key}", () => Model.ColorSlots![key], v => Model.ColorSlots![key] = v ?? "#000000", commit) { Placeholder = "rig.Skin or #RRGGBB" });
            slots.Rows.Add(row);
        }
        Fields.Add(slots);
        if (Rig != null) Fields.Add(new ActionField("Edit the face box (rig)", () => Studio.OpenItem(LibraryKind.Rig, Rig.Id!)));
    }

    private void Rename(string? value)
    {
        string? next = value == null ? null : PackDocument.ToContentId(value);
        if (next == null || next == Model.Id || Library == null) return;
        if (Library.Expressions().Expressions.Any(e => e.Id == next)) { Ui.Toast("Name taken", $"There's already an expression called '{next}'.", ToastKind.Warning); return; }
        string old = Model.Id!;
        Commit($"Rename expression {old}", lib =>
        {
            var all = lib.Expressions();
            foreach (var e in all.Expressions.Where(e => e.Id == old)) e.Id = next;
            lib.SaveExpressions(all);
            Model.Id = next;
            References.RenameExpression(lib, Studio.Shell.Document!, old, next);
        });
        ItemId = next;
        Reload();
    }

    public override void Closing() { CancelStroke(); }
}
