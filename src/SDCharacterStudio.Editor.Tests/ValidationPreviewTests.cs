using System.IO;
using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Serialization;
using SDCharacterStudio.Editor.Export;
using SDCharacterStudio.Editor.Preview;
using SDCharacterStudio.Editor.Schema;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Tests;

public class ValidationTests
{
    [Fact]
    public void ProblemsPointAtTheBeatAndShowInline()
    {
        using var editor = new EditorHarness();
        editor.NewProject();
        var shell = editor.Shell;
        shell.Storyboard.Append(LaneNames.Main, BeatCatalog.Kind("Wait"));
        shell.Storyboard.Append(LaneNames.Main, BeatCatalog.Kind("Say"));   // no NPC on stage: the line has no speaker
        var result = shell.ValidateNow();
        var problem = result.Problems.First(p => p.IsError && p.Target.Kind == TargetKind.Beat);
        Assert.Equal(new ProblemTarget(TargetKind.Beat, "first-scene", LaneNames.Main, 1, problem.Target.Field), problem.Target);
        Assert.Equal(1, editor.Shell.Problems.ErrorCount);

        shell.Select(new SceneSelection("first-scene"));
        shell.Navigate(problem);
        Assert.Equal(new BeatSelection("first-scene", LaneNames.Main, 1), shell.Selection);
        Assert.True(shell.Storyboard.Lanes[0].Cards[1].HasError);
        Assert.NotNull(editor.Field<ChoiceField>("Speaker").Problem);

        // Fixing it clears the card and the field once validation runs again.
        shell.Stage.DropCharacter("Robin", 11, 10);
        shell.Select(new BeatSelection("first-scene", LaneNames.Main, 1));
        editor.Choose("Speaker", "Robin");
        shell.ValidateNow();
        Assert.Equal(0, shell.Problems.ErrorCount);
        Assert.False(shell.Storyboard.Lanes[0].Cards[1].HasError);
    }

    [Fact]
    public void WrongNpcNameCaseHasAOneClickFix()
    {
        using var editor = new EditorHarness();
        editor.NewProject();
        var shell = editor.Shell;
        shell.Edit("Hand-typed actor", () => shell.Scene!.Actors.Add(new Actor { Id = "robin", Kind = "Npc", Start = new Position { Tile = new[] { 12, 10 } } }));
        shell.Storyboard.Append(LaneNames.Main, BeatCatalog.Kind("Say"));
        Assert.Equal("robin", ((SayBeat)shell.Scene!.Beats[0]).Actor);
        var problem = shell.ValidateNow().Problems.Single(p => p.Rule == "V7");
        Assert.Equal("Use 'Robin'", problem.FixLabel);
        Assert.Equal(TargetKind.Actor, problem.Target.Kind);
        shell.Fix(problem);
        Assert.Equal("Robin", shell.Scene!.Actors[1].Id);
        Assert.Equal("Robin", shell.Scene.Actors[1].Npc);
        Assert.Equal("Robin", ((SayBeat)shell.Scene.Beats[0]).Actor);   // the beat follows the rename
        Assert.DoesNotContain(shell.ValidateNow().Problems, p => p.Rule == "V7");
    }

    [Fact]
    public async Task BlockedPreviewOpensProblemsInsteadOfStarting()
    {
        using var editor = new EditorHarness();
        editor.NewProject();
        editor.Shell.Storyboard.Append(LaneNames.Main, BeatCatalog.Kind("Say"));
        await editor.Shell.PreviewInGameCommand.ExecuteAsync(null);
        Assert.True(editor.Shell.Problems.IsOpenRequested);
        Assert.Contains(editor.Ui.Toasts, t => t.Title == "Can't preview yet");
    }

    [Fact]
    public void SlashInDialogueHasAOneClickFix()
    {
        using var editor = new EditorHarness();
        editor.NewProject();
        var shell = editor.Shell;
        shell.Stage.DropCharacter("Robin", 11, 10);
        shell.Storyboard.Append(LaneNames.Main, BeatCatalog.Kind("Say"));
        editor.Field<TranslatableTextField>("Line").Text = "Either/or, \"maybe\".";
        var problem = shell.ValidateNow().Problems.FirstOrDefault(p => p.Rule == "V5");
        Assert.NotNull(problem);
        Assert.Equal("Replace with look-alikes", problem!.FixLabel);
        shell.Fix(problem);
        Assert.Equal("Either⁄or, “maybe”.", ((SayBeat)shell.Scene!.Beats[0]).Text!.Literal);
        Assert.DoesNotContain(shell.ValidateNow().Problems, p => p.Rule == "V5");
        Assert.True(shell.Document!.Undo());
        Assert.Equal("Either/or, \"maybe\".", ((SayBeat)shell.Scene!.Beats[0]).Text!.Literal);
    }
}

