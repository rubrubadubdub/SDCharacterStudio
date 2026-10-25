using System.Reflection;
using StardewModdingAPI;
using SDCharacterStudio.Core;
using SDCharacterStudio.Runtime;

namespace SDCharacterStudio.Engine;

/// <summary>
/// The embedded engine's entry point (Q-17). The drop-in <c>Sdcs.cs</c> loads this assembly into a load context of the
/// host mod's own and calls <see cref="Start"/> through reflection, so the host mod never references the engine and
/// no two mods' copies can meet. Only framework types (delegates, strings) cross back to the host.
/// </summary>
public static class EngineHost
{
    public static string Version => typeof(EngineHost).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(EngineHost).Assembly.GetName().Version?.ToString() ?? "?";

    /// <summary>Start the engine for <paramref name="mod"/>, playing the scenes in <paramref name="folder"/> (relative to the mod).</summary>
    public static IDictionary<string, Delegate> Start(Mod mod, string folder)
    {
        string id = mod.ModManifest.UniqueID;
        EngineNames.UseEmbedded(id);
        var host = new SdcsHost(mod.Helper, mod.Monitor, mod.ModManifest, new ModConfig(), consoleCommands: false);
        string scenes = Path.GetFullPath(Path.Combine(mod.Helper.DirectoryPath, folder));
        mod.Helper.Events.GameLoop.GameLaunched += (_, _) =>
        {
            if (!Directory.Exists(scenes))
                mod.Monitor.Log($"No SDCharacterStudio scenes at {scenes}. Copy the \"{folder}\" folder the editor exported into your mod's project, "
                    + $"and add this line to your .csproj so the build includes it: <None Update=\"{folder}\\**\\*\" CopyToOutputDirectory=\"PreserveNewest\" />", LogLevel.Warn);
            else if (!host.Api.RegisterPackFolder(mod.ModManifest, scenes))
                mod.Monitor.Log($"Couldn't load the SDCharacterStudio scenes in {scenes}; the log above says why.", LogLevel.Warn);
        };
        mod.Monitor.Log($"SDCharacterStudio engine {Version} embedded (commands {EngineNames.Prefix}_*), scenes from {scenes}.", LogLevel.Trace);
        var started = new List<Action<string>>();
        var ended = new List<Action<string>>();
        host.Api.CutsceneStarted += (_, e) => { if (string.Equals(e.PackUniqueId, id, StringComparison.OrdinalIgnoreCase)) foreach (var handler in started.ToArray()) handler(e.CutsceneId); };
        host.Api.CutsceneEnded += (_, e) => { if (string.Equals(e.PackUniqueId, id, StringComparison.OrdinalIgnoreCase)) foreach (var handler in ended.ToArray()) handler(e.CutsceneId); };
        return new Dictionary<string, Delegate>(StringComparer.Ordinal)
        {
            ["Play"] = new Func<string, bool, bool>((cutscene, checkConditions) => host.Api.PlayCutscene(id, cutscene, ignoreConditions: !checkConditions)),
            ["IsPlaying"] = new Func<bool>(() => host.Api.IsPlaying),
            ["Stop"] = new Action(host.Api.StopCutscene),
            ["Cutscenes"] = new Func<IReadOnlyList<string>>(() => host.Api.GetCutscenes(id)),
            ["Started"] = new Action<Action<string>, bool>((handler, add) => { if (add) started.Add(handler); else started.Remove(handler); }),
            ["Ended"] = new Action<Action<string>, bool>((handler, add) => { if (add) ended.Add(handler); else ended.Remove(handler); }),
            ["Version"] = new Func<string>(() => Version),
            ["CommandPrefix"] = new Func<string>(() => EngineNames.Prefix),
        };
    }
}
