using System.Globalization;
using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core;
using SDCharacterStudio.Core.Models;
using Wpf.Ui.Controls;

namespace SDCharacterStudio.Editor.Schema;

public enum BeatCategory { Dialogue, Movement, Acting, Staging, Rewards, Logic, Flow }

/// <summary>Everything the editor needs to show and create one beat type (doc 03 §6).</summary>
public sealed record BeatKind(string Type, string Name, BeatCategory Category, SymbolRegular Icon, string Help, Func<BeatContext, Beat> Create);

/// <summary>Defaults for a new beat: who's on stage and what's selected.</summary>
public sealed record BeatContext(IReadOnlyList<Actor> Actors, IReadOnlyList<string> Branches, string? PreferredActor)
{
    public string? FirstNpc => PreferredActor != null && Actors.Any(a => a.Id == PreferredActor && a.Kind == "Npc") ? PreferredActor
        : Actors.FirstOrDefault(a => a.Kind == "Npc")?.Id;
    public string AnyActor => PreferredActor ?? FirstNpc ?? "farmer";
}

public static class BeatCatalog
{
    public static readonly IReadOnlyList<BeatKind> All = new BeatKind[]
    {
        new("Say", "Say", BeatCategory.Dialogue, SymbolRegular.Chat24, "A character speaks, with their portrait.", c => new SayBeat { Actor = c.FirstNpc, Text = "…" }),
        new("Message", "Message", BeatCategory.Dialogue, SymbolRegular.TextQuote24, "A plain text box with no speaker.", _ => new MessageBeat { Text = "…" }),
        new("Choice", "Choice", BeatCategory.Dialogue, SymbolRegular.BranchFork24, "The player picks an option; each option jumps to a branch.", _ => new ChoiceBeat()),
        new("Move", "Move", BeatCategory.Movement, SymbolRegular.PersonWalking24, "Walk a character to a tile.", c => new MoveBeat { Actor = c.AnyActor, To = new Position { Tile = new[] { 10, 10 } } }),
        new("Face", "Face", BeatCategory.Movement, SymbolRegular.ArrowClockwise24, "Turn a character to face a direction.", c => new FaceBeat { Actor = c.AnyActor, Facing = "down" }),
        new("Emote", "Emote", BeatCategory.Acting, SymbolRegular.EmojiSmileSlight24, "Show an emote bubble (heart, !, ?…).", c => new EmoteBeat { Actor = c.AnyActor, Emote = "exclamation" }),
        new("Shake", "Shake", BeatCategory.Acting, SymbolRegular.Flash24, "Shake a character in place.", c => new ShakeBeat { Actor = c.AnyActor, Duration = 600 }),
        new("Jump", "Jump", BeatCategory.Acting, SymbolRegular.ArrowBounce24, "Make a character hop.", c => new JumpBeat { Actor = c.AnyActor }),
        new("Pose", "Pose", BeatCategory.Acting, SymbolRegular.Accessibility24, "Play a custom pose from the pack's library (made in the Pose tab).", c => new PoseBeat { Actor = c.FirstNpc ?? c.AnyActor, Play = "loop" }),
        new("Costume", "Costume", BeatCategory.Acting, SymbolRegular.PersonEdit24, "Change the player's clothes or an NPC's sprite.", c => new CostumeBeat { Actor = "farmer", Set = new() { ["Hat"] = "none" } }),
        new("Clip", "Clip", BeatCategory.Acting, SymbolRegular.VideoClipMultiple24, "Play a timeline clip (clips are edited in the timeline, M8).", _ => new ClipBeat()),
        new("Wait", "Wait", BeatCategory.Staging, SymbolRegular.Timer24, "Pause for a moment.", _ => new WaitBeat { Duration = 800 }),
        new("Camera", "Camera", BeatCategory.Staging, SymbolRegular.Camera24, "Move the camera, instantly or smoothly.", _ => new CameraBeat { Center = new Position { Tile = new[] { 10, 10 } }, Duration = 1000, Ease = "inOut" }),
        new("Fade", "Fade", BeatCategory.Staging, SymbolRegular.CircleHalfFill24, "Fade the screen to black or back.", _ => new FadeBeat { To = "black", Duration = 500 }),
        new("Sound", "Sound", BeatCategory.Staging, SymbolRegular.Speaker224, "Play a sound effect.", _ => new SoundBeat { Cue = "dwop" }),
        new("Music", "Music", BeatCategory.Staging, SymbolRegular.MusicNote224, "Change the music.", _ => new MusicBeat { Cue = "none" }),
        new("Prop", "Place an item", BeatCategory.Staging, SymbolRegular.Box24, "Put an item on the map (furniture, a plant, a gift) until the scene ends. Drag one from the browser onto the stage.",
            _ => new PropBeat { Item = "(BC)0", At = new Position { Tile = new[] { 10, 10 } } }),
        new("Effect", "Effect", BeatCategory.Staging, SymbolRegular.Sparkle24, "Spawn an effect: dust, sweat drop, steam, sparkle, hearts, tears.", c => new EffectBeat { Effect = "sweat-drop", Actor = c.FirstNpc ?? c.AnyActor }),
        new("Video", "Video", BeatCategory.Staging, SymbolRegular.MoviesAndTv24, "Play a frame-by-frame video cutaway from the pack.", _ => new VideoBeat()),
        new("Friendship", "Friendship", BeatCategory.Rewards, SymbolRegular.PersonHeart24, "Change friendship (250 points = 1 heart; a spouse gets two thirds).", c => new FriendshipBeat { Npc = c.FirstNpc, Points = 250 }),
        new("Money", "Money", BeatCategory.Rewards, SymbolRegular.Money24, "Give or take gold.", _ => new MoneyBeat { Amount = 500 }),
        new("Item", "Give an item", BeatCategory.Rewards, SymbolRegular.Gift24, "Give the player an item (it goes into their inventory). To show one on the map, use Place an item.", _ => new ItemBeat { Item = "(O)709", Count = 1 }),
        new("Mail", "Story flag", BeatCategory.Rewards, SymbolRegular.Flag24, "Set or clear a mail flag: the story's memory (doc 03 §5.4).", _ => new MailBeat()),
        new("Quest", "Quest", BeatCategory.Rewards, SymbolRegular.BookExclamationMark24, "Start or remove a quest.", _ => new QuestBeat()),
        new("Check", "Check", BeatCategory.Logic, SymbolRegular.BranchCompare24, "Check something (hearts, a story flag, the season, an item…) and go one way or another.",
            c => new CheckBeat { Cases = { new CheckCase { When = $"PLAYER_HEARTS Current {c.FirstNpc ?? "Abigail"} 4" } } }),
        new("Chance", "Chance", BeatCategory.Logic, SymbolRegular.ArrowShuffle24, "Let chance pick what happens next: each outcome has a chance and its own branch.",
            _ => new ChanceBeat { Outcomes = { new ChanceOutcome(), new ChanceOutcome() } }),
        new("Number", "Change a number", BeatCategory.Logic, SymbolRegular.NumberSymbol24, "Add to, take from or set a story number (jokes told, times you helped Robin…); a Check compares it.",
            _ => new NumberBeat { Name = "jokes-told", Change = "add", Amount = 1 }),
        new("Goto", "Go to branch", BeatCategory.Flow, SymbolRegular.ArrowRight24, "Jump to another branch.", c => new GotoBeat { Branch = c.Branches.FirstOrDefault() }),
        new("End", "End scene", BeatCategory.Flow, SymbolRegular.RecordStop24, "End the cutscene here.", _ => new EndBeat()),
        new("Raw", "Raw command", BeatCategory.Flow, SymbolRegular.Code24, "A vanilla event command SDCS doesn't model (advanced).", _ => new RawBeat { Command = "pause 100" }),
    };

