using System.IO;
using System.IO.Compression;
using SDCharacterStudio.Editor.Catalog;
using SDCharacterStudio.Editor.Project;

namespace SDCharacterStudio.Editor.Export;

public sealed class ExportOptions
{
    /// <summary>What players install (Q-17). The pack folder is saved either way; the other targets write <see cref="OutputFolder"/>.</summary>
    public ExportTarget Target { get; set; } = ExportTarget.ContentPack;
    /// <summary>Where a complete mod or a C# mod's scenes go (default: Export/&lt;name&gt; next to the project).</summary>
    public string? OutputFolder { get; set; }
    /// <summary>Also write a .zip of the pack folder here (the zip contains the "[SDCS] Name" folder).</summary>
    public string? ZipPath { get; set; }
    /// <summary>Also copy the pack into this Mods folder so the game loads it.</summary>
    public string? ModsFolder { get; set; }
}

public sealed record ExportResult(bool Success, ValidationResult Validation, string Message, IReadOnlyList<string> Outputs);

/// <summary>
/// Export = validate (errors block), recompute and write dependencies (doc 12 §3), save the pack folder in place,
/// then optionally zip it or copy it into Mods (doc 07 §8, §10).
/// </summary>
public static class PackExporter
{
    public static ExportResult Export(PackDocument document, ExportCatalog? catalog, ExportOptions options)
    {
        var validation = ValidationService.Validate(ValidationInput.From(document, catalog));
        if (validation.Errors > 0)
            return new ExportResult(false, validation, $"Export stopped: {validation.Errors} problem{(validation.Errors == 1 ? "" : "s")} must be fixed first.", Array.Empty<string>());
        if (validation.Dependencies is { } report)
            document.Edit("Update dependencies", () => DependencyAnalyzer.WriteInto(document, report));
        document.Save();
        var outputs = new List<string>();
        // The finished mod: the pack folder itself (content pack), or a folder written from it (Q-17).
        string mod = document.PackFolder;
        switch (options.Target)
        {
            case ExportTarget.CompleteMod:
                mod = options.OutputFolder ?? ModExporter.DefaultFolder(document, options.Target);
                ModExporter.WriteCompleteMod(document, mod);
                break;
            case ExportTarget.CodeModScenes:
                mod = options.OutputFolder ?? ModExporter.DefaultFolder(document, options.Target);
                ModExporter.WriteScenes(document, mod);
                outputs.Add(mod);
                return new ExportResult(true, validation, $"Exported {document.DisplayName} for a C# mod: copy Sdcs.cs and the scenes folder into your mod's project, then call Sdcs.Register(this) in Entry (README.txt has the details).", outputs);
        }
        outputs.Add(mod);
        if (!string.IsNullOrWhiteSpace(options.ZipPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ZipPath))!);
            if (File.Exists(options.ZipPath)) File.Delete(options.ZipPath);
            ZipFile.CreateFromDirectory(mod, options.ZipPath, CompressionLevel.Optimal, includeBaseDirectory: true);
            outputs.Add(options.ZipPath);
        }
        if (!string.IsNullOrWhiteSpace(options.ModsFolder))
        {
            string target = Path.Combine(options.ModsFolder, Path.GetFileName(mod.TrimEnd('\\', '/')));
            if (Path.GetFullPath(target).Equals(Path.GetFullPath(mod), StringComparison.OrdinalIgnoreCase))
                outputs.Add(target + " (already the pack folder)");
            else
            {
                string targetManifest = Path.Combine(target, "manifest.json");
                if (File.Exists(targetManifest) && !File.ReadAllText(targetManifest).Contains("\"" + document.PackId + "\"", StringComparison.OrdinalIgnoreCase))
                    return new ExportResult(false, validation, $"{target} already holds a different mod. Choose another Mods folder or remove it first.", outputs);
                // The same pack installed another way (a content pack and a complete mod share the ID) stops SMAPI loading either.
                if (Directory.Exists(options.ModsFolder) && Directory.EnumerateDirectories(options.ModsFolder).FirstOrDefault(d =>
                        !string.Equals(Path.GetFullPath(d), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(d, "manifest.json"))
                        && File.ReadAllText(Path.Combine(d, "manifest.json")).Contains("\"" + document.PackId + "\"", StringComparison.OrdinalIgnoreCase)) is { } other)
                    return new ExportResult(false, validation, $"{other} has the same pack installed another way. Remove that folder first: SMAPI won't load two mods with one ID.", outputs);
                // An earlier install of this same mod is replaced, so files the pack no longer has don't linger.
                if (File.Exists(targetManifest)) Directory.Delete(target, recursive: true);
                CopyDirectory(mod, target);
                outputs.Add(target);
            }
        }
        string warnings = validation.Warnings > 0 ? $" ({validation.Warnings} warning{(validation.Warnings == 1 ? "" : "s")})" : "";
        return new ExportResult(true, validation, "Exported " + document.DisplayName + warnings + ".", outputs);
    }

    public static void CopyDirectory(string source, string target)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
            string destination = Path.Combine(target, Path.GetRelativePath(source, file));
            var from = new FileInfo(file);
            var to = new FileInfo(destination);
            if (to.Exists && to.Length == from.Length && to.LastWriteTimeUtc == from.LastWriteTimeUtc) continue;
            File.Copy(file, destination, true);
            File.SetLastWriteTimeUtc(destination, from.LastWriteTimeUtc);
        }
    }
}
