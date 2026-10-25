using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SDCharacterStudio.Runtime.Api;
using StardewModdingAPI;
using StardewValley;
using GameEvent = StardewValley.Event;

namespace SDCharacterStudio.Runtime.Playback;

public sealed class PlaybackSession
{
    private readonly IMonitor monitor;
    private readonly List<ActorSnapshot> actors = new();
    private readonly Dictionary<Character, ActorSnapshot> retainedActors = new();
    private readonly List<(GameEvent Event, string Key)> repeatable = new();
    public GameEvent? ActiveEvent { get; private set; }
    public RegisteredCutscene? ActiveScene { get; private set; }
    /// <summary>Whether this cleanup must restore preview appearance regardless of normal OnEnd retention.</summary>
    public bool ForceRestoreAppearance { get; private set; }
    public event EventHandler<CutsceneEventArgs>? Started;
    public event EventHandler<CutsceneEventArgs>? Ended;
    public event Action? Cleaning;
    public event Action<bool>? ActorsRestored;
    public Func<Character,bool?>? VisibilityOverride { get; set; }
    public Action<Farmer,SpriteBatch>? DrawHiddenFarmer { get; set; }

    public PlaybackSession(IMonitor monitor) => this.monitor = monitor;
    internal bool IsHiddenFarmer(Farmer farmer) => ActiveEvent != null && ReferenceEquals(ActiveEvent, Game1.CurrentEvent)
        && (VisibilityOverride?.Invoke(farmer) ?? actors.Any(x => x.HideDuringScene && ReferenceEquals(x.Actor, farmer)));

    public static Character ResolveActor(GameEvent ev, string id) =>
        string.Equals(id, "farmer", StringComparison.OrdinalIgnoreCase) ? ev.farmer ?? Game1.player
        : ev.actors.FirstOrDefault(x => string.Equals(x.Name, id, StringComparison.OrdinalIgnoreCase))
          ?? throw new ArgumentException($"No event actor named '{id}'.");

    public void Begin(GameEvent ev, RegisteredCutscene scene)
    {
        if (ReferenceEquals(ev, ActiveEvent) && ActiveScene == scene) return;
        Cleanup(true);
        ActiveEvent = ev;
        ActiveScene = scene;
        foreach (var definition in scene.Source.Actors)
        {
            var actor = ResolveActor(ev, definition.Id!);
            var snapshot = ActorSnapshot.Capture(actor, !definition.Visible);
            actors.Add(snapshot);
            var px = definition.Start?.Px;
            if (px is { Length: 2 }) actor.Position += new Vector2(px[0] * 4, px[1] * 4);
            if (!definition.Visible)
            {
                snapshot.SetHidden(true);
            }
            if (definition.Sprite?.Asset is { } asset && actor is NPC spriteNpc)
            {
                // A late asset failure keeps the actor's normal sprite instead of abandoning the remaining setup (doc 12 §4.3).
                try
                {
                    spriteNpc.Sprite.LoadTexture(asset, syncTextureName: false);
                    spriteNpc.spriteOverridden = true;
                }
                catch (Exception ex) { monitor.Log($"{scene.Compiled.Key}: couldn't load sprite '{asset}' for '{definition.Id}'; using the normal sprite. {ex.Message}", LogLevel.Error); }
            }
        }
        Raise(Started, scene, false);
    }

