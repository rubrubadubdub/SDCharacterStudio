using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json.Linq;
using SDCharacterStudio.Bridge.Export;
using SDCharacterStudio.Core.Bridge;
using SDCharacterStudio.Runtime.Api;
using StardewModdingAPI;
using StardewValley;

namespace SDCharacterStudio.Bridge;

/// <summary>Owns game-thread export/preview work and cancels save-dependent work on world exit.</summary>
public sealed class BridgeCoordinator : IDisposable
{
    private readonly IModHelper helper;
    private readonly IMonitor monitor;
    private readonly ISdcsApi runtime;
    private readonly BridgeConfig config;
    private readonly PreviewServer server;
    private ExportSession? export;
    private Action<JObject>? exportReply;
    private Action<JObject>? exportProgress;
    private bool publishCatalog;
    private int nextHud = 10;
    private bool saveRequested;
    private bool stopFailedPreview;
    private (PreviewRequest Request, string Pack, string Scene, bool Ignore, DateTime Started, bool Launched, bool StartedObserved)? pendingPlay;
    /// <summary>The export's last step (origins, catalog, moving files into place) runs here, off the game thread.</summary>
    private Task? finishing;
    // Status snapshots for pings answered on the pipe thread (the game thread may be busy exporting).
    private volatile bool statusSaveLoaded, statusExporting;
    private volatile string? exportNote;
    /// <summary>A preview registration waiting for the previous preview to stop (a pack can't load during an event).</summary>
    private (PreviewRequest Request, DateTime Since)? pendingRegistration;
    public DateTime? LastExport { get; private set; }
    public bool IsExporting => export != null;
    public string ExportRoot => config.OutputPath();
    public BridgeCoordinator(IModHelper helper, IMonitor monitor, ISdcsApi runtime, BridgeConfig config)
    {
        this.helper = helper; this.monitor = monitor; this.runtime = runtime; this.config = config;
        server = new(config.PreviewPipeName, Environment.ExpandEnvironmentVariables(config.PreviewRequestFolder));
        runtime.Diagnostic += OnDiagnostic;
        runtime.CutsceneStarted += OnCutsceneStarted;
        runtime.CutsceneEnded += OnCutsceneEnded;
        server.Immediate = request => (string?)request["Op"] == "Ping"
            ? Ok(new JObject { ["Game"] = Game1.version, ["SaveLoaded"] = statusSaveLoaded, ["Exporting"] = statusExporting, ["ExportNote"] = exportNote })
            : null;
        server.Start();
    }
    private void OnDiagnostic(object? sender, RuntimeDiagnosticEventArgs e) => server.Publish(new JObject { ["Op"] = "Log", ["Level"] = e.Level, ["Message"] = e.Message, ["Final"] = false });
    private void OnCutsceneStarted(object? sender, CutsceneEventArgs e)
    {
        if (pendingPlay is not { Launched: true } play || !string.Equals(play.Pack, e.PackUniqueId, StringComparison.OrdinalIgnoreCase) || !string.Equals(play.Scene, e.CutsceneId, StringComparison.OrdinalIgnoreCase)) return;
        // Runtime Started intentionally precedes animation initialization. A synchronous
        // Begin failure can still emit Ended before our next Update acknowledges readiness.
        pendingPlay = (play.Request, play.Pack, play.Scene, play.Ignore, play.Started, true, true);
    }
    private void OnCutsceneEnded(object? sender, CutsceneEventArgs e)
    {
        if (pendingPlay is not { Launched: true } play || !string.Equals(play.Pack, e.PackUniqueId, StringComparison.OrdinalIgnoreCase) || !string.Equals(play.Scene, e.CutsceneId, StringComparison.OrdinalIgnoreCase)) return;
        pendingPlay = null;
        stopFailedPreview |= e.Unexpected;
        play.Request.Reply(!e.Unexpected && play.StartedObserved
            ? Ok(new JObject { ["Pack"] = play.Pack, ["Cutscene"] = play.Scene, ["Completed"] = true })
            : Fail("Cutscene ended before preview initialization completed. See Runtime diagnostics."));
    }
    public void Update(uint tick)
    {
        statusSaveLoaded = Context.IsWorldReady; statusExporting = IsExporting;
        if (stopFailedPreview) { stopFailedPreview = false; runtime.StopCutscene(); }
        if (tick % 30 == 0) server.PollFiles();
        for (int count = 0; count < 8 && server.TryDequeue(out var request); count++)
        {
            var previousExport = export;
            try { Handle(request!); }
            catch (Exception ex)
            {
                if (!ReferenceEquals(previousExport, export))
                { export?.Dispose(); export = null; exportReply = null; exportProgress = null; }
                request!.Reply(Fail(ex.Message));
            }
        }
        if (pendingRegistration is { } waiting)
        {
            if (!Game1.eventUp && !runtime.IsPlaying) { pendingRegistration = null; RegisterDevPack(waiting.Request); }
            else if (DateTime.UtcNow - waiting.Since > TimeSpan.FromSeconds(5))
            { pendingRegistration = null; waiting.Request.Reply(Fail("The scene that was playing didn't stop in time. Try the preview again.")); }
        }
        if (pendingPlay is { } play)
        {
            if (!Context.IsWorldReady || DateTime.UtcNow - play.Started > TimeSpan.FromSeconds(10))
            { pendingPlay = null; if (play.Launched) runtime.StopCutscene(); play.Request.Reply(Fail("Load a save and close menus first; preview did not become ready.")); }
            else if (play.Launched && play.StartedObserved)
            {
                pendingPlay = null;
                play.Request.Reply(Ok(new JObject { ["Pack"] = play.Pack, ["Cutscene"] = play.Scene }));
            }
            else if (!play.Launched && Context.IsPlayerFree && Game1.CurrentEvent == null && !Game1.globalFade && !Game1.fadeToBlack && !Game1.isWarping)
            {
                pendingPlay = (play.Request, play.Pack, play.Scene, play.Ignore, play.Started, true, false);
                bool ok = runtime.ReloadPack(play.Pack) && runtime.PlayCutscene(play.Pack, play.Scene, play.Ignore);
                if (!ok) { pendingPlay = null; play.Request.Reply(Fail("Could not reload/play the cutscene. See Runtime diagnostics.")); }
            }
        }
        if (export is not { } active) return;
        active.Tick();
        int progress = active.Scheduled == 0 ? 100 : active.Completed * 100 / active.Scheduled;
        if (progress >= nextHud)
        {
            if (Context.IsWorldReady) Game1.addHUDMessage(new HUDMessage($"SDCS export: {progress}%", HUDMessage.newQuest_type));
            var message = new JObject { ["Op"] = "Progress", ["Completed"] = active.Completed, ["Total"] = active.Scheduled, ["Percent"] = progress, ["Final"] = false };
            if (exportProgress != null) exportProgress(message); else server.Publish(message);
            nextHud = (progress / 10 + 1) * 10;
        }
        if (!active.Done) return;
        if (finishing == null)
        {
            // Writing thousands of files and the origin index needs no game state; on the game thread it froze the game at 100%.
            bool publish = publishCatalog;
            finishing = Task.Run(() => active.Finish(publish));
            return;
        }
        if (!finishing.IsCompleted) return;
        JObject result;
        try
        {
            finishing.GetAwaiter().GetResult();
            if (active.Errors.Count == 0) { LastExport = DateTime.UtcNow; exportNote = null; result = Ok(new JObject { ["Folder"] = active.Root, ["Completed"] = active.Completed }); monitor.Log($"Export completed: {active.Completed} jobs, {active.Root}", LogLevel.Info); }
            else result = Fail($"Export encountered {active.Errors.Count} error(s); previous catalog retained. See last-export-error.json.");
        }
        catch (Exception ex) { result = Fail(ex.Message); }
        finally { finishing = null; active.Dispose(); export = null; }
        var reply = exportReply; exportReply = null; exportProgress = null; reply?.Invoke(result);
        if (saveRequested && Context.IsWorldReady) { saveRequested = false; ExportOnce("save"); }
    }

