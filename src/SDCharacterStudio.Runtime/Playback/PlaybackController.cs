using StardewModdingAPI;
using StardewValley;

namespace SDCharacterStudio.Runtime.Playback;

public sealed class PlaybackController
{
    private readonly CutsceneRegistry registry;
    private readonly PlaybackSession session;
    private readonly IMonitor monitor;
    private readonly Func<bool> enabled;
    private DateTime? pendingSince;
    private PendingWarp? pendingWarp;
    private bool pendingFade;
    private string? pendingKey;
    public bool IsPlaying => session.ActiveEvent != null || pendingSince != null;

    public PlaybackController(CutsceneRegistry registry, PlaybackSession session, IMonitor monitor, Func<bool> enabled)
    { this.registry = registry; this.session = session; this.monitor = monitor; this.enabled = enabled; }

    public bool Play(string packId, string sceneId, bool ignoreConditions)
    {
        try
        {
            if (!enabled() || !Context.IsWorldReady || IsPlaying || Game1.eventUp || Game1.CurrentEvent != null || Game1.activeClickableMenu != null || Game1.dialogueUp
                || Game1.isWarping || Game1.globalFade || Game1.fadeToBlack || !Game1.player.CanMove)
                return Fail("The player is not in a state where a cutscene can start.");
            var scene = registry.Find(packId + "." + sceneId);
            if (scene == null) return Fail($"No loaded cutscene '{packId}.{sceneId}'.");
            var location = Game1.getLocationFromName(scene.Compiled.Location);
            if (location == null) return Fail($"No location '{scene.Compiled.Location}' for {scene.Compiled.Key}.");
            if (ignoreConditions)
            {
                Game1.player.eventsSeen.Remove(scene.Compiled.Key);
                Game1.eventsSeenSinceLastLocationChange.Remove(scene.Compiled.Key);
            }
            bool started = Game1.PlayEvent(scene.Compiled.Key, location, out bool valid, checkPreconditions: !ignoreConditions, checkSeen: !ignoreConditions);
            if (!started) return Fail(valid ? $"Conditions or seen state prevented '{scene.Compiled.Key}'." : $"Event '{scene.Compiled.Key}' was not found in Data/Events/{scene.Compiled.Location}.");
            pendingSince = DateTime.UtcNow;
            pendingKey = scene.Compiled.Key;
            pendingFade = Game1.locationRequest == null;
            if (Game1.locationRequest is { } request)
            {
                var pending = new PendingWarp(scene.Compiled.Key);
                pending.Guard = () =>
                {
                    request.OnLoad -= pending.Guard;
                    // Native PlayEvent adds its event in OnLoad, before resetForPlayerEntry
                    // starts it. Leave the warp intact, but suppress only our cancelled event.
                    if (pending.Cancelled && request.Location.currentEvent?.id == pending.Key)
                        request.Location.currentEvent = null;
                };
                request.OnLoad += pending.Guard;
                pendingWarp = pending;
            }
            return true;
        }
        catch (Exception ex) { monitor.Log($"Could not play '{packId}.{sceneId}': {ex}", LogLevel.Error); return false; }
    }

    public void Update()
    {
        session.Update();
        if (session.ActiveEvent != null) { pendingSince = null; pendingWarp = null; pendingFade = false; pendingKey = null; return; }
        if (pendingSince is not { } since) return;
        var waited = DateTime.UtcNow - since;
        // PlayEvent fades or warps first; once that has settled with no event, something cancelled it.
        bool settled = waited > TimeSpan.FromSeconds(3) && Game1.CurrentEvent == null && !Game1.eventUp && !Game1.isWarping && !Game1.globalFade;
        if (!settled && waited <= TimeSpan.FromSeconds(30)) return;
        monitor.Log($"'{pendingKey}' was started but never began. Another mod may have cancelled it (for example Event Limiter's events-per-day or events-in-a-row limit).", LogLevel.Info);
        CancelPendingStart();
    }
    public void Reset(bool forceRestoreAppearance = false) { CancelPendingStart(); session.Cleanup(true, forceRestoreAppearance); }
    private void CancelPendingStart()
    {
        if (pendingWarp != null) pendingWarp.Cancelled = true;
        pendingWarp = null;
        // Same-location PlayEvent stores its start callback in the global fade. Replacing
        // that owned pending fade with clear prevents a later event after Stop returned.
        if (pendingSince != null && pendingFade && Game1.CurrentEvent == null && Game1.globalFade)
            Game1.globalFadeToClear();
        pendingSince = null;
        pendingFade = false;
        pendingKey = null;
    }
    private sealed class PendingWarp(string key)
    {
        public string Key { get; } = key;
        public bool Cancelled;
        public LocationRequest.Callback? Guard;
    }
    private bool Fail(string message) { monitor.Log(message, LogLevel.Info); return false; }
}
