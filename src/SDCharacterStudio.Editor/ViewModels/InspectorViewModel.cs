using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Export;
using SDCharacterStudio.Editor.Infrastructure;
using SDCharacterStudio.Editor.Project;
using SDCharacterStudio.Editor.Schema;
using Wpf.Ui.Controls;

namespace SDCharacterStudio.Editor.ViewModels;

/// <summary>The Properties panel (doc 07 §5): context-sensitive editors for the selected scene, actor, beat, branch or video.</summary>
public sealed partial class InspectorViewModel : ObservableObject
{
    private readonly MainViewModel shell;
    private string problemPrefix = "";
    public InspectorViewModel(MainViewModel shell) => this.shell = shell;

    public ObservableCollection<FieldViewModel> Fields { get; } = new();
    [ObservableProperty] private string title = "";
    [ObservableProperty] private string? subtitle;
    [ObservableProperty] private SymbolRegular icon = SymbolRegular.Settings24;
    [ObservableProperty] private string? help;
    [ObservableProperty] private string? category;

    private FieldCommit Commit => (label, apply, rebuild) => shell.EditFromInspector(label, apply, rebuild);
    private Cutscene? Scene => shell.Scene;
    private ExportCatalog? Catalog => shell.Catalog;

    public void Rebuild()
    {
        Fields.Clear(); Help = null; Subtitle = null; Category = null;
        var scene = Scene;
        if (shell.Document == null || scene == null) { Title = "Nothing selected"; Icon = SymbolRegular.Info24; return; }
        switch (shell.Selection)
        {
            case BeatSelection b when LaneNames.Get(scene, b.Lane) is { } list && b.Index < list.Count: BuildBeat(scene, b, list[b.Index]); break;
            case ActorSelection a when a.Index < scene.Actors.Count: BuildActor(scene, a.Index); break;
            case BranchSelection br: BuildBranch(scene, br.Lane); break;
            case VideoSelection v when shell.Document.Videos.FirstOrDefault(x => x.Id == v.Video) is { } video: BuildVideo(video); break;
            default: BuildScene(scene); break;
        }
        if (shell.LastValidation != null) ApplyProblems(shell.LastValidation.Problems);
    }

    /// <summary>After an inline edit the editors stay (keeping keyboard focus); they read the models live, so nothing to redraw.</summary>
    public void RefreshValues() { }

