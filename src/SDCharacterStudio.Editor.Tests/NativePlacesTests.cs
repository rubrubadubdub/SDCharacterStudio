using System.IO;
using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Catalog;
using SDCharacterStudio.Editor.Export;
using SDCharacterStudio.Editor.Preview;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>
/// Positions from the spouse's room in the real game (D-52). A scene in the farmhouse puts the player two tiles below the
/// spouse's spot and Robin on it, then walks Robin one tile right of it. Without a spouse's room (a new house, nobody
/// married) the trigger's query is false, and a preview still plays, with the room at its fallback spot and a warning.
/// Married to Abigail in a twice-upgraded house, the query is true and everyone stands where the game's own spot says.
/// Off by default: tools/native-bridge/Test-Costumes.ps1 -Suite places starts an isolated game (the bench driver's
/// disposable farm, no save) and sets SDCS_NATIVE_PIPE, SDCS_NATIVE_DRIVER and SDCS_NATIVE_SUITE=places.
/// </summary>
public class NativePlacesTests
{
    private readonly List<string> log = new();
    private string evidence = "";
    private void Note(string line) { log.Add($"{DateTime.Now:HH:mm:ss.fff} {line}"); File.WriteAllLines(Path.Combine(evidence, "editor-native-places.log"), log); }

    [Fact]
    public async Task EveryoneStandsWhereTheGameSaysTheSpousesRoomIs()
    {
        string? pipe = Environment.GetEnvironmentVariable("SDCS_NATIVE_PIPE");
        string? driver = Environment.GetEnvironmentVariable("SDCS_NATIVE_DRIVER");
        if (string.IsNullOrEmpty(pipe) || string.IsNullOrEmpty(driver) || Environment.GetEnvironmentVariable("SDCS_NATIVE_SUITE") != "places") return;
        evidence = Environment.GetEnvironmentVariable("SDCS_NATIVE_EVIDENCE") ?? driver;
        Directory.CreateDirectory(evidence);

        // The editor's small test export, plus the farmhouse (so the scene's location is known and preview may start).
        using var temp = new TempFolder();
        string export = Path.Combine(temp.Path, "Export", "places");
        PackExporter.CopyDirectory(TestEnvironment.MiniExport, export);
        var catalog = JObject.Parse(File.ReadAllText(Path.Combine(export, "catalog.json")));
        ((JArray)catalog["Locations"]!).Add(new JObject { ["Name"] = "FarmHouse", ["MapAsset"] = "Maps\\FarmHouse", ["Size"] = new JArray(12, 12), ["Level"] = 0 });
        File.WriteAllText(Path.Combine(export, "catalog.json"), catalog.ToString());

        using var editor = new EditorHarness(export, pipe);
        var shell = editor.Shell;
        await WaitReady(shell);
        var doc = editor.NewProject("Home", "Player.Home", "FarmHouse", "Greeting");
        var scene = shell.Scene!;
        doc.Edit("Stage", () =>
        {
            scene.Actors[0].Start = new Position { From = Places.SpouseRoom, Tile = new[] { 0, 2 } };
            scene.Actors[0].Facing = "up";
            scene.Actors.Add(new Actor { Id = "Robin", Kind = "Npc", Npc = "Robin", Start = new Position { From = Places.SpouseRoom, Tile = new[] { 0, 0 } }, Facing = "down" });
            scene.Beats.Add(new MoveBeat { Actor = "Robin", To = new Position { From = Places.SpouseRoom, Tile = new[] { 1, 0 } } });
            scene.Beats.Add(new WaitBeat { Duration = 2500 });
        });
        var problems = shell.ValidateNow();
        Note("validation: " + (problems.Errors == 0 ? "no errors" : string.Join(" | ", problems.Problems.Select(p => p.Message))));
        Xunit.Assert.Equal(0, problems.Errors);
        const string query = "SDCS_HAS_PLACE SpouseRoom";

        // A new house, nobody married: no spouse's room. The trigger's query says so; a preview plays at the fallback spot.
        var plain = await Ask(driver, "home", new JObject());
        Note("plain house: " + plain["Data"]!.ToString(Newtonsoft.Json.Formatting.None));
        await Until(driver, s => s["Location"]?.ToString() == "FarmHouse" && (bool?)s["Settled"] == true, "the player in the farmhouse");
        Xunit.Assert.False((bool)(await Ask(driver, "query", new JObject { ["Query"] = query }))["Data"]!["Result"]!);
        var fallback = Places.Fallback(Places.SpouseRoom);
        var (robinAt, playerAt) = await PlayAndFindEveryone(shell, driver, (fallback.X + 1, fallback.Y));
        Note($"without the room: Robin at {robinAt}, the player at {playerAt}");
        Xunit.Assert.Equal((fallback.X + 1, fallback.Y), robinAt);
        Xunit.Assert.Contains(shell.Output.Lines, l => l.Text.Contains("the spouse's room isn't here", StringComparison.Ordinal));
        await Until(driver, s => (bool?)s["Settled"] == true, "the scene's end");

        // Married to Abigail, upgraded twice: the game's own spot (her room marks it), and everyone there.
        var married = (JObject)(await Ask(driver, "home", new JObject { ["Spouse"] = "Abigail", ["Level"] = 2 }))["Data"]!;
        Note("married: " + married.ToString(Newtonsoft.Json.Formatting.None));
        await Until(driver, s => s["Location"]?.ToString() == "FarmHouse" && (bool?)s["Settled"] == true, "the player back in the farmhouse");
        Xunit.Assert.True((bool)married["HasSpouse"]!);
        var spot = ((int)married["SpouseRoom"]![0]!, (int)married["SpouseRoom"]![1]!);
        Xunit.Assert.True((bool)(await Ask(driver, "query", new JObject { ["Query"] = query }))["Data"]!["Result"]!);
        (robinAt, playerAt) = await PlayAndFindEveryone(shell, driver, (spot.Item1 + 1, spot.Item2));
        Note($"in the room at {spot}: Robin at {robinAt}, the player at {playerAt}");
        Xunit.Assert.Equal((spot.Item1 + 1, spot.Item2), robinAt);
        Xunit.Assert.Equal((spot.Item1, spot.Item2 + 2), playerAt);
        await Until(driver, s => (bool?)s["Settled"] == true, "the scene's end");

        // The upgraded houses (G2): asked for, as "Ask the game to export again" does, the save export has them as the game
        // makes them, each with its spouse's spot. Married to Abigail, the twice-upgraded one is this very house.
        JObject reply = new();
        for (var until = DateTime.UtcNow.AddMinutes(5); ;)
        {
            reply = await shell.Preview.Client.SendAsync(new JObject { ["Op"] = "Export", ["Phase"] = "save" }, null, TimeSpan.FromMinutes(10));
            if ((bool?)reply["Ok"] == true || !(((string?)reply["Error"])?.Contains("already running") ?? false) || DateTime.UtcNow > until) break;
            await Task.Delay(2000);   // the launch's own first export is still going
        }
        Note("export: " + reply.ToString(Newtonsoft.Json.Formatting.None));
        Xunit.Assert.True((bool?)reply["Ok"] == true, (string?)reply["Error"]);
        var exported = ExportCatalog.Load(Environment.GetEnvironmentVariable("SDCS_NATIVE_EXPORT")!);
        var own = exported.Location("FarmHouse")!;
        Note($"exported house: level {own.Level}, spot {own.Spot(Places.SpouseRoom)}; layouts: {string.Join(", ", exported.HouseLayouts.Select(l => $"{l.Name} {l.Width}x{l.Height} spot {l.Spot(Places.SpouseRoom)} room '{l.SpouseRoom}'"))}");
        Xunit.Assert.Equal(2, own.Level);
        Xunit.Assert.Equal(spot, own.Spot(Places.SpouseRoom));
        Xunit.Assert.True(exported.HasHouseLayouts);
        var once = exported.HouseLayout("FarmHouse1")!;
        var twice = exported.HouseLayout("FarmHouse2")!;
        Xunit.Assert.Equal("Abigail", twice.SpouseRoom);
        Xunit.Assert.Equal(spot, twice.Spot(Places.SpouseRoom));
        Xunit.Assert.Equal((own.Width, own.Height), (twice.Width, twice.Height));
        Xunit.Assert.NotNull(once.Spot(Places.SpouseRoom));
        Xunit.Assert.True(File.Exists(exported.FullPath(twice.Back)) && File.Exists(exported.FullPath(once.Passable)));
        Note("PASS");
    }

