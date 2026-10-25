using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core.Compiler;
using SDCharacterStudio.Core.Loading;
using SDCharacterStudio.Core.Migration;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Serialization;
using SDCharacterStudio.Core.Validation;

namespace SDCharacterStudio.Runtime.Loading;

public sealed record SkippedCutscene(string CutsceneId, string Reason);
public sealed record PackCompilationResult(LoadedPack Pack, IReadOnlyList<RegisteredCutscene> Cutscenes,
    IReadOnlyList<ValidationIssue> Issues, IReadOnlyList<SkippedCutscene> Skipped);

public static class PackCompilation
{
    public static PackCompilationResult Compile(IPackFileSystem files, string packId, string packName,
        Func<string,string?> translate, Func<string,bool> isModLoaded, IEnumerable<RegisteredCutscene>? existingCutscenes = null)
    {
        var read = new PackReader(files, new PackMigrator()).Read(exportValidation: false);
        var pack = read.Pack;
        var issues = read.Issues.ToList();
        var loaded = new List<RegisteredCutscene>();
        var skipped = new List<SkippedCutscene>();
        var scriptOwners = new Dictionary<string, RegisteredCutscene>(StringComparer.OrdinalIgnoreCase);
        foreach (var existing in (existingCutscenes ?? Array.Empty<RegisteredCutscene>()).Where(x => !Same(x.PackId, packId)))
        {
            var conflict = CutsceneRegistry.TryReserveEventIds(existing, scriptOwners);
            if (conflict != null) throw new ArgumentException(conflict, nameof(existingCutscenes));
        }
        var errors = issues.Where(x => x.Severity == ValidationSeverity.Error).ToArray();
        var fatal = errors.FirstOrDefault(x => Same(x.File, "manifest.json") ||
            Same(x.File, "studio.json") && !x.JsonPath.StartsWith("Library", StringComparison.Ordinal) && !x.JsonPath.StartsWith("Cutscenes", StringComparison.Ordinal));
        var missing = (pack.Index?.Requires ?? new()).Where(x => !string.IsNullOrWhiteSpace(x?.Mod) && !isModLoaded(x.Mod!))
            .Select(x => x.Mod!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var nodes = LibraryNodes(pack).ToArray();
        // Core checks each video once per pack. Carry that resource failure to all
        // scenes that reference it, rather than admitting later users of the video.
        var brokenVideos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var scene in pack.Cutscenes)
        {
            var json = JObject.FromObject(scene.Value, PackJson.CreateSerializer());
            foreach (var error in errors.Where(x => Same(x.File, scene.File) && x.JsonPath.EndsWith(".Video", StringComparison.Ordinal)))
                if (json.SelectToken(error.JsonPath) is JValue { Type: JTokenType.String } video)
                    brokenVideos[video.Value<string>()!] = error.Message;
        }
        var compiler = new EventCompiler();
        foreach (var source in pack.Cutscenes)
        {
            string? reason = fatal?.Message;
            if (pack.Index == null || pack.Manifest == null) reason ??= "Missing studio.json or manifest.json.";
            if (missing.Length > 0) reason = "Needs installed mod(s): " + string.Join(", ", missing);
            reason ??= errors.FirstOrDefault(x => Same(x.File, source.File))?.Message;
            if (reason == null && brokenVideos.Count > 0)
                reason = JObject.FromObject(source.Value, PackJson.CreateSerializer()).Descendants().OfType<JProperty>()
                    .Where(x => x.Name == "Video" && x.Value.Type == JTokenType.String)
                    .Select(x => brokenVideos.TryGetValue(x.Value.Value<string>()!, out var message) ? message : null).FirstOrDefault(x => x != null);
            if (pack.Cutscenes.Count(x => Same(x.Value.Id, source.Value.Id)) > 1) reason ??= "Duplicate cutscene ID.";
            if (reason == null)
            {
                var pending = new Stack<LibraryNode>(References(source.Value, nodes));
                var visited = new HashSet<LibraryNode>();
                while (pending.Count > 0)
                {
                    var node = pending.Pop();
                    if (!visited.Add(node)) continue;
                    var invalid = errors.FirstOrDefault(x => Same(x.File, node.File) && (node.Prefix.Length == 0 || x.JsonPath.Length == 0 || x.JsonPath == "Format" ||
                        x.JsonPath == node.Prefix || x.JsonPath.StartsWith(node.Prefix + ".", StringComparison.Ordinal) || x.JsonPath.StartsWith(node.Prefix + "[", StringComparison.Ordinal)));
                    if (invalid != null) { reason = $"Uses broken {node.Kind} '{node.Id}': {invalid.Message}"; break; }
                    foreach (var dependency in References(node.Value, nodes)) pending.Push(dependency);
                }
            }
            if (reason != null) { skipped.Add(new(source.Value.Id ?? source.File, reason)); continue; }
            // Library clips become ordinary scene clips here, so the clip player sees one model (doc 03 §17.1).
            var expanded = LibraryClips.Expand(source.Value, pack);
            var result = compiler.Compile(packId, expanded, translate);
            issues.AddRange(result.Issues.Select(x => x with { File = source.File }));
            if (result.Success)
            {
                var scene = new RegisteredCutscene(packId, packName, expanded, result.Cutscene!, pack, translate);
                var conflict = CutsceneRegistry.TryReserveEventIds(scene, scriptOwners);
                if (conflict == null) loaded.Add(scene);
                else
                {
                    skipped.Add(new(source.Value.Id ?? source.File, conflict));
                    issues.Add(new(ValidationSeverity.Error, source.File, "Id", conflict, "EventIdCollision"));
                }
            }
            else
                skipped.Add(new(source.Value.Id ?? source.File, string.Join("; ", result.Issues.Where(x => x.Severity == ValidationSeverity.Error).Select(x => x.Message))));
        }
        // Documents that could not be deserialized never reached LoadedPack.Cutscenes.
        var scenePatterns = pack.Index?.Cutscenes ?? new List<string> { "cutscenes/*.json" };
        bool IsSceneFile(string file) => scenePatterns.Where(PackPath.IsSafe).Any(pattern =>
            Regex.IsMatch(file, "^" + Regex.Escape(pattern).Replace(@"\*\*", ".*").Replace(@"\*", "[^/]*").Replace(@"\?", "[^/]") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        foreach (var file in errors.Where(x => IsSceneFile(x.File)).Select(x => x.File).Distinct(StringComparer.OrdinalIgnoreCase))
            if (!pack.Cutscenes.Any(x => Same(x.File, file))) skipped.Add(new(file, errors.First(x => Same(x.File, file)).Message));
        return new(pack, loaded, issues, skipped);
    }

    private sealed record LibraryNode(string Kind, string? Id, string File, string Prefix, object Value);

    private static IEnumerable<LibraryNode> LibraryNodes(LoadedPack pack)
    {
        foreach (var x in pack.Poses) yield return new("pose", x.Value.Id, x.File, "", x.Value);
        foreach (var x in pack.Portraits) yield return new("portrait", x.Value.Id, x.File, "", x.Value);
        foreach (var x in pack.Characters) yield return new("rig", x.Value.Id, x.File, "", x.Value);
        foreach (var x in pack.Clips) yield return new("clip", x.Value.Id, x.File, "", x.Value);
        if (pack.Masks is { } masks)
            for (var i = 0; i < (masks.Value.Masks?.Count ?? 0); i++)
                if (masks.Value.Masks![i] is { } x) yield return new("mask", x.Id, masks.File, $"Masks[{i}]", x);
        if (pack.Expressions is { } expressions)
            for (var i = 0; i < (expressions.Value.Expressions?.Count ?? 0); i++)
                if (expressions.Value.Expressions![i] is { } x) yield return new("expression", x.Id, expressions.File, $"Expressions[{i}]", x);
        if (pack.Costumes is { } costumes)
            for (var i = 0; i < (costumes.Value.Costumes?.Count ?? 0); i++)
                if (costumes.Value.Costumes![i] is { } x) yield return new("costume", x.Id, costumes.File, $"Costumes[{i}]", x);
    }

    private static IEnumerable<LibraryNode> References(object value, LibraryNode[] nodes)
    {
        var json = JObject.FromObject(value, PackJson.CreateSerializer());
        foreach (var token in json.Descendants().OfType<JValue>().Where(x => x.Type == JTokenType.String))
        {
            var name = (token.Parent as JProperty)?.Name;
            var kind = name switch
            {
                "Pose" => "pose", "Portrait" => "portrait", "Expression" => "expression",
                "Preset" or "Costume" => "costume", "Mask" or "HeadMask" => "mask", "For" => "rig", "Clip" => "clip", _ => null
            };
            if (token.Parent?.Parent is JObject parent && parent.Parent is JProperty { Name: "HeadMask" }) kind = "mask";
            if (kind != null)
                foreach (var node in nodes.Where(x => x.Kind == kind && Same(x.Id, token.Value<string>()))) yield return node;
            if (token.Parent?.Parent is JObject slots && slots.Parent is JProperty { Name: "ColorSlots" })
            {
                var who = token.Value<string>()?.Split('.')[0];
                foreach (var node in nodes.Where(x => x.Kind == "rig" && (Same(x.Id, who) || x.Value is CharacterRig rig && Same(rig.Npc, who)))) yield return node;
            }
        }
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