    public void ApplyProblems(IReadOnlyList<Problem> problems)
    {
        string? file = shell.Current?.File;
        var mine = problems.Where(p => string.Equals(p.File, file, StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (var field in Fields)
        {
            if (field.Key.Length == 0) continue;
            string path = problemPrefix.Length == 0 ? field.Key : problemPrefix + "." + field.Key;
            var hit = mine.Where(p => p.JsonPath == path || p.JsonPath.StartsWith(path + ".", StringComparison.Ordinal) || p.JsonPath.StartsWith(path + "[", StringComparison.Ordinal)).ToArray();
            field.Problem = hit.Length == 0 ? null : string.Join("\n", hit.Select(p => p.Message).Distinct());
            field.ProblemIsError = hit.Any(p => p.IsError);
        }
        // Problems on the selected object itself (e.g. "Unknown beat type") show as a note.
        var own = mine.Where(p => p.JsonPath == problemPrefix && problemPrefix.Length > 0).ToArray();
        Help = own.Length > 0 ? string.Join("\n", own.Select(p => p.Message)) : Help;
    }

    // ================= scene =================

    private void BuildScene(Cutscene scene)
    {
        Title = scene.Title ?? scene.Id ?? "Cutscene"; Subtitle = "Cutscene settings"; Icon = SymbolRegular.MoviesAndTv24; problemPrefix = "";
        Fields.Add(new SectionField("Cutscene"));
        Fields.Add(new TextField("Title", "Title", () => scene.Title, v => scene.Title = v, Commit) { Placeholder = "What happens, in a few words", Help = "Only shown in the editor." });
        Fields.Add(new TextField("ID", "Id", () => scene.Id, v => scene.Id = PackDocument.ToContentId(v ?? scene.Id ?? "scene"), Commit)
        { AllowNull = false, Rebuild = true, Help = "Used in the event ID (<pack>.<id>). Changing it after release makes the scene count as unseen again." });
        Fields.Add(new ChoiceField("Location", "Location", LocationOptions(), () => scene.Location, v => scene.Location = v, Commit) { IsEditable = true, Help = "Where the scene plays. The stage shows this map." });
        MusicFields("Music", () => scene.Music, v => scene.Music = v ?? "none");
        Fields.Add(new BoolField("Camera follows the player", "Camera", () => scene.Camera?.Type == JTokenType.String, v => scene.Camera = v ? "follow" : new JObject { ["Tile"] = new JArray(10, 10) }, Commit) { Rebuild = true });
        if (scene.Camera?.Type != JTokenType.String)
            Fields.Add(Tile("Camera centre", "Camera.Tile", () => (scene.Camera as JObject)?["Tile"]?.ToObject<int[]>() ?? scene.Actors.FirstOrDefault()?.Start?.Tile,
                t => scene.Camera = new JObject { ["Tile"] = new JArray(t[0], t[1]) }));
        Fields.Add(new BoolField("Skippable", "Skippable", () => scene.Skippable, v => scene.Skippable = v, Commit)
        { Description = "The player can skip it. Add story flags or rewards to the \"If skipped\" lane so skipping still counts.", Rebuild = true });

        Fields.Add(new SectionField("When does it play?"));
        Fields.Add(new ChoiceField("Starts", "Trigger", new[]
        {
            new ChoiceItem("auto", "By itself, when the player enters the location", "and the conditions below are true"),
            new ChoiceItem("manual", "Only when started by preview or another mod", "never by itself")
        }, () => scene.Trigger == null ? "manual" : "auto", v => scene.Trigger = v == "auto" ? scene.Trigger ?? new Trigger() : null, Commit) { Rebuild = true });
        if (scene.Trigger is { } trigger)
        {
            // One list of conditions: the same rows as a Check beat (D-51), stored as the game's condition text.
            Fields.Add(new ConditionGroupField("Conditions", "Trigger.Conditions", () => trigger.Conditions, v => trigger.Conditions = v, ConditionChoices(), Commit)
            { Help = "Optional. The scene plays only when these are true: hearts, story flags, the season, an installed mod…", EmptyText = "No conditions: plays the first time the player enters." });
            // The game's older event preconditions still work; they show only on scenes that use them.
            if (trigger.Preconditions.Count > 0) Fields.Add(Preconditions(trigger));
            else Fields.Add(new ActionField("Add an event precondition (older style)", () => Commit("Add condition", () => trigger.Preconditions.Add("F"), true)));
            Fields.Add(new BoolField("Play only once", "Trigger.Once", () => trigger.Once, v => trigger.Once = v, Commit));
            Fields.Add(new TextField("Wait a day after flag", "Trigger.OnDayAfterFlag", () => trigger.OnDayAfterFlag, v => trigger.OnDayAfterFlag = v, Commit)
            { Placeholder = "optional story flag", Help = "Plays on a later day than the one this flag was set on (doc 04 §4.7)." });
        }

        Fields.Add(new SectionField("When it ends"));
        Fields.Add(new BoolField("When it ends, the player's own clothes come back", "OnEnd.RestoreCostume", () => scene.OnEnd.RestoreCostume, v => scene.OnEnd.RestoreCostume = v, Commit)
        { Help = "Off: costumes put on during the scene stay on afterwards." });
        Fields.Add(new BoolField("Restore NPC sprites and portraits", "OnEnd.RestoreSprites", () => scene.OnEnd.RestoreSprites, v => scene.OnEnd.RestoreSprites = v, Commit));
        Fields.Add(new ChoiceField("Afterwards", "OnEnd.End", new[]
        {
            new ChoiceItem("stay", "The player stays where the scene left them"),
            new ChoiceItem("default", "The player goes back where they were before the scene"),
            new ChoiceItem("warpOut", "Leave the building (warp out)"),
            new ChoiceItem("bed", "Go to bed"),
        }, () => scene.OnEnd.End, v => scene.OnEnd.End = v ?? "default", Commit) { IsEditable = true, Help = "Or type a vanilla end command, e.g. position 5 10." });

        Fields.Add(new SectionField("Branches", "A branch is a path a choice can lead to."));
        Fields.Add(new ActionField("New branch", () => shell.Storyboard.AddBranch()));
    }

    private ListField Preconditions(Trigger trigger)
    {
        var field = new ListField("Event preconditions (older style, all must be true)", "Trigger.Preconditions", "Add condition", () => Commit("Add condition", () => trigger.Preconditions.Add("F"), true))
        { EmptyText = "No conditions: plays the first time the player enters.", Help = "The game's own event preconditions. The Conditions rows above do the same and more." };
        for (int i = 0; i < trigger.Preconditions.Count; i++)
        {
            int index = i;
            var row = new ListRowViewModel(() => Commit("Remove condition", () => trigger.Preconditions.RemoveAt(index), true));
            foreach (var cell in PreconditionEditor.Cells(trigger.Preconditions[index], value => trigger.Preconditions[index] = value, Commit, NpcOptions()))
                row.Cells.Add(cell);
            field.Rows.Add(row);
        }
        return field;
    }

    // ================= actor =================

    private void BuildActor(Cutscene scene, int index)
    {
        var actor = scene.Actors[index];
        problemPrefix = $"Actors[{index}]";
        bool farmer = actor.Kind == "Farmer";
        var character = Catalog?.Character(actor.Npc ?? actor.Id ?? "");
        Title = farmer ? "Player" : character?.DisplayName ?? actor.Id ?? "Actor";
        Subtitle = farmer ? "The player (farmer)" : actor.Kind == "Temporary" ? "Temporary actor (exists only in this scene)" : $"NPC {actor.Id}";
        Icon = farmer ? SymbolRegular.Person24 : SymbolRegular.PersonCircle24;
        if (character != null && character.Origin.IsModProvided) Subtitle += $" · from {Catalog!.ModLabel(character.Origin.ProvidedBy[0])}";

        if (actor.Kind == "Temporary")
            Fields.Add(new TextField("Name in this scene", "Id", () => actor.Id, v => StageViewModel.RenameActor(scene, actor, v), Commit) { AllowNull = false, Rebuild = true });
        Fields.Add(Tile("Starts at", "Start.Tile", () => actor.Start?.Tile, t => { actor.Start ??= new Position(); actor.Start.Tile = t; },
            () => actor.Start?.Px, p => { actor.Start ??= new Position { Tile = new[] { 0, 0 } }; actor.Start.Px = p; }));
        Fields.Add(new ChoiceField("Facing", "Facing", FacingOptions(), () => actor.Facing, v => actor.Facing = v ?? "down", Commit));
        Fields.Add(new BoolField("Visible at the start", "Visible", () => actor.Visible, v => actor.Visible = v, Commit) { Description = "Hidden actors are there but invisible until a clip shows them." });
        if (actor.Kind == "Npc")
        {
            Fields.Add(new ChoiceField("Outfit at the start", "Sprite.Asset", SpriteOptions(actor.Npc ?? actor.Id), () => actor.Sprite?.Asset,
                v => actor.Sprite = v == null ? null : new SpriteReference { Asset = v }, Commit)
            { IsEditable = true, Help = "Leave on \"Their usual look\" so seasonal outfits and sprite mods follow. Picking another sheet can add a dependency." });
        }
        else if (actor.Kind == "Temporary")
        {
            Fields.Add(new ChoiceField("Sprite sheet", "Sprite.Asset", AllSpriteOptions(), () => actor.Sprite?.Asset, v => actor.Sprite = new SpriteReference { Asset = v }, Commit) { IsEditable = true });
            Fields.Add(new NumberField("Frame width", "FrameSize", () => actor.FrameSize?[0] ?? 16, v => actor.FrameSize = new[] { v ?? 16, actor.FrameSize?[1] ?? 32 }, Commit) { Minimum = 1, Maximum = 256, Suffix = "px" });
            Fields.Add(new NumberField("Frame height", "FrameSize", () => actor.FrameSize?[1] ?? 32, v => actor.FrameSize = new[] { actor.FrameSize?[0] ?? 16, v ?? 32 }, Commit) { Minimum = 1, Maximum = 256, Suffix = "px" });
        }
        else
        {
            Fields.Add(new SectionField("Starting outfit", "Changed for the scene and always restored afterwards."));
            Fields.Add(new ChoiceField("Preset", "Costume", PresetOptions(), () => actor.Costume?.Type == JTokenType.String ? (string?)actor.Costume : null,
                v => actor.Costume = v == null ? null : (JToken)v, Commit) { Rebuild = true });
            if (actor.Costume == null || actor.Costume.Type == JTokenType.Object)
                Fields.Add(CostumeParts("Clothes and look", "Costume", () => (actor.Costume as JObject)?.Properties().ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase),
                    set => actor.Costume = set == null || set.Count == 0 ? null : new JObject(set.Select(kv => new JProperty(kv.Key, kv.Value)))));
        }
        int uses = StageViewModel.BeatsUsing(scene, actor.Id);
        Fields.Add(new SectionField("In this scene", uses == 0 ? "No beats use this actor yet." : $"{uses} beat{(uses == 1 ? "" : "s")} use this actor."));
        if (StageViewModel.CanRemove(scene, actor))
            Fields.Add(new ActionField("Remove from the scene", () => shell.Stage.RemoveActor(scene.Actors.IndexOf(actor))));
    }

    // ================= branch / video =================

    private void BuildBranch(Cutscene scene, string lane)
    {
        problemPrefix = lane switch { LaneNames.Main => "Beats", LaneNames.Skip => "OnSkip", _ => "Branches." + lane };
        Icon = lane == LaneNames.Main ? SymbolRegular.TextBulletListLtr24 : SymbolRegular.Branch24;
        var list = LaneNames.Get(scene, lane) ?? new();
        Title = LaneNames.Title(lane);
        Subtitle = lane switch { LaneNames.Main => "Where the cutscene starts", LaneNames.Skip => "Rewards and story flags applied if the player skips", _ => "Branch" };
        if (lane is not (LaneNames.Main or LaneNames.Skip))
        {
            Fields.Add(new TextField("Name", "", () => lane, v => { if (!string.IsNullOrWhiteSpace(v)) shell.Ui.Post(() => shell.Storyboard.RenameBranch(lane, v)); }, (l, a, r) => a()) { AllowNull = false });
            Fields.Add(new NoteField("A branch is a list of beats a choice (or a Go to) leads to. When it runs out it continues after the choice, unless it ends with End scene or Go to."));
        }
        Fields.Add(new SectionField("Beats", $"{list.Count} beat{(list.Count == 1 ? "" : "s")}. Use + in the storyboard to add more."));
        if (lane == LaneNames.Skip) Fields.Add(new NoteField("Only Story flag, Friendship, Money, Item and Costume beats are allowed here."));
        if (lane is not (LaneNames.Main or LaneNames.Skip)) Fields.Add(new ActionField("Delete branch", () => shell.Storyboard.DeleteBranch(lane)));
    }

    private void BuildVideo(VideoEntry video)
    {
        problemPrefix = "";
        Title = video.Id; Subtitle = $"Video · {video.FrameCount} frames · {video.Width}×{video.Height}"; Icon = SymbolRegular.MoviesAndTv24;
        Fields.Add(new NumberField("Frames per second", "", () => (int)Math.Round(video.Settings.Fps), v => video.Settings.Fps = v ?? 12, Commit) { Minimum = 1, Maximum = 60, Describe = v => $"{video.FrameCount / Math.Max(1.0, v ?? 12):0.0} s long" });
        var sounds = new ListField("Sounds", "", "Add sound", () => Commit("Add video sound", () => video.Settings.Sounds[NextFreeFrame(video)] = "dwop", true))
        { EmptyText = "No sounds. Add one to play a cue on a frame." };
        foreach (var (frame, cue) in video.Settings.Sounds.OrderBy(s => int.TryParse(s.Key, out int n) ? n : 0).ToArray())
        {
            string key = frame;
            var row = new ListRowViewModel(() => Commit("Remove video sound", () => video.Settings.Sounds.Remove(key), true));
            row.Cells.Add(new NumberField("Frame", "", () => int.TryParse(key, out int n) ? n : 1, v =>
            {
                string next = (v ?? 1).ToString(CultureInfo.InvariantCulture);
                if (next == key) return;
                video.Settings.Sounds.Remove(key); video.Settings.Sounds[next] = cue;
            }, (l, a, r) => Commit(l, a, true)) { Minimum = 1, Maximum = video.FrameCount });
            row.Cells.Add(new ChoiceField("Sound", "", SoundOptions(), () => video.Settings.Sounds.GetValueOrDefault(key), v => video.Settings.Sounds[key] = v ?? "dwop", Commit) { IsEditable = true });
            sounds.Rows.Add(row);
        }
        Fields.Add(sounds);
        Fields.Add(new NoteField("Frame numbers start at 1. Videos play in a black film frame, scaled to fit; Esc skips (doc 03 §14)."));
        static string NextFreeFrame(VideoEntry v) { int n = 1; while (v.Settings.Sounds.ContainsKey(n.ToString(CultureInfo.InvariantCulture))) n++; return n.ToString(CultureInfo.InvariantCulture); }
    }

    // ================= beats =================

    private void BuildBeat(Cutscene scene, BeatSelection selection, Beat beat)
    {
        var kind = BeatCatalog.Kind(beat.Type);
        problemPrefix = LaneNames.JsonPath(selection.Lane, selection.Index);
        Title = kind.Name; Icon = kind.Icon; Help = kind.Help; Category = kind.Category.ToString();
        Subtitle = $"{LaneNames.Title(selection.Lane)} · beat {selection.Index + 1}";
        switch (beat)
        {
            case SayBeat say:
                Fields.Add(Actor("Speaker", "Actor", () => say.Actor, v => say.Actor = v, npcOnly: true));
                Fields.Add(Text("Line", "Text", () => say.Text, v => say.Text = v));
                Fields.Add(OtherLines(() => say.Or, v => say.Or = v));
                Fields.Add(new ChoiceField("Portrait emotion", "Emotion", EmotionOptions(), () => say.Emotion?.ToString() is { Length: > 0 } e ? e : "neutral", v => say.Emotion = v is null or "neutral" ? null : (JToken)v, Commit) { IsEditable = true });
                Fields.Add(new ChoiceField("Composed portrait", "Portrait", Optional(LibraryIds(shell.Document!.Library.Portraits.Select(p => p.Value.Id)), "Their usual portrait"), () => say.Portrait, v => say.Portrait = v, Commit));
                Fields.Add(new ChoiceField("Expression", "Expression", Optional(LibraryIds(shell.Document.Library.Expressions?.Value.Expressions.Select(e => e.Id) ?? Array.Empty<string?>()), "None"), () => say.Expression, v => say.Expression = v, Commit));
                Fields.Add(new NoteField("Tip: in the line, @ is the player's name. $b on its own starts a new page."));
                break;
            case MessageBeat message:
                Fields.Add(Text("Message", "Text", () => message.Text, v => message.Text = v, placeholder: "The text in the box. Ctrl+Enter commits."));
                Fields.Add(OtherLines(() => message.Or, v => message.Or = v));
                break;
            case NumberBeat number:
                Fields.Add(new ChoiceField("Number", "Name", ConditionContext.ProjectNumbers(shell.Document).Select(n => new ChoiceItem(n.Label, n.Label)),
                    () => number.Name, v => number.Name = string.IsNullOrWhiteSpace(v) ? null : PackDocument.ToContentId(v), Commit)
                { IsEditable = true, Help = "Pick a number the project uses, or type a new name (jokes-told, robin-helped…)." });
                Fields.Add(new ChoiceField("Change", "Change", new[] { new ChoiceItem("add", "Add"), new ChoiceItem("subtract", "Subtract"), new ChoiceItem("set", "Set to") },
                    () => number.Change, v => number.Change = v ?? "add", Commit));
                Fields.Add(new NumberField("Amount", "Amount", () => number.Amount, v => number.Amount = v ?? 0, Commit) { Minimum = 0, Maximum = 1_000_000, Rebuild = true });
                Fields.Add(new NumberField("Or a random amount up to", "UpTo", () => number.UpTo, v => number.UpTo = v, Commit)
                {
                    Nullable = true, Minimum = 0, Maximum = 1_000_000,
                    Describe = v => v is { } top && top > number.Amount ? $"{number.Amount} to {top}, the same if the day is replayed" : "Leave empty to use the amount above.",
                });
                Fields.Add(new NoteField($"Story numbers live in the player's save (SDCS.{shell.Document!.PackId}.{number.Name ?? "name"}), start at 0 and never go below it. A Check's \"story number\" row compares them; the Story panel lists them all."));
                break;
            case ChanceBeat chance:
                Fields.Add(ChanceOutcomes(scene, chance));
                Fields.Add(new ActionField("Equal chances", () => Commit("Equal chances", () => chance.Outcomes.ForEach(o => o.Weight = 1), true)));
                Fields.Add(new BoolField("Same result if the day is replayed", "Fresh", () => !chance.Fresh, v => chance.Fresh = !v, Commit)
                { Description = "Reloading the morning lands the same way. Off: a fresh roll every time." });
                Fields.Add(new NoteField("The game picks one outcome by its chance and goes to its branch. A branch that runs out continues with the beats after this Chance."));
                break;
            case CheckBeat check:
                Fields.Add(new CheckField(scene, check, ConditionChoices(), Commit));
                Fields.Add(new NoteField("The first case that's true goes to its branch; when none is, otherwise. A branch that runs out continues with the beats after this Check."));
                break;
            case ChoiceBeat choice:
                Fields.Add(Text("Question (optional)", "Prompt", () => choice.Prompt, v => choice.Prompt = v, optional: true, placeholder: "Shown above the answers. Leave empty if the last line asks it."));
                Fields.Add(ChoiceOptions(scene, choice));
                Fields.Add(new NoteField("Each answer jumps to its branch. A branch that runs out continues with the beats after this choice."));
                break;
            case MoveBeat move:
                Fields.Add(Actor("Who", "Actor", () => move.Actor, v => move.Actor = v));
                Fields.Add(Tile("Walk to", "To.Tile", () => move.To?.Tile, t => move.To = new Position { Tile = t }));
                Fields.Add(new ChoiceField("Speed", "Speed", new[] { new ChoiceItem("slow", "Slow"), new ChoiceItem("normal", "Normal"), new ChoiceItem("fast", "Fast") },
                    () => move.Speed.Type == JTokenType.Integer ? move.Speed.ToString() : (string?)move.Speed, v => move.Speed = int.TryParse(v, out int n) ? n : v ?? "normal", Commit) { IsEditable = true });
                Fields.Add(new ChoiceField("Route", "Path", new[] { new ChoiceItem("x-first", "Across first, then up/down"), new ChoiceItem("y-first", "Up/down first, then across") }, () => move.Path, v => move.Path = v ?? "x-first", Commit));
                Fields.Add(new BoolField("Wait until they arrive", "Wait", () => move.Wait, v => move.Wait = v, Commit));
                Fields.Add(new NoteField("Characters walk in straight lines and don't avoid walls. Check the route on the stage."));
                break;
            case FaceBeat face:
                Fields.Add(Actor("Who", "Actor", () => face.Actor, v => face.Actor = v));
                Fields.Add(new ChoiceField("Facing", "Facing", FacingOptions(), () => face.Facing, v => face.Facing = v, Commit));
                break;
            case EmoteBeat emote:
                Fields.Add(Actor("Who", "Actor", () => emote.Actor, v => emote.Actor = v));
                Fields.Add(new ChoiceField("Emote", "Emote", EmoteOptions(), () => emote.Emote?.ToString(), v => emote.Emote = v, Commit) { IsEditable = true });
                Fields.Add(new BoolField("Wait for the bubble to finish", "Wait", () => emote.Wait, v => emote.Wait = v, Commit));
                break;
            case ShakeBeat shake:
                Fields.Add(Actor("Who", "Actor", () => shake.Actor, v => shake.Actor = v));
                Fields.Add(Milliseconds("How long", "Duration", () => shake.Duration, v => shake.Duration = v));
                break;
            case JumpBeat jump:
                Fields.Add(Actor("Who", "Actor", () => jump.Actor, v => jump.Actor = v));
                Fields.Add(new NumberField("Height", "Strength", () => jump.Strength, v => jump.Strength = v ?? 8, Commit) { Minimum = 1, Maximum = 20, Help = "8 is a normal hop." });
                Fields.Add(new BoolField("Silent", "Silent", () => jump.Silent, v => jump.Silent = v, Commit));
                break;
            case PoseBeat pose:
                Fields.Add(Actor("Who", "Actor", () => pose.Actor, v => pose.Actor = v));
                Fields.Add(new BoolField("Return to their normal sprite", "Release", () => pose.Release, v => pose.Release = v, Commit) { Rebuild = true });
                if (!pose.Release)
                {
                    Fields.Add(new ChoiceField("Pose", "Pose", LibraryIds(shell.Document!.Library.Poses.Where(p => p.Value.Kind == "character").Select(p => p.Value.Id)), () => pose.Pose, v => pose.Pose = v, Commit)
                    { Help = "Poses are made in the Character Studio or come with the pack." });
                    Fields.Add(new ChoiceField("Play", "Play", PlayOptions(), () => pose.Play, v => pose.Play = v ?? "once", Commit));
                }
                Fields.Add(new BoolField(pose.Release ? "Wait for them to get up" : "Wait for it to finish", "Wait", () => pose.Wait, v => pose.Wait = v, Commit));
                break;
            case CostumeBeat costume:
                BuildCostume(scene, costume);
                break;
            case ClipBeat clip:
                BuildClipBeat(scene, clip);   // InspectorViewModel.Clips.cs (M8)
                break;
            case WaitBeat wait:
                Fields.Add(Milliseconds("Wait", "Duration", () => wait.Duration, v => wait.Duration = v));
                break;
            case CameraBeat camera:
                Fields.Add(Tile("Centre on", "Center.Tile", () => camera.Center?.Tile, t => camera.Center = new Position { Tile = t }));
                Fields.Add(Milliseconds("Pan time", "Duration", () => camera.Duration, v => camera.Duration = v, "0 = cut instantly"));
                Fields.Add(new ChoiceField("Easing", "Ease", EaseOptions(), () => camera.Ease, v => camera.Ease = v ?? "linear", Commit));
                Fields.Add(new BoolField("Wait for the pan", "Wait", () => camera.Wait, v => camera.Wait = v, Commit));
                break;
            case FadeBeat fade:
                Fields.Add(new ChoiceField("Fade", "To", new[] { new ChoiceItem("black", "To black"), new ChoiceItem("clear", "Back from black") }, () => fade.To, v => fade.To = v, Commit));
                Fields.Add(Milliseconds("Duration", "Duration", () => fade.Duration, v => fade.Duration = v));
                Fields.Add(new BoolField("Wait for the fade", "Wait", () => fade.Wait, v => fade.Wait = v, Commit));
                break;
            case SoundBeat sound:
                Fields.Add(new ChoiceField("Sound", "Cue", SoundOptions(), () => sound.Cue, v => sound.Cue = v, Commit) { IsEditable = true });
                Fields.Add(new NumberField("Pitch", "Pitch", () => sound.Pitch, v => sound.Pitch = v, Commit) { Nullable = true, Minimum = 0, Maximum = 2400, Help = "Empty = normal. 1200 is normal pitch." });
                Fields.Add(new ActionField("▶  Play sound", () => _ = shell.PreviewSoundAsync(sound.Cue, sound.Pitch)));
                Fields.Add(new ActionField("■  Stop sound", () => shell.StopSoundPreviewCommand.Execute(null)));
                break;
            case MusicBeat music:
                MusicFields("Cue", () => music.Cue, v => music.Cue = v);
                break;
            case EffectBeat effect:
                Fields.Add(new ChoiceField("Effect", "Effect", EffectOptions(), () => effect.Effect, v => effect.Effect = v, Commit) { IsEditable = true });
                Fields.Add(new ChoiceField("On", "Actor", ActorOptions(scene, false).Prepend(new ChoiceItem(null, "A tile (choose below)")), () => effect.Actor, v => { effect.Actor = v; if (v != null) effect.At = null; else effect.At ??= new Position { Tile = new[] { 10, 10 } }; }, Commit) { Rebuild = true });
                if (effect.Actor == null) Fields.Add(Tile("At", "At.Tile", () => effect.At?.Tile, t => effect.At = new Position { Tile = t }));
                Fields.Add(new NumberField("Count", "Count", () => effect.Count, v => effect.Count = v ?? 1, Commit) { Minimum = 1, Maximum = 20 });
                Fields.Add(new TextField("Colour", "Color", () => effect.Color, v => effect.Color = v, Commit) { Placeholder = "#RRGGBB (optional)" });
                Fields.Add(new NumberField("Duration", "Duration", () => effect.Duration, v => effect.Duration = v, Commit) { Nullable = true, Minimum = 0, Suffix = "ms" });
                break;
            case VideoBeat video:
                Fields.Add(new ChoiceField("Video", "Video", LibraryIds(shell.Document!.Videos.Select(v => v.Id)), () => video.Video, v => video.Video = v, Commit));
                Fields.Add(new ActionField("Import a video…", () => shell.ImportVideo()));
                break;
            case FriendshipBeat friendship:
                Fields.Add(new ChoiceField("With", "Npc", NpcOptions(), () => friendship.Npc, v => friendship.Npc = v, Commit) { IsEditable = true });
                Fields.Add(new NumberField("Points", "Points", () => friendship.Points, v => friendship.Points = v ?? 0, Commit) { Minimum = -2500, Maximum = 2500, Step = 50, Describe = v => BeatCatalog.Hearts(v ?? 0) });
                break;
            case MoneyBeat money:
                Fields.Add(new NumberField("Gold", "Amount", () => money.Amount, v => money.Amount = v ?? 0, Commit) { Suffix = "g", Help = "Negative takes gold away." });
                Fields.Add(new BoolField("Hand it over (hold-up animation)", "HoldUp", () => money.HoldUp, v => money.HoldUp = v, Commit) { Rebuild = true });
                if (money.HoldUp) Fields.Add(Text("Message afterwards", "Message", () => money.Message, v => money.Message = v, optional: true));
                break;
            case PropBeat prop:
                Fields.Add(Item("Item", "Item", () => prop.Item, v => prop.Item = v));
                Fields.Add(Tile("Stands on", "At.Tile", () => prop.At?.Tile, t => prop.At = new Position { Tile = t }));
                Fields.Add(new BoolField("Take it away instead", "Remove", () => prop.Remove, v => prop.Remove = v, Commit));
                Fields.Add(new NoteField("It stays until the scene ends, or until a beat takes it away. Its picture's bottom-left corner stands on the tile; drag it on the stage to move it. Only the scene shows it: nothing is added to the map."));
                break;
            case ItemBeat item:
                Fields.Add(new NoteField("The player gets this item (into their inventory). To show an item on the map, use Place an item, or drag it from the browser onto the stage."));
                bool bucket = item.OneOf is { Count: > 0 };
                Fields.Add(new BoolField("One of these, at random", "OneOf", () => bucket, v =>
                {
                    if (v) item.OneOf = new() { new ItemChoice { Item = item.Item ?? "(O)709", Count = item.Count }, new ItemChoice { Item = "(O)74" } };
                    else
                    {
                        if (item.OneOf?.FirstOrDefault() is { Item: not null } first) { item.Item = first.Item; item.Count = first.Count; }
                        item.OneOf = null; item.Remember = false; item.Fresh = false;
                    }
                }, Commit) { Rebuild = true, Description = "A bucket of items: the game gives one, by its chance." });
                if (!bucket)
                {
                    Fields.Add(Item("Item", "Item", () => item.Item, v => item.Item = v));
                    Fields.Add(new NumberField("How many", "Count", () => item.Count, v => item.Count = v ?? 1, Commit) { Minimum = 1, Maximum = 999 });
                }
                else
                {
                    Fields.Add(ItemBucket(item));
                    Fields.Add(new ActionField("Equal chances", () => Commit("Equal chances", () => item.OneOf!.ForEach(c => c.Weight = 1), true)));
                    Fields.Add(new BoolField("Remember which one (a story flag per item)", "Remember", () => item.Remember, v => item.Remember = v, Commit)
                    { Description = $"Sets SDCS.{shell.Document!.PackId}.{scene.Id}.got-<item>, so a Check later can say which they got." });
                    Fields.Add(new BoolField("Same result if the day is replayed", "Fresh", () => !item.Fresh, v => item.Fresh = !v, Commit)
                    { Description = "Reloading the morning can't fish for the rare one. Off: a fresh pick every time." });
                }
                Fields.Add(new BoolField("Hand it over (hold-up animation)", "HoldUp", () => item.HoldUp, v => item.HoldUp = v, Commit) { Rebuild = true });
                if (item.HoldUp) Fields.Add(Text("Message afterwards", "Message", () => item.Message, v => item.Message = v, optional: true,
                    placeholder: bucket ? "{item} becomes the item they got, e.g. You found a {item}!" : null));
                break;
            case MailBeat mail:
                Fields.Add(new ChoiceField("Action", "", new[] { new ChoiceItem("add", "Set the flag"), new ChoiceItem("remove", "Clear the flag") },
                    () => mail.Remove != null && mail.Add == null ? "remove" : "add", v => { string? flag = mail.Add ?? mail.Remove; mail.Add = v == "add" ? flag : null; mail.Remove = v == "remove" ? flag : null; }, Commit) { Rebuild = true });
                string suggested = $"SDCS.{shell.Document!.PackId}.{scene.Id}.";
                bool removing = mail.Remove != null && mail.Add == null;
                Fields.Add(new TextField("Flag", removing ? "Remove" : "Add", () => mail.Add ?? mail.Remove, v => { if (removing) mail.Remove = v; else mail.Add = v; }, Commit)
                { Placeholder = suggested + "Something", Help = "Flags remember what happened. Check them in a trigger with \"Has story flag\"." });
                if ((mail.Add ?? mail.Remove) == null)
                    Fields.Add(new ActionField("Use a name for this scene", () => Commit("Name story flag", () => mail.Add = suggested + "Done", true)));
                break;
            case QuestBeat quest:
                Fields.Add(new ChoiceField("Action", "", new[] { new ChoiceItem("add", "Start the quest"), new ChoiceItem("remove", "Remove the quest") },
                    () => quest.Remove != null && quest.Add == null ? "remove" : "add", v => { string? id = quest.Add ?? quest.Remove; quest.Add = v == "add" ? id : null; quest.Remove = v == "remove" ? id : null; }, Commit) { Rebuild = true });
                Fields.Add(new TextField("Quest ID", quest.Remove != null && quest.Add == null ? "Remove" : "Add", () => quest.Add ?? quest.Remove,
                    v => { if (quest.Remove != null && quest.Add == null) quest.Remove = v; else quest.Add = v; }, Commit));
                break;
            case GotoBeat go:
                Fields.Add(Branch("Go to", "Branch", scene, () => go.Branch, v => go.Branch = v));
                Fields.Add(new NoteField("Nothing after this beat in the same list runs."));
                break;
            case EndBeat:
                Fields.Add(new NoteField("The cutscene ends here. What happens next is set in the cutscene settings (\"When it ends\")."));
                break;
            case RawBeat raw:
                Fields.Add(new TextField("Command", "Command", () => raw.Command, v => raw.Command = v, Commit) { Placeholder = "e.g. addConversationTopic robinSecret 7" });
                Fields.Add(new NoteField("Raw commands are passed to the game as they are. No / allowed. See reference/event-commands.md.", warning: true));
                break;
            default:
                Fields.Add(new NoteField($"'{beat.Type}' isn't a beat type this version knows. It's kept as it is.", warning: true));
                break;
        }
        Fields.Add(new ActionField("▶  Preview from this beat", () => shell.PreviewFromHereCommand.Execute(null)));
    }

    private void BuildCostume(Cutscene scene, CostumeBeat costume)
    {
        Fields.Add(Actor("Who", "Actor", () => costume.Actor, v => costume.Actor = v, rebuild: true));
        var actor = scene.Actors.FirstOrDefault(a => string.Equals(a.Id, costume.Actor, StringComparison.OrdinalIgnoreCase));
        bool farmer = actor?.Kind == "Farmer" || costume.Actor == "farmer";
        string mode = costume.Restore ? "restore" : costume.Portrait != null ? "portrait" : costume.Sprite?.Pose != null ? "sheet" : costume.Sprite?.Asset != null ? "sprite" : costume.Preset != null ? "preset" : "set";
        var modes = farmer
            ? new[] { new ChoiceItem("set", "Change clothes or parts"), new ChoiceItem("preset", "Wear a costume preset"), new ChoiceItem("restore", "Back to their own look") }
            : new[] { new ChoiceItem("sprite", "Use another sprite sheet"), new ChoiceItem("sheet", "Use a generated sheet pose"), new ChoiceItem("portrait", "Use a composed portrait"), new ChoiceItem("restore", "Back to their usual look") };
        Fields.Add(new ChoiceField("Change", "", modes, () => mode, v =>
        {
            costume.Restore = v == "restore"; costume.Set = null; costume.Preset = null; costume.Sprite = null; costume.Portrait = null;
            if (v == "set") costume.Set = new() { ["Hat"] = "none" };
            if (v == "sprite") costume.Sprite = new SpriteReference { Asset = Catalog?.Character(actor?.Npc ?? costume.Actor ?? "")?.Appearances.FirstOrDefault()?.Sprite ?? "Characters/" + costume.Actor };
            if (v == "sheet") costume.Sprite = new SpriteReference { Pose = shell.Document!.Library.Poses.FirstOrDefault(p => p.Value.Kind == "sheet")?.Value.Id };
            if (v == "portrait") costume.Portrait = shell.Document!.Library.Portraits.FirstOrDefault()?.Value.Id;
            if (v == "preset") costume.Preset = shell.Document!.Library.Costumes?.Value.Costumes.FirstOrDefault()?.Id;
        }, Commit) { Rebuild = true });
        switch (mode)
        {
            case "set": Fields.Add(CostumeParts("Parts", "Set", () => costume.Set, set => costume.Set = set)); break;
            case "preset": Fields.Add(new ChoiceField("Preset", "Preset", PresetOptions().Where(o => o.Value != null), () => costume.Preset, v => costume.Preset = v, Commit)); break;
            case "sprite": Fields.Add(new ChoiceField("Sprite sheet", "Sprite.Asset", SpriteOptions(actor?.Npc ?? costume.Actor).Where(o => o.Value != null), () => costume.Sprite?.Asset, v => costume.Sprite = new SpriteReference { Asset = v }, Commit) { IsEditable = true }); break;
            case "sheet": Fields.Add(new ChoiceField("Sheet pose", "Sprite.Pose", LibraryIds(shell.Document!.Library.Poses.Where(p => p.Value.Kind == "sheet").Select(p => p.Value.Id)), () => costume.Sprite?.Pose, v => costume.Sprite = new SpriteReference { Pose = v }, Commit)); break;
            case "portrait": Fields.Add(new ChoiceField("Portrait", "Portrait", LibraryIds(shell.Document!.Library.Portraits.Select(p => p.Value.Id)), () => costume.Portrait, v => costume.Portrait = v, Commit)); break;
        }
        if (farmer && mode != "restore")
            // Scene costumes are temporary unless the scene says otherwise (the owner expected earmuffs to stay on).
            Fields.Add(new BoolField("Keep these clothes after the scene ends", "OnEnd.RestoreCostume", () => !scene.OnEnd.RestoreCostume, v => scene.OnEnd.RestoreCostume = !v, Commit)
            { Help = "Off: the player's own clothes come back when the scene ends (or if the game crashes). This is for the whole scene." });
    }

    // ================= field factories =================

    private TileField Tile(string label, string key, Func<int[]?> get, Action<int[]> set, Func<int[]?>? getPx = null, Action<int[]>? setPx = null) =>
        new(label, key, get, set, Commit, field => shell.Stage.BeginPick(label, (x, y) => field.SetTile(x, y)), getPx, setPx);

    private NumberField Milliseconds(string label, string key, Func<int> get, Action<int> set, string? help = null) =>
        new(label, key, () => get(), v => set(v ?? 0), Commit) { Minimum = 0, Maximum = 600000, Suffix = "ms", Step = 100, Help = help, Describe = v => v >= 1000 ? $"{v / 1000.0:0.##} s" : null };

    private TranslatableTextField Text(string label, string key, Func<TextValue?> get, Action<TextValue?> set, bool optional = false, string? placeholder = null) =>
        new(label, key, get, v => set(optional && v?.Literal?.Length == 0 && v.I18n == null ? null : v), Commit) { Placeholder = placeholder ?? (optional ? "Optional. Ctrl+Enter commits." : "What they say. Ctrl+Enter commits.") };

    private ChoiceField Actor(string label, string key, Func<string?> get, Action<string?> set, bool npcOnly = false, bool rebuild = false) =>
        new(label, key, ActorOptions(Scene!, npcOnly), get, set, Commit) { Rebuild = rebuild, Help = Scene!.Actors.Count <= 1 ? "Drag characters from the asset browser onto the stage to add them." : null };

    private ChoiceField Branch(string label, string key, Cutscene scene, Func<string?> get, Action<string?> set) =>
        new(label, key, scene.Branches.Keys.Select(b => new ChoiceItem(b, b, $"{scene.Branches[b].Count} beats")).Append(new ChoiceItem(NewBranch, "＋ New branch")),
            get, v =>
            {
                if (v != NewBranch) { set(v); return; }
                string id = StoryboardViewModel.UniqueBranch(scene, "branch");
                scene.Branches[id] = new List<Beat>();
                set(id);
            }, Commit) { Rebuild = true };

    private const string NewBranch = "\u0001new";

    private ListField ChoiceOptions(Cutscene scene, ChoiceBeat choice)
    {
        var field = new ListField("Answers", "Options", "Add answer", () =>
        {
            string branch = StoryboardViewModel.UniqueBranch(scene, "option-" + (choice.Options.Count + 1));
            Commit("Add answer", () => { scene.Branches[branch] = new List<Beat>(); choice.Options.Add(new ChoiceOption { Text = "Another answer", Then = branch }); }, true);
        });
        for (int i = 0; i < choice.Options.Count; i++)
        {
            int index = i;
            var option = choice.Options[index];
            var row = new ListRowViewModel(() => Commit("Remove answer", () => choice.Options.RemoveAt(index), true),
                index > 0 ? () => Commit("Move answer up", () => { choice.Options.RemoveAt(index); choice.Options.Insert(index - 1, option); }, true) : null,
                index < choice.Options.Count - 1 ? () => Commit("Move answer down", () => { choice.Options.RemoveAt(index); choice.Options.Insert(index + 1, option); }, true) : null)
            { Caption = $"Answer {index + 1}" };
            row.Cells.Add(new TextField("", $"Options[{index}].Text", () => option.Text?.Literal ?? option.Text?.I18n, v => option.Text = v ?? "", Commit) { AllowNull = false, Placeholder = "What the player says" });
            row.Cells.Add(new ChoiceField("then", $"Options[{index}].Then",
                scene.Branches.Keys.Select(b => new ChoiceItem(b, "→ " + b)).Append(new ChoiceItem(NewBranch, "→ ＋ new branch")), () => option.Then,
                v => option.Then = v == NewBranch ? NewBranchFor(scene, option) : v, Commit) { Rebuild = true });
            // Offered only if… (D-51): the same condition rows as a Check.
            row.Cells.Add(new BoolField("Only if…", $"Options[{index}].When", () => option.When != null,
                v => option.When = v ? CheckField.DefaultCondition(scene) : null, Commit) { Rebuild = true });
            if (option.When != null)
                row.Cells.Add(new ConditionGroupField("", $"Options[{index}].When", () => option.When, v => option.When = v ?? CheckField.DefaultCondition(scene), ConditionChoices(), Commit));
            field.Rows.Add(row);
        }
        return field;
    }

    /// <summary>Chance outcomes: where each goes and its chance, shown as a percentage of all of them (D-51).</summary>
    private ListField ChanceOutcomes(Cutscene scene, ChanceBeat chance)
    {
        var field = new ListField("Outcomes", "Outcomes", "Add outcome", () =>
        {
            string branch = StoryboardViewModel.UniqueBranch(scene, "outcome-" + (chance.Outcomes.Count + 1));
            Commit("Add outcome", () => { scene.Branches[branch] = new List<Beat>(); chance.Outcomes.Add(new ChanceOutcome { Then = branch }); }, true);
        });
        for (int i = 0; i < chance.Outcomes.Count; i++)
        {
            int index = i;
            var outcome = chance.Outcomes[index];
            var row = new ListRowViewModel(() => Commit("Remove outcome", () => chance.Outcomes.RemoveAt(index), true)) { Caption = $"Outcome {index + 1}" };
            row.Cells.Add(new ChoiceField("goes to", $"Outcomes[{index}].Then",
                scene.Branches.Keys.Select(b => new ChoiceItem(b, "→ " + b)).Append(new ChoiceItem(NewBranch, "→ ＋ new branch")), () => outcome.Then,
                v => outcome.Then = v == NewBranch ? NewBranchNamed(scene, "outcome-" + (index + 1)) : v, Commit) { Rebuild = true });
            row.Cells.Add(new NumberField("chance", $"Outcomes[{index}].Weight", () => outcome.Weight, v => outcome.Weight = v ?? 0, Commit)
            { Minimum = 0, Maximum = 1000, Rebuild = true, Describe = v => $"{BeatCatalog.Percent(v ?? 0, chance.Outcomes.Select(o => o.Weight))}% of the time" });
            field.Rows.Add(row);
        }
        return field;
    }

    /// <summary>A random item bucket: each item, how many, and its chance (D-51).</summary>
    private ListField ItemBucket(ItemBeat item)
    {
        var bucket = item.OneOf!;
        var field = new ListField("Items", "OneOf", "Add item", () => Commit("Add item", () => bucket.Add(new ItemChoice { Item = "(O)74" }), true));
        for (int i = 0; i < bucket.Count; i++)
        {
            int index = i;
            var pick = bucket[index];
            var row = new ListRowViewModel(() => Commit("Remove item", () => { bucket.RemoveAt(index); if (bucket.Count == 0) item.OneOf = null; }, true));
            row.Cells.Add(Item("", $"OneOf[{index}].Item", () => pick.Item, v => pick.Item = v));
            row.Cells.Add(new NumberField("how many", $"OneOf[{index}].Count", () => pick.Count, v => pick.Count = v ?? 1, Commit) { Minimum = 1, Maximum = 999 });
            row.Cells.Add(new NumberField("chance", $"OneOf[{index}].Weight", () => pick.Weight, v => pick.Weight = v ?? 0, Commit)
            { Minimum = 0, Maximum = 1000, Rebuild = true, Describe = v => $"{BeatCatalog.Percent(v ?? 0, bucket.Select(c => c.Weight))}% of the time" });
            row.Cells.Add(new BoolField("Only if…", $"OneOf[{index}].When", () => pick.When != null,
                v => pick.When = v ? "YEAR 2" : null, Commit) { Rebuild = true, Description = "In the bucket only when this is true (the rare hat only from year 2)." });
            if (pick.When != null)
                row.Cells.Add(new ConditionGroupField("", $"OneOf[{index}].When", () => pick.When, v => pick.When = v ?? "YEAR 2", ConditionChoices(), Commit));
            field.Rows.Add(row);
        }
        return field;
    }

    /// <summary>Other lines for a Say or Message: one of all the lines is used, at random (D-51).</summary>
    private ListField OtherLines(Func<List<TextValue>?> get, Action<List<TextValue>?> set)
    {
        var lines = get();
        var field = new ListField("Or one of these lines, at random", "Or", "Add a line", () => Commit("Add a line", () => { var list = get() ?? new List<TextValue>(); list.Add("…"); set(list); }, true))
        { EmptyText = "Only the line above. Add lines for variety: the game picks one each time." };
        for (int i = 0; i < (lines?.Count ?? 0); i++)
        {
            int index = i;
            var row = new ListRowViewModel(() => Commit("Remove a line", () => { var list = get()!; list.RemoveAt(index); if (list.Count == 0) set(null); }, true));
            row.Cells.Add(Text("", $"Or[{index}]", () => get()![index], v => get()![index] = v ?? ""));
            field.Rows.Add(row);
        }
        return field;
    }

    private static string NewBranchNamed(Cutscene scene, string hint)
    {
        string id = StoryboardViewModel.UniqueBranch(scene, hint);
        scene.Branches[id] = new List<Beat>();
        return id;
    }

    private static string NewBranchFor(Cutscene scene, ChoiceOption option)
    {
        string text = option.Text?.Literal ?? "answer";
        string id = StoryboardViewModel.UniqueBranch(scene, PackDocument.ToContentId(string.Join('-', text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(3))));
        scene.Branches[id] = new List<Beat>();
        return id;
    }

    private ItemField Item(string label, string key, Func<string?> get, Action<string?> set) => new(label, key, get, set, Commit,
        id =>
        {
            var item = Catalog?.Item(id);
            return (item != null ? $"{item.Label}  {item.Qualified}" : id ?? "(none)", Images.Load(Catalog?.FullPath(item?.Icon)));
        },
        current =>
        {
            var picker = new ItemPickerViewModel(shell, current, null);
            return shell.Ui.ShowDialog(picker) ? picker.Chosen : null;
        });

    /// <summary>Farmer parts: Hat, Shirt, Pants, Shoes, Hair, colours… (doc 03 §13).</summary>
    private ListField CostumeParts(string label, string key, Func<Dictionary<string, JToken>?> get, Action<Dictionary<string, JToken>?> set)
    {
        var parts = new[] { "Hat", "Shirt", "Pants", "Shoes", "Hair", "HairColor", "Accessory", "Skin", "EyeColor" };
        var field = new ListField(label, key, "Add part", () =>
        {
            var current = get() ?? new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase);
            string? free = parts.FirstOrDefault(p => !current.ContainsKey(p));
            if (free == null) return;
            Commit("Add costume part", () => { current[free] = DefaultPartValue(free); set(current); }, true);
        }) { EmptyText = "Nothing changes yet. Add a part." };
        var values = get();
        foreach (var (part, value) in (values ?? new()).ToArray())
        {
            string name = part;
            var row = new ListRowViewModel(() => Commit("Remove costume part", () => { var d = get()!; d.Remove(name); set(d); }, true));
            row.Cells.Add(new ChoiceField("", "", parts.Select(p => new ChoiceItem(p, PartLabel(p))), () => name, v =>
            {
                if (v == null || v == name) return;
                var d = get()!; d.Remove(name); d[v] = DefaultPartValue(v); set(d);
            }, Commit) { Rebuild = true });
            row.Cells.Add(new ChoiceField("", $"{key}.{name}", PartValueOptions(name), () => value.ToString(), v =>
            {
                var d = get()!;
                d[name] = name is "Hair" or "Accessory" or "Skin" && int.TryParse(v, out int n) ? n : v ?? "original";
                set(d);
            }, Commit) { IsEditable = true });
            field.Rows.Add(row);
        }
        return field;

        static JToken DefaultPartValue(string part) => part switch { "Hat" or "Shoes" => "none", "Hair" => 0, "Accessory" => -1, "Skin" => 0, "HairColor" or "EyeColor" => "#7F4A2A", _ => "original" };
        static string PartLabel(string part) => part switch { "HairColor" => "Hair colour", "EyeColor" => "Eye colour", "Shoes" => "Shoes/boots", _ => part };
    }

