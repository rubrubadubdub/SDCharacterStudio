using SDCharacterStudio.Core.Models;

namespace SDCharacterStudio.Runtime.Rendering;

internal static class RigSelection
{
    public static CharacterRig Select(IEnumerable<CharacterRig> rigs,string? poseRig,string? sourceNpc)
    {
        var available=rigs.ToArray();
        // For selects geometry and masks. A layer naming a different NPC deliberately overrides that choice.
        var selected=poseRig!=null ? available.FirstOrDefault(x=>Same(x.Id,poseRig)) : null;
        if(selected!=null && (sourceNpc==null || Same(selected.Npc,sourceNpc) || Same(selected.Id,sourceNpc)))
            return selected;
        var matches=available.Where(x=>sourceNpc!=null && (Same(x.Npc,sourceNpc)||Same(x.Id,sourceNpc))).ToArray();
        return matches.Length switch
        {
            1=>matches[0],
            0=>throw new ArgumentException($"No rig matches live source '{sourceNpc ?? poseRig}'."),
            _=>throw new ArgumentException($"Multiple rigs match live source '{sourceNpc}'; select a compatible pose For rig.")
        };
    }

    private static bool Same(string? a,string? b)=>string.Equals(a,b,StringComparison.OrdinalIgnoreCase);
}
