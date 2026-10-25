using SDCharacterStudio.Runtime.Playback;
using SDCharacterStudio.Runtime.Loading;
using StardewModdingAPI;
using StardewValley;
using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Runtime.Animation;
using SDCharacterStudio.Runtime.Costumes;
using Newtonsoft.Json.Linq;

namespace SDCharacterStudio.Runtime.Api;

public sealed class SdcsApi : ISdcsApi
{
    private readonly PlaybackController playback;
    private readonly PlaybackSession session;
    private readonly CutsceneRegistry registry;
    private readonly PackLoader packs;
    private readonly Func<bool> enabled;
    private readonly AnimationRuntime animation;
    private readonly DiagnosticMonitor diagnostics;

    public SdcsApi(PlaybackController playback, PlaybackSession session, CutsceneRegistry registry, PackLoader packs, Func<bool> enabled, AnimationRuntime animation, DiagnosticMonitor diagnostics)
    { this.playback = playback; this.session = session; this.registry = registry; this.packs = packs; this.enabled = enabled; this.animation = animation; this.diagnostics = diagnostics; }
    public bool PlayCutscene(string packUniqueId, string cutsceneId, bool ignoreConditions = false) => playback.Play(packUniqueId, cutsceneId, ignoreConditions);
    public bool IsPlaying => playback.IsPlaying;
    public IReadOnlyList<string> GetCutscenes(string packUniqueId) => registry.ForPack(packUniqueId).Select(x => x.Source.Id!).ToArray();
    public bool RegisterPackFolder(IManifest owner, string absoluteFolder) => enabled() && !IsPlaying && packs.RegisterPackFolder(owner, absoluteFolder);
    public bool ReloadPack(string packUniqueId) => enabled() && !IsPlaying && packs.Reload(packUniqueId);
    public void StopCutscene()
    {
        var current = Game1.CurrentEvent;
        playback.Reset(forceRestoreAppearance:true);
        PreviewEventExit.Exit(current);
    }
    public RgbaImage RenderFarmerPart(Farmer farmer, int frame, int facing, string parts) => animation.Compositor.Farmers.Render(farmer, frame, facing, parts);
    public Farmer CreatePreviewFarmer(IReadOnlyDictionary<string, string> loadout)
    {
        if (!Context.IsWorldReady) throw new InvalidOperationException("Load a save first.");
        var values = loadout.ToDictionary(x => x.Key, x => x.Key is "Hair" or "Skin" or "Accessory" && int.TryParse(x.Value, out var n) ? new JValue(n) : (JToken)new JValue(x.Value), StringComparer.OrdinalIgnoreCase);
        var costumes = new CostumeManager(diagnostics);
        costumes.Validate(values);
        var farmer = Game1.player.CreateFakeEventFarmer();
        try
        {
            farmer.hat.Value = Game1.player.hat.Value?.getOne() as StardewValley.Objects.Hat;
            farmer.boots.Value = Game1.player.boots.Value?.getOne() as StardewValley.Objects.Boots;
            farmer.shirtItem.Value = Game1.player.shirtItem.Value?.getOne() as StardewValley.Objects.Clothing;
            farmer.pantsItem.Value = Game1.player.pantsItem.Value?.getOne() as StardewValley.Objects.Clothing;
            costumes.Apply(farmer, values);
            farmer.modData.Remove(CostumeManager.BackupKey);
            return farmer;
        }
        catch
        {
            try { animation.Reflection.GetField<Microsoft.Xna.Framework.Graphics.Texture2D?>(farmer.FarmerRenderer,"baseTexture").GetValue()?.Dispose(); }
            finally { farmer.FarmerRenderer.unload(); }
            throw;
        }
    }
    public event EventHandler<RuntimeDiagnosticEventArgs>? Diagnostic { add => diagnostics.Diagnostic += value; remove => diagnostics.Diagnostic -= value; }
    public event EventHandler<CutsceneEventArgs>? CutsceneStarted { add => session.Started += value; remove => session.Started -= value; }
    public event EventHandler<CutsceneEventArgs>? CutsceneEnded { add => session.Ended += value; remove => session.Ended -= value; }
}
