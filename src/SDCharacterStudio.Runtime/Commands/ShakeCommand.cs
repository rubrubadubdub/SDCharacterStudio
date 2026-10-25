using SDCharacterStudio.Core.Models;
using StardewValley;
using GameEvent = StardewValley.Event;

namespace SDCharacterStudio.Runtime.Commands;

/// <summary>
/// A Shake beat on the player. Vanilla <c>shake</c> takes NPCs only (on the player the game logs "no NPC found with name
/// 'farmer'" and skips it), so this uses the game's own player jitter (<c>startJittering</c>) for the beat's time,
/// without pausing the scene, like an NPC's shake.
/// </summary>
public sealed class ShakeCommand(CommandServices services)
{
    public void Handle(GameEvent ev, string[] args, EventContext context)
    {
        var beat = services.GetBeat<ShakeBeat>(args);
        services.ShakeFarmer(ev.farmer, beat.Duration);
        ev.CurrentCommand++;
    }
}
