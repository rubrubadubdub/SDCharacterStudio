using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Runtime.Animation;
using SDCharacterStudio.Runtime.Playback;
using StardewValley;
using GameEvent = StardewValley.Event;

namespace SDCharacterStudio.Runtime.Commands;

public sealed class EndCommand
{
    private readonly CommandServices services;
    /// <summary>The camera gliding back to the player before a "stay" ending.</summary>
    private readonly CommandState<CameraMotion> glide = new();

    public EndCommand(CommandServices services) { this.services = services; services.Session.Cleaning += glide.Clear; }

    public void Handle(GameEvent ev, string[] args, EventContext context)
    {
        // Native end fades conflict with a global fade that is still clearing.
        if (Game1.globalFade) return;
        var scene = services.GetScene(args);
        bool stay = args.Length > 2 && args[2] == "stay" && string.IsNullOrEmpty(Game1.player.locationBeforeForcedEvent.Value) && ReferenceEquals(ev.farmer, Game1.player);
        if (stay)
        {
            // The game recentres on the player after its end fade: if the scene panned away, glide back first so
            // the view after the fade is the view before it, with no leap.
            var motion = glide.Get(ev, ev.eventCommands, ev.CurrentCommand);
            if (motion == null && GlideBack(ev.farmer) is { } started) { motion = started; glide.Set(ev, ev.eventCommands, ev.CurrentCommand, motion); }
            if (motion is { Finished: false }) return;
            glide.Clear();
            // "stay": vanilla 'end' returns the player to where they stood before the scene; its own 'end position'
            // sets the same field, under the same condition (not a forced warp to the scene).
            Game1.player.positionBeforeEvent = ev.farmer.Tile;
            Game1.player.orientationBeforeEvent = ev.farmer.FacingDirection;
        }
        services.Session.End(ev, scene.Compiled.Key);
        ev.CurrentCommand++;
    }

    /// <summary>A smooth pan back onto the player, or null when the camera is already there (within a tile).</summary>
    private CameraMotion? GlideBack(Farmer farmer)
    {
        if (Game1.currentLocation?.Map == null) return null;
        var tile = farmer.TilePoint;
        var target = CameraMotion.Clamp(new WorldPoint(tile.X * 64 + 32, tile.Y * 64 + 32));
        double distance = Math.Abs(target.X - Game1.viewport.X) + Math.Abs(target.Y - Game1.viewport.Y);
        if (distance <= 64) return null;
        int ms = (int)Math.Clamp(distance / 64 * 120, 300, 1000);
        return services.Animation.StartCamera(new CameraBeat { Center = new Position { Tile = new[] { tile.X, tile.Y } }, Duration = ms, Ease = "inOut" });
    }
}
