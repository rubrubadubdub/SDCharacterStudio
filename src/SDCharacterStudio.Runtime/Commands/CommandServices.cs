using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Runtime.Loading;
using SDCharacterStudio.Runtime.Playback;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using SDCharacterStudio.Runtime.Animation;

namespace SDCharacterStudio.Runtime.Commands;

public sealed class CommandServices
{
    public CutsceneRegistry Registry { get; }
    public PackLoader Packs { get; }
    public PlaybackSession Session { get; }
    public IMonitor Monitor { get; }
    public AnimationRuntime Animation { get; }
    public MenuOwnership<IClickableMenu> Menus { get; } = new();
    public CommandServices(CutsceneRegistry registry, PackLoader packs, PlaybackSession session, IMonitor monitor, AnimationRuntime animation)
    {
        Registry = registry; Packs = packs; Session = session; Monitor = monitor; Animation=animation;
        void ReleaseMenu()=> Menus.Release(Game1.activeClickableMenu, _ =>
        {
            // closeDialogue advances whichever event is current, which may already be a replacement.
            Game1.exitActiveMenu();
            Game1.dialogueUp = false;
            Game1.isQuestion = false;
            Game1.currentObjectDialogue.Clear();
            Game1.currentSpeaker = null;
            if (!Game1.eventUp && !Game1.isWarping) Game1.player.CanMove = true;
        });
        session.Cleaning+=ReleaseMenu;
        animation.Releasing+=ReleaseMenu;
        session.Cleaning+=StopShake;
    }

    private Farmer? shaking;
    private double shakeLeft;
    /// <summary>Shake the player for a time with the game's own jitter (vanilla 'shake' takes NPCs only).</summary>
    public void ShakeFarmer(Farmer farmer, int milliseconds)
    {
        StopShake();
        if (milliseconds <= 0) return;
        shaking = farmer; shakeLeft = milliseconds;
        farmer.jitterStrength = 1f;
    }
    /// <summary>Called every tick: stops the player's shake when its time is up.</summary>
    public void Update(double milliseconds) { if (shaking != null && (shakeLeft -= milliseconds) <= 0) StopShake(); }
    private void StopShake() { shaking?.stopJittering(); shaking = null; }

    public RegisteredCutscene GetScene(string[] args)
    {
        if (args.Length < 2) throw new ArgumentException("Expected a cutscene key.");
        return Registry.Find(args[1]) ?? throw new ArgumentException($"Unknown cutscene '{args[1]}'.");
    }
    public T GetBeat<T>(string[] args) where T : Beat
    {
        var scene = GetScene(args);
        if (args.Length < 3 || !scene.Compiled.Beats.TryGetValue(args[2], out var info) || info.Beat is not T beat)
            throw new ArgumentException($"Expected a {typeof(T).Name} beat in '{scene.Compiled.Key}'.");
        return beat;
    }
    public static string Text(RegisteredCutscene scene, TextValue? value) => value?.I18n is { } key
        ? scene.Translate(key) ?? throw new ArgumentException($"Missing translation '{key}'.") : value?.Literal ?? "";
}
