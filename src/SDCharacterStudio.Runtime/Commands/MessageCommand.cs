using SDCharacterStudio.Core.Models;
using StardewValley;
using GameEvent = StardewValley.Event;

namespace SDCharacterStudio.Runtime.Commands;

/// <summary>A Message with other lines (D-51): one of them, at random, shown by the game's own message command.</summary>
public sealed class MessageCommand(CommandServices services)
{
    public void Handle(GameEvent ev, string[] args, EventContext context)
    {
        var scene = services.GetScene(args);
        var beat = services.GetBeat<MessageBeat>(args);
        var lines = new[] { beat.Text }.Concat(beat.Or ?? new()).Where(t => t != null).ToArray();
        string text = CommandServices.Text(scene, lines[Game1.random.Next(lines.Length)]);
        // The compiler checked every line (no slashes or straight quotes), so the vanilla command takes it as is.
        ev.InsertNextCommand("message \"" + text + "\"");
        ev.CurrentCommand++;
    }
}