    public void End(GameEvent ev, string key)
    {
        if (ActiveEvent == null) return;
        if (!ReferenceEquals(ev, ActiveEvent) || !string.Equals(ActiveScene?.Compiled.Key, key, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Cannot end inactive cutscene '{key}'.");
        Cleanup(false);
    }

    public void RestoreActor(Character actor)=>actors.FirstOrDefault(x=>ReferenceEquals(x.Actor,actor))?.Restore(true);

    public void Update()
    {
        if (ActiveEvent != null && !ReferenceEquals(Game1.CurrentEvent, ActiveEvent)) Cleanup(true);
        if (ActiveEvent != null)
            foreach (var actor in actors)
            {
                var hidden=VisibilityOverride?.Invoke(actor.Actor);
                if(hidden.HasValue)actor.SetHidden(hidden.Value);
                else if(actor.HideDuringScene)actor.SetHidden(true);
            }
        foreach (var completed in repeatable.Where(x => !ReferenceEquals(x.Event, Game1.CurrentEvent)).ToArray())
        {
            Game1.player.eventsSeen.Remove(completed.Key);
            Game1.eventsSeenSinceLastLocationChange.Remove(completed.Key);
            repeatable.Remove(completed);
        }
    }

    public void Cleanup(bool unexpected, bool forceRestoreAppearance = false)
    {
        var previousForceRestore = ForceRestoreAppearance;
        ForceRestoreAppearance = forceRestoreAppearance;
        try
        {
            var scene = ActiveScene;
            var ev = ActiveEvent;
            bool restoreSprites = forceRestoreAppearance || scene?.Source.OnEnd.RestoreSprites == true;
            ActiveEvent = null;
            ActiveScene = null;
            // Release our menu/state before restoring actors. A failed listener must not stop the remaining cleanup.
            if (Cleaning != null)
                foreach (Action listener in Cleaning.GetInvocationList())
                    try { listener(); } catch (Exception ex) { monitor.Log($"Command cleanup failed: {ex}", LogLevel.Error); }
            foreach (var actor in actors)
                try
                {
                    actor.Restore(restoreSprites);
                    if (!restoreSprites) retainedActors.TryAdd(actor.Actor, actor);
                }
                catch (Exception ex) { monitor.Log($"Could not restore actor '{actor.Actor.Name}': {ex}", LogLevel.Error); }
            if (forceRestoreAppearance)
            {
                // The earliest skipped baseline survives natural OnEnd=false completion and
                // subsequent scenes, so an explicit preview stop can return to the original look.
                foreach (var actor in retainedActors.Values)
                    try { actor.Restore(true); }
                    catch (Exception ex) { monitor.Log($"Could not restore retained actor '{actor.Actor.Name}': {ex}", LogLevel.Error); }
                retainedActors.Clear();
            }
            if(ActorsRestored!=null)
                foreach(Action<bool> listener in ActorsRestored.GetInvocationList())
                    try{listener(restoreSprites);}catch(Exception ex){monitor.Log($"Actor cleanup failed: {ex}",LogLevel.Error);}
            actors.Clear();
            if (scene == null || ev == null) return;
            if (scene.Source.Trigger?.Once == false)
            {
                Game1.player.eventsSeen.Remove(scene.Compiled.Key);
                Game1.eventsSeenSinceLastLocationChange.Remove(scene.Compiled.Key);
                repeatable.Add((ev, scene.Compiled.Key));
            }
            if (unexpected) monitor.Log($"{scene.Compiled.Key}: cleanup after unexpected end", LogLevel.Info);
            if (!Game1.eventUp && !Game1.isWarping) Game1.player.CanMove = true;
            Raise(Ended, scene, unexpected);
        }
        finally { ForceRestoreAppearance = previousForceRestore; }
    }

    private void Raise(EventHandler<CutsceneEventArgs>? handlers, RegisteredCutscene scene, bool unexpected)
    {
        if (handlers == null) return;
        var args = new CutsceneEventArgs(scene.PackId, scene.Source.Id!, unexpected);
        foreach (EventHandler<CutsceneEventArgs> handler in handlers.GetInvocationList())
            try { handler(this, args); } catch (Exception ex) { monitor.Log($"Cutscene API subscriber failed: {ex}", LogLevel.Error); }
    }

    private sealed record ActorSnapshot(Character Actor, bool Hidden, Texture2D? Texture, Texture2D? Portrait, bool SpriteOverridden, bool PortraitOverridden, string? OverrideTexture, string? LoadedTexture, string? TextureName, bool HideDuringScene)
    {
        public static ActorSnapshot Capture(Character actor, bool hideDuringScene) => actor is NPC npc
            ? new(actor, npc.IsInvisible, npc.Sprite.Texture, npc.Portrait, npc.spriteOverridden, npc.portraitOverridden, npc.Sprite.overrideTextureName, npc.Sprite.loadedTexture, npc.Sprite.textureName.Value, hideDuringScene)
            : new(actor, ((Farmer)actor).hidden.Value, null, null, false, false, null, null, null, hideDuringScene);
        public void SetHidden(bool value)
        {
            if (Actor is Farmer farmer)
            {
                farmer.hidden.Value = value;
            }
            else if (Actor is NPC npc) npc.IsInvisible = value;
        }
        public void Restore(bool restoreSprites)
        {
            if (Actor is Farmer farmer)
            {
                farmer.hidden.Value = Hidden;
            }
            else if (Actor is NPC npc)
            {
                npc.IsInvisible = Hidden;
                if (restoreSprites)
                {
                    npc.Sprite.overrideTextureName = OverrideTexture;
                    npc.Sprite.textureName.Value = TextureName;
                    npc.Sprite.spriteTexture = Texture!;
                    npc.Sprite.loadedTexture = LoadedTexture;
                    npc.Portrait = Portrait!;
                    npc.spriteOverridden = SpriteOverridden;
                    npc.portraitOverridden = PortraitOverridden;
                    Game1.getCharacterFromName(npc.Name)?.ChooseAppearance();
                }
            }
        }
    }
}
