using StardewValley;

namespace SDCharacterStudio.Runtime.Api;

/// <summary>Interrupts native event waits/dialogue before direct exit, without applying OnSkip actions.</summary>
internal static class PreviewEventExit
{
    public static void Exit(Event? current)
    {
        if (current == null) return;
        // Native skipEvent clears these before ending. Direct exitEvent omits them: a
        // remaining pause blocks ScreenFade's exit-warp update once eventUp becomes false.
        Game1.exitActiveMenu();
        Game1.fadeClear();
        Game1.dialogueUp = false;
        Game1.dialogueTyping = false;
        Game1.messagePause = false;
        Game1.isQuestion = false;
        Game1.currentObjectDialogue.Clear();
        Game1.currentSpeaker = null;
        Game1.messageAfterPause = "";
        Game1.pauseTime = 0f;
        current.exitEvent();
    }
}