    private IEnumerable<ChoiceItem> PartValueOptions(string part)
    {
        var common = new List<ChoiceItem> { new("original", "Their own (original)") };
        if (part is "Hat" or "Shoes") common.Add(new ChoiceItem("none", "None (take it off)"));
        string? type = part switch { "Hat" => "(H)", "Shirt" => "(S)", "Pants" => "(P)", "Shoes" => "(B)", _ => null };
        if (type != null && Catalog?.ItemsByType.TryGetValue(type, out var items) == true)
            common.AddRange(items.Select(i => new ChoiceItem(i.Qualified, $"{i.Label}  {i.Qualified}", null, Images.Load(Catalog.FullPath(i.Icon)))));
        if (part == "Hair" && Catalog != null) common.AddRange(Catalog.Hairstyles.Select(h => new ChoiceItem(h.Index.ToString(CultureInfo.InvariantCulture), $"Hairstyle {h.Index}{(h.Origin.IsModProvided ? " · " + Catalog.ModLabel(h.Origin.ProvidedBy[0]) : "")}")));
        return common;
    }

    // ================= option lists =================

    private IEnumerable<ChoiceItem> ActorOptions(Cutscene scene, bool npcOnly) => scene.Actors
        .Where(a => !npcOnly || a.Kind != "Farmer")
        .Select(a => new ChoiceItem(a.Id, a.Kind == "Farmer" ? "Player (farmer)" : Catalog?.Character(a.Npc ?? a.Id ?? "")?.Label ?? a.Id ?? "?"));

