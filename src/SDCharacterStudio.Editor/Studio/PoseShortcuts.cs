namespace SDCharacterStudio.Editor.Studio;

public sealed record Shortcut(string Keys, string Does);
public sealed record ShortcutGroup(string Title, IReadOnlyList<Shortcut> Items);

/// <summary>The pose editor's keys (doc 07 §16.1), for the shortcut sheet (F1 or ?). Aseprite's where they exist.</summary>
public static class PoseShortcuts
{
    public static IReadOnlyList<ShortcutGroup> All { get; } = new[]
    {
        new ShortcutGroup("Tools", new[]
        {
            new Shortcut("M", "Select a box"),
            new Shortcut("Q", "Lasso: draw around what you want"),
            new Shortcut("W", "Magic wand (Ctrl+click: that colour everywhere)"),
            new Shortcut("V", "Move what's selected, or the whole picture"),
            new Shortcut("B", "Pencil"),
            new Shortcut("E", "Eraser"),
            new Shortcut("D", "Shade: left click darker, right click lighter"),
            new Shortcut("G", "Fill (Ctrl+click: that colour everywhere)"),
            new Shortcut("I", "Eyedropper, then back to your tool"),
            new Shortcut("Hold Alt", "Pick a colour with any painting tool"),
            new Shortcut("L", "Line (Shift: 45°)"),
            new Shortcut("U / Shift+U", "Rectangle / ellipse (Shift while drawing: square, circle)"),
            new Shortcut("[  ]", "Smaller / bigger brush"),
            new Shortcut("X", "Swap the left and right colours"),
        }),
        new ShortcutGroup("Selection", new[]
        {
            new Shortcut("Shift / Alt", "Add to / take away from the selection"),
            new Shortcut("Drag inside", "Move what it shows (Ctrl+drag copies)"),
            new Shortcut("Ctrl+T", "Transform: move, scale and rotate with handles"),
            new Shortcut("Enter / Esc", "Apply / cancel a transform"),
            new Shortcut("Shift+R", "A quarter turn right (Ctrl+Shift+R: left)"),
            new Shortcut("Arrows", "Nudge 1 px (Shift: 4 px)"),
            new Shortcut("H or Shift+H", "Flip horizontally"),
            new Shortcut("Shift+V", "Flip vertically"),
            new Shortcut("Ctrl+A / Ctrl+D", "Select all / deselect"),
            new Shortcut("Ctrl+Shift+I / D", "Invert / reselect"),
            new Shortcut("Shift+I", "Isolate: dim everything else"),
            new Shortcut("Ctrl+C / X / V", "Copy / cut / paste"),
            new Shortcut("Delete", "Clear what it shows"),
            new Shortcut("Esc", "Cancel a drag, or deselect"),
        }),
        new ShortcutGroup("View and frames", new[]
        {
            new Shortcut("Wheel", "Zoom at the pointer"),
            new Shortcut("Space+drag, middle drag", "Pan"),
            new Shortcut("Ctrl+0", "Fit the frame"),
            new Shortcut("Tab on canvas", "Hide / restore side panels"),
            new Shortcut("Space (tap) or Enter", "Play / stop"),
            new Shortcut(", / .", "Previous / next frame"),
            new Shortcut("Home / End", "First / last frame"),
            new Shortcut("O or F3", "Onion skin"),
            new Shortcut("Shift+M", "Mirror the view (only how you see it)"),
            new Shortcut("Ctrl+Z / Ctrl+Y", "Undo / redo"),
            new Shortcut("F1 or ?", "This sheet"),
        }),
    };
}