    /// <summary>
    /// The automatic export, on game launch (characters, art) and on the first save load (maps): only when the editor
    /// has none yet (D-47). It never repeats by itself, not even when mods change: the editor shows that the export is
    /// out of date, and the designer asks for a refresh (Ask the game to export again, or sdcs_export).
    /// </summary>
    public void ExportOnce(string phase)
    {
        JObject? catalog = null;
        try { if (File.Exists(Path.Combine(ExportRoot, "catalog.json"))) catalog = JObject.Parse(File.ReadAllText(Path.Combine(ExportRoot, "catalog.json"))); }
        catch (Exception ex) when (ex is IOException or Newtonsoft.Json.JsonException or UnauthorizedAccessException) { monitor.Log($"Couldn't read the existing export: {ex.Message}", LogLevel.Trace); }
        exportNote = catalog == null ? null : ChangedSince(catalog);
        bool exported = phase == "save"
            ? catalog?["Player"] != null && (!config.ExportLocations || catalog["Locations"] != null)
            : catalog?["Characters"] != null && catalog["FarmerParts"] != null;
        if (exported)
        {
            monitor.Log($"The editor's game export already has this ({phase}); not exporting again. {exportNote}", LogLevel.Trace);
            return;
        }
        monitor.Log(phase == "save"
            ? "Exporting this game's maps for the SDCharacterStudio editor. This happens once; the game may pause now and then until it reaches 100%."
            : "Exporting this game's characters and art for the SDCharacterStudio editor. This happens once; the game may pause now and then until it reaches 100%.", LogLevel.Info);
        Export(phase);
    }

