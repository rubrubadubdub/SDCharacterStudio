using System.IO;
using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core.Compiler;
using SDCharacterStudio.Core.Loading;
using SDCharacterStudio.Core.Migration;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Serialization;
using SDCharacterStudio.Core.Validation;
using SDCharacterStudio.Editor.Catalog;
using SDCharacterStudio.Editor.Project;

namespace SDCharacterStudio.Editor.Export;

public enum TargetKind { Scene, Beat, Actor, Library, Pack }

/// <summary>Where a problem points in the editor, for "jump to" (doc 07 §12).</summary>
public sealed record ProblemTarget(TargetKind Kind, string? Cutscene = null, string? Lane = null, int Index = -1, string Field = "");

public sealed class Problem
{
    public ValidationSeverity Severity { get; init; }
    public string File { get; init; } = "";
    public string JsonPath { get; init; } = "";
    public string Message { get; init; } = "";
    public string? Rule { get; init; }
    public ProblemTarget Target { get; init; } = new(TargetKind.Pack);
    /// <summary>A one-click fix offered in the Problems panel, if any.</summary>
    public string? FixLabel { get; init; }
    /// <summary>For NPC-name problems (V7/V19): the NPC the author probably meant.</summary>
    public string? Suggestion { get; init; }
    public bool IsError => Severity == ValidationSeverity.Error;
    public string Location => JsonPath.Length == 0 ? File : $"{File} › {JsonPath}";
}

/// <summary>An immutable copy of what to validate, taken on the UI thread and checked on a background thread.</summary>
public sealed record ValidationInput(string PackFolder, string PackId, Dictionary<string, string> Files, IReadOnlyList<string> Removed,
    LoadedPack Library, ProjectFile Project, ExportCatalog? Catalog, IReadOnlyDictionary<string, string>? BlobPaths = null, IReadOnlyDictionary<string, string>? TextAssets = null, string? BlobRoot = null)
{
    public static ValidationInput From(PackDocument document, ExportCatalog? catalog)
    {
        var blobs = document.CaptureBlobPaths();
        var text = document.RenderFiles().Where(p => !blobs.ContainsKey(p.Key)).ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        return new(document.PackFolder, document.PackId, document.RenderFiles(), document.RemovedFiles, document.Library,
            Newtonsoft.Json.JsonConvert.DeserializeObject<ProjectFile>(document.Project.ToJson())!, catalog, blobs, text, document.ProjectPath + ".media");
    }
}

public sealed record ValidationResult(IReadOnlyList<Problem> Problems, DependencyReport? Dependencies)
{
    public int Errors => Problems.Count(p => p.IsError);
    public int Warnings => Problems.Count(p => !p.IsError);
}

