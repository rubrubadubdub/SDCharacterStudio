using SDCharacterStudio.Core.Compositing;

namespace SDCharacterStudio.Editor.Studio;

/// <summary>How a body part moves in a preset frame: whole pixels, and a tilt in degrees about its base (a head about the neck).</summary>
public sealed record PartMove(int Dx, int Dy, double Angle = 0)
{
    public static readonly PartMove None = new(0, 0);
}

/// <summary>One frame of a preset: its time, how much of the legs stays (kneeling hides the shins), and each part's move.</summary>
public sealed record PresetFrame(int Ms, double KeepThighs, Func<string, string, PartMove> Move);

/// <summary>A ready-made pose or a little animation, made from a character's own body map (so it fits her body).</summary>
/// <param name="Plays">"hold" (one frame), "once" (then the last frame holds) or "loop".</param>
public sealed record PosePreset(string Id, string Title, string Hint, string Plays, IReadOnlyList<PresetFrame> Frames)
{
    public bool IsAnimated => Frames.Count > 1;
}

/// <summary>
/// Ready-made poses (D-49, replacing D-48's recipes in the simple editor): each one moves her parts by her body map,
/// one frame or a few, so the result is her own pixels rearranged (live: it follows sprite mods and outfits) and fits
/// her body. Fix the body map where it's wrong and every preset follows. Moves are in sprite pixels; "forward" follows
/// the facing. The parts are drawn legs first, then the body, the arms and the head on top.
/// </summary>
public static class PosePresets
{
    private static readonly string[] DrawOrder = { "legs", "body", "left arm", "right arm", "head" };
    private static int Fwd(string facing) => facing switch { "left" => -1, "right" => 1, _ => 0 };
    private static bool Upper(string part) => part != "legs";
    private static PresetFrame Still(Func<string, string, PartMove> move, double keep = 1) => new(1000, keep, move);
    private static PresetFrame Step(int ms, Func<string, string, PartMove> move, double keep = 1) => new(ms, keep, move);
    private static PartMove Whole(int dx, int dy) => new(dx, dy);

    public static IReadOnlyList<PosePreset> All { get; } = new[]
    {
        new PosePreset("bow", "Bow the head", "Head down, a little forward.", "hold", new[] { Still((p, f) => p == "head" ? new PartMove(Fwd(f), 2) : PartMove.None) }),
        new PosePreset("look-down", "Look down", "Head a pixel lower.", "hold", new[] { Still((p, f) => p == "head" ? new PartMove(0, 1) : PartMove.None) }),
        new PosePreset("tilt", "Tilt the head", "Head tipped to one side, about the neck.", "hold", new[] { Still((p, f) => p == "head" ? new PartMove(0, 0, f == "left" ? -10 : 10) : PartMove.None) }),
        new PosePreset("lean", "Lean forward", "Head, body and arms a pixel forward and down.", "hold", new[] { Still((p, f) => Upper(p) ? new PartMove(Fwd(f), 1) : PartMove.None) }),
        new PosePreset("slump", "Slump", "Sad: the head and shoulders sag.", "hold", new[] { Still((p, f) => p == "head" ? new PartMove(Fwd(f), 2) : Upper(p) ? new PartMove(0, 1) : PartMove.None) }),
        new PosePreset("shrug", "Shrug", "Head down a touch, shoulders up (front and back views).", "hold", new[] { Still((p, f) => BodyMaps.IsArm(p) ? new PartMove(0, -1) : p == "head" ? new PartMove(0, 1) : PartMove.None) }),
        new PosePreset("crouch", "Crouch", "Knees bent: a little lower.", "hold", new[] { Still((p, f) => PartMove.None, keep: 0.7) }),
        new PosePreset("kneel", "Kneel", "On the knees: the lower legs go out of sight.", "hold", new[] { Still((p, f) => PartMove.None, keep: 0.4) }),
        new PosePreset("beg", "Beg", "Kneeling, head bowed, hands raised (front and back views).", "hold",
            new[] { Still((p, f) => BodyMaps.IsArm(p) ? new PartMove(0, -3) : p == "head" ? new PartMove(Fwd(f), 1) : PartMove.None, keep: 0.4) }),

        new PosePreset("nod", "Nod", "Yes: the head dips and comes back.", "once", new[]
        {
            Step(120, (p, f) => PartMove.None), Step(160, (p, f) => p == "head" ? new PartMove(0, 1) : PartMove.None),
            Step(200, (p, f) => p == "head" ? new PartMove(Fwd(f), 2) : PartMove.None), Step(160, (p, f) => p == "head" ? new PartMove(0, 1) : PartMove.None),
            Step(150, (p, f) => PartMove.None),
        }),
        new PosePreset("shake", "Shake the head", "No: the head turns side to side.", "once", new[]
        {
            Step(110, (p, f) => p == "head" ? new PartMove(-1, 0) : PartMove.None), Step(110, (p, f) => p == "head" ? new PartMove(1, 0) : PartMove.None),
            Step(110, (p, f) => p == "head" ? new PartMove(-1, 0) : PartMove.None), Step(110, (p, f) => p == "head" ? new PartMove(1, 0) : PartMove.None),
            Step(150, (p, f) => PartMove.None),
        }),
        new PosePreset("laugh", "Laugh", "Shoulders and head bob.", "loop", new[]
        {
            Step(140, (p, f) => PartMove.None), Step(140, (p, f) => Upper(p) ? new PartMove(0, 1) : PartMove.None),
        }),
        new PosePreset("jump", "Jump for joy", "Up and back down.", "once", new[]
        {
            Step(90, (p, f) => Whole(0, -1)), Step(120, (p, f) => Whole(0, -3)), Step(110, (p, f) => Whole(0, -1)), Step(140, (p, f) => PartMove.None),
        }),
        new PosePreset("shiver", "Shiver", "A fast little shake.", "loop", new[] { Step(60, (p, f) => Whole(-1, 0)), Step(60, (p, f) => Whole(1, 0)) }),
        new PosePreset("sob", "Sob", "Head down, shoulders shaking.", "loop", new[]
        {
            Step(220, (p, f) => p == "head" ? new PartMove(Fwd(f), 2) : Upper(p) ? new PartMove(0, 1) : PartMove.None),
            Step(220, (p, f) => p == "head" ? new PartMove(Fwd(f), 3) : Upper(p) ? new PartMove(0, 2) : PartMove.None),
        }),
    };

