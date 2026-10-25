using StardewModdingAPI;

namespace SDCharacterStudio.EmbeddedMod;

/// <summary>
/// The mod the editor exports for creators who don't write code (Export › As a complete mod, Q-17). The editor copies
/// this DLL under the pack's own name next to the pack's files; the engine inside plays them. Nothing else to install.
/// </summary>
public sealed class ModEntry : Mod
{
    public override void Entry(IModHelper helper) => Sdcs.Register(this, ".");

    public override object GetApi() => new Api();
}

/// <summary>
/// What other mods can do with this mod's scenes, through SMAPI: declare an interface with the members you need, then
/// <c>helper.ModRegistry.GetApi&lt;IYourInterface&gt;("this.mod.id")?.PlayCutscene("scene-id")</c>.
/// </summary>
public sealed class Api
{
    /// <summary>Play a scene now (or, with <paramref name="checkConditions"/>, only when its conditions are met).</summary>
    public bool PlayCutscene(string cutscene, bool checkConditions = false) => Sdcs.Play(cutscene, checkConditions);
    public bool IsPlaying => Sdcs.IsPlaying;
    public void StopCutscene() => Sdcs.Stop();
    public IReadOnlyList<string> GetCutscenes() => Sdcs.Cutscenes;
    public string EngineVersion => Sdcs.EngineVersion;
    public string CommandPrefix => Sdcs.CommandPrefix;
}
