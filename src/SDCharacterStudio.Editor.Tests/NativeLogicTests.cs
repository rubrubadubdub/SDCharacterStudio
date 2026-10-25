using System.IO;
using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Preview;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>
/// Scene logic in the real game (D-51): a Check takes the branch its conditions say, using the game's own conditions;
/// "keep going" carries on after it. Each branch sets a story flag, and the driver reports the flags afterwards.
/// Off by default: tools/native-bridge/Test-Costumes.ps1 -Suite logic starts an isolated game (the bench driver's
/// disposable farm, no save) and sets SDCS_NATIVE_PIPE, SDCS_NATIVE_DRIVER and SDCS_NATIVE_SUITE=logic.
/// </summary>
public class NativeLogicTests
{
    private const string Prefix = "SDCS.Player.Logic.";
    private readonly List<string> log = new();
    private string evidence = "", logFile = "editor-native-logic.log";
    private void Note(string line) { log.Add($"{DateTime.Now:HH:mm:ss.fff} {line}"); File.WriteAllLines(Path.Combine(evidence, logFile), log); }

    [Fact]
    public async Task ChecksTakeTheBranchesTheirConditionsSay()
    {
        string? pipe = Environment.GetEnvironmentVariable("SDCS_NATIVE_PIPE");
        string? driver = Environment.GetEnvironmentVariable("SDCS_NATIVE_DRIVER");
        if (string.IsNullOrEmpty(pipe) || string.IsNullOrEmpty(driver) || Environment.GetEnvironmentVariable("SDCS_NATIVE_SUITE") != "logic") return;
        evidence = Environment.GetEnvironmentVariable("SDCS_NATIVE_EVIDENCE") ?? driver;
        Directory.CreateDirectory(evidence);

        using var editor = new EditorHarness(pipeName: pipe);
        var shell = editor.Shell;
        await WaitReady(shell);
        var doc = editor.NewProject("Logic", "Player.Logic", "ScienceHouse");
        var scene = shell.Scene!;
        string Flag(string name) => Prefix + scene.Id + "." + name;
        doc.Edit("Stage", () =>
        {
            scene.Actors[0].Start = new Position { Tile = new[] { 16, 9 } };
            scene.Beats.Add(new MailBeat { Add = Flag("knows") });
            // Case 1 is false (it's spring on the bench farm); case 2 is true (the flag just set), so it wins.
            scene.Beats.Add(new CheckBeat
            {
                Cases =
                {
                    new CheckCase { When = "SEASON Winter", Then = "winter" },
                    new CheckCase { When = $"PLAYER_HAS_MAIL Current {Flag("knows")}, !PLAYER_HAS_MAIL Current {Flag("never")}", Then = "knows" },
                },
                Otherwise = "neither",
            });
            scene.Beats.Add(new MailBeat { Add = Flag("after-first") });
            // Nothing true and nothing to go to: keep going.
            scene.Beats.Add(new CheckBeat { Cases = { new CheckCase { When = "ANY \"SEASON Winter\" \"PLAYER_HAS_MAIL Current " + Flag("never") + "\"", Then = "winter" } } });
            scene.Beats.Add(new MailBeat { Add = Flag("kept-going") });
            scene.Branches["winter"] = new() { new MailBeat { Add = Flag("went-winter") } };
            scene.Branches["knows"] = new() { new MailBeat { Add = Flag("went-knows") } };
            scene.Branches["neither"] = new() { new MailBeat { Add = Flag("went-neither") } };
        });
        var problems = shell.ValidateNow();
        Note("validation: " + (problems.Errors == 0 ? "no errors" : string.Join(" | ", problems.Problems.Select(p => p.Message))));
        Xunit.Assert.Equal(0, problems.Errors);

        int toasts = ((FakeUi)shell.Ui).Toasts.Count;
        await shell.PreviewInGameCommand.ExecuteAsync(null);
        Note("preview: " + string.Join(" | ", ((FakeUi)shell.Ui).Toasts.Skip(toasts).Select(t => t.Title + ": " + t.Message)));
        var flags = await FlagsAfterTheScene(driver);
        Note("flags after the scene: " + string.Join(", ", flags));
        Xunit.Assert.Contains(Flag("went-knows"), flags);
        Xunit.Assert.DoesNotContain(Flag("went-winter"), flags);
        Xunit.Assert.DoesNotContain(Flag("went-neither"), flags);
        Xunit.Assert.Contains(Flag("after-first"), flags);   // the branch came back to the beats after the Check
        Xunit.Assert.Contains(Flag("kept-going"), flags);    // no case true, no otherwise: keep going
        Note("PASS");
    }

