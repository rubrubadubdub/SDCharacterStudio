using SDCharacterStudio.Core;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Runtime.Playback;
using StardewValley;
using SDCharacterStudio.Runtime.Rendering;
using StardewValley.Menus;
using GameEvent = StardewValley.Event;

namespace SDCharacterStudio.Runtime.Commands;

public sealed class SayCommand
{
    private readonly CommandServices services;
    private readonly CommandState<object> state = new();
    private NPC? speaker;
    private Dialogue? dialogue;
    private IClickableMenu? menu;
    private GameEvent? ownerEvent;
    private object? expressionOwner;
    private string? actor,portraitId;
    public SayCommand(CommandServices services)
    {
        this.services = services;
        services.Animation.Releasing += () => { RemoveOwnDialogue(); state.Clear(); };
        services.Animation.Updating += _=>UpdateLine();
    }
    public void Handle(GameEvent ev, string[] args, EventContext context)
    {
        if (state.Get(ev, ev.eventCommands, ev.CurrentCommand) != null || Game1.dialogueUp) return;
        var scene = services.GetScene(args);
        var beat = services.GetBeat<SayBeat>(args);
        if (PlaybackSession.ResolveActor(ev, beat.Actor!) is not NPC npc) throw new ArgumentException("Say requires an NPC actor.");
        Ids.TryEmotion(beat.Emotion?.ToString(), out string emotion);
        RemoveOwnDialogue();
        try
        {
            speaker = npc;actor=beat.Actor;ownerEvent=ev;portraitId=beat.Portrait;
            if(beat.Expression!=null)
            {
                expressionOwner=new object();
                services.Animation.SetExpression(actor!,beat.Expression,expressionOwner);
            }
            // One of several lines, at random (D-51).
            var lines = new[] { beat.Text }.Concat(beat.Or ?? new()).Where(t => t != null).ToArray();
            dialogue = new Dialogue(npc, null, CommandServices.Text(scene, lines.Length > 1 ? lines[Game1.random.Next(lines.Length)] : beat.Text) + emotion);
            UpdatePortrait();
            state.Set(ev, ev.eventCommands, ev.CurrentCommand, new object());
            npc.CurrentDialogue.Push(dialogue);
            Game1.drawDialogue(npc);
            menu=Game1.activeClickableMenu;
            services.Menus.Capture(menu);
        }
        catch{RemoveOwnDialogue();throw;}
        // Vanilla's dialogue close advances CurrentCommand.
    }
    private void RemoveOwnDialogue()
    {
        if(expressionOwner!=null&&actor!=null&&services.Session.ActiveEvent!=null)services.Animation.ClearExpression(actor,expressionOwner);
        if(dialogue!=null)dialogue.overridePortrait=null;
        if (speaker?.CurrentDialogue.TryPeek(out var current) == true && ReferenceEquals(current, dialogue)) speaker.CurrentDialogue.Pop();
        speaker = null;
        dialogue = null;
        menu=null;ownerEvent=null;expressionOwner=null;actor=null;portraitId=null;
    }
    private void UpdateLine()
    {
        if(dialogue==null)return;
        if(menu==null||!ReferenceEquals(Game1.activeClickableMenu,menu)||!ReferenceEquals(Game1.CurrentEvent,ownerEvent)||!Game1.dialogueUp)
        {RemoveOwnDialogue();return;}
        UpdatePortrait();
    }
    private void UpdatePortrait()
    {
        if(dialogue==null||speaker==null)return;
        var active=services.Animation.ExpressionFor(speaker);
        var expression=active?.Definition.Target.Equals("Portrait",StringComparison.OrdinalIgnoreCase)==true?active:null;
        string? id=portraitId??(expression!=null?services.Animation.PortraitCostumes.Definition(speaker):null);
        dialogue.overridePortrait=id!=null?services.Animation.ComposePortrait(id,expression).Texture
            :expression!=null?services.Animation.ComposeLivePortrait(speaker,expression).Texture:null;
    }
}
