using SDCharacterStudio.Core.Loading;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Validation;
using SDCharacterStudio.Editor.Catalog;
using SDCharacterStudio.Editor.Project;

namespace SDCharacterStudio.Editor.Export;

/// <summary>One use of outside content, with the cutscene that uses it.</summary>
public sealed record DependencyUse(OutsideReference Reference, string Cutscene)
{
    public string Describe() => $"{KindName(Reference.Kind)} {Reference.Value} ({Reference.Usage} in cutscene {Cutscene})";
    public static string KindName(OutsideReferenceKind kind) => kind switch
    {
        OutsideReferenceKind.Npc => "NPC", OutsideReferenceKind.Location => "location", OutsideReferenceKind.Asset => "asset",
        OutsideReferenceKind.Item => "item", _ => "hairstyle"
    };
}

/// <summary>A mod the pack relates to, with every use that ties it in.</summary>
public sealed class ModDependency
{
    public string ModId { get; init; } = "";
    public string ModName { get; init; } = "";
    public List<DependencyUse> Uses { get; } = new();
    public bool Uncertain { get; set; }
    /// <summary>Cutscenes that use this mod's content.</summary>
    public IEnumerable<string> Cutscenes => Uses.Select(u => u.Cutscene).Distinct(StringComparer.OrdinalIgnoreCase);
    public string Reason
    {
        get
        {
            var distinct = Uses.Select(u => u.Describe()).Distinct().ToArray();
            return distinct.Length <= 3 ? string.Join("; ", distinct) : string.Join("; ", distinct.Take(3)) + $"; and {distinct.Length - 3} more";
        }
    }
}

public sealed class DependencyReport
{
    /// <summary>Mods that provide content the pack uses (D-7: a dependency).</summary>
    public List<ModDependency> Providers { get; } = new();
    /// <summary>Mods that only edit vanilla content the pack uses ("looks different without", not a dependency).</summary>
    public List<ModDependency> Editors { get; } = new();
    /// <summary>Not vanilla, provider unknown: the designer picks the mod or "not a dependency" (doc 12 §2.3).</summary>
    public List<DependencyUse> Unknown { get; } = new();
    /// <summary>Not in the game export at all (typo, mod removed, or an export that predates it).</summary>
    public List<DependencyUse> Missing { get; } = new();

    public IReadOnlyList<ModRequirement> Requires { get; set; } = Array.Empty<ModRequirement>();
    public IReadOnlyList<ManifestDependency> ManifestDependencies { get; set; } = Array.Empty<ManifestDependency>();
    public IEnumerable<DependencyUse> UnresolvedUnknown(ProjectFile project) => Unknown.Where(u => !project.UnknownProviders.ContainsKey(Key(u.Reference)));
    public static string Key(OutsideReference reference) => DependencyUse.KindName(reference.Kind) + ":" + reference.Value;
}

/// <summary>
/// Finds every outside reference in the pack, classifies it with the export's origins (doc 06 §6) and turns the
/// result into studio.json Requires (with reasons) and manifest.json Dependencies (doc 12 §3). Recomputed on every export.
/// </summary>
public static class DependencyAnalyzer
{
    public static DependencyReport Analyze(PackDocument document, ExportCatalog? catalog) =>
        Analyze(document.Library, document.Cutscenes, document.Project, catalog);

