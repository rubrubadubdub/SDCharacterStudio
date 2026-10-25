using SDCharacterStudio.Runtime.Playback;
using StardewValley;
using GameEvent=StardewValley.Event;

namespace SDCharacterStudio.Runtime.Commands;

public sealed class ClipCommand
{
    private readonly CommandServices services;
    private readonly CommandState<object> state=new();
    public ClipCommand(CommandServices services)
    {this.services=services;services.Session.Cleaning+=state.Clear;}

    public void Handle(GameEvent ev,string[] args,EventContext context)
    {
        if(state.Get(ev,ev.eventCommands,ev.CurrentCommand)==null)
        {
            if(args.Length is <3 or >4 || args.Length==4 && !args[3].Equals("hold",StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Expected SDCS_Clip <key> <clipId> [hold].");
            services.GetScene(args);
            services.Animation.StartClip(args[2],args.Length==4);
            state.Set(ev,ev.eventCommands,ev.CurrentCommand,new object());
        }
        if(!services.Animation.ClipFinished)return;
        state.Clear();ev.CurrentCommand++;
    }
}
