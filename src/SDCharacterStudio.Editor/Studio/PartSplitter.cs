using SDCharacterStudio.Core.Compositing;

namespace SDCharacterStudio.Editor.Studio;

/// <summary>
/// Splits a character's sprite into puppet parts (D-48 stage 2): head, arms, body, legs. Works on the frame as shown
/// (canvas pixels): the head is what the rig's head mask covers (or everything above the neck); legs are the rows from
/// the hips down (where a gap opens between the legs, else the lower 40% of the body); in front and back views the
/// arms are the two outermost columns of each body row from the shoulders to the hands. Side views have no separate
/// arms (the near arm overlaps the body). Every shown pixel belongs to exactly one part.
/// </summary>
public static class PartSplitter
{
    public sealed record Part(string Name, bool[] Pixels);

    /// <param name="shown">The layer as composed on the canvas (alpha &gt; 0 = shown).</param>
    /// <param name="isHead">Whether a canvas pixel is the head (from the rig's head mask).</param>
    /// <param name="facing">down, up, left or right.</param>
    public static IReadOnlyList<Part> Split(RgbaImage shown, Func<int, int, bool> isHead, string facing)
    {
        int w = shown.Width, h = shown.Height;
        bool Opaque(int x, int y) => shown.Pixels[y * w + x] >> 24 != 0;
        var head = new bool[w * h];
        var body = new bool[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (Opaque(x, y)) (isHead(x, y) ? head : body)[y * w + x] = true;

        int top = -1, bottom = -1, left = w, right = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (body[y * w + x]) { if (top < 0) top = y; bottom = y; left = Math.Min(left, x); right = Math.Max(right, x); }
        var parts = new List<Part>();
        if (top < 0) { parts.Add(new("head", head)); return parts; }

        int hip = HipRow(body, w, top, bottom, left, right);
        var legs = new bool[w * h];
        for (int y = hip; y <= bottom; y++)
            for (int x = 0; x < w; x++)
                if (body[y * w + x]) { legs[y * w + x] = true; body[y * w + x] = false; }

        bool frontOrBack = facing is "down" or "up";
        var leftArm = new bool[w * h];
        var rightArm = new bool[w * h];
        if (frontOrBack)
        {
            // From the shoulders (one row under the body's top) down to the hands (a little below the hips).
            int handRow = Math.Min(bottom, hip + 1);
            for (int y = top + 1; y <= handRow; y++)
            {
                int l = -1, r = -1;
                for (int x = 0; x < w; x++) if (body[y * w + x] || legs[y * w + x]) { if (l < 0) l = x; r = x; }
                if (l < 0 || r - l < 5) continue;   // too narrow to have arms beside a body
                for (int x = l; x <= l + 1; x++) Move(x, y, leftArm);
                for (int x = r - 1; x <= r; x++) Move(x, y, rightArm);
            }
            void Move(int x, int y, bool[] arm)
            {
                int i = y * w + x;
                if (body[i]) { body[i] = false; arm[i] = true; }
                else if (legs[i]) { legs[i] = false; arm[i] = true; }
            }
        }
        // Drawn bottom to top: legs, body, arms, head.
        parts.Add(new("legs", legs));
        parts.Add(new("body", body));
        if (frontOrBack) { parts.Add(new(facing == "down" ? "right arm" : "left arm", leftArm)); parts.Add(new(facing == "down" ? "left arm" : "right arm", rightArm)); }
        parts.Add(new("head", head));
        return parts.Where(p => p.Pixels.Any(b => b)).ToList();
    }

    /// <summary>The first row of the legs: where a gap opens between them in the lower body, else 60% of the way down.</summary>
    private static int HipRow(bool[] body, int w, int top, int bottom, int left, int right)
    {
        int height = bottom - top + 1;
        int fallback = top + (int)Math.Round(height * 0.6);
        int centre = (left + right) / 2;
        int gapTop = -1;
        for (int y = bottom; y >= top + height / 2; y--)
        {
            bool gap = !body[y * w + centre] && Enumerable.Range(left, centre - left).Any(x => body[y * w + x]) && Enumerable.Range(centre + 1, Math.Max(0, right - centre)).Any(x => body[y * w + x]);
            if (gap) gapTop = y; else if (gapTop >= 0) break;
        }
        return gapTop >= 0 && bottom - gapTop >= 2 ? Math.Max(top + 1, gapTop - 1) : fallback;
    }
}
