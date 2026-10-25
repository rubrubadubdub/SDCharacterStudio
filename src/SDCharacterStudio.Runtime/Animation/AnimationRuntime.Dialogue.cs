using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;

namespace SDCharacterStudio.Runtime.Animation;

public sealed partial class AnimationRuntime
{
    private DialogueBox? observedMenu;
    private Dialogue? observedDialogue;
    private Texture2D? originalDialoguePortrait,assignedDialoguePortrait;
    private void UpdateObservedDialogue()
    {
        if(Game1.activeClickableMenu is not DialogueBox box){ClearObservedDialogue();return;}
        var dialogue=Reflection.GetField<Dialogue>(box,"characterDialogue").GetValue();
        var expression=dialogue?.speaker is{} speaker?ExpressionFor(speaker):null;
        if(expression?.Definition.Target.Equals("Portrait",StringComparison.OrdinalIgnoreCase)!=true)
        {ClearObservedDialogue();return;}
        if(dialogue!.overridePortrait!=null&&!ReferenceEquals(dialogue.overridePortrait,assignedDialoguePortrait))
        {ClearObservedDialogue();return;}
        if(!ReferenceEquals(observedMenu,box))
        {
            ClearObservedDialogue();observedMenu=box;observedDialogue=dialogue;
            originalDialoguePortrait=dialogue!.overridePortrait;
        }
        string? id=PortraitCostumes.Definition(dialogue!.speaker);
        // SDCS_Say handles explicit portrait definitions itself. Native speak needs a live-source sheet.
        assignedDialoguePortrait=id!=null?ComposePortrait(id,expression).Texture:ComposeLivePortrait(dialogue.speaker,expression).Texture;
        dialogue.overridePortrait=assignedDialoguePortrait;
    }
    private void ClearObservedDialogue()
    {
        if(observedDialogue!=null&&assignedDialoguePortrait!=null&&ReferenceEquals(observedDialogue.overridePortrait,assignedDialoguePortrait))
            observedDialogue.overridePortrait=originalDialoguePortrait;
        observedMenu=null;observedDialogue=null;originalDialoguePortrait=null;assignedDialoguePortrait=null;
    }
    private void ReleaseObservedDialogue()
    {
        var menu=assignedDialoguePortrait!=null?observedMenu:null;var dialogue=observedDialogue;
        ClearObservedDialogue();
        if(menu==null||!ReferenceEquals(Game1.activeClickableMenu,menu))return;
        Game1.exitActiveMenu();Game1.dialogueUp=false;Game1.isQuestion=false;
        if(dialogue?.speaker.CurrentDialogue.TryPeek(out var current)==true&&ReferenceEquals(current,dialogue))dialogue.speaker.CurrentDialogue.Pop();
    }
}
