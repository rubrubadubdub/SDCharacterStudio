using System.Collections.ObjectModel;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Validation;

namespace SDCharacterStudio.Core.Compiler;

public sealed record BeatInfo(string Id, string JsonPath, Beat Beat);

public sealed record CompiledCutscene(
    string Key,
    string Location,
    string MainKey,
    string MainScript,
    IReadOnlyDictionary<string, string> SubScripts,
    IReadOnlyDictionary<string, BeatInfo> Beats)
{
    public string ToGoldenText()
    {
        var entries = new[] { new KeyValuePair<string, string>(MainKey, MainScript) }
            .Concat(SubScripts.OrderBy(x => x.Key, StringComparer.Ordinal));
        return string.Concat(entries.Select(x => $"== {x.Key}\n{x.Value}\n\n"));
    }
}

public sealed record CompilationResult(CompiledCutscene? Cutscene, IReadOnlyList<ValidationIssue> Issues)
{
    public bool Success => Cutscene is not null && !Issues.Any(x => x.Severity == ValidationSeverity.Error);
}
