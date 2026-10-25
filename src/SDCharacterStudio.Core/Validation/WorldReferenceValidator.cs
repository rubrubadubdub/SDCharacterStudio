using SDCharacterStudio.Core.Loading;
using SDCharacterStudio.Core.Models;

namespace SDCharacterStudio.Core.Validation;

/// <summary>
/// V19: everything a cutscene uses from outside its pack must exist in the game it runs in. Other mods
/// (SVE, Ridgeside Village, East Scarp, sprite and clothing mods) supply many of these NPCs, assets, items
/// and hairstyles, so a failure usually means the providing mod isn't installed. Actor NPCs and the
/// location are V7/V15 (<see cref="PackValidator.ValidateCutscene"/>).
/// </summary>
public static class WorldReferenceValidator
{
    public static IReadOnlyList<ValidationIssue> Validate(LoadedPack pack, Cutscene scene, string file, ICatalog catalog)
    {
        var issues = new List<ValidationIssue>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reference in OutsideReferences.Collect(pack, scene, file, includeActorsAndLocation: false))
        {
            // Report each missing thing once, where it is first used.
            if (!seen.Add(reference.Kind + "\n" + reference.Value)) continue;
            string? problem = reference.Kind switch
            {
                OutsideReferenceKind.Npc => PackValidator.NpcNameProblem(catalog, reference.Value),
                OutsideReferenceKind.Asset => catalog.AssetExists(reference.Value) ? null : Missing("Asset", reference.Value),
                OutsideReferenceKind.Item => catalog.ItemExists(reference.Value) ? null : Missing("Item", reference.Value),
                OutsideReferenceKind.Hairstyle => int.TryParse(reference.Value, out int index) && !catalog.HairstyleExists(index) ? Missing("Hairstyle", reference.Value) : null,
                _ => null
            };
            if (problem != null) issues.Add(new(ValidationSeverity.Error, reference.File, reference.JsonPath, problem, "V19"));
        }
        return issues;
    }

    private static string Missing(string kind, string value) => $"{kind} '{value}' isn't available in this game. If it comes from another mod, that mod isn't installed.";
}
