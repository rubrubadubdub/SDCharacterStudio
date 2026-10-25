using StardewModdingAPI;
using StardewValley;
using SDCharacterStudio.Core.Compositing;

namespace SDCharacterStudio.Runtime.Api;

public interface ISdcsApi
{
    bool PlayCutscene(string packUniqueId, string cutsceneId, bool ignoreConditions = false);
    bool IsPlaying { get; }
    IReadOnlyList<string> GetCutscenes(string packUniqueId);
    bool RegisterPackFolder(IManifest owner, string absoluteFolder);
    bool ReloadPack(string packUniqueId);
    /// <summary>Cancel pending preview starts and stop playback, restoring retained actor and farmer appearance regardless of OnEnd settings.</summary>
    void StopCutscene();
    RgbaImage RenderFarmerPart(Farmer farmer, int frame, int facing, string parts);
    Farmer CreatePreviewFarmer(IReadOnlyDictionary<string, string> loadout);
    event EventHandler<RuntimeDiagnosticEventArgs>? Diagnostic;
    event EventHandler<CutsceneEventArgs>? CutsceneStarted;
    event EventHandler<CutsceneEventArgs>? CutsceneEnded;
}

public sealed class RuntimeDiagnosticEventArgs : EventArgs
{
    public string Level { get; }
    public string Message { get; }
    public RuntimeDiagnosticEventArgs(string level, string message) { Level = level; Message = message; }
}

public sealed class CutsceneEventArgs : EventArgs
{
    public string PackUniqueId { get; }
    public string CutsceneId { get; }
    public bool Unexpected { get; }
    public CutsceneEventArgs(string packUniqueId, string cutsceneId, bool unexpected = false)
    { PackUniqueId = packUniqueId; CutsceneId = cutsceneId; Unexpected = unexpected; }
}
