using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Schema;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>
/// D-51 step 2 (the owner: "a bucket of items that can be randomly given", "random set of messages that leads to different
/// outcomes"): Chance beats, a random item bucket, and lines picked at random.
/// </summary>
public sealed class ChanceBeatTests
{
    [Fact]
    public void AChanceComesWithTwoOutcomesAndShowsItsOddsAsPercentages()
    {
        using var editor = new EditorHarness();
        editor.NewProject();
        var shell = editor.Shell;
        var scene = shell.Scene!;
        shell.Storyboard.Insert(LaneNames.Main, 0, BeatCatalog.Kind("Chance"));
        var chance = Assert.IsType<ChanceBeat>(scene.Beats[0]);
        Assert.Equal(new[] { "outcome-1", "outcome-2" }, chance.Outcomes.Select(o => o.Then));
        Assert.All(chance.Outcomes, o => Assert.True(scene.Branches.ContainsKey(o.Then!)));
        Assert.Equal("2 outcomes: outcome-1 50% · outcome-2 50%", BeatCatalog.Summary(chance));
        Assert.Equal(0, shell.ValidateNow().Errors);

        // The inspector: each outcome's branch and chance, with the percentage beside it.
        var outcomes = editor.Field<ListField>("Outcomes");
        ((NumberField)outcomes.Rows[0].Cells[1]).Value = 3;
        outcomes = editor.Field<ListField>("Outcomes");   // rebuilt: every percentage follows
        Assert.Equal("75% of the time", ((NumberField)outcomes.Rows[0].Cells[1]).Hint);
        Assert.Equal("25% of the time", ((NumberField)outcomes.Rows[1].Cells[1]).Hint);
        outcomes.AddCommand.Execute(null);
        Assert.Equal(3, chance.Outcomes.Count);
        Assert.True(scene.Branches.ContainsKey(chance.Outcomes[2].Then!));
        editor.Shell.Inspector.Fields.OfType<ActionField>().Single(a => a.ButtonText == "Equal chances").Action.Execute(null);
        Assert.All(chance.Outcomes, o => Assert.Equal(1, o.Weight));
        var replay = editor.Field<BoolField>("Same result if the day is replayed");
        Assert.True(replay.Value);
        replay.Value = false;
        Assert.True(chance.Fresh);

        // The card links each outcome to its branch; renaming follows into the Chance.
        var card = shell.Storyboard.Lanes.First(l => l.IsMain).Cards[0];
        Assert.Equal(new[] { "33% chance", "33% chance", "33% chance" }, card.Links.Select(l => l.Label));
        Assert.True(shell.Storyboard.RenameBranch("outcome-1", "laughs"));
        Assert.Equal("laughs", chance.Outcomes[0].Then);
        Assert.Contains("chance", shell.Storyboard.Lanes.First(l => l.Name == "laughs").IncomingText);
    }

    [Fact]
    public void GiveAnItemCanGiveOneOfABucket()
    {
        using var editor = new EditorHarness();
        var doc = editor.NewProject();
        var shell = editor.Shell;
        var scene = shell.Scene!;
        doc.Edit("item", () => scene.Beats.Add(new ItemBeat { Item = "(O)709", Count = 3 }));
        shell.Select(new BeatSelection(scene.Id!, LaneNames.Main, 0));
        var item = (ItemBeat)scene.Beats[0];

        editor.Field<BoolField>("One of these, at random").Value = true;
        Assert.Equal(new[] { "(O)709", "(O)74" }, item.OneOf!.Select(c => c.Item));
        Assert.Equal(3, item.OneOf[0].Count);   // the item it had stays, with its count
        var items = editor.Field<ListField>("Items");
        ((ItemField)items.Rows[1].Cells[0]).Value = "(O)336";   // one the small test export has
        ((NumberField)items.Rows[0].Cells[2]).Value = 4;
        items = editor.Field<ListField>("Items");
        Assert.Equal("80% of the time", ((NumberField)items.Rows[0].Cells[2]).Hint);
        Assert.Equal($"one of 2: Hardwood 80% · {shell.Catalog!.Item("(O)336")!.Label} 20%", BeatCatalog.Summary(item, shell.Catalog));
        editor.Field<BoolField>("Remember which one (a story flag per item)").Value = true;
        Assert.True(item.Remember);
        Assert.Contains(ConditionContext.ProjectFlags(shell.Document), f => f.Value == $"SDCS.{shell.Document!.PackId}.{scene.Id}.got-O709");
        var problems = shell.ValidateNow();
        Assert.True(problems.Errors == 0, string.Join(" | ", problems.Problems.Select(p => p.JsonPath + ": " + p.Message)));

        // Back to one item: the first of the bucket.
        editor.Field<BoolField>("One of these, at random").Value = false;
        Assert.Null(item.OneOf);
        Assert.Equal(("(O)709", 3), (item.Item, item.Count));
        Assert.False(item.Remember);
    }

    [Fact]
    public void SayAndMessageCanPickALineAtRandom()
    {
        using var editor = new EditorHarness();
        var doc = editor.NewProject();
        var shell = editor.Shell;
        var scene = shell.Scene!;
        shell.Stage.DropCharacter("Robin", 12, 10);
        doc.Edit("say", () => scene.Beats.Add(new SayBeat { Actor = "Robin", Text = "See you around." }));
        shell.Select(new BeatSelection(scene.Id!, LaneNames.Main, 0));
        var say = (SayBeat)scene.Beats[0];
        var lines = editor.Field<ListField>("Or one of these lines, at random");
        Assert.Empty(lines.Rows);
        lines.AddCommand.Execute(null);
        lines = editor.Field<ListField>("Or one of these lines, at random");
        ((TranslatableTextField)lines.Rows[0].Cells[0]).Text = "Don't get eaten by slimes.";
        Assert.Equal("Don't get eaten by slimes.", say.Or![0].Literal);
        Assert.Equal("Robin: “See you around.” (1 of 2 lines)", BeatCatalog.Summary(say));
        Assert.Equal(0, shell.ValidateNow().Errors);
        lines.Rows[0].RemoveCommand.Execute(null);
        Assert.Null(say.Or);
    }
}
