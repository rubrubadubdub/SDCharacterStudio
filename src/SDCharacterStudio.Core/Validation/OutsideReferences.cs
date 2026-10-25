using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core.Loading;
using SDCharacterStudio.Core.Models;

namespace SDCharacterStudio.Core.Validation;

public enum OutsideReferenceKind { Npc, Location, Asset, Item, Hairstyle }

/// <summary>Something a cutscene uses from outside its pack: an NPC, location, texture asset, item or hairstyle.</summary>
/// <param name="Usage">What it's used for, in words (e.g. "actor", "live source in pose wave"), for dependency reasons.</param>
public sealed record OutsideReference(OutsideReferenceKind Kind, string Value, string File, string JsonPath, string Usage);

/// <summary>
/// Walks a cutscene and the library items it reaches (poses, portraits, expressions, costume presets, colour-sample
/// rigs) for every outside reference. World validation (V19) and the editor's dependency analysis (doc 12) both use it,
/// so they always agree on what a pack depends on.
/// </summary>
public static class OutsideReferences
{
    private static readonly string[] ItemParts = { "Hat", "Shirt", "Pants", "Shoes" };

    /// <param name="includeActorsAndLocation">Also report actor NPCs and the location (V7/V15 check those separately).</param>
    public static IReadOnlyList<OutsideReference> Collect(LoadedPack pack, Cutscene scene, string file, bool includeActorsAndLocation = true)
    {
        var found = new List<OutsideReference>();
        var visitedPoses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visitedPortraits = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Temporary actors exist only inside the event, so they're never in the game's NPC data.
        var temporary = new HashSet<string>((scene.Actors ?? new()).Where(x => Same(x?.Kind, "Temporary") && x!.Id != null).Select(x => x!.Id!), StringComparer.OrdinalIgnoreCase);

        void Add(OutsideReferenceKind kind, string? value, string where, string path, string usage)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            if (kind == OutsideReferenceKind.Npc && temporary.Contains(value)) return;
            found.Add(new(kind, value, where, path, usage));
        }
        void Costume(string where, string path, IReadOnlyDictionary<string, JToken>? values, string usage)
        {
            foreach (var (key, token) in values ?? new Dictionary<string, JToken>())
            {
                string part = ItemParts.FirstOrDefault(x => x.Equals(key, StringComparison.OrdinalIgnoreCase)) ?? key;
                if (Array.IndexOf(ItemParts, part) >= 0 && token?.Type == JTokenType.String && token.Value<string>() is { Length: > 0 } id && id is not ("none" or "original"))
                {
                    string prefix = part switch { "Hat" => "(H)", "Shirt" => "(S)", "Pants" => "(P)", _ => "(B)" };
                    Add(OutsideReferenceKind.Item, id.StartsWith('(') ? id : prefix + id, where, path + "." + key, usage + " " + part.ToLowerInvariant());
                }
                else if (part.Equals("Hair", StringComparison.OrdinalIgnoreCase) && token?.Type == JTokenType.Integer)
                    Add(OutsideReferenceKind.Hairstyle, token.Value<int>().ToString(System.Globalization.CultureInfo.InvariantCulture), where, path + "." + key, usage + " hairstyle");
            }
        }
        void Preset(string? id)
        {
            if (id == null || pack.Costumes is not { } library) return;
            var costumes = library.Value.Costumes ?? new();
            for (int i = 0; i < costumes.Count; i++)
                if (string.Equals(costumes[i]?.Id, id, StringComparison.OrdinalIgnoreCase))
                    Costume(library.File, $"Costumes[{i}].Set", costumes[i].Set, "costume preset " + id);
        }
        CharacterRig? Rig(string? id) => id == null ? null : pack.Characters.Select(x => x.Value).FirstOrDefault(x => x != null && Same(x.Id, id));
        void SampledNpc(string where, string path, string? colour, string usage)
        {
            int dot = colour?.LastIndexOf('.') ?? -1;
            if (dot <= 0 || colour!.StartsWith('#')) return;
            string owner = colour[..dot];
            if (Same(owner, "Farmer")) return;
            var rig = pack.Characters.Select(x => x.Value).FirstOrDefault(x => x != null && (Same(x.Id, owner) || Same(x.Npc, owner)));
            if (rig != null && Same(rig.Kind, "Npc")) Add(OutsideReferenceKind.Npc, rig.Npc, where, path, usage);
        }
        void Layers(string where, string path, IReadOnlyList<Layer?>? layers, string? rigId, string usage)
        {
            for (int i = 0; i < (layers?.Count ?? 0); i++)
            {
                var layer = layers![i];
                if (layer == null) continue;
                string layerPath = $"{path}[{i}]";
                if (layer is LiveLayer { Source: { } source })
                {
                    if (Same(source.Kind, "Npc") || Same(source.Kind, "Portrait"))
                        Add(OutsideReferenceKind.Npc, source.Npc ?? Rig(rigId)?.Npc, where, layerPath + ".Source.Npc", "live source in " + usage);
                    else if (Same(source.Kind, "NpcSheet") || Same(source.Kind, "PortraitSheet") || Same(source.Kind, "Asset"))
                        Add(OutsideReferenceKind.Asset, source.Asset, where, layerPath + ".Source.Asset", "live source in " + usage);
                }
                if (layer is EffectLayer effect) Pose(effect.Effect, custom: true);
                foreach (var (slot, colour) in layer.ColorSlots ?? new())
                    SampledNpc(where, $"{layerPath}.ColorSlots.{slot}", colour, "colours in " + usage);
            }
        }
        void Pose(string? id, bool custom = false)
        {
            if (id == null || !visitedPoses.Add(id)) return;
            var entry = pack.Poses.FirstOrDefault(x => Same(x.Value?.Id, id));
            // Built-in effect names aren't pack poses; unknown pose IDs are reported by the pack validator.
            if (entry == null || (custom && !Same(entry.Value.Kind, "effect"))) return;
            for (int f = 0; f < (entry.Value.Frames?.Count ?? 0); f++)
                if (entry.Value.Frames![f] is { } frame) Layers(entry.File, $"Frames[{f}].Layers", frame.Layers, entry.Value.For, "pose " + id);
        }
        void Portrait(string? id)
        {
            if (id == null || !visitedPortraits.Add(id)) return;
            var entry = pack.Portraits.FirstOrDefault(x => Same(x.Value?.Id, id));
            foreach (var (emotion, frame) in entry?.Value.Emotions ?? new())
                if (frame != null) Layers(entry!.File, $"Emotions.{emotion}.Layers", frame.Layers, entry.Value.For, "portrait " + id);
        }
        void Expression(string? id)
        {
            var library = pack.Expressions;
            var expressions = library?.Value.Expressions ?? new();
            for (int i = 0; i < expressions.Count; i++)
                if (expressions[i] is { } expression && Same(expression.Id, id))
                    foreach (var (slot, colour) in expression.ColorSlots ?? new())
                        SampledNpc(library!.File, $"Expressions[{i}].ColorSlots.{slot}", colour, "expression " + id);
        }
        void Beats(string path, IReadOnlyList<Beat?>? beats)
        {
            for (int i = 0; i < (beats?.Count ?? 0); i++)
            {
                string beatPath = $"{path}[{i}]";
                switch (beats![i])
                {
                    case ItemBeat item:
                        Add(OutsideReferenceKind.Item, item.Item, file, beatPath + ".Item", "item reward");
                        for (int j = 0; j < (item.OneOf?.Count ?? 0); j++) Add(OutsideReferenceKind.Item, item.OneOf![j]?.Item, file, $"{beatPath}.OneOf[{j}].Item", "item reward");
                        break;
                    case FriendshipBeat friendship: Add(OutsideReferenceKind.Npc, friendship.Npc, file, beatPath + ".Npc", "friendship change"); break;
                    case PoseBeat pose: Pose(pose.Pose); break;
                    case EffectBeat effect: Pose(effect.Effect, custom: true); break;
                    case SayBeat say: Portrait(say.Portrait); Expression(say.Expression); break;
                    case CostumeBeat costume:
                        Costume(file, beatPath + ".Set", costume.Set, "costume");
                        Preset(costume.Preset);
                        Add(OutsideReferenceKind.Asset, costume.Sprite?.Asset, file, beatPath + ".Sprite.Asset", "sprite change");
                        Pose(costume.Sprite?.Pose);
                        Portrait(costume.Portrait);
                        break;
                }
            }
        }

