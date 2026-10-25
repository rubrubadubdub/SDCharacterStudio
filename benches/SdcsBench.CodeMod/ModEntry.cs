using StardewModdingAPI;

namespace SdcsBench.CodeMod;

/// <summary>
/// A C# mod using SDCharacterStudio scenes, written the way a modder would: Sdcs.cs dropped into the project, the
/// scenes the editor exported copied into "scenes", and one line in Entry. Players install only this mod.
/// </summary>
public sealed class ModEntry : Mod
{
    public override void Entry(IModHelper helper)
    {
        Sdcs.Register(this);

        // Not needed for scenes to play; a handy way to try one: bench_code_play [scene]
        helper.ConsoleCommands.Add("bench_code_play", "Play a scene of the SDCS code bench now.", (_, args) => Sdcs.Play(args.Length > 0 ? args[0] : "bow"));
    }

    /// <summary>Lets other mods (and the bench driver) play these scenes.</summary>
    public override object GetApi() => new Api();
}

public sealed class Api
{
    public bool PlayCutscene(string cutscene, bool checkConditions = false) => Sdcs.Play(cutscene, checkConditions);
    public bool IsPlaying => Sdcs.IsPlaying;
    public void StopCutscene() => Sdcs.Stop();
    public IReadOnlyList<string> GetCutscenes() => Sdcs.Cutscenes;
    public string EngineVersion => Sdcs.EngineVersion;
    public string CommandPrefix => Sdcs.CommandPrefix;
}
