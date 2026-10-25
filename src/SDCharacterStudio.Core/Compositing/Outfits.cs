using SDCharacterStudio.Core.Loading;
using SDCharacterStudio.Core.Models;

namespace SDCharacterStudio.Core.Compositing;

/// <summary>
/// Outfits: a pack's sheet poses, worn in place of a character's own sprite sheet. A pose wears one with a live
/// source of Kind "Outfit"; an outfit may itself be made from another outfit, never (directly or not) from itself.
/// </summary>
public static class Outfits
{
    public const string SourceKind = "Outfit";

    public static bool IsOutfit(Pose? pose) => pose != null && string.Equals(pose.Kind, "sheet", StringComparison.OrdinalIgnoreCase);

    public static bool IsOutfitSource(LiveSource? source) => string.Equals(source?.Kind, SourceKind, StringComparison.OrdinalIgnoreCase);

    /// <summary>The outfits a pose's layers wear (each once).</summary>
    public static IEnumerable<string> Worn(Pose pose) =>
        (pose.Frames ?? new()).Where(f => f != null).SelectMany(f => f.Layers ?? new()).OfType<LiveLayer>()
            .Where(l => IsOutfitSource(l.Source) && l.Source!.Outfit != null).Select(l => l.Source!.Outfit!).Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The outfit covering a whole sheet of <paramref name="width"/>×<paramref name="height"/> (her sheet as the game has it
    /// now): frames it doesn't have (a sprite mod that adds frames, an outfit made before them) show her own frame there,
    /// from what the outfit is made from, so the game's animations never show a blank. Returns the outfit itself when it
    /// already covers the sheet; otherwise a copy.
    /// </summary>
    public static Pose CoverSheet(Pose outfit, int width, int height)
    {
        if (outfit.FrameSize is not { Length: 2 } size || size[0] <= 0 || size[1] <= 0 || width < size[0] || height < size[1]) return outfit;
        int w = size[0], h = size[1];
        int columns = width / w, cells = columns * (height / h);
        if (cells <= outfit.Frames.Count) return outfit;
        var template = outfit.Frames.SelectMany(f => f?.Layers ?? new()).OfType<LiveLayer>().FirstOrDefault(l => l.Source != null)?.Source;
        if (template == null) return outfit;
        var copy = Serialization.PackJson.Deserialize<Pose>(Serialization.PackJson.Serialize(outfit))!;
        for (int i = copy.Frames.Count; i < cells; i++)
            copy.Frames.Add(new PoseFrame
            {
                Duration = 150,
                Layers = { new LiveLayer { Source = new LiveSource { Kind = template.Kind, Npc = template.Npc, Asset = template.Asset, Outfit = template.Outfit }, Part = "Full", SourceFrame = new SourceFrame { Index = i } } },
            });
        copy.SheetSize = new[] { columns * w, (cells + columns - 1) / columns * h };
        return copy;
    }

    /// <summary>The chain of outfits that leads from <paramref name="outfit"/> back to itself, or null when there's none.</summary>
    public static IReadOnlyList<string>? Cycle(LoadedPack pack, string outfit)
    {
        var path = new List<string>();
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool Visit(string id)
        {
            if (path.Count > 0 && string.Equals(id, outfit, StringComparison.OrdinalIgnoreCase)) { path.Add(id); return true; }
            if (!done.Add(id)) return false;   // other loops are reported from their own outfits
            var pose = pack.Poses.Select(p => p.Value).FirstOrDefault(p => string.Equals(p?.Id, id, StringComparison.OrdinalIgnoreCase));
            if (pose == null) return false;
            path.Add(id);
            foreach (var next in Worn(pose)) if (Visit(next)) return true;
            path.RemoveAt(path.Count - 1);
            return false;
        }
        return Visit(outfit) ? path : null;
    }
}