        if (includeActorsAndLocation) Add(OutsideReferenceKind.Location, scene.Location, file, "Location", "location");
        for (int i = 0; i < (scene.Actors?.Count ?? 0); i++)
        {
            var actor = scene.Actors![i];
            if (actor == null) continue;
            if (includeActorsAndLocation && Same(actor.Kind, "Npc")) Add(OutsideReferenceKind.Npc, actor.Npc ?? actor.Id, file, $"Actors[{i}].Id", "actor");
            Add(OutsideReferenceKind.Asset, actor.Sprite?.Asset, file, $"Actors[{i}].Sprite.Asset", Same(actor.Kind, "Temporary") ? "temporary actor sprite" : "starting sprite");
            Pose(actor.Sprite?.Pose);
            if (actor.Costume?.Type == JTokenType.String) Preset(actor.Costume.Value<string>());
            else if (actor.Costume is JObject inline)
                Costume(file, $"Actors[{i}].Costume", (inline["Set"] as JObject ?? inline).Properties().ToDictionary(x => x.Name, x => x.Value, StringComparer.OrdinalIgnoreCase), "starting costume");
        }
        Beats("Beats", scene.Beats);
        foreach (var (name, branch) in scene.Branches ?? new()) Beats("Branches." + name, branch);
        Beats("OnSkip", scene.OnSkip);
        foreach (var (name, clip) in scene.Clips ?? new())
            for (int t = 0; t < (clip?.Tracks?.Count ?? 0); t++)
            {
                if (clip!.Tracks[t] is not { } track) continue;
                foreach (var key in track.Pose ?? new()) Pose(key?.Pose);
                foreach (var key in track.Effect ?? new()) Pose(key?.Effect, custom: true);
                foreach (var key in track.Expression ?? new()) Expression(key?.Expression);
                for (int k = 0; k < (track.Parts?.Count ?? 0); k++)
                    Costume(file, $"Clips.{name}.Tracks[{t}].Parts[{k}].Set", track.Parts![k]?.Set, "clip costume");
            }
        return found;
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
