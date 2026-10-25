using SDCharacterStudio.Core.Models;
using StardewModdingAPI;
using StardewValley;
using GameEvent = StardewValley.Event;

namespace SDCharacterStudio.Runtime.Commands;

/// <summary>
/// A Check (D-51): <c>SDCS_Check &lt;scene&gt; &lt;beat&gt; &lt;script per case&gt;… &lt;otherwise script&gt;</c>. The cases are the
/// game's own conditions (game state queries), checked in order; the first true one switches to its branch's script,
/// none true to the otherwise script (its branch, the beats after the Check, or the scene's end).
/// </summary>
public sealed class CheckCommand(CommandServices services)
{
    public void Handle(GameEvent ev, string[] args, EventContext context)
    {
        var beat = services.GetBeat<CheckBeat>(args);
        if (args.Length != 3 + beat.Cases.Count + 1) throw new ArgumentException($"Check expects {beat.Cases.Count + 1} scripts, got {args.Length - 3}.");
        int way = beat.Cases.FindIndex(c => GameStateQuery.CheckConditions(c.When, Game1.currentLocation, Game1.player));
        if (way < 0) way = beat.Cases.Count;
        services.Monitor.Log($"{args[1]} {args[2]}: {(way < beat.Cases.Count ? $"case {way + 1} is true" : "no case is true, otherwise")}.", LogLevel.Trace);
        // InsertNextCommand puts it right after this command, which runs next.
        ev.InsertNextCommand("switchEvent " + args[3 + way]);
        ev.CurrentCommand++;
    }
}
