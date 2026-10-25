using SDCharacterStudio.Core.Validation;

namespace SDCharacterStudio.Runtime.Loading;

public sealed record ReferenceValidationResult(IReadOnlyList<RegisteredCutscene> Valid, IReadOnlyList<SkippedCutscene> Skipped, IReadOnlyList<ValidationIssue> Issues);

public static class PackReferenceValidation
{
    /// <summary>Skip scenes whose NPCs, location, assets, items or hairstyles aren't in the loaded game (V7, V15, V19).</summary>
    public static ReferenceValidationResult Validate(IEnumerable<RegisteredCutscene> scenes, ICatalog catalog)
    {
        var valid = new List<RegisteredCutscene>();
        var skipped = new List<SkippedCutscene>();
        var issues = new List<ValidationIssue>();
        foreach (var scene in scenes)
        {
            string file = scene.Pack.Cutscenes.FirstOrDefault(x => ReferenceEquals(x.Value, scene.Source))?.File ?? "cutscenes/" + scene.Source.Id + ".json";
            var errors = PackValidator.ValidateCutscene(scene.Source, file, catalog)
                .Where(x => x.Severity == ValidationSeverity.Error && x.Rule is "V7" or "V15")
                .Concat(WorldReferenceValidator.Validate(scene.Pack, scene.Source, file, catalog)).ToArray();
            if (errors.Length == 0) valid.Add(scene);
            else
            {
                issues.AddRange(errors);
                skipped.Add(new(scene.Source.Id!, string.Join("; ", errors.Select(x => x.Message))));
            }
        }
        return new(valid, skipped, issues);
    }
}
