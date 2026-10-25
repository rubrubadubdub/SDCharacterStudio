using SDCharacterStudio.Core.Models;

namespace SDCharacterStudio.Editor.Studio;

/// <summary>What a copy for another character needs that the target's rig doesn't have yet.</summary>
public sealed record CopyPlan(string NewId, string TargetRig, List<string> Problems, List<(string From, string To)> MissingMasks);

/// <summary>
/// "Make a copy for &lt;character&gt;" (doc 03 §17.2): live layers switch to the new character, masks and colour samples
/// switch to the target rig's ones with the same names, and paint gets its own copy. Whatever the target lacks is
/// listed instead of making a broken pose.
/// </summary>
public static class PoseCopies
{
    /// <summary>Check a copy without changing the pack (the target's rig is made if needed, inside the edit).</summary>
    public static CopyPlan Plan(PackLibrary library, ExportCatalog? catalog, string poseId, string targetNpc)
    {
        var pose = library.Pose(poseId) ?? throw new ArgumentException($"No pose '{poseId}'.");
        var sourceRig = library.Rig(pose.For);
        string sourceNpc = sourceRig?.Npc ?? pose.Frames.SelectMany(f => f.Layers).OfType<LiveLayer>().Select(l => l.Source?.Npc).FirstOrDefault(n => n != null) ?? "";
        var targetRig = library.RigFor(targetNpc);
        string targetRigId = targetRig?.Id ?? library.UniqueId(LibraryKind.Rig, catalog?.Character(targetNpc)?.Name ?? targetNpc);
        var problems = new List<string>();
        var missing = new List<(string, string)>();
        int[]? targetSize = targetRig?.FrameSize ?? (catalog?.Character(targetNpc) is { } c ? new[] { c.FrameWidth, c.FrameHeight } : null);
        if (targetSize != null && pose.FrameSize is { Length: 2 } size && (size[0] != targetSize[0] || size[1] != targetSize[1]) && pose.Kind == "sheet")
            problems.Add($"{targetNpc}'s sprite frames are {targetSize[0]}×{targetSize[1]}; this sheet is {size[0]}×{size[1]}.");
        foreach (var maskId in pose.Frames.SelectMany(f => f.Layers).Select(l => l.Mask).Where(m => m != null).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string wanted = SwapName(maskId!, sourceRig?.Id, sourceNpc, targetRigId, targetNpc);
            bool madeWithRig = targetRig == null && wanted.StartsWith(targetRigId + "-head-", StringComparison.OrdinalIgnoreCase);   // a new rig brings its head masks
            if (library.Mask(wanted) == null && !madeWithRig) missing.Add((maskId!, wanted));
        }
        foreach (var source in pose.Frames.SelectMany(f => f.Layers).SelectMany(l => l.ColorSlots?.Values ?? Enumerable.Empty<string>()).Distinct())
            if (source.Split('.') is { Length: 2 } parts && sourceRig != null && string.Equals(parts[0], sourceRig.Id, StringComparison.OrdinalIgnoreCase)
                && targetRig != null && !targetRig.ColorSamples.ContainsKey(parts[1]))
                problems.Add($"{targetNpc}'s rig has no '{parts[1]}' colour sample (used for a colour slot). Add it in the rig editor.");
        foreach (var layer in pose.Frames.SelectMany(f => f.Layers).OfType<LiveLayer>())
            if (layer.Source?.Kind is "NpcSheet" or "PortraitSheet" && layer.Source.Asset?.Contains(sourceNpc, StringComparison.OrdinalIgnoreCase) == true)
                problems.Add($"A layer uses {sourceNpc}'s sheet '{layer.Source.Asset}'. Pick {targetNpc}'s sheet in the copy.");
        foreach (var outfit in Core.Compositing.Outfits.Worn(pose))
            problems.Add($"This pose wears {sourceNpc}'s outfit '{outfit}'. The copy wears {targetNpc}'s own look; pick one of {targetNpc}'s outfits for it under Outfit.");
        string newId = library.UniqueId(LibraryKind.Pose, SwapName(pose.Id!, sourceRig?.Id, sourceNpc, targetRigId, targetNpc) is { } swapped && swapped != pose.Id ? swapped : targetNpc + " " + pose.Id);
        return new CopyPlan(newId, targetRigId, problems, missing);
    }

