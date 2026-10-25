using SDCharacterStudio.Core;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Runtime.Playback;
using StardewModdingAPI;
using StardewValley;
using GameEvent = StardewValley.Event;

namespace SDCharacterStudio.Runtime.Commands;

/// <summary>
/// Change a story number (D-51): the player stat SDCS.&lt;pack&gt;.&lt;name&gt; goes up, down or to a value, never below 0. Checks
/// read it with the game's PLAYER_STAT condition. A skipped scene applies it through <see cref="Ids.SkipNumberAction"/>.
/// </summary>
public sealed class NumberCommand(CommandServices services)
{
    public void Handle(GameEvent ev, string[] args, EventContext context)
    {
        Apply(services.GetScene(args), services.GetBeat<NumberBeat>(args), args[2]);
        ev.CurrentCommand++;
    }

    public void Apply(RegisteredCutscene scene, NumberBeat beat, string beatId)
    {
        string stat = Ids.NumberStat(scene.PackId, beat.Name!);
        int amount = beat.UpTo is { } upTo && upTo > beat.Amount ? Rolls.For(scene.Compiled.Key, beatId, false).Next(beat.Amount, upTo + 1) : beat.Amount;
        int current = (int)Math.Min(int.MaxValue, Game1.player.stats.Get(stat));
        long next = beat.Change switch { "subtract" => (long)current - amount, "set" => amount, _ => (long)current + amount };
        Game1.player.stats.Set(stat, (int)Math.Clamp(next, 0, int.MaxValue));
        services.Monitor.Log($"{stat}: {current} → {Game1.player.stats.Get(stat)}.", LogLevel.Trace);
    }
}
