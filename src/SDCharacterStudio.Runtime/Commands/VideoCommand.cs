using SDCharacterStudio.Core;
using SDCharacterStudio.Runtime.Media;
using SDCharacterStudio.Runtime.Playback;
using StardewValley;
using GameEvent = StardewValley.Event;

namespace SDCharacterStudio.Runtime.Commands;

public sealed class VideoCommand
{
    private readonly CommandServices services;
    private readonly CommandState<VideoMenu> state = new();
    private VideoMenu? ownedMenu;
    public VideoCommand(CommandServices services)
    {
        this.services = services;
        services.Session.Cleaning += () => { ownedMenu?.CloseForCleanup(); ownedMenu = null; state.Clear(); };
    }
    public void Handle(GameEvent ev, string[] args, EventContext context)
    {
        var menu = state.Get(ev, ev.eventCommands, ev.CurrentCommand);
        if (menu != null)
        {
            if (ReferenceEquals(Game1.activeClickableMenu, menu)) return;
            ownedMenu = null;
            state.Clear();
            ev.CurrentCommand++;
            return;
        }
        if (args.Length != 2 || args[1].IndexOf('.') is not > 0) throw new ArgumentException("Expected <packHash>.<videoId>.");
        int dot = args[1].IndexOf('.');
        var scene = services.Registry.All.FirstOrDefault(x => Ids.PackHash(x.PackId) == args[1][..dot])
            ?? throw new ArgumentException($"Unknown video pack hash '{args[1][..dot]}'.");
        var pack = services.Packs.FindPack(scene.PackId) ?? throw new ArgumentException($"Pack '{scene.PackId}' is unavailable.");
        menu = VideoMenu.TryLoad(pack, services.Monitor, args[1][(dot + 1)..]);
        if (menu == null) { ev.CurrentCommand++; return; }
        ownedMenu = menu;
        state.Set(ev, ev.eventCommands, ev.CurrentCommand, menu);
        Game1.activeClickableMenu = menu;
    }
}
