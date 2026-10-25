using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Runtime.Animation;
using SDCharacterStudio.Runtime.Playback;
using StardewValley;
using GameEvent=StardewValley.Event;

namespace SDCharacterStudio.Runtime.Commands;

public sealed class PoseCommand
{
    private readonly CommandServices services;
    private readonly CommandState<Progress> state=new();
    public PoseCommand(CommandServices services)
    {this.services=services;services.Session.Cleaning+=state.Clear;}

    public void Handle(GameEvent ev,string[] args,EventContext context)
    {
        var progress=state.Get(ev,ev.eventCommands,ev.CurrentCommand);
        if(progress==null)
        {
            var beat=services.GetBeat<PoseBeat>(args);
            var actor=beat.Actor ?? throw new ArgumentException("Pose requires an actor.");
            if(beat.Release)
            {
                var stand=services.Animation.Find(actor);
                services.Animation.Release(actor,true);
                if(!beat.Wait || stand==null){ev.CurrentCommand++;return;}
                progress=new(actor,stand,true);
            }
            else
            {
                var stand=services.Animation.SetPose(actor,beat.Pose ?? throw new ArgumentException("Pose ID is required."),beat.Play);
                if(!beat.Wait || beat.Play!="once"){ev.CurrentCommand++;return;}
                progress=new(actor,stand,false);
            }
            state.Set(ev,ev.eventCommands,ev.CurrentCommand,progress);
        }
        bool current=ReferenceEquals(services.Animation.Find(progress.Actor),progress.Stand);
        if(current && (progress.Release || !progress.Stand.Finished))return;
        state.Clear();ev.CurrentCommand++;
    }

    private sealed record Progress(string Actor,StandIn Stand,bool Release);
}
