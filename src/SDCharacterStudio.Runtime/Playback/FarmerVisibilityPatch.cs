using HarmonyLib;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace SDCharacterStudio.Runtime.Playback;

/// <summary>Vanilla event drawing bypasses Farmer.hidden for the event's farmer.</summary>
internal static class FarmerVisibilityPatch
{
    private static PlaybackSession? session;
    public static void Apply(string owner, PlaybackSession playback)
    {
        session = playback;
        var draw = AccessTools.DeclaredMethod(typeof(Farmer), nameof(Farmer.draw), new[] { typeof(SpriteBatch) });
        new Harmony(owner).Patch(draw, prefix: new HarmonyMethod(typeof(FarmerVisibilityPatch), nameof(BeforeDraw)));
    }
    private static bool BeforeDraw(Farmer __instance,SpriteBatch b)
    {
        if(session?.IsHiddenFarmer(__instance)!=true)return true;
        session.DrawHiddenFarmer?.Invoke(__instance,b);
        return false;
    }
}