/// <summary>
/// Validates the pack exactly as the runtime will read it if saved now (doc 03 §16, doc 07 §12): Core's reader and
/// validator over an in-memory overlay, the event compiler, and world checks against the game export (V7/V15/V19).
/// Dependencies are computed the way export writes them, so V14 only reports what export can't resolve.
/// </summary>
public static class ValidationService
{
    public static ValidationResult Validate(ValidationInput input)
    {
        var problems = new List<Problem>();
        var files = new Dictionary<string, string>(input.Files, StringComparer.OrdinalIgnoreCase);
        DependencyReport? dependencies = null;
        try
        {
            // First pass: the current models, to compute dependencies as export would.
            var first = Read(input, files, null);
            dependencies = DependencyAnalyzer.Analyze(first.Pack, first.Pack.Cutscenes.Select(c => new CutsceneDocument(c.File, c.Value)), input.Project, input.Catalog);
            PatchDependencies(files, dependencies);
            ICatalog? catalog = input.Catalog == null ? null : new ProjectCatalog(input.Catalog, input.Project);
            var read = Read(input, files, catalog);
            var scenes = read.Pack.Cutscenes;
            var issues = read.Issues.ToList();
            Func<string, string?> translate = Translations(input, files);
            foreach (var scene in scenes)
            {
                var compiled = new EventCompiler().Compile(input.PackId, scene.Value, read.Pack, translate);
                issues.AddRange(compiled.Issues.Select(i => i with { File = scene.File }));
                if (catalog != null) issues.AddRange(WorldReferenceValidator.Validate(read.Pack, scene.Value, scene.File, catalog));
            }
            foreach (var issue in issues.GroupBy(i => (i.File, i.JsonPath, i.Message)).Select(g => g.First()))
                problems.Add(ToProblem(issue, scenes));
            foreach (var unknown in dependencies.UnresolvedUnknown(input.Project).GroupBy(u => DependencyReport.Key(u.Reference)).Select(g => g.First()))
                problems.Add(new Problem
                {
                    Severity = ValidationSeverity.Warning, File = unknown.Reference.File, JsonPath = unknown.Reference.JsonPath, Rule = "V14",
                    Message = $"The game export can't tell which mod provides {DependencyUse.KindName(unknown.Reference.Kind)} '{unknown.Reference.Value}'. Choose it in Pack › Dependencies, or mark it as not a dependency.",
                    Target = Locate(unknown.Reference.File, unknown.Reference.JsonPath, scenes), FixLabel = "Dependencies…"
                });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            problems.Add(new Problem { Severity = ValidationSeverity.Error, File = "studio.json", Message = "Couldn't check the pack: " + ex.Message });
        }
        var ordered = problems.OrderByDescending(p => p.IsError).ThenBy(p => p.File, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.JsonPath, StringComparer.Ordinal).ToArray();
        return new ValidationResult(ordered, dependencies);
    }

    /// <summary>
    /// The export catalog as Core's validator should see it for this project: assets whose provider the export couldn't tell
    /// count as coming from the mod the designer assigned (or as no dependency for "none"); unassigned ones are left to the
    /// editor's own V14 warning instead of failing export.
    /// </summary>
    internal sealed class ProjectCatalog : ICatalog
    {
        private readonly ExportCatalog inner;
        private readonly ProjectFile project;
        public ProjectCatalog(ExportCatalog inner, ProjectFile project) { this.inner = inner; this.project = project; }
        public bool NpcExists(string name) => inner.NpcExists(name);
        public bool LocationExists(string name) => inner.LocationExists(name);
        public AssetOrigin? FindAsset(string asset)
        {
            var origin = inner.AssetOrigin(asset);
            if (origin == null || !origin.IsUnknown) return inner.FindAsset(asset);
            string key = DependencyUse.KindName(OutsideReferenceKind.Asset) + ":" + asset;
            return project.UnknownProviders.TryGetValue(key, out var mod) ? mod == "none" ? new AssetOrigin(true, null) : new AssetOrigin(false, mod) : null;
        }
        public bool ColorSourceExists(string source) => ((ICatalog)inner).ColorSourceExists(source);
        public string? FindNpc(string name) => inner.FindNpc(name);
        public string? SuggestNpc(string name) => inner.SuggestNpc(name);
        public bool AssetExists(string asset) => inner.AssetExists(asset);
        public bool ItemExists(string qualifiedItemId) => inner.ItemExists(qualifiedItemId);
        public bool HairstyleExists(int index) => inner.HairstyleExists(index);
    }

    private static PackReadResult Read(ValidationInput input, Dictionary<string, string> files, ICatalog? catalog) =>
        new PackReader(new OverlayPackFileSystem(new DiskPackFileSystem(input.PackFolder), files, input.Removed, null, null, input.TextAssets, input.BlobPaths, input.BlobRoot), new PackMigrator()).Read(catalog, exportValidation: true);

    private static void PatchDependencies(Dictionary<string, string> files, DependencyReport report)
    {
        if (files.TryGetValue("manifest.json", out var manifestJson) && PackJson.Deserialize<Manifest>(manifestJson) is { } manifest)
        {
            manifest.Dependencies = report.ManifestDependencies.ToList();
            files["manifest.json"] = PackWriter.Serialize(manifest);
        }
        if (files.TryGetValue("studio.json", out var studioJson) && PackJson.Deserialize<StudioIndex>(studioJson) is { } studio)
        {
            studio.Requires = report.Requires.ToList();
            files["studio.json"] = PackWriter.Serialize(studio);
        }
    }