    [Fact]
    public async Task ChanceAndRandomItemsPickByTheirChances()
    {
        string? pipe = Environment.GetEnvironmentVariable("SDCS_NATIVE_PIPE");
        string? driver = Environment.GetEnvironmentVariable("SDCS_NATIVE_DRIVER");
        if (string.IsNullOrEmpty(pipe) || string.IsNullOrEmpty(driver) || Environment.GetEnvironmentVariable("SDCS_NATIVE_SUITE") != "logic") return;
        evidence = Environment.GetEnvironmentVariable("SDCS_NATIVE_EVIDENCE") ?? driver;
        logFile = "editor-native-dice.log";
        Directory.CreateDirectory(evidence);

        using var editor = new EditorHarness(pipeName: pipe);
        var shell = editor.Shell;
        await WaitReady(shell);
        var doc = editor.NewProject("Dice", "Player.Dice", "ScienceHouse", "Roll");
        var scene = shell.Scene!;
        string Flag(string name) => "SDCS.Player.Dice." + scene.Id + "." + name;
        doc.Edit("Stage", () =>
        {
            scene.Actors[0].Start = new Position { Tile = new[] { 16, 9 } };
            // Only the second outcome can happen, and only the Gold Bar can be given: the picks are certain.
            // (Items the editor's test export knows, so preview isn't blocked.)
            scene.Beats.Add(new ChanceBeat { Outcomes = { new ChanceOutcome { Weight = 0, Then = "one" }, new ChanceOutcome { Weight = 3, Then = "two" }, new ChanceOutcome { Weight = 0, Then = "three" } } });
            scene.Beats.Add(new ItemBeat { OneOf = new() { new ItemChoice { Item = "(O)709", Weight = 0 }, new ItemChoice { Item = "(O)336", Weight = 1 } }, Remember = true });
            scene.Beats.Add(new MailBeat { Add = Flag("done") });
            foreach (string branch in new[] { "one", "two", "three" }) scene.Branches[branch] = new() { new MailBeat { Add = Flag("went-" + branch) } };
        });
        var problems = shell.ValidateNow();
        Note("dice validation: " + (problems.Errors == 0 ? "no errors" : string.Join(" | ", problems.Problems.Select(p => p.Message))));
        Xunit.Assert.Equal(0, problems.Errors);

        int toasts = ((FakeUi)shell.Ui).Toasts.Count;
        await shell.PreviewInGameCommand.ExecuteAsync(null);
        Note("dice preview: " + string.Join(" | ", ((FakeUi)shell.Ui).Toasts.Skip(toasts).Select(t => t.Title + ": " + t.Message)));
        var flags = await FlagsAfterTheScene(driver);
        Note("dice flags after the scene: " + string.Join(", ", flags));
        Xunit.Assert.Contains(Flag("went-two"), flags);
        Xunit.Assert.DoesNotContain(Flag("went-one"), flags);
        Xunit.Assert.DoesNotContain(Flag("went-three"), flags);
        Xunit.Assert.Contains(Flag("got-O336"), flags);     // the Gold Bar, remembered
        Xunit.Assert.DoesNotContain(Flag("got-O709"), flags);
        Xunit.Assert.Contains(Flag("done"), flags);
        Note("dice PASS");
    }