    private static readonly Dictionary<string, BeatKind> ByType = All.ToDictionary(k => k.Type, StringComparer.OrdinalIgnoreCase);
    public static BeatKind Kind(string type) => ByType.TryGetValue(type, out var kind) ? kind
        : new BeatKind(type, type, BeatCategory.Flow, SymbolRegular.QuestionCircle24, "Unknown beat type.", _ => new UnknownBeat());

    public static string CategoryName(BeatCategory category) => category switch
    {
        BeatCategory.Dialogue => "Dialogue", BeatCategory.Movement => "Movement", BeatCategory.Acting => "Acting",
        BeatCategory.Staging => "Staging & timing", BeatCategory.Rewards => "Rewards & story flags", BeatCategory.Logic => "Checks & chance", _ => "Flow"
    };

    /// <summary>One line for a storyboard card, in plain words. With the game export, items show their names ("Earmuffs", not "(H)11").</summary>
    public static string Summary(Beat beat, Catalog.ExportCatalog? catalog = null)
    {
        string ItemName(string? id) => id == null ? "?" : catalog?.Item(id)?.Label ?? id;
        string Text(TextValue? t) => t?.I18n != null ? "{" + t.I18n + "}" : Shorten(t?.Literal ?? "", 70);
        // A position from a place (D-52) says so: "(1, 0) from the spouse's room".
        string Tile(Position? p) => p?.Tile is { Length: 2 } t ? $"({t[0]}, {t[1]})" + (Places.IsAnchored(p) ? " from " + Places.Words(p.From) : "") : "(no tile)";
        return beat switch
        {
            SayBeat say => $"{say.Actor ?? "?"}: “{Text(say.Text)}”{Lines(say.Or)}",
            MessageBeat message => $"“{Text(message.Text)}”{Lines(message.Or)}",
            ChoiceBeat choice => choice.Prompt != null ? Text(choice.Prompt) : $"{choice.Options.Count} option{(choice.Options.Count == 1 ? "" : "s")}",
            MoveBeat move => $"{move.Actor ?? "?"} → {Tile(move.To)}{SpeedText(move.Speed)}{(move.Wait ? "" : ", keeps going")}",
            FaceBeat face => $"{face.Actor ?? "?"} faces {face.Facing ?? "?"}",
            EmoteBeat emote => $"{emote.Actor ?? "?"} {EmoteName(emote.Emote)}{(emote.Wait ? "" : ", keeps going")}",
            ShakeBeat shake => $"{shake.Actor ?? "?"} · {shake.Duration} ms",
            JumpBeat jump => $"{jump.Actor ?? "?"}{(jump.Strength != 8 ? $" · strength {jump.Strength}" : "")}{(jump.Silent ? " · silent" : "")}",
            PoseBeat { Release: true } pose => $"{pose.Actor ?? "?"} back to normal{(pose.Wait ? " (waits)" : "")}",
            PoseBeat pose => $"{pose.Actor ?? "?"}: {pose.Pose ?? "(choose a pose)"} · {pose.Play}",
            CostumeBeat costume => CostumeSummary(costume, catalog),
            ClipBeat clip => $"{clip.Clip ?? "(choose a clip)"}{(clip.Hold ? " · hold" : "")}",
            WaitBeat wait => $"{wait.Duration} ms",
            CameraBeat camera => camera.Duration > 0 ? $"to {Tile(camera.Center)} · {camera.Duration} ms" : $"jump to {Tile(camera.Center)}",
            FadeBeat fade => $"to {fade.To ?? "?"} · {fade.Duration} ms",
            SoundBeat sound => $"♪ {sound.Cue ?? "?"}{(sound.Pitch is { } p ? $" · pitch {p}" : "")}",
            MusicBeat music => $"♫ {music.Cue ?? "?"}",
            PropBeat { Remove: true } prop => $"take away {(prop.Item == null ? "what's" : ItemName(prop.Item))} at {Tile(prop.At)}",
            PropBeat prop => $"{ItemName(prop.Item)} at {Tile(prop.At)}",
            EffectBeat effect => $"{effect.Effect ?? "?"}{(effect.Actor != null ? " on " + effect.Actor : effect.At != null ? " at " + Tile(effect.At) : "")}",
            VideoBeat video => $"▶ {video.Video ?? "(choose a video)"}",
            FriendshipBeat friendship => $"{friendship.Npc ?? "?"} {friendship.Points:+#;-#;0} ({Hearts(friendship.Points)})",
            MoneyBeat money => $"{money.Amount:+#,0;-#,0;0}g{(money.HoldUp ? " · hold up" : "")}",
            ItemBeat { OneOf.Count: > 0 } bucket => $"one of {bucket.OneOf!.Count}: {string.Join(" · ", bucket.OneOf.Select(c => $"{ItemName(c.Item)} {Percent(c.Weight, bucket.OneOf.Select(x => x.Weight))}%"))}{(bucket.HoldUp ? " · hold up" : "")}",
            ItemBeat item => $"{item.Count} × {ItemName(item.Item)}{(item.HoldUp ? " · hold up" : "")}",
            NumberBeat number => NumberSummary(number),
            ChanceBeat chance => $"{chance.Outcomes.Count} outcomes: {string.Join(" · ", chance.Outcomes.Select(o => $"{o.Then ?? "?"} {Percent(o.Weight, chance.Outcomes.Select(x => x.Weight))}%"))}",
            MailBeat mail => mail.Add != null ? $"set {Shorten(mail.Add, 60)}" : mail.Remove != null ? $"clear {Shorten(mail.Remove, 60)}" : "(choose a flag)",
            QuestBeat quest => quest.Add != null ? $"start {quest.Add}" : quest.Remove != null ? $"remove {quest.Remove}" : "(choose a quest)",
            CheckBeat { Cases.Count: 1 } check => "if " + Conditions.Describe(check.Cases[0].When, ItemName),
            CheckBeat check => $"{check.Cases.Count} cases, the first true one wins",
            GotoBeat go => $"→ {go.Branch ?? "?"}",
            EndBeat => "the cutscene ends",
            RawBeat raw => Shorten(raw.Command ?? "", 70),
            _ => beat.Type
        };
    }

