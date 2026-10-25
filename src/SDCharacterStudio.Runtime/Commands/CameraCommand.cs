using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Runtime.Animation;
using SDCharacterStudio.Runtime.Playback;
using StardewValley;
using GameEvent=StardewValley.Event;

namespace SDCharacterStudio.Runtime.Commands;

public sealed class CameraCommand
{
    private readonly CommandServices services;
    private readonly CommandState<CameraMotion> state=new();
    public CameraCommand(CommandServices services)
    {this.services=services;services.Session.Cleaning+=state.Clear;}

    public void Handle(GameEvent ev,string[] args,EventContext context)
    {
        var motion=state.Get(ev,ev.eventCommands,ev.CurrentCommand);
        if(motion==null)
        {
            var beat=services.GetBeat<CameraBeat>(args);
            motion=services.Animation.StartCamera(beat);
            if(!beat.Wait){ev.CurrentCommand++;return;}
            state.Set(ev,ev.eventCommands,ev.CurrentCommand,motion);
        }
        if(!motion.Finished)return;
        state.Clear();ev.CurrentCommand++;
    }
}
