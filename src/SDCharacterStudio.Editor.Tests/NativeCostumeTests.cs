using System.IO;
using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Preview;
using SDCharacterStudio.Editor.Studio;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>
/// D-49 in the real game (the owner put earmuffs on the player and they "vanished"):
/// <list type="bullet">
/// <item>the game draws a costume outfit for the stage (the Bridge's RenderFarmer with a loadout);</item>
/// <item>a scene's costume stays on through Face, Shake and Wait, and comes off at the end only when the scene says so.</item>
/// </list>
/// Off by default: tools/native-bridge/Test-Costumes.ps1 starts an isolated game (the bench driver's disposable farm,
/// no save) and sets SDCS_NATIVE_PIPE, SDCS_NATIVE_DRIVER, SDCS_NATIVE_EXPORT and SDCS_NATIVE_SUITE=costume.
/// </summary>
public class NativeCostumeTests
{
    private readonly List<string> log = new();
    private string evidence = "";
    private void Note(string line) { log.Add($"{DateTime.Now:HH:mm:ss.fff} {line}"); File.WriteAllLines(Path.Combine(evidence, "editor-native-costume.log"), log); }

    [Fact]
    public async Task CostumesDrawForTheStageAndLastAsLongAsTheSceneSays()
    {
        string? pipe = Environment.GetEnvironmentVariable("SDCS_NATIVE_PIPE");
        string? driver = Environment.GetEnvironmentVariable("SDCS_NATIVE_DRIVER");
        string? export = Environment.GetEnvironmentVariable("SDCS_NATIVE_EXPORT");
        if (string.IsNullOrEmpty(pipe) || string.IsNullOrEmpty(driver) || string.IsNullOrEmpty(export) || Environment.GetEnvironmentVariable("SDCS_NATIVE_SUITE") != "costume") return;
        evidence = Environment.GetEnvironmentVariable("SDCS_NATIVE_EVIDENCE") ?? driver;
        Directory.CreateDirectory(evidence);

        using var editor = new EditorHarness(pipeName: pipe);
        var shell = editor.Shell;
        await WaitReady(shell);

        // ---------- the stage: the game draws the player in earmuffs ----------
        var earmuffs = await shell.Preview.Client.SendAsync(new JObject { ["Op"] = "RenderFarmer", ["Loadout"] = new JObject { ["Hat"] = "(H)11" } }, timeout: TimeSpan.FromMinutes(5));
        var bare = await shell.Preview.Client.SendAsync(new JObject { ["Op"] = "RenderFarmer", ["Loadout"] = new JObject { ["Hat"] = "none" } }, timeout: TimeSpan.FromMinutes(5));
        string withHat = Path.Combine(export, ((string)earmuffs["PartsFolder"]!).Replace('/', Path.DirectorySeparatorChar));
        string without = Path.Combine(export, ((string)bare["PartsFolder"]!).Replace('/', Path.DirectorySeparatorChar));
        Note($"rendered: earmuffs {earmuffs["PartsFolder"]}, none {bare["PartsFolder"]}");
        var hat = StudioImages.LoadFile(Path.Combine(withHat, "hat_down_000.png"))!;
        Xunit.Assert.False(StudioImages.IsEmpty(hat), "The earmuffs part is empty.");
        Xunit.Assert.True(StudioImages.IsEmpty(StudioImages.LoadFile(Path.Combine(without, "hat_down_000.png"))!));
        Xunit.Assert.NotEqual(StudioImages.LoadFile(Path.Combine(withHat, "full_down_000.png"))!.Pixels, StudioImages.LoadFile(Path.Combine(without, "full_down_000.png"))!.Pixels);
        Note($"earmuffs drawn: {hat.Pixels.Count(p => p >> 24 != 0)} pixels on the hat part");

        // ---------- the scene: earmuffs on, face down, shake, wait, end ----------
        var doc = editor.NewProject("Earmuffs", "Player.Earmuffs", "ScienceHouse");
        var scene = shell.Scene!;
        doc.Edit("Stage", () =>
        {
            scene.Actors[0].Start = new Position { Tile = new[] { 16, 9 } };
            scene.Actors[0].Facing = "up";
            // The editor here validates against the small test export, which knows the Cowboy Hat; any hat proves it.
            scene.Beats.Add(new CostumeBeat { Actor = "farmer", Set = new() { ["Hat"] = "(H)0" } });
            scene.Beats.Add(new FaceBeat { Actor = "farmer", Facing = "down" });
            scene.Beats.Add(new ShakeBeat { Actor = "farmer", Duration = 800 });
            scene.Beats.Add(new WaitBeat { Duration = 1500 });
        });
        var problems = shell.ValidateNow();
        Note("validation: " + (problems.Errors == 0 ? "no errors" : string.Join(" | ", problems.Problems.Select(p => p.Message))));
        var during = await PlayAndWatch(shell, driver, "default ending");
        Xunit.Assert.Contains("(H)0", during.Seen);
        Xunit.Assert.False(during.LostMidScene, "The earmuffs came off during the scene.");
        Xunit.Assert.Null(during.After);   // the scene puts the player's own look back (RestoreCostume)

        doc.Edit("Keep", () => scene.OnEnd.RestoreCostume = false);
        var kept = await PlayAndWatch(shell, driver, "keep the clothes");
        Xunit.Assert.False(kept.LostMidScene);
        Xunit.Assert.Equal("(H)0", kept.After);   // "Keep these clothes after the scene ends"
        Note("PASS");
    }

    private async Task WaitReady(MainViewModel shell)
    {
        var until = DateTime.UtcNow.AddMinutes(5);
        do { await shell.RefreshBridgeStatus(); if (shell.Bridge.State == BridgeState.Ready) break; await Task.Delay(1000); } while (DateTime.UtcNow < until);
        Xunit.Assert.Equal(BridgeState.Ready, shell.Bridge.State);
    }

    /// <summary>Play the scene from the start; sample the player's hat the whole time; return what it was after the scene.</summary>
    private async Task<(HashSet<string?> Seen, bool LostMidScene, string? After)> PlayAndWatch(MainViewModel shell, string driver, string label)
    {
        int toasts = ((FakeUi)shell.Ui).Toasts.Count;
        await shell.PreviewInGameCommand.ExecuteAsync(null);
        Note($"{label}: " + string.Join(" | ", ((FakeUi)shell.Ui).Toasts.Skip(toasts).Select(t => t.Title + ": " + t.Message)));
        var seen = new HashSet<string?>();
        bool started = false, wearing = false, lost = false;
        var deadline = DateTime.UtcNow.AddSeconds(40);
        while (DateTime.UtcNow < deadline)
        {
            JObject state;
            try
            {
                // Share everything so the driver can replace the file while we read it.
                using var stream = new FileStream(Path.Combine(driver, "state.json"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                state = JObject.Parse(new StreamReader(stream).ReadToEnd());
            }
            catch (Exception ex) when (ex is IOException or Newtonsoft.Json.JsonException) { await Task.Delay(30); continue; }
            bool up = (bool?)state["EventUp"] == true;
            string? hat = (string?)state["Hat"];
            if (up) { started = true; seen.Add(hat); if (hat == "(H)0") wearing = true; else if (wearing) lost = true; }
            if (started && !up && (bool?)state["Settled"] == true)
            {
                Note($"{label}: hats during the scene {string.Join(", ", seen.Select(h => h ?? "none"))}; after it: {hat ?? "none"}");
                return (seen, lost, hat);
            }
            await Task.Delay(40);
        }
        throw new TimeoutException($"{label}: the scene didn't play and end.");
    }
}
