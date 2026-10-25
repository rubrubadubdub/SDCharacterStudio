using SDCharacterStudio.Core;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Runtime.Playback;
using StardewValley;
using GameEvent = StardewValley.Event;

namespace SDCharacterStudio.Runtime.Commands;

public sealed class GiveCommand
{
    private readonly CommandServices services;
    private readonly CommandState<GiftProgress> state = new();
    private Farmer? holder;
    private GameEvent? holdingEvent;
    private Item? shownItem;
    private Item? previousShownItem;
    public GiveCommand(CommandServices services)
    {
        this.services = services;
        services.Session.Cleaning += () => { ReleaseHoldUp(); state.Clear(); };
    }
    public void Handle(GameEvent ev, string[] args, EventContext context)
    {
        double now = Game1.currentGameTime.TotalGameTime.TotalMilliseconds;
        var progress = state.Get(ev, ev.eventCommands, ev.CurrentCommand);
        if (progress == null)
        {
            var scene = services.GetScene(args);
            var beat = services.GetBeat<Beat>(args);
            // One of a bucket (D-51): picked by weight, the same on a replayed day unless the beat says Fresh.
            string? itemId = (beat as ItemBeat)?.Item;
            int count = (beat as ItemBeat)?.Count ?? 1;
            if (beat is ItemBeat { OneOf: { Count: > 0 } oneOf } bucket)
            {
                // Items whose "only if" isn't true have no chance this time; with none left, nothing is given.
                int pick = Rolls.Pick(oneOf.Select(c => string.IsNullOrWhiteSpace(c.When) || GameStateQuery.CheckConditions(c.When, Game1.currentLocation, Game1.player) ? c.Weight : 0).ToArray(),
                    Rolls.For(args[1], args[2], bucket.Fresh));
                if (pick < 0) { ev.CurrentCommand++; return; }
                itemId = oneOf[pick].Item; count = oneOf[pick].Count;
            }
            if (beat is ItemBeat { HoldUp: false })
            {
                Grant(beat, itemId!, count, scene);
                ev.CurrentCommand++;
                return;
            }
            var (shownId, message) = beat switch
            {
                ItemBeat item => (itemId!, CommandServices.Text(scene, item.Message)),
                MoneyBeat money => ("(O)GoldCoin", CommandServices.Text(scene, money.Message)),
                _ => throw new ArgumentException("Give requires an Item or Money beat.")
            };
            string shownName = ItemRegistry.Create(shownId).DisplayName;
            if (string.IsNullOrEmpty(message)) message = beat is MoneyBeat money ? $"Received {money.Amount:N0}g." : "Received " + shownName + ".";
            message = message.Replace("{item}", shownName, StringComparison.OrdinalIgnoreCase);
            progress = new GiftProgress(now, message);
            state.Set(ev, ev.eventCommands, ev.CurrentCommand, progress);
            progress.GrantOnce(() => Grant(beat, itemId!, count, scene));
            services.Menus.Capture(Game1.activeClickableMenu);
            holder = ev.farmer;
            holdingEvent = ev;
            previousShownItem = holder.mostRecentlyGrabbedItem;
            shownItem = ItemRegistry.Create(shownId);
            holder.holdUpItemThenMessage(shownItem, showMessage: false);
            context.Location.playSound("coin");
            return;
        }
        if (progress.TryShowMessage(now, Game1.activeClickableMenu != null, Game1.dialogueUp))
        {
            Game1.drawObjectDialogue(progress.Message);
            services.Menus.Capture(Game1.activeClickableMenu);
        }
    }
    /// <summary>Give the money or the item; a random item can leave a story flag saying which one.</summary>
    private static void Grant(Beat beat, string itemId, int count, RegisteredCutscene scene)
    {
        if (beat is MoneyBeat money) { Game1.player.Money += money.Amount; return; }
        Game1.player.addItemByMenuIfNecessary(ItemRegistry.Create(itemId, count));
        if (beat is ItemBeat { Remember: true }) Game1.player.mailReceived.Add(Ids.PickedItemFlag(scene.PackId, scene.Source.Id!, itemId));
    }
    private void ReleaseHoldUp()
    {
        // Do not cancel an animation/control state belonging to a replacement event.
        if (holder != null && (Game1.CurrentEvent == null || ReferenceEquals(Game1.CurrentEvent, holdingEvent))
            && ReferenceEquals(holder.mostRecentlyGrabbedItem, shownItem))
        {
            holder.completelyStopAnimatingOrDoingAction();
            holder.freezePause = 0;
            if (ReferenceEquals(holder.mostRecentlyGrabbedItem, shownItem)) holder.mostRecentlyGrabbedItem = previousShownItem;
        }
        holder = null;
        holdingEvent = null;
        shownItem = previousShownItem = null;
    }
}