    /// <summary>What condition rows pick from (D-51).</summary>
    private ConditionContext ConditionChoices() => new()
    {
        Npcs = NpcOptions().ToArray(),
        Locations = LocationOptions().ToArray(),
        Mods = Catalog?.Mods.Where(m => !m.IsContentPack).Select(m => new ChoiceItem(m.UniqueID, m.Name ?? m.UniqueID, m.UniqueID)).ToArray() ?? Array.Empty<ChoiceItem>(),
        Flags = ConditionContext.ProjectFlags(shell.Document),
        Scenes = ConditionContext.ProjectScenes(shell.Document),
        SuggestedFlag = $"SDCS.{shell.Document?.PackId}.{Scene?.Id}.flag",
        Numbers = ConditionContext.ProjectNumbers(shell.Document),
        NumberPrefix = $"SDCS.{shell.Document?.PackId}.",
        ItemName = id => Catalog?.Item(id)?.Label,
        PickItem = current => { var picker = new ItemPickerViewModel(shell, current, null); return shell.Ui.ShowDialog(picker) ? picker.Chosen : null; },
    };

    private IEnumerable<ChoiceItem> NpcOptions() => Catalog?.Characters.Select(c => new ChoiceItem(c.Name, c.Label, c.Origin.IsModProvided ? Catalog.ModLabel(c.Origin.ProvidedBy[0]) : null))
        ?? Scene?.Actors.Where(a => a.Kind == "Npc").Select(a => new ChoiceItem(a.Id, a.Id ?? "?")) ?? Enumerable.Empty<ChoiceItem>();

