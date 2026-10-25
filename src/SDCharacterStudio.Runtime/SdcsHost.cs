using StardewModdingAPI;
using StardewValley;
using SDCharacterStudio.Core;
using SDCharacterStudio.Runtime.Api;
using SDCharacterStudio.Runtime.Commands;
using SDCharacterStudio.Runtime.Loading;
using SDCharacterStudio.Runtime.Playback;
using SDCharacterStudio.Runtime.Media;
using SDCharacterStudio.Runtime.Animation;

namespace SDCharacterStudio.Runtime;

/// <summary>
/// Everything the engine does in the game: load packs, inject their events, play them, and clean up after them. The
/// framework mod starts one with console commands and its owned content packs; a mod that embeds the engine (Q-17)
/// starts one quietly for its own scenes, after <see cref="EngineNames.UseEmbedded"/>.
/// </summary>
public sealed class SdcsHost
{
    private readonly IModHelper helper;
    private readonly IMonitor log;
    private readonly ModConfig config;
    private readonly CutsceneRegistry registry;
    private readonly PlaybackController playback;
    private readonly AnimationRuntime animation;
    private bool reloadRequested;
    private bool animationPrepared;

    public SdcsHost(IModHelper helper, IMonitor monitor, IManifest owner, ModConfig config, bool consoleCommands)
    {
        this.helper = helper; log = monitor; this.config = config;
        var diagnostics = new DiagnosticMonitor(monitor);
        registry = new CutsceneRegistry();
        Packs = new PackLoader(helper, diagnostics, registry, () => config.LogCompiledScripts);
        var session = new PlaybackSession(diagnostics);
        animation = new AnimationRuntime(helper, diagnostics, session, Packs);
        FarmerVisibilityPatch.Apply(EngineNames.IsEmbedded ? owner.UniqueID + ".SDCS" : owner.UniqueID, session);
        playback = new PlaybackController(registry, session, diagnostics, () => config.Enabled);
        Api = new SdcsApi(playback, session, registry, Packs, () => config.Enabled, animation, diagnostics);
        var injector = new EventInjector(registry, diagnostics);
        var commands = new CommandServices(registry, Packs, session, diagnostics, animation);
        CommandRegistration.Register(commands);
        Conditions.ModQueries.Register(helper.ModRegistry);
        CueSounds.MuteDuringDialogue = config.MuteStandInSoundsDuringDialogue;
        helper.Events.GameLoop.GameLaunched += (_, _) => Safe("load packs", () => { if (config.Enabled) Packs.LoadAll(); });
        helper.Events.Content.AssetRequested += (s, e) => { if (config.Enabled) injector.OnAssetRequested(s, e); };
        helper.Events.Content.AssetRequested += (_, e) => Safe("provide generated assets", () => animation.Generated.OnAssetRequested(e));
        helper.Events.Content.AssetsInvalidated += (_, e) => Safe("invalidate animation sources", () => animation.Compositor.InvalidateAssets(e.NamesWithoutLocale.Select(x => x.Name)));
        helper.Events.Content.LocaleChanged += (_, _) => { if (config.Enabled) reloadRequested = true; };
        helper.Events.GameLoop.UpdateTicked += (_, _) => Safe("update playback", () =>
        {
            playback.Update();
            animation.Update(Game1.currentGameTime.ElapsedGameTime.TotalMilliseconds);
            commands.Update(Game1.currentGameTime.ElapsedGameTime.TotalMilliseconds);
            if (reloadRequested && !playback.IsPlaying && !Game1.eventUp)
            { reloadRequested = false; Packs.Reload(); }
            if (!animationPrepared && Context.IsWorldReady && !playback.IsPlaying && !Game1.eventUp)
            { animation.Prepare(registry.All); animationPrepared = true; }
        });
        helper.Events.GameLoop.DayStarted += (_, _) => Safe("grant day-after flags", () => { if (config.Enabled) Packs.GrantDayAfterFlags(); });
        // Re-read all candidates so a scene rejected for a previous save's NPCs
        // or locations is reconsidered for this world. Defer around native events.
        helper.Events.GameLoop.SaveLoaded += (_, _) => Safe("repair saved costume", () => { animation.Costumes.RepairAfterCrash(Game1.player); if (config.Enabled) reloadRequested = true; });
        helper.Events.GameLoop.Saving += (_, _) => Safe("suspend costumes for saving", animation.Costumes.SuspendForSave);
        helper.Events.GameLoop.Saved += (_, _) => Safe("resume costumes after saving", animation.Costumes.ResumeAfterSave);
        helper.Events.GameLoop.ReturnedToTitle += (_, _) => Safe("return to title", () => { playback.Reset(forceRestoreAppearance: true); animation.Costumes.RestoreAll(); animation.PortraitCostumes.RestoreAll(); animation.Compositor.Farmers.Dispose(); VideoMenu.ClearPackCache(); animationPrepared = false; });
        if (consoleCommands) RegisterConsoleCommands();
    }

    public ISdcsApi Api { get; }
    public PackLoader Packs { get; }

