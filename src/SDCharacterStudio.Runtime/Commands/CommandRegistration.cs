using StardewModdingAPI;
using StardewValley;
using StardewValley.Delegates;
using GameEvent = StardewValley.Event;
using StardewValley.Triggers;
using SDCharacterStudio.Core;
using SDCharacterStudio.Core.Models;

namespace SDCharacterStudio.Runtime.Commands;

public static class CommandRegistration
{
    public static void Register(CommandServices services)
    {
        Add(EngineNames.Command("Begin"), new BeginCommand(services).Handle);
        Add(EngineNames.Command("End"), new EndCommand(services).Handle);
        Add(EngineNames.Command("Say"), new SayCommand(services).Handle);
        Add(EngineNames.Command("Give"), new GiveCommand(services).Handle);
        Add(EngineNames.Command("Video"), new VideoCommand(services).Handle);
        Add(EngineNames.Command("Sound"), new SoundCommand(services).Handle);
        Add(EngineNames.Command("Move"), new MoveCommand(services).Handle);
        Add(EngineNames.Command("Pose"), new PoseCommand(services).Handle);
        Add(EngineNames.Command("Clip"), new ClipCommand(services).Handle);
        Add(EngineNames.Command("Effect"), new EffectCommand(services).Handle);
        Add(EngineNames.Command("Prop"), new PropCommand(services).Handle);
        Add(EngineNames.Command("Check"), new CheckCommand(services).Handle);
        Add(EngineNames.Command("Chance"), new ChanceCommand(services).Handle);
        Add(EngineNames.Command("Choice"), new ChoiceCommand(services).Handle);
        Add(EngineNames.Command("Message"), new MessageCommand(services).Handle);
        var numbers = new NumberCommand(services);
        Add(EngineNames.Command("Number"), numbers.Handle);
        // A skipped scene still changes its story numbers (OnSkip), like its costumes.
        TriggerActionManager.RegisterAction(Ids.SkipNumberAction,(string[] args,TriggerActionContext context,out string error)=>
        {
            try { numbers.Apply(services.GetScene(args), services.GetBeat<NumberBeat>(args), args[2]); error = null!; return true; }
            catch (Exception ex) { error = ex.Message; services.Monitor.Log($"Skip number failed: {ex}", LogLevel.Error); return false; }
        });
        Add(EngineNames.Command("Camera"), new CameraCommand(services).Handle);
        Add(EngineNames.Command("Costume"), new CostumeCommand(services).Handle);
        Add(EngineNames.Command("Shake"), new ShakeCommand(services).Handle);
        TriggerActionManager.RegisterAction(Ids.SkipCostumeAction,(string[] args,TriggerActionContext context,out string error)=>
        {
            try
            {
                var scene=services.GetScene(args);
                var ev=services.Session.ActiveEvent;
                if(ev==null||!ReferenceEquals(ev,Game1.CurrentEvent)||!ReferenceEquals(scene,services.Session.ActiveScene))
                    throw new InvalidOperationException("Skip costume action doesn't belong to the active SDCS event.");
                services.Animation.SettleClipParts();
                new CostumeCommand(services).Apply(ev,services.GetBeat<CostumeBeat>(args),true);
                error=null!;return true;
            }
            catch(Exception ex){error=ex.Message;services.Monitor.Log($"Skip costume failed: {ex}",LogLevel.Error);return false;}
        });
        void Add(string name, EventCommandDelegate handler) => GameEvent.RegisterCommand(name, (ev, args, context) =>
        {
            try { handler(ev, args, context); }
            catch (Exception ex)
            {
                // A partially-applied Begin still owns snapshots and must release them.
                if (name == EngineNames.Command("Begin")) services.Session.Cleanup(true);
                context.LogErrorAndSkip($"{name} {string.Join(' ', args.Skip(1))}: {ex.Message}");
                services.Monitor.Log(ex.ToString(), LogLevel.Error);
            }
        });
    }
}