    private static Func<string, string?> Translations(ValidationInput input, Dictionary<string, string> files)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string path = Path.Combine(input.PackFolder, "i18n", "default.json");
        string? json = files.TryGetValue("i18n/default.json", out var overlay) ? overlay : File.Exists(path) ? File.ReadAllText(path) : null;
        if (json != null)
            try { foreach (var property in JObject.Parse(json).Properties()) map[property.Name] = (string?)property.Value ?? ""; }
            catch (Newtonsoft.Json.JsonException) { }
        return key => map.TryGetValue(key, out var value) ? value : null;
    }

    private static Problem ToProblem(ValidationIssue issue, IReadOnlyList<SourceEntry<Cutscene>> scenes)
    {
        // V7/V19 name the NPC the author probably meant ("Did you mean 'JadeMalic'?", "use 'Robin', not 'robin'").
        string? suggestion = issue.Rule is "V7" or "V19" && NpcSuggestion.Match(issue.Message) is { Success: true } m ? m.Groups[1].Value : null;
        return new()
        {
            Severity = issue.Severity, File = issue.File, JsonPath = issue.JsonPath, Rule = issue.Rule, Message = issue.Message,
            Target = Locate(issue.File, issue.JsonPath, scenes), Suggestion = suggestion,
            FixLabel = suggestion != null ? $"Use '{suggestion}'"
                : issue.Rule switch { "V5" when issue.AutoFix != null => "Replace with look-alikes", "V10" when issue.AutoFix != null => "Sort keys", _ => null }
        };
    }

    private static readonly System.Text.RegularExpressions.Regex NpcSuggestion = new(@"(?:Did you mean|use) '([^']+)'", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>Map a file + JSON path to something the editor can select.</summary>
    public static ProblemTarget Locate(string file, string path, IReadOnlyList<SourceEntry<Cutscene>> scenes)
    {
        var scene = scenes.FirstOrDefault(s => string.Equals(s.File, file, StringComparison.OrdinalIgnoreCase))?.Value;
        if (scene == null) return new ProblemTarget(file is "manifest.json" or "studio.json" ? TargetKind.Pack : TargetKind.Library);
        string id = scene.Id ?? "";
        if (TryIndexed(path, "Beats", out int beat, out string rest)) return new ProblemTarget(TargetKind.Beat, id, ViewModels.LaneNames.Main, beat, rest);
        if (TryIndexed(path, "OnSkip", out int skip, out rest)) return new ProblemTarget(TargetKind.Beat, id, ViewModels.LaneNames.Skip, skip, rest);
        if (TryIndexed(path, "Actors", out int actor, out rest)) return new ProblemTarget(TargetKind.Actor, id, null, actor, rest);
        if (path.StartsWith("Branches.", StringComparison.Ordinal))
        {
            // Branch IDs may contain dots; match the longest existing branch name.
            foreach (var name in (scene.Branches ?? new()).Keys.OrderByDescending(k => k.Length))
                if (TryIndexed(path, "Branches." + name, out int index, out rest)) return new ProblemTarget(TargetKind.Beat, id, name, index, rest);
                else if (path == "Branches." + name || path.StartsWith("Branches." + name + ".", StringComparison.Ordinal)) return new ProblemTarget(TargetKind.Scene, id, name);
        }
        return new ProblemTarget(TargetKind.Scene, id, null, -1, path);
    }

    private static bool TryIndexed(string path, string prefix, out int index, out string rest)
    {
        index = -1; rest = "";
        if (!path.StartsWith(prefix + "[", StringComparison.Ordinal)) return false;
        int close = path.IndexOf(']', prefix.Length);
        if (close < 0 || !int.TryParse(path.AsSpan(prefix.Length + 1, close - prefix.Length - 1), out index)) return false;
        rest = path[(close + 1)..].TrimStart('.');
        return true;
    }
}