    /// <summary>"jokes-told +1", "trust −1 to 3", "visits = 0".</summary>
    public static string NumberSummary(NumberBeat number)
    {
        string amount = number.UpTo is { } top && top > number.Amount ? $"{number.Amount} to {top}" : number.Amount.ToString(CultureInfo.InvariantCulture);
        return number.Change switch
        {
            "subtract" => $"{number.Name ?? "?"} −{amount}",
            "set" => $"{number.Name ?? "?"} = {amount}",
            _ => $"{number.Name ?? "?"} +{amount}",
        };
    }

    private static string Lines(List<TextValue>? or) => or is { Count: > 0 } ? $" (1 of {or.Count + 1} lines)" : "";

    /// <summary>A weight as a whole percentage of all the weights (0 when they're all 0).</summary>
    public static int Percent(int weight, IEnumerable<int> weights)
    {
        int total = weights.Sum(w => Math.Max(0, w));
        return total == 0 ? 0 : (int)Math.Round(Math.Max(0, weight) * 100.0 / total);
    }

    public static string CostumeSummary(CostumeBeat costume, Catalog.ExportCatalog? catalog = null)
    {
        string who = costume.Actor ?? "?";
        if (costume.Restore) return $"{who}: back to their own look";
        if (costume.Portrait != null) return $"{who}: portrait {costume.Portrait}";
        if (costume.Sprite?.Asset != null) return $"{who}: sprite {costume.Sprite.Asset}";
        if (costume.Sprite?.Pose != null) return $"{who} puts on {OutfitFactory.Label(costume.Sprite.Pose, who)} (your outfit)";
        var parts = new List<string>();
        if (costume.Preset != null) parts.Add("preset " + costume.Preset);
        // Item slots name the item when the export knows it: "Hat: Earmuffs".
        foreach (var (key, value) in costume.Set ?? new())
        {
            string text = value.Type == JTokenType.String ? (string)value! : value.ToString(Newtonsoft.Json.Formatting.None);
            parts.Add(text == "none" ? $"no {key.ToLowerInvariant()}" : $"{key}: {(catalog?.Item(text)?.Label ?? text)}");
        }
        return $"{who}: {(parts.Count == 0 ? "(nothing set)" : string.Join(", ", parts))}";
    }

    /// <summary>The game gives a spouse two thirds of a friendship change (Farmer.changeFriendship), up or down.</summary>
    public const string SpouseFriendshipNote = "If they're married to the player, the game gives two thirds of this (+80 gives their spouse +52, -250 takes 165).";

    public static string Hearts(int points)
    {
        double hearts = points / 250.0;
        return Math.Abs(hearts) == 1 ? $"{hearts:+0;-0} heart" : $"{hearts.ToString("+0.##;-0.##;0", CultureInfo.InvariantCulture)} hearts";
    }

    public static string EmoteName(JToken? emote)
    {
        string? value = emote?.ToString();
        if (value != null && int.TryParse(value, out int id)) return Ids.Emotes.FirstOrDefault(e => e.Value == id).Key ?? "#" + id;
        return value ?? "?";
    }

    private static string SpeedText(JToken speed) => speed.Type == JTokenType.String && (string?)speed == "normal" ? "" : " · " + speed;
    public static string Shorten(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