    /// <summary>Preview the scene; once Robin has walked to <paramref name="robinTarget"/>, where Robin and the player are.</summary>
    private async Task<((int, int) Robin, (int, int) Player)> PlayAndFindEveryone(MainViewModel shell, string driver, (int, int) robinTarget)
    {
        int toasts = ((FakeUi)shell.Ui).Toasts.Count;
        await shell.PreviewInGameCommand.ExecuteAsync(null);
        Note("preview: " + string.Join(" | ", ((FakeUi)shell.Ui).Toasts.Skip(toasts).Select(t => t.Title + ": " + t.Message)));
        await Until(driver, s => (bool?)s["EventUp"] == true, "the scene");
        var deadline = DateTime.UtcNow.AddSeconds(20);
        (int, int) robin = default, player = default;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var actors = (JObject)(await Ask(driver, "actors", new JObject()))["Data"]!;
                var entry = ((JArray)actors["Actors"]!).OfType<JObject>().FirstOrDefault(a => (string?)a["Name"] == "Robin");
                var state = await State(driver);
                if (entry != null && state?["PlayerTile"] is JArray tile)
                {
                    robin = ((int)entry["Tile"]![0]!, (int)entry["Tile"]![1]!);
                    player = ((int)tile[0]!, (int)tile[1]!);
                    if (robin == robinTarget) return (robin, player);
                }
            }
            catch (InvalidOperationException) { }   // between the start and the event: "No event is running."
            await Task.Delay(200);
        }
        return (robin, player);
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
            using var stream = new FileStream(Path.Combine(driver, "state.json"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JObject.Parse(await new StreamReader(stream).ReadToEndAsync());
        }
        catch (Exception ex) when (ex is IOException or Newtonsoft.Json.JsonException) { return null; }
    }

    private static async Task Until(string driver, Func<JObject, bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(40);
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
        string id = "places-" + Guid.NewGuid().ToString("N");
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
}
