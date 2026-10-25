using System.IO;
using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Preview;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>
/// The camera in the real game (the owner: "the camera panned itself over to the farmer then did a jump back to where it
/// started then faded to black"). A scene in Town with a fixed camera; the player walks out of view and the scene ends with
/// "stay". While any of it is on screen the camera only ever glides: no step between two samples is a leap. A Moon Table
/// the scene places (D-50) is drawn while it plays and stays until the screen is black.
/// Off by default: tools/native-bridge/Test-Costumes.ps1 -Suite camera starts an isolated game (the bench driver's
/// disposable farm, no save) and sets SDCS_NATIVE_PIPE, SDCS_NATIVE_DRIVER and SDCS_NATIVE_SUITE=camera.
/// </summary>
public class NativeCameraTests
{
    /// <summary>A glide moves at most about 100 px between two samples; the old snap back was well over 1000.</summary>
    private const int Leap = 400;
    private readonly List<string> log = new();
    private string evidence = "";
    private void Note(string line) { log.Add($"{DateTime.Now:HH:mm:ss.fff} {line}"); File.WriteAllLines(Path.Combine(evidence, "editor-native-camera.log"), log); }

    private sealed record Sample(int Tick, bool EventUp, int X, int Y, double Fade, string? Location, JArray? PlayerTile, int Furniture)
    {
        public bool Visible => Fade < 0.9;
    }

    [Fact]
    public async Task TheCameraNeverLeapsWhileTheSceneIsOnScreen()
    {
        string? pipe = Environment.GetEnvironmentVariable("SDCS_NATIVE_PIPE");
        string? driver = Environment.GetEnvironmentVariable("SDCS_NATIVE_DRIVER");
        if (string.IsNullOrEmpty(pipe) || string.IsNullOrEmpty(driver) || Environment.GetEnvironmentVariable("SDCS_NATIVE_SUITE") != "camera") return;
        evidence = Environment.GetEnvironmentVariable("SDCS_NATIVE_EVIDENCE") ?? driver;
        Directory.CreateDirectory(evidence);

        using var editor = new EditorHarness(pipeName: pipe);
        var shell = editor.Shell;
        await WaitReady(shell);

        // Walk into Town first: the scene plays where the player is, so "stay" leaves them where it put them.
        await Ask(driver, "warp", new JObject { ["Location"] = "Town", ["X"] = 44, ["Y"] = 60, ["Facing"] = 1 });
        await Until(driver, s => s["Location"]?.ToString() == "Town" && (bool?)s["Settled"] == true, "the player in Town");

        var doc = editor.NewProject("Camera", "Player.Camera", "Town");
        var scene = shell.Scene!;
        doc.Edit("Stage", () =>
        {
            scene.Actors[0].Start = new Position { Tile = new[] { 44, 60 } };
            scene.Actors[0].Facing = "right";
            scene.Camera = new JObject { ["Tile"] = new JArray(25, 60) };   // fixed, well away from where the player ends up
            scene.Beats.Add(new PropBeat { Item = "(F)1130", At = new Position { Tile = new[] { 47, 59 } } });   // a Moon Table by the road
            scene.Beats.Add(new MoveBeat { Actor = "farmer", To = new Position { Tile = new[] { 52, 60 } } });
            scene.Beats.Add(new WaitBeat { Duration = 600 });
            scene.OnEnd.End = "stay";
        });
        var problems = shell.ValidateNow();
        Note("validation: " + (problems.Errors == 0 ? "no errors" : string.Join(" | ", problems.Problems.Select(p => p.Message))));

        int toasts = ((FakeUi)shell.Ui).Toasts.Count;
        await shell.PreviewInGameCommand.ExecuteAsync(null);
        Note("preview: " + string.Join(" | ", ((FakeUi)shell.Ui).Toasts.Skip(toasts).Select(t => t.Title + ": " + t.Message)));
        var samples = await Record(driver);
        File.WriteAllLines(Path.Combine(evidence, "camera-samples.txt"),
            samples.Select(s => $"{s.Tick}\t{(s.EventUp ? "event" : "-")}\t{s.X},{s.Y}\tfade {s.Fade:0.00}\t{s.Location}\t{s.PlayerTile?.ToString(Newtonsoft.Json.Formatting.None)}\tfurniture {s.Furniture}"));

        // The scene panned: the last view of it is on the player, not where it started.
        var lastInScene = samples.Last(s => s.EventUp && s.Visible);
        var firstInScene = samples.First(s => s.EventUp && s.Visible);
        Note($"scene opened at {firstInScene.X},{firstInScene.Y}; last seen at {lastInScene.X},{lastInScene.Y}");
        Xunit.Assert.True(Math.Abs(lastInScene.X - firstInScene.X) > 640, "The camera never went to the player before the end.");

        // No leap between two samples while anything is on screen: during the scene, at its end, or after the fade.
        var leaps = samples.Zip(samples.Skip(1))
            .Where(p => p.First.Visible && p.Second.Visible && p.First.Location == p.Second.Location)
            .Select(p => (p.First, p.Second, Step: Math.Abs(p.Second.X - p.First.X) + Math.Abs(p.Second.Y - p.First.Y)))
            .Where(p => p.Step > Leap).ToList();
        foreach (var leap in leaps) Note($"LEAP {leap.Step}px at tick {leap.Second.Tick}: {leap.First.X},{leap.First.Y} -> {leap.Second.X},{leap.Second.Y} (event {leap.First.EventUp}->{leap.Second.EventUp})");
        Xunit.Assert.Empty(leaps);

        // After the fade the game shows the player where the scene left them: the same view as its last moment.
        var afterwards = samples.Last(s => !s.EventUp && s.Visible);
        Note($"after the scene: {afterwards.X},{afterwards.Y}; player at {afterwards.PlayerTile?.ToString(Newtonsoft.Json.Formatting.None)}");
        Xunit.Assert.True(Math.Abs(afterwards.X - lastInScene.X) + Math.Abs(afterwards.Y - lastInScene.Y) <= 128,
            $"The view after the scene ({afterwards.X},{afterwards.Y}) isn't its last view ({lastInScene.X},{lastInScene.Y}).");

        // The Moon Table: on the map during the scene, never gone while the scene is still on screen, gone afterwards.
        Xunit.Assert.Contains(samples, s => s.EventUp && s.Furniture > 0);
        var vanished = samples.Zip(samples.Skip(1)).FirstOrDefault(p => p.First.Furniture > 0 && p.Second.Furniture == 0);
        Note($"the Moon Table: on in {samples.Count(s => s.Furniture > 0)} samples; gone at tick {vanished.Second?.Tick} (fade {vanished.Second?.Fade:0.00}, event {vanished.Second?.EventUp})");
        Xunit.Assert.NotNull(vanished.Second);
        Xunit.Assert.False(vanished.First.Visible && vanished.Second.Visible, "The Moon Table vanished while the scene was on screen.");
        Xunit.Assert.Equal(0, samples[^1].Furniture);
        Note("PASS");
    }

