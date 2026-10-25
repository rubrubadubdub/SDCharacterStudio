using StardewValley;

namespace SDCharacterStudio.Runtime.Animation;

/// <summary>Pose-frame, clip and effect cue sounds stay quiet behind dialogue and menus unless config opts out (doc 04 §12).</summary>
internal static class CueSounds
{
    public static bool MuteDuringDialogue { get; set; } = true;
    public static bool Muted => MuteDuringDialogue && (Game1.activeClickableMenu != null || Game1.dialogueUp);
}