    public static PosePreset? Find(string? id) => All.FirstOrDefault(p => p.Id == id);

    /// <summary>
    /// A frame of a preset as a flat-picture map over her standing frame (<paramref name="standing"/>, labelled by
    /// <paramref name="bodyMap"/>): each map cell names the standing-frame pixel it shows.
    /// </summary>
    public static RgbaImage Map(RgbaImage standing, RgbaImage bodyMap, PresetFrame frame, string facing)
    {
        int w = standing.Width, h = standing.Height;
        var result = new RgbaImage(w, h, new uint[w * h]);
        string LabelAt(int x, int y) => PixelTools.Get(standing, x, y) >> 24 == 0 ? "" : BodyMaps.PartAt(bodyMap, x, y) ?? "body";

        // Kneeling and crouching: only the thighs stay, and everything drops so the knees meet the ground.
        int legsTop = -1, legsBottom = -1;
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) if (LabelAt(x, y) == "legs") { if (legsTop < 0) legsTop = y; legsBottom = y; }
        int lastLegRow = legsBottom, drop = 0;
        if (frame.KeepThighs < 1 && legsTop >= 0)
        {
            lastLegRow = legsTop + Math.Max(1, (int)Math.Ceiling((legsBottom - legsTop + 1) * frame.KeepThighs)) - 1;
            drop = legsBottom - lastLegRow;
        }

        foreach (var part in DrawOrder)
        {
            var cells = new List<(int X, int Y)>();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (LabelAt(x, y) == part && !(part == "legs" && y > lastLegRow)) cells.Add((x, y));
            if (cells.Count == 0) continue;
            var move = frame.Move(part, facing);
            int dx = move.Dx, dy = move.Dy + drop;
            if (Math.Abs(move.Angle) < 1e-9)
            {
                foreach (var (x, y) in cells)
                    if (x + dx >= 0 && y + dy >= 0 && x + dx < w && y + dy < h) result.Pixels[(y + dy) * w + x + dx] = Compositor.MapCell(x, y);
                continue;
            }
            // A tilt: RotSprite about the part's base (the neck for a head), so no pixel is mixed or invented.
            var bounds = SelectionMask.FromCells(w, h, cells).Bounds;
            var grid = Enumerable.Repeat(-1, bounds.Width * bounds.Height).ToArray();
            for (int k = 0; k < cells.Count; k++) grid[(cells[k].Y - bounds.Y) * bounds.Width + cells[k].X - bounds.X] = k;
            var transform = new PieceTransform(dx, dy, 1, 1, move.Angle, bounds.X + bounds.Width / 2.0, bounds.Y + bounds.Height);
            var landed = PixelTransform.Land(grid, bounds, transform, w, h, k => PixelTools.Get(standing, cells[k].X, cells[k].Y));
            for (int i = 0; i < landed.Length; i++) if (landed[i] >= 0) result.Pixels[i] = Compositor.MapCell(cells[landed[i]].X, cells[landed[i]].Y);
        }
        return result;
    }
}
