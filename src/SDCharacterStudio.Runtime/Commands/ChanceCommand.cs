using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Runtime.Playback;
using StardewModdingAPI;
using StardewValley;
using GameEvent = StardewValley.Event;

namespace SDCharacterStudio.Runtime.Commands;

/// <summary>
/// A Chance (D-51): <c>SDCS_Chance &lt;scene&gt; &lt;beat&gt; &lt;script per outcome&gt;…</c>. One outcome is picked by its weight
/// (the same on a replayed day unless the beat says Fresh) and the event switches to its branch's script.
/// </summary>
public sealed class ChanceCommand(CommandServices services)
{
    public void Handle(GameEvent ev, string[] args, EventContext context)
    {
        var beat = services.GetBeat<ChanceBeat>(args);
        if (args.Length != 3 + beat.Outcomes.Count) throw new ArgumentException($"Chance expects {beat.Outcomes.Count} scripts, got {args.Length - 3}.");
        int way = Rolls.Pick(beat.Outcomes.Select(o => o.Weight).ToArray(), Rolls.For(args[1], args[2], beat.Fresh));
        if (way < 0) throw new ArgumentException("Every outcome has a chance of 0.");
        services.Monitor.Log($"{args[1]} {args[2]}: outcome {way + 1} ({beat.Outcomes[way].Then}).", LogLevel.Trace);
        ev.InsertNextCommand("switchEvent " + args[3 + way]);
        ev.CurrentCommand++;
    }
}