    /// <summary>Make the copy (inside an edit). With <paramref name="copyMissingMasks"/>, masks the target lacks start as copies of the source's.</summary>
    public static string Make(PackLibrary library, ExportCatalog? catalog, string poseId, string targetNpc, bool copyMissingMasks)
    {
        var plan = Plan(library, catalog, poseId, targetNpc);
        var pose = library.Pose(poseId)!;
        var sourceRig = library.Rig(pose.For);
        string sourceNpc = sourceRig?.Npc ?? "";
        string targetRigId = RigFactory.EnsureNpcRig(library, catalog, targetNpc);
        var targetRig = library.Rig(targetRigId)!;
        if (copyMissingMasks)
            foreach (var (from, to) in plan.MissingMasks)
                if (library.Mask(to) == null && library.Mask(from) is { } mask)   // the new rig may have just made it
                {
                    mask.Id = to;
                    mask.DisplayName = (mask.DisplayName ?? from).Replace(sourceNpc, targetNpc, StringComparison.OrdinalIgnoreCase);
                    mask.Tags = mask.Tags.Select(t => string.Equals(t, sourceRig?.Id, StringComparison.OrdinalIgnoreCase) ? targetRigId : t).ToList();
                    mask.Image = LibraryCopies.CopyImage(library, mask.Image, "masks/", to);
                    mask.PerFrame = mask.PerFrame.ToDictionary(p => p.Key, p => LibraryCopies.CopyImage(library, p.Value, "masks/", $"{to}-{p.Key}")!);
                    library.SaveMask(mask);
                }
        pose.Id = plan.NewId;
        pose.For = targetRigId;
        foreach (var layer in pose.Frames.SelectMany(f => f.Layers))
        {
            if (layer.Mask != null)
            {
                string swapped = SwapName(layer.Mask, sourceRig?.Id, sourceNpc, targetRigId, targetNpc);
                if (library.Mask(swapped) != null) layer.Mask = swapped;
            }
            if (layer.ColorSlots != null)
                layer.ColorSlots = layer.ColorSlots.ToDictionary(p => p.Key, p => p.Value.Split('.') is { Length: 2 } parts && string.Equals(parts[0], sourceRig?.Id, StringComparison.OrdinalIgnoreCase)
                    && targetRig.ColorSamples.ContainsKey(parts[1]) ? $"{targetRigId}.{parts[1]}" : p.Value, StringComparer.OrdinalIgnoreCase);
            switch (layer)
            {
                case LiveLayer live when Core.Compositing.Outfits.IsOutfitSource(live.Source):
                    // Their outfit is theirs: the copy wears the other character's own look.
                    live.Source = new LiveSource { Kind = "Npc", Npc = targetNpc };
                    if (live.Map != null) live.Map = LibraryCopies.CopyImage(library, live.Map, $"art/poses/{plan.NewId}/");
                    break;
                case LiveLayer live when live.Source?.Kind is "Npc" or "Portrait" && string.Equals(live.Source.Npc, sourceNpc, StringComparison.OrdinalIgnoreCase):
                    live.Source.Npc = targetNpc;
                    // A flat picture's map points at sprite pixels; the other character's sprite has the same layout.
                    if (live.Map != null) live.Map = LibraryCopies.CopyImage(library, live.Map, $"art/poses/{plan.NewId}/");
                    break;
                case PaintLayer paint:
                    paint.Image = LibraryCopies.CopyImage(library, paint.Image, $"art/poses/{plan.NewId}/");
                    break;
            }
        }
        // Rig anchors (the live head's neck) come from the target's rig at draw time; the frame's own anchors stay where the body is drawn.
        library.SavePose(pose);
        return plan.NewId;
    }

    /// <summary>"robin-head-left" → "abigail-head-left": the source rig or NPC name swapped for the target's.</summary>
    public static string SwapName(string name, string? sourceRig, string sourceNpc, string targetRig, string targetNpc)
    {
        foreach (var (from, to) in new[] { (sourceRig, targetRig), (sourceNpc, targetNpc) })
        {
            if (string.IsNullOrEmpty(from)) continue;
            string fromId = PackDocument.ToContentId(from), toId = PackDocument.ToContentId(to);
            if (name.StartsWith(fromId + "-", StringComparison.OrdinalIgnoreCase)) return toId + name[fromId.Length..];
            if (name.StartsWith(from + "-", StringComparison.OrdinalIgnoreCase)) return to + name[from.Length..];
        }
        return name;
    }
}