    private static async Task WaitReady(MainViewModel shell)
    {
        var until = DateTime.UtcNow.AddMinutes(5);
        do { await shell.RefreshBridgeStatus(); if (shell.Bridge.State == BridgeState.Ready) break; await Task.Delay(1000); } while (DateTime.UtcNow < until);
        Xunit.Assert.Equal(BridgeState.Ready, shell.Bridge.State);
    }

    private static async Task<JObject?> State(string driver)
    {
        try
        {
            // Share everything so the driver can replace the file while we read it.
            using var stream = new FileStream(Path.Combine(driver, "state.json"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JObject.Parse(await new StreamReader(stream).ReadToEndAsync());
        }
        catch (Exception ex) when (ex is IOException or Newtonsoft.Json.JsonException) { return null; }
    }

    private static async Task Until(string driver, Func<JObject, bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (await State(driver) is { } state && condition(state)) return;
            await Task.Delay(100);
        }
        throw new TimeoutException("Never saw " + what + ".");
    }

    /// <summary>A driver request (assertions/&lt;id&gt;.json) and its answer.</summary>
    private static async Task<JObject> Ask(string driver, string check, JObject request)
    {
        string id = "camera-" + Guid.NewGuid().ToString("N");
        request["Check"] = check;
        File.WriteAllText(Path.Combine(driver, "assertions", id + ".json.tmp"), request.ToString());
        File.Move(Path.Combine(driver, "assertions", id + ".json.tmp"), Path.Combine(driver, "assertions", id + ".json"));
        string result = Path.Combine(driver, "assertion-results", id + ".json");
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(result))
            {
                var answer = JObject.Parse(File.ReadAllText(result));
                if ((bool?)answer["Ok"] != true) throw new InvalidOperationException($"Driver {check}: {answer["Error"]}");
                return answer;
            }
            await Task.Delay(100);
        }
        throw new TimeoutException($"Driver {check}: no answer.");
    }

    /// <summary>Every state the driver writes from before the scene until the game has settled after it.</summary>
    private static async Task<List<Sample>> Record(string driver)
    {
        var samples = new List<Sample>();
        bool started = false;
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < deadline)
        {
            if (await State(driver) is { } state)
            {
                var sample = new Sample((int)state["Tick"]!, (bool?)state["EventUp"] == true, (int)state["ViewportX"]!, (int)state["ViewportY"]!,
                    (double?)state["Fade"] ?? 0, (string?)state["Location"], state["PlayerTile"] as JArray, (int?)state["FurnitureSprites"] ?? 0);
                if (samples.Count == 0 || samples[^1].Tick != sample.Tick) samples.Add(sample);
                started |= sample.EventUp;
                // Settled and faded in for a moment, so a late recentre would still show.
                if (started && !sample.EventUp && (bool?)state["Settled"] == true && sample.Fade <= 0.01
                    && samples.Count(s => !s.EventUp && s.Visible && s.Tick > samples.Last(e => e.EventUp).Tick) >= 6) return samples;
            }
            await Task.Delay(15);
        }
        throw new TimeoutException(started ? "The scene didn't end." : "The scene didn't start.");
    }
}