    /// <summary>What changed since the export, for the editor to show; null when the game and mods are the same.</summary>
    private string? ChangedSince(JObject catalog)
    {
        try
        {
            var exported = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string modsPath = Path.Combine(ExportRoot, "mods.json");
            if (File.Exists(modsPath))
                foreach (var mod in JArray.Parse(File.ReadAllText(modsPath)).OfType<JObject>())
                    if ((string?)mod["UniqueID"] is { } id) exported[id] = (string?)mod["Version"] ?? "";
            var installed = helper.ModRegistry.GetAll().ToDictionary(m => m.Manifest.UniqueID, m => m.Manifest.Version.ToString(), StringComparer.OrdinalIgnoreCase);
            int added = installed.Keys.Count(id => !exported.ContainsKey(id)), removed = exported.Keys.Count(id => !installed.ContainsKey(id));
            int updated = installed.Count(m => exported.TryGetValue(m.Key, out var version) && version != m.Value);
            bool game = (string?)catalog["Game"]?["Version"] != Game1.version;
            if (added + removed + updated == 0 && !game) return null;
            var parts = new List<string>();
            if (game) parts.Add("the game was updated");
            if (added > 0) parts.Add($"{added} mod{(added == 1 ? "" : "s")} added");
            if (removed > 0) parts.Add($"{removed} removed");
            if (updated > 0) parts.Add($"{updated} updated");
            return $"Your game changed since the export ({string.Join(", ", parts)}). To see that in the editor, use Ask the game to export again.";
        }
        catch (Exception ex) when (ex is IOException or Newtonsoft.Json.JsonException or UnauthorizedAccessException) { return null; }
    }
    private ExportSession Begin(Action<JObject>? reply, bool publish)
    {
        if (export != null) throw new InvalidOperationException("An export is already running.");
        var store = new ExportStore(ExportRoot);
        JObject catalog = File.Exists(store.PathFor("catalog.json")) ? JObject.Parse(File.ReadAllText(store.PathFor("catalog.json"))) : new JObject();
        catalog["CatalogFormat"] = "1.0"; catalog["Profile"] = config.Profile;
        catalog["Game"] = new JObject { ["Version"] = Game1.version, ["SmapiVersion"] = Constants.ApiVersion.ToString() };
        catalog["Mods"] = "mods.json"; catalog["Origins"] = "origins.json";
        string indexPath = Path.Combine(helper.DirectoryPath, "assets", $"vanilla-index-{Game1.version}.json");
        var vanilla = File.Exists(indexPath) ? JObject.Parse(File.ReadAllText(indexPath)) : new JObject();
        if (!File.Exists(indexPath)) monitor.Log($"No vanilla origin index matches game {Game1.version}; dependency origins are uncertain.", LogLevel.Warn);
        var mods = new ModCatalog(helper, monitor);
        var origins = new OriginIndex(vanilla, mods.Sources);
        export = new(helper, monitor, runtime, store, origins, catalog);
        export.Queue("installed mods", () => export.Json("mods.json", mods.Mods));
        exportReply = reply; publishCatalog = publish; nextHud = 10;
        return export;
    }
    public void Export(string phase = "all", Action<JObject>? reply = null)
    {
        phase = phase.ToLowerInvariant();
        if (phase is not ("static" or "save" or "all")) throw new ArgumentException("Phase must be static, save or all.");
        if (phase is "save" or "all" && !Context.IsWorldReady) throw new InvalidOperationException("Load a save before exporting its current appearances and locations.");
        var session = Begin(reply, true);
        if (phase is "static" or "all" || session.Catalog["FarmerParts"] == null) CatalogExporter.ScheduleStatic(session);
        if (phase is "save" or "all")
        { CatalogExporter.ScheduleSave(session); if (config.ExportLocations) LocationExporter.Schedule(session); }
    }
    public void ScheduleSaveExport() { if (IsExporting) saveRequested = true; else ExportOnce("save"); }
    public void ReExportAsset(string asset, Action<JObject>? reply = null)
    {
        if (string.IsNullOrWhiteSpace(asset)) throw new ArgumentException("Asset is required.");
        var session = Begin(reply, true);
        helper.GameContent.InvalidateCache(asset);
        session.ScheduleTexture(asset);
    }
    private void Handle(PreviewRequest request)
    {
        string op = (string?)request.Request["Op"] ?? throw new ArgumentException("Op is required.");
        string Required(string name) => (string?)request.Request[name] is { Length: > 0 } value ? value : throw new ArgumentException(name + " is required.");
        switch (op)
        {
            case "Ping": request.Reply(Ok(new JObject { ["Game"] = Game1.version, ["SaveLoaded"] = Context.IsWorldReady, ["Exporting"] = IsExporting })); break;
            case "Export": Export((string?)request.Request["Phase"] ?? "all", result => request.Reply(result)); exportProgress = message => request.Reply(message, false); break;
            case "ReExportAsset": ReExportAsset(Required("Asset"), result => request.Reply(result)); exportProgress = message => request.Reply(message, false); break;
            case "RegisterDevPack":
            {
                Required("Folder");
                if (pendingRegistration != null) throw new InvalidOperationException("A preview is already getting ready.");
                // A pack can't load while a scene plays: stop our own scene (an earlier preview) first, then register.
                if (runtime.IsPlaying) { runtime.StopCutscene(); pendingRegistration = (request, DateTime.UtcNow); break; }
                if (Game1.eventUp) throw new InvalidOperationException("Another event is playing in the game. Let it finish (or skip it), then preview again.");
                RegisterDevPack(request);
                break;
            }
            case "PlayCutscene":
            {
                string pack = Required("Pack"), scene = Required("Cutscene");
                if (!Context.IsWorldReady || (!Context.IsPlayerFree && Game1.CurrentEvent == null && !runtime.IsPlaying))
                    throw new InvalidOperationException("Load a save and close menus first.");
                if (pendingPlay != null) throw new InvalidOperationException("A preview is already waiting to start.");
                runtime.StopCutscene();
                pendingPlay = (request, pack, scene, (bool?)request.Request["IgnoreConditions"] ?? true, DateTime.UtcNow, false, false);
                break;
            }
            case "StopCutscene":
                if (pendingPlay is { } pending) { pendingPlay = null; pending.Request.Reply(Fail("Preview start was cancelled.")); }
                runtime.StopCutscene(); request.Reply(Ok()); break;
            case "RenderFarmer":
            {
                if (!Context.IsWorldReady) throw new InvalidOperationException("Load a save first.");
                var loadout = ((JObject?)request.Request["Loadout"] ?? new JObject()).Properties().ToDictionary(x => x.Name, x => x.Value.ToString(), StringComparer.OrdinalIgnoreCase);
                if (IsExporting) throw new InvalidOperationException("An export is already running.");
                Farmer? unowned = runtime.CreatePreviewFarmer(loadout);
                try
                {
                    var session = Begin(result => request.Reply(result), false);
                    var fake = unowned;
                    session.RegisterCleanup(() => ReleaseFake(fake)); unowned = null;
                    string folder = FarmerExporter.Schedule(session, fake, false);
                    exportReply = result => { result["PartsFolder"] = folder; request.Reply(result); };
                    exportProgress = message => request.Reply(message, false);
                }
                finally { if (unowned != null) ReleaseFake(unowned); }
                break;
            }
            default: request.Reply(Fail("Unknown operation: " + op)); break;
        }
    }
    private void RegisterDevPack(PreviewRequest request)
    {
        try
        {
            string folder = (string)request.Request["Folder"]!;
            if (!Path.IsPathFullyQualified(folder) || !Directory.Exists(folder)) throw new ArgumentException("Folder must be an existing absolute pack directory.");
            var manifest = JObject.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")));
            string id = (string?)manifest["UniqueID"] ?? throw new ArgumentException("Pack manifest requires UniqueID.");
            var owner = helper.ContentPacks.CreateTemporary(folder, id, (string?)manifest["Name"] ?? id, (string?)manifest["Description"] ?? "SDCS development preview", (string?)manifest["Author"] ?? "Designer", new SemanticVersion((string?)manifest["Version"] ?? "1.0.0")).Manifest;
            bool registered = runtime.RegisterPackFolder(owner, folder);
            request.Reply(registered ? Ok(new JObject { ["Pack"] = id, ["Cutscenes"] = new JArray(runtime.GetCutscenes(id)) })
                : Fail("The game couldn't load the preview copy of the pack. The editor's Output panel has the game's reasons."));
        }
        catch (Exception ex) { request.Reply(Fail(ex.Message)); }
    }

