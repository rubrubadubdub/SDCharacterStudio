using System.Text.RegularExpressions;
using AvalonDock.Layout;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Infrastructure;
using SDCharacterStudio.Editor.Schema;
using SDCharacterStudio.Editor.ViewModels;
using SDCharacterStudio.Editor.Workspace;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>
/// D-51 step 3 (the owner: "operators"): story numbers you add to, take from and compare, and the Story panel that
/// lists every flag and number with where it's set and where it's checked.
/// </summary>
public sealed class StoryNumberTests
{
    [Fact]
    public void ChangeANumberAndCheckIt()
    {
        using var editor = new EditorHarness();
        var doc = editor.NewProject();
        var shell = editor.Shell;
        var scene = shell.Scene!;
        shell.Storyboard.Insert(LaneNames.Main, 0, BeatCatalog.Kind("Number"));
        var number = Assert.IsType<NumberBeat>(scene.Beats[0]);
        Assert.Equal("jokes-told +1", BeatCatalog.Summary(number));

        // The fields: a name (typed names become content IDs), the change, the amount, an optional random top.
        editor.Field<ChoiceField>("Number").Text = "Times Robin Laughed";
        Assert.Equal("times-robin-laughed", number.Name);
        editor.Choose("Change", "subtract");
        editor.Field<NumberField>("Amount").Value = 2;
        editor.Field<NumberField>("Or a random amount up to").Value = 4;
        Assert.Equal("times-robin-laughed −2 to 4", BeatCatalog.Summary(number));
        Assert.Equal("2 to 4, the same if the day is replayed", editor.Field<NumberField>("Or a random amount up to").Hint);
        Assert.Equal(0, shell.ValidateNow().Errors);

        // A Check compares it: the "story number" row offers the project's numbers, and a typed name joins the pack's.
        shell.Storyboard.Insert(LaneNames.Main, 1, BeatCatalog.Kind("Check"));
        var check = (CheckBeat)scene.Beats[1];
        var group = editor.Shell.Inspector.Fields.OfType<CheckField>().Single().Cases[0].Group;
        group.Rows[0].RemoveCommand.Execute(null);
        group = editor.Shell.Inspector.Fields.OfType<CheckField>().Single().Cases[0].Group;
        group.AddChoice = group.AddOptions.First(o => o.Value == "number");
        group = editor.Shell.Inspector.Fields.OfType<CheckField>().Single().Cases[0].Group;
        var row = group.Rows[0];
        Assert.Equal($"SDCS.{doc.PackId}.times-robin-laughed", row.SubjectValue);
        Assert.Equal($"PLAYER_STAT Current SDCS.{doc.PackId}.times-robin-laughed 1", check.Cases[0].When);
        row.SubjectValue = "Visits";
        Assert.Equal($"PLAYER_STAT Current SDCS.{doc.PackId}.visits 1", check.Cases[0].When);
        Assert.Equal("if visits ≥ 1", BeatCatalog.Summary(check));
    }

    [Fact]
    public void TheStoryPanelListsFlagsAndNumbersAndWhatLooksWrong()
    {
        using var editor = new EditorHarness();
        var doc = editor.NewProject();
        var shell = editor.Shell;
        var scene = shell.Scene!;
        string flag = $"SDCS.{doc.PackId}.{scene.Id}.trusts-you";
        doc.Edit("story", () =>
        {
            scene.Beats.Add(new MailBeat { Add = flag });
            scene.Beats.Add(new NumberBeat { Name = "jokes-told", Amount = 1 });
            scene.Beats.Add(new CheckBeat
            {
                Cases =
                {
                    new CheckCase { When = $"PLAYER_HAS_MAIL Current {flag}, PLAYER_STAT Current SDCS.{doc.PackId}.jokes-told 3", Then = "close" },
                    new CheckCase { When = $"PLAYER_HAS_MAIL Current {flag.Replace("trusts", "trust")}", Then = "close" },   // a typo
                },
            });
            scene.Branches["close"] = new() { new WaitBeat { Duration = 1 } };
            scene.Trigger = new Trigger { Conditions = $"PLAYER_STAT Current SDCS.{doc.PackId}.visits 2" };   // checked, never changed
        });
        var story = shell.Story;
        var trusts = story.Entries.Single(e => e.Id == flag);
        Assert.Equal("trusts-you", trusts.Label);
        Assert.Equal("set in 1 place · checked in 1 place", trusts.Summary);
        Assert.False(trusts.HasWarning);
        var typo = story.Entries.Single(e => e.Id == flag.Replace("trusts", "trust"));
        Assert.True(typo.IsWarning);
        Assert.Contains("A typo", typo.Warning);
        var jokes = story.Entries.Single(e => e.Label == "jokes-told");
        Assert.True(jokes.IsNumber);
        Assert.Contains("jokes-told +1", jokes.Sets[0].Text);
        Assert.Contains("Nothing changes it", story.Entries.Single(e => e.Label == "visits").Warning);
        Assert.Equal(2, story.WarningCount);

        // A note sticks to the project; "Go there" opens the beat.
        jokes.Note = "How many of Abigail's jokes the player sat through.";
        Assert.Equal("How many of Abigail's jokes the player sat through.", doc.Project.StoryNotes[jokes.Id]);
        Assert.Equal("How many of Abigail's jokes the player sat through.", shell.Story.Entries.Single(e => e.Label == "jokes-told").Note);
        shell.Story.Entries.Single(e => e.Label == "jokes-told").Sets[0].Go.Execute(null);
        Assert.Equal(new BeatSelection(scene.Id!, LaneNames.Main, 1), shell.Selection);
    }

    [Fact]
    public void ALayoutSavedBeforeTheStoryPanelKeepsItsShapeAndGainsIt() => EditorUiThread.Run(() =>
    {
        using var harness = new EditorHarness();
        var window = new MainWindow();
        window.Attach(harness.Shell, new WpfUserInterface(window));
        try
        {
            var host = window.WorkspaceHost!;
            host.Find(WorkspacePaneIds.Assets)!.FloatingWidth = 777;   // something of the owner's own to keep
            // Remove the Story panel from the saved layout, as a layout saved before it existed.
            string xml = Regex.Replace(host.Capture(), "<LayoutAnchorable[^>]*ContentId=\"tool.story\"[^>]*/>", "");
            Assert.DoesNotContain("\"tool.story\"", xml);
            Assert.True(host.Restore(xml, out var error), error);
            var story = Assert.IsType<LayoutAnchorable>(host.Find(WorkspacePaneIds.Story));
            Assert.Same(host.Find(WorkspacePaneIds.Storyboard)!.Parent, story.Parent);
            Assert.Equal(777, host.Find(WorkspacePaneIds.Assets)!.FloatingWidth);   // not a reset
        }
        finally { window.Close(); }
    });
}
