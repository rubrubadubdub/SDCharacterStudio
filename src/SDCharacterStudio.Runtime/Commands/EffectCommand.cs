using SDCharacterStudio.Core.Models;
using StardewValley;
using GameEvent=StardewValley.Event;

namespace SDCharacterStudio.Runtime.Commands;

public sealed class EffectCommand(CommandServices services)
{
    public void Handle(GameEvent ev,string[] args,EventContext context)
    {services.Animation.Spawn(services.GetBeat<EffectBeat>(args));ev.CurrentCommand++;}
}
