using SDCharacterStudio.Core.Models;
using StardewValley;
using GameEvent = StardewValley.Event;

namespace SDCharacterStudio.Runtime.Commands;

public sealed class SoundCommand(CommandServices services)
{
    public void Handle(GameEvent ev, string[] args, EventContext context)
    {
        var beat = services.GetBeat<SoundBeat>(args);
        context.Location.playSound(beat.Cue!, pitch: beat.Pitch);
        ev.CurrentCommand++;
    }
}
