using SDCharacterStudio.Core.Models;
using StardewValley;
using GameEvent = StardewValley.Event;

namespace SDCharacterStudio.Runtime.Commands;

/// <summary>
/// A Choice with answers offered only if… (D-51): <c>SDCS_Choice &lt;scene&gt; &lt;beat&gt; &lt;script per answer&gt;… &lt;after script&gt;</c>.
/// The answers whose conditions are true become the game's own quickQuestion, each switching to its branch; with none,
/// the scene goes on after the Choice.
/// </summary>
public sealed class ChoiceCommand(CommandServices services)
{
    public void Handle(GameEvent ev, string[] args, EventContext context)
    {
        var scene = services.GetScene(args);
        var beat = services.GetBeat<ChoiceBeat>(args);
        if (args.Length != 3 + beat.Options.Count + 1) throw new ArgumentException($"Choice expects {beat.Options.Count + 1} scripts, got {args.Length - 3}.");
        var offered = Enumerable.Range(0, beat.Options.Count)
            .Where(i => string.IsNullOrWhiteSpace(beat.Options[i].When) || GameStateQuery.CheckConditions(beat.Options[i].When, Game1.currentLocation, Game1.player))
            .ToArray();
        // The compiler checked every text (no slashes, #, backslashes or (break)), so the vanilla command takes them as they are.
        ev.InsertNextCommand(offered.Length == 0
            ? "switchEvent " + args[^1]
            : "quickQuestion " + CommandServices.Text(scene, beat.Prompt) + "#" + string.Join("#", offered.Select(i => CommandServices.Text(scene, beat.Options[i].Text)))
              + string.Concat(offered.Select(i => "(break)switchEvent " + args[3 + i])));
        ev.CurrentCommand++;
    }
}