    public static DependencyReport Analyze(LoadedPack library, IEnumerable<CutsceneDocument> cutscenes, ProjectFile project, ExportCatalog? catalog)
    {
        var report = new DependencyReport();
        var providers = new Dictionary<string, ModDependency>(StringComparer.OrdinalIgnoreCase);
        var editors = new Dictionary<string, ModDependency>(StringComparer.OrdinalIgnoreCase);
        ModDependency For(Dictionary<string, ModDependency> map, string mod) =>
            map.TryGetValue(mod, out var d) ? d : map[mod] = new ModDependency { ModId = mod, ModName = catalog?.ModLabel(mod) ?? mod };

        foreach (var cutscene in cutscenes)
        {
            foreach (var reference in OutsideReferences.Collect(library, cutscene.Scene, cutscene.File))
            {
                var use = new DependencyUse(reference, cutscene.Scene.Id ?? cutscene.File);
                if (catalog == null) continue;
                var origin = Origin(catalog, reference);
                if (origin == null) { if (!IsSdcsGenerated(reference)) report.Missing.Add(use); continue; }
                if (origin.IsModProvided)
                    foreach (var mod in origin.ProvidedBy)
                    {
                        var dependency = For(providers, mod);
                        dependency.Uses.Add(use);
                        dependency.Uncertain |= !origin.Certain;
                    }
                else if (origin.IsUnknown)
                {
                    report.Unknown.Add(use);
                    if (project.UnknownProviders.TryGetValue(DependencyReport.Key(reference), out var assigned) && assigned != "none")
                        For(providers, assigned).Uses.Add(use);
                }
                foreach (var mod in origin.Vanilla ? origin.EditedBy : Enumerable.Empty<string>())
                    For(editors, mod).Uses.Add(use);
            }
        }
        // Outfits pinned in library poses no scene uses yet (D-49): the pack still ships them, so their mods count.
        if (catalog != null)
        {
            var reached = providers.Values.SelectMany(p => p.Uses).Select(u => u.Reference.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var shelf = new Cutscene { Id = "library", Beats = library.Poses.Where(p => p.Value?.Id != null).Select(p => (Beat)new PoseBeat { Pose = p.Value!.Id }).ToList() };
            foreach (var reference in OutsideReferences.Collect(library, shelf, "library", includeActorsAndLocation: false))
            {
                if (reference.Kind != OutsideReferenceKind.Asset || reached.Contains(reference.Value) || catalog.AssetOrigin(reference.Value) is not { IsModProvided: true } origin) continue;
                var use = new DependencyUse(reference, "the pose library");
                foreach (var mod in origin.ProvidedBy) { var dependency = For(providers, mod); dependency.Uses.Add(use); dependency.Uncertain |= !origin.Certain; }
            }
        }
        report.Providers.AddRange(providers.Values.OrderBy(d => d.ModName, StringComparer.OrdinalIgnoreCase));
        report.Editors.AddRange(editors.Values.Where(e => !providers.ContainsKey(e.ModId)).OrderBy(d => d.ModName, StringComparer.OrdinalIgnoreCase));
        Apply(report, project);
        return report;
    }

    /// <summary>Turn the report plus the designer's choices into the two lists written on export.</summary>
    public static void Apply(DependencyReport report, ProjectFile project)
    {
        var requires = new List<ModRequirement>();
        var manifest = new List<ManifestDependency>();
        foreach (var provider in report.Providers)
        {
            bool required = project.ModeFor(provider.ModId) == DependencyMode.Required;
            manifest.Add(new ManifestDependency { UniqueID = provider.ModId, IsRequired = required });
            // Only required dependencies go in Requires (doc 12 §3); optional ones are manifest-only.
            if (required) requires.Add(new ModRequirement { Mod = provider.ModId, Reason = provider.Reason });
        }
        foreach (var editor in report.Editors.Where(e => project.OptionalEditedBy.Contains(e.ModId, StringComparer.OrdinalIgnoreCase)))
            manifest.Add(new ManifestDependency { UniqueID = editor.ModId, IsRequired = false });
        foreach (var manual in project.ManualDependencies.Where(m => !manifest.Any(d => string.Equals(d.UniqueID, m.Mod, StringComparison.OrdinalIgnoreCase))))
        {
            manifest.Add(new ManifestDependency { UniqueID = manual.Mod, IsRequired = manual.IsRequired });
            if (manual.IsRequired) requires.Add(new ModRequirement { Mod = manual.Mod, Reason = manual.Reason ?? "added by hand" });
        }
        report.Requires = requires;
        report.ManifestDependencies = manifest;
    }

    /// <summary>Write the analysis into the document's studio.json and manifest.json (part of Export, one undo step).</summary>
    public static void WriteInto(PackDocument document, DependencyReport report)
    {
        document.Studio.Requires = report.Requires.Select(r => new ModRequirement { Mod = r.Mod, Reason = r.Reason }).ToList();
        document.Manifest.Dependencies = report.ManifestDependencies.Select(d => new ManifestDependency { UniqueID = d.UniqueID, IsRequired = d.IsRequired, MinimumVersion = d.MinimumVersion }).ToList();
    }

    private static OriginInfo? Origin(ExportCatalog catalog, OutsideReference reference) => reference.Kind switch
    {
        OutsideReferenceKind.Npc => catalog.Character(reference.Value)?.Origin,
        OutsideReferenceKind.Location => catalog.HasWorld ? catalog.Location(reference.Value)?.Origin : OriginInfo.VanillaOrigin,
        OutsideReferenceKind.Asset => catalog.AssetOrigin(reference.Value),
        OutsideReferenceKind.Item => catalog.Item(reference.Value)?.Origin,
        OutsideReferenceKind.Hairstyle => int.TryParse(reference.Value, out int index) ? catalog.Hairstyles.FirstOrDefault(h => h.Index == index)?.Origin : null,
        _ => null
    };

    // Sheets made by SDCS itself (Characters/<Npc>_SDCS_<hash>_<pose>) never come from another mod.
    private static bool IsSdcsGenerated(OutsideReference reference) => reference.Kind == OutsideReferenceKind.Asset && reference.Value.Contains("_SDCS_", StringComparison.Ordinal);
}