    private IEnumerable<ChoiceItem> LocationOptions() => Catalog?.Locations.Select(l => new ChoiceItem(l.Name, l.Name, l.Origin.IsModProvided ? Catalog.ModLabel(l.Origin.ProvidedBy[0]) : l.Origin.IsEdited ? "edited by " + Catalog.ModLabel(l.Origin.EditedBy[0]) : null))
        ?? Enumerable.Empty<ChoiceItem>();

    private IEnumerable<ChoiceItem> SpriteOptions(string? npc)
    {
        yield return new ChoiceItem(null, "Their usual look", "follows seasons and sprite mods");
        var character = npc == null ? null : Catalog?.Character(npc);
        if (character == null) yield break;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in new[] { character.Sprite }.Concat(character.Appearances.Select(a => a.Sprite)))
            if (asset != null && seen.Add(asset))
            {
                var origin = Catalog!.AssetOrigin(asset);
                yield return new ChoiceItem(asset, asset, origin?.IsModProvided == true ? "from " + Catalog.ModLabel(origin.ProvidedBy[0]) + " (dependency)" : null,
                    Images.Frame(Catalog.TextureFile(asset), character.FrameWidth, character.FrameHeight, 0));
            }
    }

    private IEnumerable<ChoiceItem> AllSpriteOptions() => Catalog?.Characters.SelectMany(c => new[] { c.Sprite }.Concat(c.Appearances.Select(a => a.Sprite))).Where(a => a != null)
        .Distinct(StringComparer.OrdinalIgnoreCase).Select(a => new ChoiceItem(a, a!)) ?? Enumerable.Empty<ChoiceItem>();

    private IEnumerable<ChoiceItem> PresetOptions() => new[] { new ChoiceItem(null, "No preset") }
        .Concat((shell.Document?.Library.Costumes?.Value.Costumes ?? new()).Where(c => c.Id != null).Select(c => new ChoiceItem(c.Id, c.Id!, string.Join(", ", c.Set.Keys))));

    private IEnumerable<ChoiceItem> SoundOptions() => (Catalog?.Sounds ?? new[] { "dwop", "coin", "shwip", "thudStep", "money", "getNewSpecialItem" }).Select(s => new ChoiceItem(s, s));
    private IEnumerable<ChoiceItem> MusicOptions() => new[] { new ChoiceItem("none", "Silence (none)"), new ChoiceItem("continue", "Keep the current music") }
        .Concat((shell.Document?.Studio.Music ?? new()).Select(p => new ChoiceItem(Core.Audio.PackMusic.Reference(p.Key), p.Value?.Name ?? p.Key, "Imported music")))
        .Concat(SoundOptions());

    private static IEnumerable<ChoiceItem> FacingOptions() => new[] { new ChoiceItem("down", "Down ▼"), new ChoiceItem("left", "Left ◀"), new ChoiceItem("up", "Up ▲"), new ChoiceItem("right", "Right ▶") };
    private static IEnumerable<ChoiceItem> EmotionOptions() => Ids.Emotions.Keys.Select(e => new ChoiceItem(e, char.ToUpperInvariant(e[0]) + e[1..], e == "neutral" ? null : Ids.Emotions[e]));
    private IEnumerable<ChoiceItem> EmoteOptions() => Ids.Emotes.OrderBy(e => e.Value).Select(e => new ChoiceItem(e.Key, Catalog?.Emotes.FirstOrDefault(x => x.Id == e.Value)?.DisplayName ?? e.Key));
    private static IEnumerable<ChoiceItem> PlayOptions() => new[] { new ChoiceItem("once", "Once (stop on the last frame)"), new ChoiceItem("loop", "Loop"), new ChoiceItem("once-then-loop", "Intro, then loop"), new ChoiceItem("hold-first", "Hold the first frame") };
    private static IEnumerable<ChoiceItem> EaseOptions() => new[] { new ChoiceItem("linear", "Steady"), new ChoiceItem("inOut", "Smooth start and stop"), new ChoiceItem("in", "Speed up"), new ChoiceItem("out", "Slow down"), new ChoiceItem("step", "Jump at the end") };
    private IEnumerable<ChoiceItem> EffectOptions() => new[] { "dust", "sweat-drop", "steam", "sparkle", "heart-burst", "tears" }.Select(e => new ChoiceItem(e, e))
        .Concat((shell.Document?.Library.Poses ?? new()).Where(p => p.Value.Kind == "effect").Select(p => new ChoiceItem(p.Value.Id, p.Value.Id!, "custom effect")));
    private static IEnumerable<ChoiceItem> LibraryIds(IEnumerable<string?> ids) => ids.Where(i => i != null).Select(i => new ChoiceItem(i, i!));
    private static IEnumerable<ChoiceItem> Optional(IEnumerable<ChoiceItem> items, string none) => new[] { new ChoiceItem(null, none) }.Concat(items);
}
