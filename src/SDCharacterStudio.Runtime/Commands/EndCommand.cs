using SDCharacterStudio.Runtime.Playback;
using StardewValley;
using GameEvent = StardewValley.Event;

namespace SDCharacterStudio.Runtime.Commands;

public sealed class EndCommand
{
    private readonly CommandServices services;
    public EndCommand(CommandServices services) => this.services = services;

    public void Handle(GameEvent ev, string[] args, EventContext context)
    {
        // Native end fades conflict with a global fade that is still clearing.
        if (Game1.globalFade) return;
        var scene = services.GetScene(args);
        bool stay = args.Length > 2 && args[2] == "stay" && string.IsNullOrEmpty(Game1.player.locationBeforeForcedEvent.Value) && ReferenceEquals(ev.farmer, Game1.player);
        if (stay)
        {
            // Keep the authored camera through the end fade. Vanilla recentres behind black; moving toward
            // the farmer here would show an unwanted pan before the fade starts.
            // "stay": vanilla 'end' returns the player to where they stood before the scene; its own 'end position'
            // sets the same field, under the same condition (not a forced warp to the scene).
            Game1.player.positionBeforeEvent = ev.farmer.Tile;
            Game1.player.orientationBeforeEvent = ev.farmer.FacingDirection;
        }
        services.Session.End(ev, scene.Compiled.Key);
        ev.CurrentCommand++;
    }

}
