using StardewValley;
using GameEvent = StardewValley.Event;

namespace SDCharacterStudio.Runtime.Commands;

public sealed class BeginCommand(CommandServices services)
{
    public void Handle(GameEvent ev, string[] args, EventContext context)
    {
        services.Session.Begin(ev, services.GetScene(args));
        services.Animation.Begin(ev,services.GetScene(args));
        // PlayEvent starts the event before its entry fade has finished.
        if (Game1.globalFade) return;
        ev.CurrentCommand++;
    }
}
