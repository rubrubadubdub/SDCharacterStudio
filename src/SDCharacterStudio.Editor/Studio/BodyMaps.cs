using SDCharacterStudio.Core.Compositing;

namespace SDCharacterStudio.Editor.Studio;

/// <summary>A body part: its name in recipes, what people call it, and its colour in a body map.</summary>
public sealed record BodyPart(string Id, string Label, uint Color);

/// <summary>
/// Body maps (D-49): which part of the character each pixel of her standing frame is, per facing, stored with her rig
/// (doc 03 §9) as a PNG whose colours name the parts. Made automatically (the puppet-part splitter with her rig's head
/// mask) and fixed with a brush where the guess is wrong (long hair that belongs to the head, an arm drawn over the
/// body). Part selection and the ready-made poses read it, so a pose is made for her body, not an average one.
/// </summary>
public static class BodyMaps
{
    public static IReadOnlyList<BodyPart> Parts { get; } = new[]
    {
        new BodyPart("head", "Head", 0xFFE0524A),
        new BodyPart("body", "Body", 0xFF52B86A),
        new BodyPart("left arm", "Arm (left)", 0xFF4A86E0),
        new BodyPart("right arm", "Arm (right)", 0xFF4AC8E0),
        new BodyPart("legs", "Legs", 0xFFE0B84A),
    };

    public static BodyPart? Find(string? id) => Parts.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The part a body map pixel names (null: not part of her).</summary>
    public static string? PartAt(RgbaImage map, int x, int y)
    {
        uint color = PixelTools.Get(map, x, y);
        return color >> 24 == 0 ? null : Parts.FirstOrDefault(p => p.Color == color)?.Id;
    }

    /// <summary>A body map guessed from her standing frame: the splitter's head (her rig's head mask), body, arms and legs.</summary>
    public static RgbaImage Auto(RgbaImage standing, Func<int, int, bool> isHead, string facing)
    {
        var map = StudioImages.Blank(standing.Width, standing.Height);
        foreach (var part in PartSplitter.Split(standing, isHead, facing))
        {
            uint color = Find(part.Name)?.Color ?? Parts[1].Color;
            for (int i = 0; i < part.Pixels.Length; i++) if (part.Pixels[i]) map.Pixels[i] = color;
        }
        return map;
    }

    /// <summary>Is this an arm (either side)?</summary>
    public static bool IsArm(string? part) => part is "left arm" or "right arm";
}