    private void RegisterConsoleCommands()
    {
        Add("sdcs_restore_costume", "Restore the player's original appearance and clear render part overrides.", args =>
        {
            Require(args, 0, "sdcs_restore_costume");
            if (!Context.IsWorldReady) throw new InvalidOperationException("Load a world first.");
            animation.RestoreCostumes(); animation.Costumes.Restore(Game1.player);
            log.Log("Restored the player's original costume.", LogLevel.Info);
        });
        Add("sdcs_farmer_parts", "Usage: sdcs_farmer_parts <frame> <facing>. Save each farmer part as a PNG in this mod's debug folder.", args =>
        {
            Require(args, 2, "sdcs_farmer_parts <frame> <facing>");
            if (!Context.IsWorldReady) throw new InvalidOperationException("Load a world first.");
            if (!int.TryParse(args[0], out int frame)) throw new ArgumentException("Frame must be an integer from0 to125.");
            int facing = Ids.Facings.TryGetValue(args[1], out int direction) ? direction : int.TryParse(args[1], out direction) && direction >= 0 && direction <= 3 ? direction : throw new ArgumentException("Facing must be up, right, down, left or0–3.");
            string folder = Path.Combine(helper.DirectoryPath, "debug"); Directory.CreateDirectory(folder);
            foreach (string part in new[] { "Full", "Base", "Head", "Body", "Eyes", "Pants", "Shirt", "Accessory", "Hair", "Hat", "Arms", "Shoes" })
            {
                var pixels = animation.Compositor.Farmers.Render(Game1.player, frame, facing, part);
                using var texture = Rendering.TexturePixels.Upload(Game1.graphics.GraphicsDevice, pixels);
                using var output = File.Create(Path.Combine(folder, $"farmer-{frame}-{facing}-{part.ToLowerInvariant()}.png"));
                texture.SaveAsPng(output, texture.Width, texture.Height);
            }
            log.Log($"Saved farmer parts to {folder}.", LogLevel.Info);
        });
        Add("sdcs_list", "List loaded SDCS packs, cutscenes and skip reasons.", args =>
        {
            foreach (var status in Packs.Statuses)
            {
                log.Log($"{status.PackId}: {status.LoadedCount} cutscene(s)", LogLevel.Info);
                foreach (var scene in registry.ForPack(status.PackId)) log.Log($"  {scene.Source.Id} ({scene.Compiled.Location})", LogLevel.Info);
                foreach (var skipped in status.Skipped) log.Log($"  {skipped.CutsceneId}: skipped: {skipped.Reason}", LogLevel.Info);
            }
            if (Packs.Statuses.Count == 0) log.Log("No SDCS packs loaded.", LogLevel.Info);
        });
        Add("sdcs_play", "Usage: sdcs_play <packId> <cutsceneId>. Play now, ignoring conditions.", args =>
        { Require(args, 2, "sdcs_play <packId> <cutsceneId>"); playback.Play(args[0], args[1], true); });
        Add("sdcs_reload", "Usage: sdcs_reload [packId]. Reload packs and invalidate their event assets.", args =>
        {
            if (args.Length > 1) throw new ArgumentException("Usage: sdcs_reload [packId]");
            if (playback.IsPlaying) { log.Log("Wait until the cutscene ends before reloading.", LogLevel.Info); return; }
            Packs.Reload(args.FirstOrDefault());
        });
        Add("sdcs_reset", "Usage: sdcs_reset <packId> <cutsceneId>. Forget seen state and this cutscene's SDCS mail flags.", args =>
        {
            Require(args, 2, "sdcs_reset <packId> <cutsceneId>");
            if (!Context.IsWorldReady || playback.IsPlaying) throw new InvalidOperationException("Load a save and finish the active cutscene before resetting.");
            var scene = Find(args);
            Game1.player.eventsSeen.Remove(scene.Compiled.Key);
            Game1.eventsSeenSinceLastLocationChange.Remove(scene.Compiled.Key);
            foreach (var flag in ResetFlags.ForCutscene(scene.Compiled.Key, Game1.player.mailReceived)) Game1.player.mailReceived.Remove(flag);
            log.Log($"Reset {scene.Compiled.Key}.", LogLevel.Info);
        });
        Add("sdcs_dump", "Usage: sdcs_dump <packId> <cutsceneId>. Print the main event script and all sub-scripts.", args =>
        { Require(args, 2, "sdcs_dump <packId> <cutsceneId>"); log.Log(Find(args).Compiled.ToGoldenText(), LogLevel.Info); });
        RegisteredCutscene Find(string[] args) => registry.Find(args[0] + "." + args[1]) ?? throw new ArgumentException($"No loaded cutscene '{args[0]}.{args[1]}'.");
        void Add(string name, string description, Action<string[]> action) => helper.ConsoleCommands.Add(name, description, (_, args) =>
        {
            if (!config.Enabled) { log.Log("SDCharacterStudio is disabled in config.json.", LogLevel.Info); return; }
            try { action(args); }
            catch (ArgumentException ex) { log.Log(ex.Message, LogLevel.Info); }
            catch (InvalidOperationException ex) { log.Log(ex.Message, LogLevel.Info); }
            catch (Exception ex) { log.Log($"{name} failed: {ex}", LogLevel.Error); }
        });
    }

    private static void Require(string[] args, int count, string usage)
    { if (args.Length != count) throw new ArgumentException("Usage: " + usage); }

    private void Safe(string operation, Action action)
    { try { action(); } catch (Exception ex) { log.Log($"Could not {operation}: {ex}", LogLevel.Error); } }
}
