using System.IO;
using SDCharacterStudio.Core.Validation;

namespace SDCharacterStudio.Editor.Export;

/// <summary>
/// Check and export a project without the editor window (the <c>sdcs</c> console tool), for authors and scripts:
/// <code>
/// sdcs check  &lt;project.sdcs&gt; [--game-export &lt;folder&gt;]
/// sdcs export &lt;project.sdcs&gt; --as mod|csharp|pack --to &lt;folder&gt; [--game-export &lt;folder&gt;]
/// </code>
/// Problems print one per line; the exit code is 0 when there are no errors, 1 when there are, 2 when the command
/// couldn't run. The checks and the export are the editor's own (Problems, Export), against the game export.
/// </summary>
public static class CommandLine
{
    public const int Ok = 0, HasErrors = 1, Failed = 2;

    public const string Usage =
        "sdcs check  <project.sdcs> [--game-export <folder>]\n" +
        "sdcs export <project.sdcs> --as mod|csharp|pack --to <folder> [--game-export <folder>]\n\n" +
        "  check   Check the project as the editor's Problems panel does.\n" +
        "  export  Check, then export: mod = a complete mod (players need nothing else), csharp = Sdcs.cs and a scenes\n" +
        "          folder for your C# mod, pack = a content pack for the SDCharacterStudio framework (copied into --to).\n" +
        "          Like the editor's Export, it saves the project's pack folder with its dependencies first.\n" +
        "  --game-export  The game export to check against (a profile folder with catalog.json). Default: the editor's.\n\n" +
        "Exit code: 0 no errors, 1 errors (nothing exported), 2 the command couldn't run.";

    public static int Run(IReadOnlyList<string> args, TextWriter output, TextWriter error, EditorSettings? settings = null)
    {
        try
        {
            if (args.Count < 2 || args[0] is not ("check" or "export")) { error.WriteLine(Usage); return Failed; }
            string command = args[0], project = args[1];
            string? exportAs = null, to = null, gameExport = null;
            for (int i = 2; i < args.Count; i++)
            {
                string Value() => i + 1 < args.Count ? args[++i] : throw new ArgumentException($"{args[i]} needs a value.");
                switch (args[i])
                {
                    case "--as": exportAs = Value(); break;
                    case "--to": to = Value(); break;
                    case "--game-export": gameExport = Value(); break;
                    default: throw new ArgumentException($"Unknown option {args[i]}.");
                }
            }
            if (!project.EndsWith(".sdcs", StringComparison.OrdinalIgnoreCase) || !File.Exists(project)) throw new FileNotFoundException($"No project file at {project} (a .sdcs file).");
            var document = PackDocument.Open(project);
            var catalog = Catalog(gameExport, settings ?? EditorSettings.Load(), output);

            if (command == "check")
            {
                var result = ValidationService.Validate(ValidationInput.From(document, catalog));
                Print(result, output);
                return result.Errors > 0 ? HasErrors : Ok;
            }

            var target = exportAs switch
            {
                "mod" => ExportTarget.CompleteMod,
                "csharp" => ExportTarget.CodeModScenes,
                "pack" => ExportTarget.ContentPack,
                _ => throw new ArgumentException("--as must be mod, csharp or pack."),
            };
            if (string.IsNullOrWhiteSpace(to)) throw new ArgumentException("--to <folder> is required.");
            to = Path.GetFullPath(to);
            var options = new ExportOptions { Target = target };
            if (target == ExportTarget.ContentPack) options.ModsFolder = to; else options.OutputFolder = to;
            var outcome = PackExporter.Export(document, catalog, options);
            Print(outcome.Validation, output);
            output.WriteLine(outcome.Message);
            foreach (var path in outcome.Outputs) output.WriteLine("  " + path);
            return outcome.Success ? Ok : outcome.Validation.Errors > 0 ? HasErrors : Failed;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException or Newtonsoft.Json.JsonException)
        {
            error.WriteLine(ex.Message);
            return Failed;
        }
    }

    private static ExportCatalog? Catalog(string? folder, EditorSettings settings, TextWriter output)
    {
        string? profile = folder ?? (settings.ExportProfile is { } chosen && File.Exists(Path.Combine(chosen, "catalog.json")) ? chosen
            : ExportCatalog.FindProfiles(settings.ExportFolder).FirstOrDefault());
        if (profile == null) { output.WriteLine("No game export: characters, maps and items aren't checked."); return null; }
        var catalog = ExportCatalog.Load(profile);
        output.WriteLine($"Game export: {catalog.Root}");
        return catalog;
    }

    private static void Print(ValidationResult result, TextWriter output)
    {
        foreach (var problem in result.Problems)
            output.WriteLine($"{(problem.IsError ? "error" : "warning")} {problem.Rule ?? "-"} {problem.Location}: {problem.Message}");
        output.WriteLine($"{result.Errors} error{(result.Errors == 1 ? "" : "s")}, {result.Warnings} warning{(result.Warnings == 1 ? "" : "s")}.");
    }
}
