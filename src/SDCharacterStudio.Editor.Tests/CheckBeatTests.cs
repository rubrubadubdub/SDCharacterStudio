using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Schema;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>
/// D-51 step 1 (the owner: "Can we have operators and conditionals?"): conditions are rows of pills over the game's own
/// condition text, and a Check beat goes one way or another.
/// </summary>
public sealed class CheckBeatTests
{
    [Theory]
    [InlineData("PLAYER_HEARTS Current Abigail 6", "hearts", "atLeast", "Abigail ≥ 6♥")]
    [InlineData("!PLAYER_HEARTS Current Abigail 6", "hearts", "lessThan", "Abigail < 6♥")]
    [InlineData("PLAYER_HEARTS Current Abigail 0 3", "hearts", "atMost", "Abigail ≤ 3♥")]
    [InlineData("PLAYER_HEARTS Current Abigail 4 4", "hearts", "exactly", "Abigail = 4♥")]
    [InlineData("PLAYER_HEARTS Current Abigail 2 8", "hearts", "between", "Abigail 2–8♥")]
    [InlineData("PLAYER_HAS_MAIL Current SDCS.Player.Pack.scene.trusted", "flag", "yes", "trusted set")]
    [InlineData("!PLAYER_HAS_MAIL Current SDCS.Player.Pack.scene.trusted", "flag", "no", "trusted not set")]
    [InlineData("PLAYER_HAS_ITEM Current (O)709", "item", "atLeast", "(O)709 ≥ 1")]
    [InlineData("!PLAYER_HAS_ITEM Current (O)709", "item", "lessThan", "no (O)709")]
    [InlineData("PLAYER_HAS_ITEM Current (O)709 10", "item", "atLeast", "(O)709 ≥ 10")]
    [InlineData("PLAYER_FISHING_LEVEL Current 5", "skill", "atLeast", "fishing ≥ 5")]
    [InlineData("SEASON Fall", "season", "yes", "fall")]
    [InlineData("!WEATHER Here Rain", "weather", "no", "not rain")]
    [InlineData("TIME 600 1200", "time", "atMost", "time ≤ 1200")]
    [InlineData("LOCATION_NAME Here Town", "location", "yes", "in Town")]
    [InlineData("RANDOM 0.25", "chance", "yes", "25% chance")]
    [InlineData("Player.SDCharacterStudio_HAS_MOD FlashShifter.StardewValleyExpandedCP", "mod", "yes", "FlashShifter.StardewValleyExpandedCP installed")]
    [InlineData("PLAYER_NPC_RELATIONSHIP Current Abigail Dating", "relationship", "yes", "Abigail dating")]
    [InlineData("IS_FESTIVAL_DAY", "festival", "yes", "festival day")]
    [InlineData("SOME_MOD_QUERY a b", "query", "atLeast", "SOME_MOD_QUERY a b")]
    public void ConditionTextBecomesRowsAndBackUnchanged(string text, string kind, string compare, string words)
    {
        var group = Conditions.Parse(text);
        var row = Assert.Single(group.Rows);
        Assert.Equal(kind, row.Kind.Id);
        if (kind != "query") Assert.Equal(compare, row.Compare);
        Assert.Equal(text, Conditions.Print(group));
        Assert.Equal(words, Conditions.Describe(text));
    }

    [Fact]
    public void AllAndAnyAndTextTheRowsCantShow()
    {
        var all = Conditions.Parse("PLAYER_HEARTS Current Abigail 6, PLAYER_HAS_MAIL Current Flag");
        Assert.False(all.Any);
        Assert.Equal(2, all.Rows.Count);
        all.Any = true;
        Assert.Equal("ANY \"PLAYER_HEARTS Current Abigail 6\" \"PLAYER_HAS_MAIL Current Flag\"", Conditions.Print(all));
        var any = Conditions.Parse(Conditions.Print(all));
        Assert.True(any.Any);
        Assert.Equal(new[] { "hearts", "flag" }, any.Rows.Select(r => r.Kind.Id));
        Assert.Equal("Abigail ≥ 6♥ or Flag set", Conditions.Describe(Conditions.Print(all)));

        // Kept word for word: a nested group, a location other than here, a negated ANY.
        foreach (string text in new[] { "ANY \"SEASON Fall, WEATHER Here Rain\" \"SEASON Winter\"", "WEATHER Farm Rain", "!ANY \"SEASON Fall\" \"SEASON Winter\"" })
            Assert.Equal(text, Conditions.Print(Conditions.Parse(text)));
        Assert.Equal("query", Conditions.Parse("WEATHER Farm Rain").Rows[0].Kind.Id);
        Assert.Null(Conditions.Print(Conditions.Parse("")));
    }

