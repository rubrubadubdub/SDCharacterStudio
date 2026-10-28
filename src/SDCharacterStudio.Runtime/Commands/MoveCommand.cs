using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Runtime.Playback;
using StardewValley;
using GameEvent = StardewValley.Event;

namespace SDCharacterStudio.Runtime.Commands;

public sealed class MoveCommand(CommandServices services)
{
    public void Handle(GameEvent ev, string[] args, EventContext context)
    {
        var beat = services.GetBeat<MoveBeat>(args);
        var actor = PlaybackSession.ResolveActor(ev, beat.Actor!);
        // A walk to a place (D-52) goes to where the place is in this game.
        var commands = MovePlanner.Plan(beat, actor.TilePoint.X, actor.TilePoint.Y, services.Session.Places.AbsoluteOrNull(beat.To)?.Tile);
        // InsertNextCommand always inserts at CurrentCommand + 1.
        foreach (string command in commands.Reverse()) ev.InsertNextCommand(command);
        ev.CurrentCommand++;
    }
}
