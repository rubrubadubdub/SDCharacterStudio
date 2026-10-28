using StardewModdingAPI;

namespace CameraBench;

public sealed class ModEntry : Mod
{
    public override void Entry(IModHelper helper) => Sdcs.Register(this);
    public override object GetApi() => this;
    public bool PlayCutscene(string cutscene, bool checkConditions = false) => Sdcs.Play(cutscene, checkConditions);
    public bool IsPlaying => Sdcs.IsPlaying;
    public void StopCutscene() => Sdcs.Stop();
    public IReadOnlyList<string> GetCutscenes() => Sdcs.Cutscenes;
    public string EngineVersion => Sdcs.EngineVersion;
    public string CommandPrefix => Sdcs.CommandPrefix;
}