    [Fact]
    public void ACheckIsAddedWithItsBranchAndEditedAsRows()
    {
        using var editor = new EditorHarness();
        var doc = editor.NewProject();
        var shell = editor.Shell;
        var scene = shell.Scene!;
        shell.Stage.DropCharacter("Robin", 12, 10);
        doc.Edit("before", () => scene.Beats.Add(new MessageBeat { Text = "Hi." }));
        shell.Storyboard.Insert(LaneNames.Main, 1, BeatCatalog.Kind("Check"));

        // A new Check: one case (hearts with the scene's NPC) leading to its own new branch, otherwise keep going.
        var check = Assert.IsType<CheckBeat>(scene.Beats[1]);
        var @case = Assert.Single(check.Cases);
        Assert.Equal("PLAYER_HEARTS Current Robin 4", @case.When);
        Assert.True(scene.Branches.ContainsKey(@case.Then!));
        Assert.Null(check.Otherwise);
        Assert.Equal("if Robin ≥ 4♥", BeatCatalog.Summary(check));
        var problems = shell.ValidateNow();
        Assert.True(problems.Errors == 0, string.Join(" | ", problems.Problems.Select(p => p.JsonPath + ": " + p.Message)));

        // The inspector: rows of pills. Change the comparison and the value; add a story flag; switch to "any".
        var field = editor.Shell.Inspector.Fields.OfType<CheckField>().Single();
        var group = field.Cases[0].Group;
        var hearts = Assert.Single(group.Rows);
        hearts.Compare = hearts.CompareOptions.First(o => o.Value == "atMost");
        hearts.Value = "3";
        Assert.Equal("PLAYER_HEARTS Current Robin 0 3", @case.When);
        group.AddChoice = group.AddOptions.First(o => o.Value == "flag");
        group = editor.Shell.Inspector.Fields.OfType<CheckField>().Single().Cases[0].Group;   // rebuilt
        var flag = group.Rows[1];
        Assert.True(flag.SubjectIsTyped);
        flag.SubjectValue = "SDCS.Player.TestPack.first-scene.lied";
        Assert.Equal("PLAYER_HEARTS Current Robin 0 3, PLAYER_HAS_MAIL Current SDCS.Player.TestPack.first-scene.lied", @case.When);
        group.IsAny = true;
        Assert.StartsWith("ANY ", @case.When);
        Assert.Equal("if Robin ≤ 3♥ or lied set", BeatCatalog.Summary(check));

        // A second case and an otherwise branch; the card links every way out.
        field = editor.Shell.Inspector.Fields.OfType<CheckField>().Single();
        field.AddCaseCommand.Execute(null);
        Assert.Equal(2, check.Cases.Count);
        Assert.True(scene.Branches.ContainsKey(check.Cases[1].Then!));
        field = editor.Shell.Inspector.Fields.OfType<CheckField>().Single();
        field.Otherwise = field.OtherwiseOptions.First(o => o.Value == CheckField.NewBranch);
        Assert.NotNull(check.Otherwise);
        Assert.True(scene.Branches.ContainsKey(check.Otherwise!));
        var card = shell.Storyboard.Lanes.First(l => l.IsMain).Cards[1];
        Assert.Equal(3, card.Links.Count);
        Assert.Equal(check.Otherwise, card.Links[2].Target);
        Assert.Equal(0, shell.ValidateNow().Errors);

        // Renaming a branch follows into the Check; the branch says where it's reached from.
        string old = check.Cases[0].Then!;
        Assert.True(shell.Storyboard.RenameBranch(old, "softer"));
        Assert.Equal("softer", check.Cases[0].Then);
        Assert.Contains("check", shell.Storyboard.Lanes.First(l => l.Name == "softer").IncomingText);
    }

    [Fact]
    public void TheSceneTriggerUsesTheSameRows()
    {
        using var editor = new EditorHarness();
        var doc = editor.NewProject();
        var shell = editor.Shell;
        var scene = shell.Scene!;
        doc.Edit("trigger", () => scene.Trigger = new Trigger { Conditions = "Player.SDCharacterStudio_HAS_MOD FlashShifter.StardewValleyExpandedCP" });
        shell.Select(new SceneSelection(scene.Id!));
        var field = editor.Shell.Inspector.Fields.OfType<ConditionGroupField>().Single();
        var mod = Assert.Single(field.Group.Rows);
        Assert.Equal("a mod is installed", mod.KindLabel);
        mod.Compare = mod.CompareOptions.First(o => o.Value == "no");
        Assert.Equal("!Player.SDCharacterStudio_HAS_MOD FlashShifter.StardewValleyExpandedCP", scene.Trigger!.Conditions);
        // An install variant still reads it (doc 15 §3).
        Assert.Equal(("FlashShifter.StardewValleyExpandedCP", true), Project.InstallVariants.Of(scene));
        field.Group.QueryText = "SEASON Fall";
        Assert.Equal("SEASON Fall", scene.Trigger.Conditions);
        field = editor.Shell.Inspector.Fields.OfType<ConditionGroupField>().Single();
        field.Group.Rows[0].RemoveCommand.Execute(null);
        Assert.Null(scene.Trigger.Conditions);
    }

    [Fact]
    public void TheStageFollowsACheckIntoItsBranch()
    {
        var scene = new Cutscene
        {
            Id = "check",
            Actors = { new Actor { Id = "farmer", Kind = "Farmer", Start = new Position { Tile = new[] { 5, 5 } } } },
            Beats =
            {
                new MoveBeat { Actor = "farmer", To = new Position { Tile = new[] { 9, 5 } } },
                new CheckBeat { Cases = { new CheckCase { When = "SEASON Fall", Then = "fall" } }, Otherwise = "other" },
            },
            Branches = { ["fall"] = new() { new MoveBeat { Actor = "farmer", To = new Position { Tile = new[] { 9, 2 } } } }, ["other"] = new() { new WaitBeat { Duration = 100 } } },
        };
        var place = StageTimeline.Place(scene, scene.Actors[0], StageTimeline.Played(scene, new BeatSelection(scene.Id!, "fall", 0)));
        Assert.Equal((9, 2), (place.TileX, place.TileY));
        Assert.Equal((9, 5), (place.From!.TileX, place.From.TileY));
        Assert.Single(StageTimeline.Played(scene, new BeatSelection(scene.Id!, "other", 0)).Where(p => p.Lane == LaneNames.Main));
    }
}
