using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Models;

namespace SDCharacterStudio.Editor.Studio;

/// <summary>
/// Where a character's head ends in her standing frames (the chin in front and side views, the hair in the back view),
/// read from her own pixels, so a starting rig's head masks fit her and not an average character (the owner: "she's cut
/// below her nose making it look like she's pacman"). Her clothes are the colours of the middle of her torso that her
/// head doesn't use (the outline and skin shades are shared, so they don't count); the head ends on the row above the
/// first one whose middle shows them. Side views follow the front view within a pixel (long hair hanging in front of
/// her hides her clothes there); the back view may reach a little lower (hair).
/// </summary>
public static class HeadFinder
{
    private static readonly string[] Facings = { "down", "right", "up", "left" };

    /// <summary>Vanilla characters the colours can't tell (a hat the colour of the coat): their last head row, 16×32 frames.</summary>
    private static readonly Dictionary<string, int> Known = new(StringComparer.OrdinalIgnoreCase) { ["Gunther"] = 16 };

    /// <summary>The last head row for each facing, from her own sheet (not an outfit), or null when it can't tell.</summary>
    public static IReadOnlyDictionary<string, int>? Rows(StudioContext context, string npc, CharacterRig? rig = null)
    {
        var character = context.Catalog?.Character(npc);
        if (context.NpcTexture(npc, false, ownOutfits: false) is not { } sheet) return null;
        int width = rig?.FrameSize[0] ?? character?.FrameWidth ?? 16, height = rig?.FrameSize[1] ?? character?.FrameHeight ?? 32;
        return Rows(character?.Name ?? npc, facing =>
        {
            int index = rig != null && rig.Frames.TryGetValue(facing, out var frames) ? frames.Stand : Array.IndexOf(Facings, facing) * 4;
            return StudioImages.Frame(sheet, width, height, index);
        });
    }

    /// <summary>The last head row for each facing, from her standing frames, or null when it can't tell.</summary>
    public static IReadOnlyDictionary<string, int>? Rows(string npc, Func<string, RgbaImage?> standing)
    {
        var found = Facings.ToDictionary(f => f, f => standing(f) is { } frame && !StudioImages.IsEmpty(frame) ? Detect(frame, f == "up") : null);
        int? front = found["down"] ?? (found["right"], found["left"]) switch
        {
            (int r, int l) => Math.Min(r, l), (int r, null) => r, (null, int l) => l, _ => null,
        };
        if (front is not int d)
        {
            if (!Known.TryGetValue(npc, out int known) || standing("down") is not { Width: 16, Height: 32 }) return null;
            d = known;
            found = Facings.ToDictionary(f => f, _ => (int?)known);
        }
        return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["down"] = d,
            ["right"] = Math.Clamp(found["right"] ?? d, d - 1, d + 1),
            ["left"] = Math.Clamp(found["left"] ?? d, d - 1, d + 1),
            ["up"] = Math.Clamp(found["up"] ?? d, d - 1, d + 4),
        };
    }

    /// <summary>The last head row of one standing frame, or null when her clothes can't be told from her head.</summary>
    public static int? Detect(RgbaImage frame, bool back)
    {
        int w = frame.Width, h = frame.Height;
        uint At(int x, int y) => frame.Pixels[y * w + x];
        bool Shown(int x, int y) => At(x, y) >> 24 != 0;
        int top = -1, bottom = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (Shown(x, y)) { if (top < 0) top = y; bottom = y; break; }
        int tall = bottom - top + 1;
        if (top < 0 || tall < 10) return null;
        int Row(double share) => top + (int)Math.Round(share * tall);

        // Her head's colours: everything in the top 30% (hair, hat, outline, the top of her face).
        var head = new HashSet<uint>();
        for (int y = top; y <= Row(0.3); y++)
            for (int x = 0; x < w; x++)
                if (Shown(x, y)) head.Add(At(x, y));

        // The middle of her torso, five pixels wide, between 55% and 72% of her height.
        int from = Row(0.55), to = Row(0.72), rows = 0;
        double middle = 0;
        for (int y = from; y <= to; y++)
        {
            int left = -1, right = -1;
            for (int x = 0; x < w; x++) if (Shown(x, y)) { if (left < 0) left = x; right = x; }
            if (left < 0) continue;
            middle += (left + right) / 2.0; rows++;
        }
        if (rows == 0) return null;
        middle /= rows;
        int bandLeft = Math.Max(0, (int)Math.Round(middle - 2.5));
        int bandRight = Math.Min(w - 1, (int)Math.Round(middle + 2.5));
        var counts = new Dictionary<uint, int>();
        for (int y = from; y <= to; y++)
            for (int x = bandLeft; x <= bandRight; x++)
                if (Shown(x, y)) counts[At(x, y)] = counts.GetValueOrDefault(At(x, y)) + 1;
        var clothes = counts.Where(c => c.Value >= 2 && !head.Contains(c.Key)).Select(c => c.Key).ToHashSet();
        if (clothes.Count == 0) return null;

        for (int y = Row(0.25); y <= Math.Min(h - 1, Row(0.75)); y++)
        {
            int seen = 0;
            for (int x = bandLeft; x <= bandRight; x++) if (Shown(x, y) && clothes.Contains(At(x, y))) seen++;
            if (seen < 2) continue;
            int last = y - 1;
            double share = (last - top + 1) / (double)tall;
            // A head is roughly a third to a half of a villager; much more is a coat the colour of the hat.
            return share >= 0.28 && share <= (back ? 0.7 : 0.6) ? last : null;
        }
        return null;
    }
}
