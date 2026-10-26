using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Studio;

/// <summary>Starting points for new poses (doc 07 §3.2, §6.4, doc 03 §10.4).</summary>
public static class PoseFactory
{
    public static Pose Create(CharacterStudioViewModel studio, PackLibrary library, CharacterChoice who, string kind, string id, string? facing)
    {
        if (kind == "effect")
        {
            string path = library.UniquePath($"art/poses/{id}/frame-0.png");
            library.WriteImage(path, StudioImages.Blank(16, 16));
            return new Pose { Format = "1.0", Id = id, Kind = "effect", FrameSize = new[] { 16, 16 }, Frames = { new PoseFrame { Duration = 100, Layers = { new PaintLayer { Image = path } } } } };
        }
        if (who.IsFarmer)
        {
            string rigId = RigFactory.EnsureFarmerRig(library);
            facing ??= "down";
            int direction = FarmerExport.FacingIndex(facing);
            int stand = direction switch { 0 => 12, 1 or 3 => 6, _ => 0 };
            var neck = FarmerExport.Neck(stand, direction);
            return new Pose
            {
                Format = "1.0", Id = id, Kind = "character", For = rigId, FrameSize = new[] { FarmerExport.Width, FarmerExport.Height }, Facing = facing,
                Frames = { new PoseFrame { Duration = 150, Anchors = { ["Neck"] = new[] { neck.X, neck.Y } },
                    Layers = { new LiveLayer { Source = new LiveSource { Kind = "Farmer" }, Part = "Full", SourceFrame = new SourceFrame { Facing = facing, Stand = true } } } } }
            };
        }
        string npcRig = RigFactory.EnsureNpcRig(library, studio.Shell.Catalog, who.Key, studio.Context);
        var rig = library.Rig(npcRig)!;
        int width = rig.FrameSize[0], height = rig.FrameSize[1];
        if (kind == "sheet")
        {
            var sheet = new Pose { Format = "1.0", Id = id, Kind = "sheet", For = npcRig, FrameSize = new[] { width, height }, SheetSize = new[] { width * 4, height * 4 }, MirrorForOppositeFacing = false };
            for (int i = 0; i < 16; i++)
            {
                string side = i switch { < 4 => "down", < 8 => "right", < 12 => "up", _ => "left" };
                var neck = rig.NeckPoint.TryGetValue(side, out var n) ? n : new[] { width / 2, height / 2 - 1 };
                sheet.Frames.Add(new PoseFrame { Duration = 150, Anchors = { ["Neck"] = (int[])neck.Clone() },
                    Layers = { new LiveLayer { Source = new LiveSource { Kind = "Npc", Npc = who.Key }, Part = "Full", SourceFrame = new SourceFrame { Index = i } } } });
            }
            return sheet;
        }
        facing ??= "left";
        var neckPoint = rig.NeckPoint.TryGetValue(facing, out var point) ? point : new[] { width / 2, height / 2 - 1 };
        return new Pose
        {
            Format = "1.0", Id = id, Kind = "character", For = npcRig, FrameSize = new[] { width, height }, Facing = facing,
            Frames = { new PoseFrame { Duration = 150, Anchors = { ["Neck"] = (int[])neckPoint.Clone() },
                Layers = { new LiveLayer { Source = new LiveSource { Kind = "Npc", Npc = who.Key }, Part = "Full", SourceFrame = new SourceFrame { Facing = facing, Stand = true } } } } }
        };
    }
}