    [Fact]
    public async Task StoryNumbersChangeInTheSaveAndChecksCompareThem()
    {
        string? pipe = Environment.GetEnvironmentVariable("SDCS_NATIVE_PIPE");
        string? driver = Environment.GetEnvironmentVariable("SDCS_NATIVE_DRIVER");
        if (string.IsNullOrEmpty(pipe) || string.IsNullOrEmpty(driver) || Environment.GetEnvironmentVariable("SDCS_NATIVE_SUITE") != "logic") return;
        evidence = Environment.GetEnvironmentVariable("SDCS_NATIVE_EVIDENCE") ?? driver;
        logFile = "editor-native-numbers.log";
        Directory.CreateDirectory(evidence);

        using var editor = new EditorHarness(pipeName: pipe);
        var shell = editor.Shell;
        await WaitReady(shell);
        var doc = editor.NewProject("Count", "Player.Count", "ScienceHouse", "Tally");
        var scene = shell.Scene!;
        string Flag(string name) => "SDCS.Player.Count." + scene.Id + "." + name;
        const string Stat = "SDCS.Player.Count.tally";
        doc.Edit("Stage", () =>
        {
            scene.Actors[0].Start = new Position { Tile = new[] { 16, 9 } };
            scene.Beats.Add(new NumberBeat { Name = "tally", Change = "add", Amount = 2 });
            scene.Beats.Add(new NumberBeat { Name = "tally", Change = "subtract", Amount = 1 });
            scene.Beats.Add(new NumberBeat { Name = "floor", Change = "subtract", Amount = 5 });   // never below 0
            scene.Beats.Add(new CheckBeat { Cases = { new CheckCase { When = $"PLAYER_STAT Current {Stat} 1 1", Then = "one" } }, Otherwise = "other" });
            scene.Branches["one"] = new() { new MailBeat { Add = Flag("went-one") } };
            scene.Branches["other"] = new() { new MailBeat { Add = Flag("went-other") } };
        });
        var problems = shell.ValidateNow();
        Note("numbers validation: " + (problems.Errors == 0 ? "no errors" : string.Join(" | ", problems.Problems.Select(p => p.Message))));
        Xunit.Assert.Equal(0, problems.Errors);

        await shell.PreviewInGameCommand.ExecuteAsync(null);
        var flags = await FlagsAfterTheScene(driver);
        var stats = (await State(driver))?["Stats"] as JObject ?? new JObject();
        Note($"numbers: flags {string.Join(", ", flags)}; stats {stats.ToString(Newtonsoft.Json.Formatting.None)}");
        Xunit.Assert.Contains(Flag("went-one"), flags);   // 2 − 1 = exactly 1
        Xunit.Assert.DoesNotContain(Flag("went-other"), flags);
        Xunit.Assert.Equal(1, (int?)stats[Stat]);
        Xunit.Assert.Null(stats["SDCS.Player.Count.floor"]);   // 0 isn't stored
        Note("numbers PASS");
    }

