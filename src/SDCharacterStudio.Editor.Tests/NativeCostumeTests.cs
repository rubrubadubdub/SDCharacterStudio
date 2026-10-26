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
    /// <summary>Each test keeps its own evidence (xUnit makes a new instance per test).</summary>
    private string logName = "editor-native-costume.log";
    private void Note(string line) { log.Add($"{DateTime.Now:HH:mm:ss.fff} {line}"); File.WriteAllLines(Path.Combine(evidence, logName), log); }

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

    /// <summary>
    /// Your own outfit in the real game (the owner: "what if I built my own clothes for Abigail?"): a raincoat painted on
    /// Robin's walk, put on by a Costume beat, and a pose started from it that wears it, drawn by the runtime.
    /// </summary>
    [Fact]
    public async Task YourOutfitIsWornInTheGame()
    {
        logName = "editor-native-outfit.log";
        string? pipe = Environment.GetEnvironmentVariable("SDCS_NATIVE_PIPE");
        string? driver = Environment.GetEnvironmentVariable("SDCS_NATIVE_DRIVER");
        if (string.IsNullOrEmpty(pipe) || string.IsNullOrEmpty(driver) || Environment.GetEnvironmentVariable("SDCS_NATIVE_SUITE") != "costume") return;
        evidence = Environment.GetEnvironmentVariable("SDCS_NATIVE_EVIDENCE") ?? driver;
        Directory.CreateDirectory(evidence);

        using var editor = new EditorHarness(pipeName: pipe);
        var shell = editor.Shell;
        await WaitReady(shell);
        var doc = editor.NewProject("Raincoat", "Player.Raincoat", "ScienceHouse");

        // In the Character Studio, as a designer would: the outfit, yellow on every walking frame at once, then a pose from it.
        shell.IsCharacterStudio = true;
        var studio = shell.CharacterStudio;
        studio.Settings.AdvancedPoseTools = false;
        studio.Character = studio.Characters.First(c => c.Key == "Robin");
        string outfit = studio.NewOutfit("raincoat")!;
        var coat = (PoseDocumentViewModel)studio.ActiveDocument!;
        coat.PickFrames(Enumerable.Range(0, 16), 0);
        coat.Tool = StudioTool.Rectangle; coat.FilledShapes = true;
        studio.Palette.Primary = 0xFFE8C020;
        coat.PointerDown(5, 18, PointerButton.Primary); coat.PointerMove(10, 24); coat.PointerUp(10, 24);
        string wave = studio.NewPose("character", "wave", "down")!;
        var waveDoc = (PoseDocumentViewModel)studio.ActiveDocument!;
        waveDoc.ShowSheetFrames = true;
        waveDoc.StartFromSheetFrame(0);
        Xunit.Assert.Equal(outfit, ((LiveLayer)studio.Library!.Pose(wave)!.Frames[0].Layers[0]).Source!.Outfit);
        shell.IsCharacterStudio = false;

        var scene = shell.Scene!;
        doc.Edit("Stage", () =>
        {
            scene.Actors[0].Start = new Position { Tile = new[] { 16, 9 } };
            scene.Actors.Add(new Actor { Id = "Robin", Kind = "Npc", Npc = "Robin", Start = new Position { Tile = new[] { 17, 6 } }, Facing = "down" });
            scene.Beats.Add(new CostumeBeat { Actor = "Robin", Sprite = new SpriteReference { Pose = outfit } });
            scene.Beats.Add(new WaitBeat { Duration = 2500 });
            scene.Beats.Add(new PoseBeat { Actor = "Robin", Pose = wave, Play = "loop" });
            scene.Beats.Add(new WaitBeat { Duration = 2500 });
            scene.Beats.Add(new PoseBeat { Actor = "Robin", Release = true });
            scene.Beats.Add(new CostumeBeat { Actor = "Robin", Restore = true });
            scene.Beats.Add(new WaitBeat { Duration = 1500 });
        });
        var problems = shell.ValidateNow();
        Note("outfit validation: " + (problems.Errors == 0 ? "no errors" : string.Join(" | ", problems.Problems.Select(p => p.Message))));
        Xunit.Assert.Equal(0, problems.Errors);

        await shell.PreviewInGameCommand.ExecuteAsync(null);
        const string yellow = "#FFE8C020";
        bool worn = false, standIn = false, started = false;
        string? lastSheet = null, ownSize = null, wornSize = null;
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < deadline)
        {
            var looks = (JObject)(await Ask(driver, "looks", new JObject { ["X"] = 7, ["Y"] = 20 }))["Data"]!;
            var robin = ((JArray)looks["Actors"]!).OfType<JObject>().FirstOrDefault(a => (string?)a["Name"] == "Robin");
            if (robin != null)
            {
                started = true;
                string? sheet = (string?)robin["Sheet"];
                if (sheet != lastSheet) { Note($"Robin wears {sheet} ({robin["Size"]?.ToString(Newtonsoft.Json.Formatting.None)}), pixel {robin["Pixel"]}"); lastSheet = sheet; }
                string size = robin["Size"]!.ToString(Newtonsoft.Json.Formatting.None);
                bool wearing = sheet?.Contains("_SDCS_", StringComparison.Ordinal) == true && sheet.EndsWith(outfit, StringComparison.Ordinal);
                if (wearing) wornSize = size; else ownSize ??= size;
                worn |= wearing && (string?)robin["Pixel"] == yellow;
            }
            if (((JArray)looks["StandIns"]!).Any(p => (string?)p == yellow) && !standIn) { standIn = true; Note("a stand-in shows the raincoat"); }
            if (started && robin == null) break;   // the scene is over
            await Task.Delay(150);
        }
        Xunit.Assert.True(started, "The scene never started.");
        Xunit.Assert.True(worn, "Robin never wore the raincoat (its sheet with the yellow paint).");
        Xunit.Assert.True(standIn, "The pose that wears the raincoat never showed it.");
        // The outfit was made on the small test export (16 frames); in the game it covers her whole sheet.
        Xunit.Assert.Equal(ownSize, wornSize);
        Note("PASS outfit");
    }

    /// <summary>
    /// A live recolour in the real game: a pose of Robin with her shirt recoloured green, drawn by the runtime (the same
    /// Core code as the editor), shows green where her own sprite has none.
    /// </summary>
    [Fact]
    public async Task ARecolourShowsInTheGame()
    {
        logName = "editor-native-recolour.log";
        string? pipe = Environment.GetEnvironmentVariable("SDCS_NATIVE_PIPE");
        string? driver = Environment.GetEnvironmentVariable("SDCS_NATIVE_DRIVER");
        if (string.IsNullOrEmpty(pipe) || string.IsNullOrEmpty(driver) || Environment.GetEnvironmentVariable("SDCS_NATIVE_SUITE") != "costume") return;
        evidence = Environment.GetEnvironmentVariable("SDCS_NATIVE_EVIDENCE") ?? driver;
        Directory.CreateDirectory(evidence);

        using var editor = new EditorHarness(pipeName: pipe);
        var shell = editor.Shell;
        await WaitReady(shell);
        var doc = editor.NewProject("Green Shirt", "Player.GreenShirt", "ScienceHouse");
        shell.IsCharacterStudio = true;
        var studio = shell.CharacterStudio;
        studio.Settings.AdvancedPoseTools = false;
        studio.Character = studio.Characters.First(c => c.Key == "Robin");
        studio.Outfit = studio.Outfits[0];
        string green = studio.NewPose("character", "green", "down")!;
        var pose = (PoseDocumentViewModel)studio.ActiveDocument!;
        studio.Palette.Primary = 0xFF30C040;
        pose.AddRecolor();
        pose.Recolors.Single().RangeChoice = "40";
        Note("recolour: " + string.Join(", ", pose.Recolors.Select(r => $"{r.From} -> {r.ToText}")));
        shell.IsCharacterStudio = false;
        var scene = shell.Scene!;
        doc.Edit("Stage", () =>
        {
            scene.Actors[0].Start = new Position { Tile = new[] { 16, 9 } };
            scene.Actors.Add(new Actor { Id = "Robin", Kind = "Npc", Npc = "Robin", Start = new Position { Tile = new[] { 17, 6 } }, Facing = "down" });
            scene.Beats.Add(new WaitBeat { Duration = 1000 });
            scene.Beats.Add(new PoseBeat { Actor = "Robin", Pose = green, Play = "loop" });
            scene.Beats.Add(new WaitBeat { Duration = 2500 });
            scene.Beats.Add(new PoseBeat { Actor = "Robin", Release = true });
            scene.Beats.Add(new WaitBeat { Duration = 800 });
        });
        var problems = shell.ValidateNow();
        Note("recolour validation: " + (problems.Errors == 0 ? "no errors" : string.Join(" | ", problems.Problems.Select(p => p.Message))));
        Xunit.Assert.Equal(0, problems.Errors);

        static int Greens(JToken? block) => block is JArray pixels ? pixels.Count(p => (string?)p is { Length: 9 } hex
            && Core.Palette.Shading.Hsv(Convert.ToUInt32(hex[1..], 16)) is var hsv && Convert.ToUInt32(hex[1..3], 16) == 255 && hsv.Hue is >= 100 and <= 150 && hsv.Saturation > 0.3) : 0;
        await shell.PreviewInGameCommand.ExecuteAsync(null);
        int own = -1, best = 0;
        bool started = false;
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < deadline)
        {
            var looks = (JObject)(await Ask(driver, "looks", new JObject { ["X"] = 0, ["Y"] = 0, ["W"] = 16, ["H"] = 32 }))["Data"]!;
            var robin = ((JArray)looks["Actors"]!).OfType<JObject>().FirstOrDefault(a => (string?)a["Name"] == "Robin");
            if (robin != null) { started = true; if (own < 0) { own = Greens(robin["Pixel"]); Note($"green pixels in her own frame 0: {own}"); } }
            foreach (var block in (JArray)looks["StandIns"]!)
                if (Greens(block) is var count && count > best) { best = count; Note($"green pixels in the stand-in: {count}"); }
            if (started && robin == null) break;
            await Task.Delay(150);
        }
        Xunit.Assert.True(started, "The scene never started.");
        Xunit.Assert.True(best >= 4 && best > own, $"The recoloured pose showed {best} green pixels; her own frame has {own}.");
        Note("PASS recolour");
    }

    /// <summary>A driver request (assertions/&lt;id&gt;.json) and its answer.</summary>
    private static async Task<JObject> Ask(string driver, string check, JObject request)
    {
        string id = "outfit-" + Guid.NewGuid().ToString("N");
        request["Check"] = check;
        File.WriteAllText(Path.Combine(driver, "assertions", id + ".json.tmp"), request.ToString());
        File.Move(Path.Combine(driver, "assertions", id + ".json.tmp"), Path.Combine(driver, "assertions", id + ".json"));
        string result = Path.Combine(driver, "assertion-results", id + ".json");
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(result))
            {
                JObject answer;
                try { answer = JObject.Parse(File.ReadAllText(result)); }
                catch (Exception ex) when (ex is IOException or Newtonsoft.Json.JsonException) { await Task.Delay(30); continue; }
                if ((bool?)answer["Ok"] != true) throw new InvalidOperationException($"Driver {check}: {answer["Error"]}");
                return answer;
            }
            await Task.Delay(50);
        }
        throw new TimeoutException($"Driver {check}: no answer.");
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
