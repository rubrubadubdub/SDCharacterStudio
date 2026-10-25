using SDCharacterStudio.Core.Models;
using StardewValley;
using GameEvent=StardewValley.Event;

namespace SDCharacterStudio.Runtime.Commands;

public sealed class PropCommand(CommandServices services)
{
    public void Handle(GameEvent ev,string[] args,EventContext context)
    {services.Animation.Props.Apply(services.GetBeat<PropBeat>(args),Game1.currentLocation);ev.CurrentCommand++;}
}