    public void CancelWorldWork()
    {
        if (pendingRegistration is { } waiting) { pendingRegistration = null; waiting.Request.Reply(Fail("Preview cancelled because the world closed.")); }
        saveRequested = false;
        stopFailedPreview = false;
        // An export that is only writing its files needs no world: let it finish (Update completes it).
        if (finishing == null)
        {
            export?.Dispose(); export = null;
            var reply = exportReply; exportReply = null; exportProgress = null; reply?.Invoke(Fail("Export cancelled because the world closed."));
        }
        if (pendingPlay is { } play) { pendingPlay = null; play.Request.Reply(Fail("Preview cancelled because the world closed.")); }
    }
    private void ReleaseFake(Farmer farmer)
    { try { helper.Reflection.GetField<Texture2D?>(farmer.FarmerRenderer, "baseTexture").GetValue()?.Dispose(); } finally { farmer.FarmerRenderer.unload(); } }
    public string Status() => $"Pipe {config.PreviewPipeName}: {(server.IsListening ? "listening" : server.LastError ?? "starting")}; export {ExportRoot}; {(IsExporting ? "running" : "idle")}; last completed {LastExport?.ToString("O") ?? "never"}";
    private static JObject Ok(JObject? value = null) { value ??= new(); value["Ok"] = true; return value; }
    private static JObject Fail(string error) => new() { ["Ok"] = false, ["Error"] = error };
    public void Dispose() { try { finishing?.Wait(TimeSpan.FromSeconds(60)); } catch (AggregateException) { } CancelWorldWork(); runtime.Diagnostic -= OnDiagnostic; runtime.CutsceneStarted -= OnCutsceneStarted; runtime.CutsceneEnded -= OnCutsceneEnded; server.Dispose(); }
}
