using System;
using SDCharacterStudio.Core.Compiler;
using SDCharacterStudio.Core.Loading;
using SDCharacterStudio.Core.Models;

namespace SDCharacterStudio.Runtime;

public sealed record RegisteredCutscene(string PackId, string PackName, Cutscene Source,
    CompiledCutscene Compiled, LoadedPack Pack, Func<string, string?> Translate);
