using System.Globalization;
using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Studio;

/// <summary>
/// A new outfit (a sheet pose): every frame of her sprite sheet in its place (walking, and her special frames), each
/// one flat picture of her live frame with an empty paint layer on top, so the clothes are painted straight onto her.
/// It starts from what the studio shows: her own look (which follows seasons and sprite mods), a game outfit (the
/// Flower Dance...), or another of your outfits.
/// </summary>
public static class OutfitFactory
{
    /// <param name="basedOn">The studio's outfit choice: null for her own look, a sprite asset, or an <see cref="StudioContext.OutfitKey"/>.</param>
    public static Pose Create(StudioContext context, PackLibrary library, ExportCatalog? catalog, string npc, string id, string? basedOn)
    {
        string rigId = RigFactory.EnsureNpcRig(library, catalog, npc);
        var rig = library.Rig(rigId)!;
        int width = rig.FrameSize[0], height = rig.FrameSize[1];
        string? outfit = StudioContext.OutfitId(basedOn);
        var source = basedOn == null ? new LiveSource { Kind = "Npc", Npc = npc }
            : outfit != null ? new LiveSource { Kind = Outfits.SourceKind, Npc = npc, Outfit = outfit }
            : new LiveSource { Kind = "NpcSheet", Npc = npc, Asset = basedOn };
        // As many frames as the sheet she starts from (at least the 16 walking frames), in the same places.
        var sheet = basedOn == null ? context.NpcTexture(npc, false, ownOutfits: false) : context.AssetTexture(basedOn);
        int columns = Math.Max(4, (sheet?.Width ?? 0) / width), rows = Math.Max(4, (sheet?.Height ?? 0) / height);
        var pose = new Pose
        {
            Format = "1.0", Id = id, Kind = "sheet", For = rigId, FrameSize = new[] { width, height },
            SheetSize = new[] { columns * width, rows * height }, MirrorForOppositeFacing = false,
        };
        var map = new RgbaImage(width, height, new uint[width * height]);
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) map.Pixels[y * width + x] = Compositor.MapCell(x, y);
        var slots = FlatPose.PaintSlots(library, rigId);
        for (int i = 0; i < columns * rows; i++)
        {
            string facing = FacingOf(rig, i);
            var neck = rig.NeckPoint.TryGetValue(facing, out var n) ? (int[])n.Clone() : new[] { width / 2, height / 2 - 1 };
            string mapPath = library.UniquePath($"art/poses/{id}/map-{i}.png"), paintPath = library.UniquePath($"art/poses/{id}/paint-{i}.png");
            library.WriteImage(mapPath, StudioImages.Clone(map));
            library.WriteImage(paintPath, StudioImages.Blank(width, height));
            pose.Frames.Add(new PoseFrame
            {
                Duration = 150, Anchors = { ["Neck"] = neck },
                Layers =
                {
                    new LiveLayer { Source = new LiveSource { Kind = source.Kind, Npc = source.Npc, Asset = source.Asset, Outfit = source.Outfit }, Part = "Full", SourceFrame = new SourceFrame { Index = i }, Map = mapPath },
                    new PaintLayer { Image = paintPath, ColorSlots = slots == null ? null : new(slots, StringComparer.OrdinalIgnoreCase) },
                }
            });
        }
        return pose;
    }

    /// <summary>The way a sheet frame faces: the walking rows (down, right, up, left), else what her rig says, else down.</summary>
    public static string FacingOf(CharacterRig? rig, int index)
    {
        if (index < 16) return PoseDocumentViewModel.SheetFacing(index);
        var match = rig?.Frames.FirstOrDefault(f => f.Value.Stand == index || f.Value.Walk.Contains(index));
        return match?.Key ?? "down";
    }

    /// <summary>What an outfit is called in lists: "abigail-raincoat" for Abigail is "Raincoat".</summary>
    public static string Label(string outfitId, string? npc)
    {
        string name = outfitId;
        if (npc != null && name.StartsWith(npc + "-", StringComparison.OrdinalIgnoreCase)) name = name[(npc.Length + 1)..];
        name = name.Replace('-', ' ').Replace('_', ' ').Trim();
        return name.Length == 0 ? outfitId : CultureInfo.InvariantCulture.TextInfo.ToUpper(name[0]) + name[1..];
    }
}
