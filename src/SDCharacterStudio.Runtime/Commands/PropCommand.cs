using SDCharacterStudio.Core.Models;
using StardewValley;
using GameEvent=StardewValley.Event;

namespace SDCharacterStudio.Runtime.Commands;

public sealed class PropCommand(CommandServices services)
{
    public void Handle(GameEvent ev,string[] args,EventContext context)
    {
        var beat=services.GetBeat<PropBeat>(args);
        // An item placed from a place (D-52) stands where the place is in this game.
        services.Animation.Props.Apply(beat,Game1.currentLocation,services.Session.Places.AbsoluteOrNull(beat.At));
        ev.CurrentCommand++;
    }
}
