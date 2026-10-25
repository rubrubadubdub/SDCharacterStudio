using System.IO;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Serialization;
using SDCharacterStudio.Editor.Schema;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>D-51 step 4: answers offered only if…, and "Try it as…" for previews.</summary>
public sealed class TryItAsTests
{
    [Fact]
    public void AnAnswerCanBeOfferedOnlyIf()
    {
        using var editor = new EditorHarness();
        editor.NewProject();
        var shell = editor.Shell;
        var scene = shell.Scene!;
        shell.Stage.DropCharacter("Robin", 12, 10);
        shell.Storyboard.Insert(LaneNames.Main, 0, BeatCatalog.Kind("Choice"));
        var choice = (ChoiceBeat)scene.Beats[0];
        var answers = editor.Field<ListField>("Answers");
        var onlyIf = answers.Rows[1].Cells.OfType<BoolField>().Single(f => f.Label == "Only if…");
        onlyIf.Value = true;
        Assert.Equal("PLAYER_HEARTS Current Robin 4", choice.Options[1].When);
        answers = editor.Field<ListField>("Answers");
        var rows = answers.Rows[1].Cells.OfType<ConditionGroupField>().Single().Group;
        rows.Rows[0].Value = "6";
        Assert.Equal("PLAYER_HEARTS Current Robin 6", choice.Options[1].When);
        Assert.Contains("only if Robin ≥ 6♥", shell.Storyboard.Lanes.First(l => l.IsMain).Cards[0].Links[1].Label);
        Assert.Equal(0, shell.ValidateNow().Errors);
        editor.Field<ListField>("Answers").Rows[1].Cells.OfType<BoolField>().Single(f => f.Label == "Only if…").Value = false;
        Assert.Null(choice.Options[1].When);
    }

    [Fact]
    public void TryItAsForcesHowThePreviewCopyLandsAndLeavesThePackAlone()
    {
        using var editor = new EditorHarness();
        var doc = editor.NewProject();
        var shell = editor.Shell;
        var scene = shell.Scene!;
        doc.Edit("logic", () =>
        {
            scene.Beats.Add(new CheckBeat { Cases = { new CheckCase { When = "SEASON Fall", Then = "fall" }, new CheckCase { When = "SEASON Winter", Then = "winter" } } });
            scene.Beats.Add(new ChanceBeat { Outcomes = { new ChanceOutcome { Weight = 5, Then = "fall" }, new ChanceOutcome { Weight = 5, Then = "winter" } } });
            scene.Beats.Add(new ItemBeat { OneOf = new() { new ItemChoice { Item = "(O)709", Weight = 9 }, new ItemChoice { Item = "(O)336", Weight = 1, When = "YEAR 2" } } });
            scene.Branches["fall"] = new() { new WaitBeat { Duration = 1 } };
            scene.Branches["winter"] = new() { new WaitBeat { Duration = 1 } };
        });
        var tryItAs = shell.TryItAs;
        tryItAs.Refresh();
        Assert.Equal(new[] { "Check · beat 1", "Chance · beat 2", "Random item · beat 3" }, tryItAs.Entries.Select(e => e.Title));
        Assert.Equal("Let the game decide", tryItAs.Entries[0].Selected!.Label);
        Assert.Equal("Try it as…", tryItAs.ButtonText);
        tryItAs.Entries[0].Selected = tryItAs.Entries[0].Options.Single(o => o.Label.StartsWith("none true"));   // otherwise
        tryItAs.Entries[1].Selected = tryItAs.Entries[1].Options[2];   // the second outcome
        tryItAs.Entries[2].Selected = tryItAs.Entries[2].Options[2];   // the rare item, even before year 2
        Assert.Equal("Try it as… (3)", tryItAs.ButtonText);

        var played = tryItAs.Apply(scene);
        Assert.NotSame(scene, played);
        Assert.Equal(new[] { "FALSE", "FALSE" }, ((CheckBeat)played.Beats[0]).Cases.Select(c => c.When));
        Assert.Equal(new[] { 0, 1 }, ((ChanceBeat)played.Beats[1]).Outcomes.Select(o => o.Weight));
        var bucket = ((ItemBeat)played.Beats[2]).OneOf!;
        Assert.Equal(new[] { 0, 1 }, bucket.Select(c => c.Weight));
        Assert.Null(bucket[1].When);
        // The pack itself is untouched.
        Assert.Equal("SEASON Fall", ((CheckBeat)scene.Beats[0]).Cases[0].When);
        Assert.Equal(5, ((ChanceBeat)scene.Beats[1]).Outcomes[0].Weight);

        // The preview's copy of the scene is the forced one.
        var (folder, _, _) = shell.Preview.PreparePreviewPack(doc, scene.Id!, adjust: tryItAs.Apply);
        var written = PackJson.Deserialize<Cutscene>(File.ReadAllText(Path.Combine(folder, doc.FindCutscene(scene.Id!)!.File.Replace('/', Path.DirectorySeparatorChar))))!;
        Assert.Equal("FALSE", ((CheckBeat)written.Beats[0]).Cases[0].When);

        tryItAs.ClearCommand.Execute(null);
        Assert.Equal("Try it as…", tryItAs.ButtonText);
        Assert.Same(scene, tryItAs.Apply(scene));
    }
}
