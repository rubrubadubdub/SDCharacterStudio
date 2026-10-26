using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Models;

namespace SDCharacterStudio.Editor.Studio;

/// <summary>
/// A starting rig for any character (doc 03 §8): vanilla frame layout, neck points, face boxes, colour samples and
/// "above the neck line" head masks, scaled to the character's frame size. The neck line is where her own head ends
/// (<see cref="HeadFinder"/>), per facing, when her sprite is known; else row 15 of 32. The designer refines it in the
/// rig and mask editors (ponytails, hats, tall mod sprites).
/// </summary>
public static class RigFactory
{
    public static readonly string[] Facings = { "down", "right", "up", "left" };

    public sealed record Result(CharacterRig Rig, IReadOnlyList<(Mask Mask, RgbaImage Image)> Masks);

    /// <param name="headRows">Her last head row per facing (<see cref="HeadFinder"/>), or null for the average one.</param>
    public static Result ForNpc(string npc, string rigId, int frameWidth, int frameHeight, IReadOnlyDictionary<string, int>? headRows = null)
    {
        double sx = frameWidth / 16.0, sy = frameHeight / 32.0;
        int X(double v) => (int)Math.Round(v * sx);
        int Y(double v) => (int)Math.Round(v * sy);
        int[] Box(double x, double y, double w, double h)
        {
            int bx = Math.Clamp(X(x), 0, frameWidth - 1), by = Math.Clamp(Y(y), 0, frameHeight - 1);
            return new[] { bx, by, Math.Clamp(X(w), 1, frameWidth - bx), Math.Clamp(Y(h), 1, frameHeight - by) };
        }
        int neckY = Math.Max(0, Y(15));
        int Neck(string facing) => headRows != null && headRows.TryGetValue(facing, out int row) ? Math.Clamp(row, 0, frameHeight - 1) : neckY;
        // Her face sits on her chin: face boxes and the skin sample move with it, the shirt sample starts under it.
        double Lower(string facing) => (Neck(facing) - neckY) / sy;
        int[] FaceBox(string facing, double x, double w) => Box(x, 9 + Lower(facing), w, 6);
        var rig = new CharacterRig
        {
            Format = "1.0", Id = rigId, Kind = "Npc", Npc = npc, FrameSize = new[] { frameWidth, frameHeight },
            Frames = new(StringComparer.OrdinalIgnoreCase)
            {
                ["down"] = new FacingFrames { Stand = 0, Walk = new[] { 0, 1, 2, 3 } },
                ["right"] = new FacingFrames { Stand = 4, Walk = new[] { 4, 5, 6, 7 } },
                ["up"] = new FacingFrames { Stand = 8, Walk = new[] { 8, 9, 10, 11 } },
                ["left"] = new FacingFrames { Stand = 12, Walk = new[] { 12, 13, 14, 15 } }
            },
            NeckPoint = new(StringComparer.OrdinalIgnoreCase)
            {
                ["down"] = new[] { X(8), Neck("down") }, ["right"] = new[] { X(8), Neck("right") }, ["up"] = new[] { X(8), Neck("up") }, ["left"] = new[] { X(7), Neck("left") }
            },
            FaceBox = new(StringComparer.OrdinalIgnoreCase)
            {
                ["down"] = FaceBox("down", 4, 8), ["up"] = FaceBox("up", 4, 8), ["left"] = FaceBox("left", 2, 6), ["right"] = FaceBox("right", 8, 6)
            },
            ColorSamples = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Skin"] = new ColorSample { Mode = "auto-skin", Frame = 0, Box = Box(3, 7 + Lower("down"), 10, 8) },
                ["Outline"] = new ColorSample { Mode = "auto-edge", Frame = 0 },
                ["Hair"] = new ColorSample { Mode = "auto-common", Frame = 8, Box = Box(3, 1, 10, 7), Fallback = "Outline" },
                ["Shirt"] = new ColorSample { Mode = "auto-common", Frame = 0, Box = Box(3, 16 + Lower("down"), 10, 7) },
                ["Pants"] = new ColorSample { Mode = "auto-common", Frame = 0, Box = Box(3, 24, 10, 3), Fallback = "Shirt" },
                ["Shoes"] = new ColorSample { Mode = "auto-common", Frame = 0, Box = Box(2, 28, 12, 4), Fallback = "Pants" }
            },
            Portrait = new PortraitRig { FrameSize = new[] { 64, 64 }, NeckPoint = new[] { 32, 50 }, FaceBox = new[] { 18, 14, 28, 26 } }
        };
        var masks = new List<(Mask, RgbaImage)>();
        foreach (var facing in Facings)
        {
            string id = $"{rigId}-head-{facing}";
            rig.HeadMask[facing] = id;
            masks.Add((new Mask
            {
                Id = id, DisplayName = $"{npc} head, facing {facing}", FrameSize = new[] { frameWidth, frameHeight },
                Image = $"masks/{id}.png", Mode = "keep", Tags = new() { rigId, "head" }
            }, PixelTools.AboveRow(frameWidth, frameHeight, Neck(facing))));
        }
        return new Result(rig, masks);
    }

    /// <summary>The head mask a rig started with before heads were found per character (row 15 of 32, every facing).</summary>
    public static bool IsAverageHead(RgbaImage mask, int frameWidth, int frameHeight)
    {
        if (mask.Width != frameWidth || mask.Height != frameHeight) return false;
        var average = PixelTools.AboveRow(frameWidth, frameHeight, Math.Max(0, (int)Math.Round(15 * frameHeight / 32.0)));
        for (int i = 0; i < mask.Pixels.Length; i++)
            if ((mask.Pixels[i] >> 24 != 0) != (average.Pixels[i] >> 24 != 0)) return false;
        return true;
    }

    /// <summary>
    /// A rig made before heads were found per character: its head masks still cut every character at row 15. Give the
    /// untouched ones (and their untouched neck points and face boxes) her own head line. Masks someone edited stay.
    /// </summary>
    public static bool UpgradeAverageHeads(PackLibrary library, CharacterRig rig, IReadOnlyDictionary<string, int> headRows)
    {
        if (rig.FrameSize is not { Length: 2 } size) return false;
        int width = size[0], height = size[1];
        var fresh = ForNpc(rig.Npc ?? "", rig.Id ?? "", width, height, headRows).Rig;
        var average = ForNpc(rig.Npc ?? "", rig.Id ?? "", width, height).Rig;
        bool changed = false;
        foreach (var facing in Facings)
        {
            if (!rig.HeadMask.TryGetValue(facing, out var maskId) || library.Mask(maskId) is not { } mask || mask.PerFrame.Count > 0) continue;
            if (library.ReadImage(mask.Image) is not { } image || !IsAverageHead(image, width, height)) continue;
            int row = fresh.NeckPoint[facing][1];
            if (row == average.NeckPoint[facing][1]) continue;
            library.WriteImage(mask.Image!, PixelTools.AboveRow(width, height, row));
            if (rig.NeckPoint.TryGetValue(facing, out var neck) && neck.SequenceEqual(average.NeckPoint[facing])) rig.NeckPoint[facing] = fresh.NeckPoint[facing];
            if (rig.FaceBox.TryGetValue(facing, out var box) && box.SequenceEqual(average.FaceBox[facing])) rig.FaceBox[facing] = fresh.FaceBox[facing];
            changed = true;
        }
        if (changed) library.SaveRig(rig);
        return changed;
    }

    /// <summary>The player's rig: layered parts need no head masks (doc 03 §8).</summary>
    public static CharacterRig Farmer() => new()
    {
        Format = "1.0", Id = "farmer", Kind = "Farmer", FrameSize = new[] { 16, 32 },
        Frames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["down"] = new FacingFrames { Stand = 0, Walk = new[] { 1, 0, 2, 0 } },
            ["right"] = new FacingFrames { Stand = 6, Walk = new[] { 7, 6, 8, 6 } },
            ["up"] = new FacingFrames { Stand = 12, Walk = new[] { 13, 12, 14, 12 } },
            ["left"] = new FacingFrames { Stand = 6, Walk = new[] { 7, 6, 8, 6 } }
        },
        NeckPoint = new(StringComparer.OrdinalIgnoreCase) { ["down"] = new[] { 8, 15 }, ["right"] = new[] { 8, 15 }, ["up"] = new[] { 8, 15 }, ["left"] = new[] { 8, 15 } }
    };

    /// <summary>
    /// Ensure the pack has a rig (and its head masks) for an NPC; returns the rig ID. Call inside an edit. With the
    /// studio's <paramref name="context"/>, the head masks fit her own head, and an older rig's untouched ones are fixed.
    /// </summary>
    public static string EnsureNpcRig(PackLibrary library, Catalog.ExportCatalog? catalog, string npc, StudioContext? context = null)
    {
        if (library.RigFor(npc) is { Id: { } existing } rigNow)
        {
            if (context != null && HeadFinder.Rows(context, npc, rigNow) is { } rows) UpgradeAverageHeads(library, rigNow, rows);
            return existing;
        }
        var character = catalog?.Character(npc);
        int width = character?.FrameWidth ?? 16, height = character?.FrameHeight ?? 32;
        string id = library.UniqueId(LibraryKind.Rig, character?.Name ?? npc);
        var result = ForNpc(character?.Name ?? npc, id, width, height, context == null ? null : HeadFinder.Rows(context, npc));
        var masks = library.Masks();
        foreach (var (mask, image) in result.Masks)
        {
            if (masks.Masks.Any(m => string.Equals(m?.Id, mask.Id, StringComparison.OrdinalIgnoreCase))) continue;
            masks.Masks.Add(mask);
            library.WriteImage(mask.Image!, image);
        }
        library.SaveMasks(masks);
        library.SaveRig(result.Rig);
        return id;
    }

    public static string EnsureFarmerRig(PackLibrary library)
    {
        if (library.FarmerRig() is { Id: { } existing }) return existing;
        var rig = Farmer();
        rig.Id = library.UniqueId(LibraryKind.Rig, "farmer");
        library.SaveRig(rig);
        return rig.Id;
    }
}