/// <summary>Copies of library items, with their own copies of the images they own.</summary>
public static class LibraryCopies
{
    public static void Duplicate(PackLibrary library, LibraryKind kind, string id, string newId)
    {
        switch (kind)
        {
            case LibraryKind.Pose when library.Pose(id) is { } pose:
                pose.Id = newId;
                // Linked frames share a picture: copy each picture once, so the copy's frames stay linked.
                var copied = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                string? Once(string? path) => path == null ? null : copied.TryGetValue(path, out var done) ? done : copied[path] = CopyImage(library, path, $"art/poses/{newId}/");
                foreach (var layer in pose.Frames.SelectMany(f => f.Layers).OfType<PaintLayer>())
                    layer.Image = Once(layer.Image);
                foreach (var layer in pose.Frames.SelectMany(f => f.Layers).OfType<LiveLayer>().Where(l => l.Map != null))
                    layer.Map = Once(layer.Map);
                library.SavePose(pose);
                break;
            case LibraryKind.Clip when library.Clip(id) is { } clip:
                clip.Id = newId; clip.Title = (clip.Title ?? id) + " (copy)";
                library.SaveClip(clip);
                break;
            case LibraryKind.Mask when library.Mask(id) is { } mask:
                mask.Id = newId; mask.DisplayName = (mask.DisplayName ?? id) + " (copy)";
                mask.Image = CopyImage(library, mask.Image, "masks/", newId);
                mask.PerFrame = mask.PerFrame.ToDictionary(p => p.Key, p => CopyImage(library, p.Value, "masks/", $"{newId}-{p.Key}")!);
                library.SaveMask(mask);
                break;
            case LibraryKind.Rig when library.Rig(id) is { } rig:
                rig.Id = newId;
                library.SaveRig(rig);
                break;
            case LibraryKind.Expression when library.Expressions().Expressions.FirstOrDefault(e => e.Id == id) is { } expression:
                expression.Id = newId;
                foreach (var frame in expression.Frames) frame.Image = CopyImage(library, frame.Image, "art/expressions/", newId);
                library.SaveExpression(expression);
                break;
            case LibraryKind.Portrait when library.Portrait(id) is { } portrait:
                portrait.Id = newId;
                foreach (var layer in portrait.Emotions.Values.SelectMany(e => e.Layers).OfType<PaintLayer>())
                    layer.Image = CopyImage(library, layer.Image, $"art/portraits/{newId}/");
                library.SavePortrait(portrait);
                break;
        }
    }

    /// <summary>Copy a pack image to a new path in <paramref name="folder"/>; returns the new path.</summary>
    public static string? CopyImage(PackLibrary library, string? path, string folder, string? stem = null)
    {
        if (path == null || library.ReadImage(path) is not { } image) return path;
        string name = stem ?? System.IO.Path.GetFileNameWithoutExtension(path);
        string target = library.UniquePath(folder + name + ".png");
        library.WriteImage(target, image);
        return target;
    }
}

/// <summary>A new pose as one flat picture (D-48): its live layer gets an identity pixel map, and an empty paint layer goes on top.</summary>
public static class FlatPose
{
    /// <summary>
    /// The colour slots for paint on a character's picture: each slot the rig samples becomes that character's real
    /// colour (the player's for a farmer rig), so painting with "Robin skin" shows her skin and follows outfits.
    /// </summary>
    public static Dictionary<string, string>? PaintSlots(PackLibrary library, string? rigId)
    {
        if (library.Rig(rigId) is not { } rig) return null;
        if (string.Equals(rig.Kind, "Farmer", StringComparison.OrdinalIgnoreCase))
            return new(StringComparer.OrdinalIgnoreCase) { ["Skin"] = "Farmer.Skin", ["Hair"] = "Farmer.Hair", ["Eyes"] = "Farmer.Eyes", ["Shirt"] = "Farmer.Shirt", ["Pants"] = "Farmer.Pants", ["Shoes"] = "Farmer.Shoes", ["Outline"] = "#000000" };
        var slots = rig.ColorSamples.Keys.Where(k => Core.Palette.ColorSlots.Names.Contains(k, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(k => k, k => $"{rig.Id}.{k}", StringComparer.OrdinalIgnoreCase);
        return slots.Count > 0 ? slots : null;
    }

    public static void MakeFlat(PackLibrary library, Pose pose)
    {
        if (pose.Kind is not ("character" or "sheet") || pose.FrameSize is not [var width, var height]) return;
        for (int index = 0; index < pose.Frames.Count; index++)
        {
            var frame = pose.Frames[index];
            if (frame.Layers is not [LiveLayer { Part: "Full", Map: null } live] || live.Offset is not [0, 0]) continue;
            var map = new Core.Compositing.RgbaImage(width, height, new uint[width * height]);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    map.Pixels[y * width + x] = Core.Compositing.Compositor.MapCell(x, y);
            live.Map = library.UniquePath($"art/poses/{pose.Id}/map-{index}.png");
            library.WriteImage(live.Map, map);
            string paint = library.UniquePath($"art/poses/{pose.Id}/paint-{index}.png");
            library.WriteImage(paint, StudioImages.Blank(width, height));
            frame.Layers.Add(new PaintLayer { Image = paint, ColorSlots = PaintSlots(library, pose.For) });
        }
    }
}
