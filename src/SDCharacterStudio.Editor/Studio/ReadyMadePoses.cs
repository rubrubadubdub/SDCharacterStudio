using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Project;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Studio;

/// <summary>
/// Ready-made poses made for one character (D-49 phase 4): her standing frame, her body map, and a preset's moves
/// become pixel maps of her own pixels. The pose editor draws them on a frame; an animation's "Show a pose" step makes a
/// library pose of one (the owner: "I don't see where I can select from the ready-made poses, only the ones I make").
/// </summary>
public static class ReadyMadePoses
{
    /// <summary>Her body map for a facing: the one saved with her rig, else a fresh guess from her standing frame.</summary>
    public static RgbaImage BodyMap(StudioContext context, CharacterRig? rig, RgbaImage standing, string facing, out bool saved)
    {
        saved = false;
        if (rig?.BodyMap?.TryGetValue(facing, out var path) == true && context.PackImage(path) is { } image && image.Width == standing.Width && image.Height == standing.Height)
        { saved = true; return image; }
        return BodyMaps.Auto(standing, HeadTest(context, rig, facing, standing), facing);
    }

    /// <summary>
    /// Is a standing-frame pixel her head? Her rig's head mask, else where her own head ends (<see cref="HeadFinder"/>),
    /// else everything above her neck. A rig's untouched average mask (row 15 for everyone) gives way to her own head.
    /// </summary>
    public static Func<int, int, bool> HeadTest(StudioContext context, CharacterRig? rig, string facing, RgbaImage standing)
    {
        if (rig != null && rig.HeadMask.TryGetValue(facing, out var maskId) && context.FindMask(maskId) is { } mask)
        {
            int stand = rig.Frames.TryGetValue(facing, out var frames) ? frames.Stand : 0;
            string? path = mask.PerFrame.TryGetValue(stand.ToString(System.Globalization.CultureInfo.InvariantCulture), out var perFrame) ? perFrame : mask.Image;
            if (context.PackImage(path) is { } image && image.Width == standing.Width && image.Height == standing.Height
                && !(mask.PerFrame.Count == 0 && RigFactory.IsAverageHead(image, standing.Width, standing.Height) && OwnHeadRow(context, rig, facing) != null))
            {
                bool remove = string.Equals(mask.Mode, "remove", StringComparison.OrdinalIgnoreCase);
                return (x, y) => (image.Pixels[y * image.Width + x] >> 24 != 0) != remove;
            }
        }
        if (rig != null && OwnHeadRow(context, rig, facing) is int row) return (_, y) => y <= row;
        int neckY = rig != null && rig.NeckPoint.TryGetValue(facing, out var neck) && neck is { Length: 2 } ? neck[1] : standing.Height / 3;
        return (_, y) => y <= neckY;
    }

    private static int? OwnHeadRow(StudioContext context, CharacterRig rig, string facing) =>
        rig.Npc != null && HeadFinder.Rows(context, rig.Npc, rig) is { } rows && rows.TryGetValue(facing, out int row) ? row : null;

    /// <summary>The preset's frames as pixel maps over her standing frame, one per frame.</summary>
    public static IReadOnlyList<RgbaImage> Maps(PosePreset preset, RgbaImage standing, RgbaImage body, string facing) =>
        preset.Frames.Select(f => PosePresets.Map(standing, body, f, facing)).ToList();

    /// <summary>The library pose a ready-made pose becomes for a character and facing.</summary>
    public static string PoseId(string character, string presetId, string facing) => PackDocument.ToContentId($"{character}-{presetId}-{facing}");

    /// <summary>
    /// Make the ready-made pose a library pose for her (one flat frame per preset frame, her own pixels, still live), or
    /// reuse the one made before. Null when the game export has no standing frame of hers for that facing.
    /// </summary>
    public static string? Ensure(CharacterStudioViewModel studio, PackLibrary library, CharacterChoice who, string presetId, string facing)
    {
        if (who.IsFarmer || PosePresets.Find(presetId) is not { } preset) return null;
        string id = PoseId(who.Key, presetId, facing);
        if (library.Pose(id) != null) return id;
        if (studio.StandingFrame(who, facing) is not { } standing) return null;
        var pose = PoseFactory.Create(studio, library, who, "character", id, facing);
        if (pose.FrameSize is not [var width, var height] || standing.Width != width || standing.Height != height) return null;
        FlatPose.MakeFlat(library, pose);
        var maps = Maps(preset, standing, BodyMap(studio.Context, library.Rig(pose.For), standing, facing, out _), facing);
        var first = pose.Frames[0];
        if (PoseDocumentViewModel.FlatOf(first) is not { } flat) return null;
        flat.Live.SourceFrame = new SourceFrame { Facing = facing, Stand = true };
        first.Duration = preset.Frames[0].Ms;
        library.WriteImage(flat.Live.Map!, maps[0]);
        for (int k = 1; k < maps.Count; k++)
        {
            var copy = PackWriter.Clone(first);
            copy.Duration = preset.Frames[k].Ms;
            var images = PoseDocumentViewModel.FlatOf(copy)!;
            images.Live.Map = library.UniquePath($"art/poses/{id}/map-{k}.png");
            images.Paint.Image = library.UniquePath($"art/poses/{id}/paint-{k}.png");
            library.WriteImage(images.Live.Map, maps[k]);
            library.WriteImage(images.Paint.Image, StudioImages.Blank(width, height));
            pose.Frames.Add(copy);
        }
        // Loops loop all; "once" plays through and holds the last frame.
        if (preset.Plays == "once" && pose.Frames.Count > 1)
        {
            pose.Intro = Enumerable.Range(0, pose.Frames.Count - 1).ToArray();
            pose.Loop = new[] { pose.Frames.Count - 1 };
        }
        library.SavePose(pose);
        return id;
    }
}