public class FieldTests
{
    [Fact]
    public void EditableChoiceKeepsANullValuedOptionWhenItsLabelIsCommitted()
    {
        using var editor = new EditorHarness();
        editor.NewProject();
        editor.Shell.Stage.DropCharacter("Robin", 12, 10);
        var outfit = editor.Field<ChoiceField>("Outfit at the start");
        outfit.Selected = outfit.Options.Single(o => o.Value == "Characters/Robin_Summer");
        Assert.Equal("Characters/Robin_Summer", editor.Shell.Scene!.Actors[1].Sprite!.Asset);
        // The combo box picks "Their usual look"; its text box commits the label later, on lost focus.
        outfit.Selected = outfit.Options.Single(o => o.Value == null);
        outfit.Text = "Their usual look";
        Assert.Null(editor.Shell.Scene!.Actors[1].Sprite);
        // Typed values that aren't options still work.
        outfit.Text = "Characters/Robin_Beach";
        Assert.Equal("Characters/Robin_Beach", editor.Shell.Scene!.Actors[1].Sprite!.Asset);
    }
}

public class PreviewTests
{
    [Fact]
    public void PreviewPackUsesItsOwnIdAndUnsavedEdits()
    {
        using var editor = new EditorHarness();
        var doc = editor.NewProject();
        editor.Shell.Stage.DropCharacter("Robin", 12, 10);   // unsaved
        var (folder, packId, playId) = editor.Shell.Preview.PreparePreviewPack(doc, "first-scene");
        Assert.Equal("Player.TestPack.DevPreview", packId);
        Assert.Equal("first-scene", playId);
        var manifest = JObject.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")));
        Assert.Equal(packId, (string?)manifest["UniqueID"]);
        Assert.Empty(manifest["Dependencies"] ?? new JArray());
        var scene = PackJson.Deserialize<Cutscene>(File.ReadAllText(Path.Combine(folder, "cutscenes", "first-scene.json")))!;
        Assert.Contains(scene.Actors, a => a.Id == "Robin");
        Assert.False(File.Exists(Path.Combine(doc.PackFolder, "cutscenes", "missing.json")));
        Assert.StartsWith(Path.Combine(Path.GetTempPath(), "sdcs-editor-tests"), folder);   // tests never write the real preview folder
    }

    [Fact]
    public void PreviewFromHerePlacesActorsWhereEarlierBeatsLeftThem()
    {
        var scene = new Cutscene
        {
            Id = "walk", Location = "Town", Trigger = new Trigger(),
            Actors = { new Actor { Id = "farmer", Kind = "Farmer", Start = new Position { Tile = new[] { 5, 5 } } }, new Actor { Id = "Robin", Kind = "Npc", Start = new Position { Tile = new[] { 10, 5 } }, Facing = "down" } },
            Beats =
            {
                new MoveBeat { Actor = "Robin", To = new Position { Tile = new[] { 7, 5 } } },
                new FaceBeat { Actor = "farmer", Facing = "right" },
                new SayBeat { Actor = "Robin", Text = "Hi" },
            }
        };
        var variant = PreviewService.FromBeat(scene, LaneNames.Main, 2);
        Assert.Equal("walk-from-main-2", variant.Id);
        Assert.Null(variant.Trigger);
        Assert.Equal(new[] { 7, 5 }, variant.Actors[1].Start!.Tile);
        Assert.Equal("left", variant.Actors[1].Facing);
        Assert.Equal("right", variant.Actors[0].Facing);
        Assert.Collection(variant.Beats, b => Assert.IsType<SayBeat>(b), b => Assert.IsType<EndBeat>(b));
        Assert.Equal(3, scene.Beats.Count);   // the original is untouched
    }
}