    [Fact]
    public async Task AnswersOnlyIfAndTryItAsShapeThePreview()
    {
        string? pipe = Environment.GetEnvironmentVariable("SDCS_NATIVE_PIPE");
        string? driver = Environment.GetEnvironmentVariable("SDCS_NATIVE_DRIVER");
        if (string.IsNullOrEmpty(pipe) || string.IsNullOrEmpty(driver) || Environment.GetEnvironmentVariable("SDCS_NATIVE_SUITE") != "logic") return;
        evidence = Environment.GetEnvironmentVariable("SDCS_NATIVE_EVIDENCE") ?? driver;
        logFile = "editor-native-try.log";
        Directory.CreateDirectory(evidence);

        using var editor = new EditorHarness(pipeName: pipe);
        var shell = editor.Shell;
        await WaitReady(shell);
        var doc = editor.NewProject("Try", "Player.Try", "ScienceHouse", "Ask");
        var scene = shell.Scene!;
        string Flag(string name) => "SDCS.Player.Try." + scene.Id + "." + name;
        doc.Edit("Stage", () =>
        {
            scene.Actors[0].Start = new Position { Tile = new[] { 16, 9 } };
            // Its only answer is never offered, so there's no question: the scene goes on after it.
            scene.Beats.Add(new ChoiceBeat { Prompt = "Hm?", Options = { new ChoiceOption { Text = "Never shown", Then = "never", When = "FALSE" } } });
            scene.Beats.Add(new MailBeat { Add = Flag("after-choice") });
            // False on the bench farm (spring), but Try it as… picks it.
            scene.Beats.Add(new CheckBeat { Cases = { new CheckCase { When = "SEASON Winter", Then = "winter" } } });
            // The Hardwood's "only if" is false, so the Gold Bar is the one given.
            scene.Beats.Add(new ItemBeat { OneOf = new() { new ItemChoice { Item = "(O)709", Weight = 50, When = "FALSE" }, new ItemChoice { Item = "(O)336", Weight = 1 } }, Remember = true });
            scene.Branches["never"] = new() { new MailBeat { Add = Flag("went-never") } };
            scene.Branches["winter"] = new() { new MailBeat { Add = Flag("went-winter") } };
        });
        var problems = shell.ValidateNow();
        Note("try validation: " + (problems.Errors == 0 ? "no errors" : string.Join(" | ", problems.Problems.Select(p => p.Message))));
        Xunit.Assert.Equal(0, problems.Errors);
        shell.TryItAs.Refresh();
        var check = shell.TryItAs.Entries.Single(e => e.Title.StartsWith("Check"));
        check.Selected = check.Options[1];   // case 1: the winter branch

        await shell.PreviewInGameCommand.ExecuteAsync(null);
        var flags = await FlagsAfterTheScene(driver);
        Note("try flags after the scene: " + string.Join(", ", flags));
        Xunit.Assert.Contains(Flag("after-choice"), flags);
        Xunit.Assert.DoesNotContain(Flag("went-never"), flags);
        Xunit.Assert.Contains(Flag("went-winter"), flags);
        Xunit.Assert.Contains(Flag("got-O336"), flags);
        Xunit.Assert.DoesNotContain(Flag("got-O709"), flags);
        Note("try PASS");
    }

    private static async Task WaitReady(MainViewModel shell)
    {
        var until = DateTime.UtcNow.AddMinutes(5);
        do { await shell.RefreshBridgeStatus(); if (shell.Bridge.State == BridgeState.Ready) break; await Task.Delay(1000); } while (DateTime.UtcNow < until);
        Xunit.Assert.Equal(BridgeState.Ready, shell.Bridge.State);
    }

    internal static async Task<JObject?> State(string driver)
    {
        try
        {
            using var stream = new FileStream(Path.Combine(driver, "state.json"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JObject.Parse(await new StreamReader(stream).ReadToEndAsync());
        }
        catch (Exception ex) when (ex is IOException or Newtonsoft.Json.JsonException) { return null; }
    }

    /// <summary>Wait for the scene to start and end, then return the SDCS story flags the player has.</summary>
    internal static async Task<HashSet<string>> FlagsAfterTheScene(string driver)
    {
        bool started = false;
        var deadline = DateTime.UtcNow.AddSeconds(40);
        while (DateTime.UtcNow < deadline)
        {
            if (await State(driver) is { } state)
            {
                bool up = (bool?)state["EventUp"] == true;
                started |= up;
                if (started && !up && (bool?)state["Settled"] == true)
                    return new HashSet<string>((state["Mail"] as JArray ?? new JArray()).Select(m => (string)m!), StringComparer.Ordinal);
            }
            await Task.Delay(50);
        }
        throw new TimeoutException(started ? "The scene didn't end." : "The scene didn't start.");
    }
}
