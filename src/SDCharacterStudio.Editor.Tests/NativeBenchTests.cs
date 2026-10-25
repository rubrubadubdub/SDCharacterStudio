using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>
/// Q-17 proof, native: the two benches (a C# mod with Sdcs.cs, and a complete mod exported by the editor) play their
/// scenes in one game with no SDCharacterStudio installed, each with its own engine copy and version; with the framework
/// installed too, all three coexist. The quickstart walkthrough's mod (what a first-time creator exports) plays by
/// itself when the player walks into Robin's house. Off by default: benches/Run-Benches.ps1 stages the game and sets
/// SDCS_NATIVE_SUITE=benches, SDCS_BENCH_DRIVER, SDCS_BENCH_FRAMEWORK and SDCS_BENCH_CODE_VERSION.
/// </summary>
public class NativeBenchTests
{
    private const string CodeMod = "Bench.SdcsCode", NoCodeMod = "Bench.SdcsNoCode", WalkthroughMod = "Player.FirstScene", StudioMod = "Bench.SdcsStudio";
    private readonly List<string> log = new();
    private string evidence = "";
    private void Note(string line) { log.Add($"{DateTime.Now:HH:mm:ss.fff} {line}"); File.WriteAllLines(Path.Combine(evidence, "benches-native.log"), log); }

    [Fact]
    public async Task BothBenchesPlayInOneGame()
    {
        if (Environment.GetEnvironmentVariable("SDCS_NATIVE_SUITE") != "benches") return;
        string driver = Environment.GetEnvironmentVariable("SDCS_BENCH_DRIVER")!;
        bool framework = Environment.GetEnvironmentVariable("SDCS_BENCH_FRAMEWORK") == "1";
        string codeVersion = Environment.GetEnvironmentVariable("SDCS_BENCH_CODE_VERSION") ?? "";
        evidence = Environment.GetEnvironmentVariable("SDCS_NATIVE_EVIDENCE") ?? driver;
        Directory.CreateDirectory(evidence);
        Note(framework ? "Mods: bench driver, code bench, no-code bench, and the SDCharacterStudio framework" : "Mods: bench driver, code bench, no-code bench (no SDCharacterStudio installed)");

        // Each bench has its own engine copy: its own version and its own command names.
        var code = Check(driver, "bench", new JObject { ["Mod"] = CodeMod });
        var noCode = Check(driver, "bench", new JObject { ["Mod"] = NoCodeMod });
        Note($"code bench: {code.ToString(Newtonsoft.Json.Formatting.None)}");
        Note($"no-code bench: {noCode.ToString(Newtonsoft.Json.Formatting.None)}");
        if (codeVersion.Length > 0) Assert.Equal(codeVersion, (string?)code["EngineVersion"]);
        Assert.NotEqual((string?)code["EngineVersion"], (string?)noCode["EngineVersion"]);
        Assert.NotEqual((string?)code["CommandPrefix"], (string?)noCode["CommandPrefix"]);
        Assert.All(new[] { code, noCode }, b => Assert.Matches("^SDCS[0-9a-f]{8}$", (string?)b["CommandPrefix"]));
        Assert.Contains("bow", code["Cutscenes"]!.Values<string>());
        Assert.Contains("wave", noCode["Cutscenes"]!.Values<string>());

        // Each plays: its actor's stand-in (the custom pose) shows, and the animation moves them.
        Assert.Equal(new[] { 16, 6 }, await PlayAndFollow(driver, CodeMod, "bow", "Robin"));       // one tile left of 17,6
        Assert.Equal(new[] { 14, 7 }, await PlayAndFollow(driver, NoCodeMod, "wave", "Demetrius"));    // one tile right of 13,7

        // The walkthrough's mod, exported exactly as docs/quickstart.md says: nobody calls it; walking in starts it.
        var walkthrough = Check(driver, "bench", new JObject { ["Mod"] = WalkthroughMod });
        Note($"walkthrough mod: {walkthrough.ToString(Newtonsoft.Json.Formatting.None)}");
        Assert.Contains("hello-robin", walkthrough["Cutscenes"]!.Values<string>());
        Assert.NotEqual((string?)walkthrough["CommandPrefix"], (string?)noCode["CommandPrefix"]);
        Assert.Equal(new[] { 17, 6 }, await WalkInAndFollow(driver, WalkthroughMod, "Robin"));        // Robin holds the scarf pose where she was placed

        // The simple Character Studio (D-48): a pose made from live puppet parts, in a flipbook that walks two tiles left.
        var studioBench = Check(driver, "bench", new JObject { ["Mod"] = StudioMod });
        Note($"studio bench: {studioBench.ToString(Newtonsoft.Json.Formatting.None)}");
        Assert.Contains("greet", studioBench["Cutscenes"]!.Values<string>());
        Assert.Equal(new[] { 15, 6 }, await PlayAndFollow(driver, StudioMod, "greet", "Robin"));

        // Nothing replaced anything; the embedded engines stay out of the player's console.
        string smapi = ReadShared(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StardewValley", "ErrorLogs", "SMAPI-latest.txt"));
        Assert.DoesNotContain("already defined and will be overwritten", smapi);
        Assert.DoesNotContain("already registered", smapi);
        var errors = Regex.Matches(smapi, @"^\[[0-9:]+ ERROR[^\]]*\].*$", RegexOptions.Multiline).Select(m => m.Value).Where(l => !l.Contains("Steam achievements")).ToList();
        Note("SMAPI errors: " + (errors.Count == 0 ? "none" : string.Join(" | ", errors)));
        Assert.Empty(errors);
        var loud = Regex.Matches(smapi, @"^\[[0-9:]+ (INFO|WARN|ALERT)\s+(SDCS Bench \(code\)|NoCode Bench|My First Scene|Studio Bench)\].*$", RegexOptions.Multiline).Select(m => m.Value).ToList();
        Note("bench console lines: " + (loud.Count == 0 ? "none" : string.Join(" | ", loud)));
        Assert.Empty(loud);
        Assert.Contains($"SDCharacterStudio engine {code["EngineVersion"]} embedded (commands {code["CommandPrefix"]}_*)", smapi);
        Assert.Contains($"SDCharacterStudio engine {noCode["EngineVersion"]} embedded (commands {noCode["CommandPrefix"]}_*)", smapi);
        Assert.Contains($"SDCharacterStudio engine {walkthrough["EngineVersion"]} embedded (commands {walkthrough["CommandPrefix"]}_*)", smapi);
        if (framework) Assert.Contains("SDCharacterStudio 0.1.0 loaded.", smapi);
        else Assert.DoesNotContain("SDCharacterStudio 0.1.0 loaded.", smapi);
        Note("PASS");
    }

    private async Task<int[]> PlayAndFollow(string driver, string mod, string scene, string actor)
    {
        await Until(driver, s => (bool?)s["Settled"] == true);
        var started = Check(driver, "play", new JObject { ["Mod"] = mod, ["Cutscene"] = scene });
        Assert.True((bool?)started["Started"], $"{mod} didn't start '{scene}'.");
        return await Follow(driver, mod, actor);
    }

    /// <summary>Leave and re-enter ScienceHouse as a player would; the mod's scene must start by itself.</summary>
    private async Task<int[]> WalkInAndFollow(string driver, string mod, string actor)
    {
        await Until(driver, s => (bool?)s["Settled"] == true);
        var forgot = Check(driver, "forget", new JObject { ["Prefix"] = mod + "." });
        Note($"{mod}: un-seen {forgot["Forgotten"]} event(s); leaving Robin's house");
        Check(driver, "warp", new JObject { ["Location"] = "Town", ["X"] = 52, ["Y"] = 68, ["Facing"] = 2 });
        await Until(driver, s => (bool?)s["Settled"] == true && (string?)s["Location"] == "Town");
        Note($"{mod}: in Town; walking back in");
        Check(driver, "warp", new JObject { ["Location"] = "ScienceHouse", ["X"] = 16, ["Y"] = 9, ["Facing"] = 0 });
        return await Follow(driver, mod, actor);
    }

    private async Task<int[]> Follow(string driver, string mod, string actor)
    {
        bool hidden = false;
        JObject? sample = null;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var state = State(driver);
            if (state == null || (bool?)state["EventUp"] != true || state["Event"]?.Type != JTokenType.String) { await Task.Delay(50); continue; }
            Assert.Contains(mod, (string?)state["Event"], StringComparison.OrdinalIgnoreCase);
            var actors = Check(driver, "actors", new JObject());
            sample = actors["Actors"]!.OfType<JObject>().FirstOrDefault(a => (string?)a["Name"] == actor);
            if ((bool?)sample?["Invisible"] == true && !hidden) { hidden = true; Note($"{mod}: {actor} during the clip (stand-in showing): {sample!.ToString(Newtonsoft.Json.Formatting.None)}"); }
            if ((bool?)state["DialogueUp"] == true) break;
        }
        Note($"{mod}: {actor} at the line: {sample?.ToString(Newtonsoft.Json.Formatting.None)}");
        Assert.True(hidden, $"{actor}'s stand-in (the custom pose) never showed.");
        Check(driver, "stop", new JObject { ["Mod"] = mod });
        await Until(driver, s => (bool?)s["Settled"] == true);
        Note($"{mod}: stopped; the player is free");
        return sample!["Tile"]!.ToObject<int[]>()!;
    }

    private static JObject? State(string driver)
    {
        try { return JObject.Parse(File.ReadAllText(Path.Combine(driver, "state.json"))); }
        catch (Exception ex) when (ex is IOException or Newtonsoft.Json.JsonException) { return null; }
    }

    private static async Task Until(string driver, Func<JObject, bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline) { if (State(driver) is { } s && condition(s)) return; await Task.Delay(50); }
        throw new TimeoutException("The game didn't reach the expected state. Last: " + State(driver));
    }

    private static JObject Check(string driver, string check, JObject request)
    {
        string id = check + "-" + Guid.NewGuid().ToString("N")[..8];
        request["Check"] = check;
        string folder = Path.Combine(driver, "assertions");
        File.WriteAllText(Path.Combine(folder, id + ".tmp"), request.ToString());
        File.Move(Path.Combine(folder, id + ".tmp"), Path.Combine(folder, id + ".json"));
        string result = Path.Combine(driver, "assertion-results", id + ".json");
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(result))
            {
                try
                {
                    var json = JObject.Parse(File.ReadAllText(result));
                    Assert.True((bool?)json["Ok"] == true, $"Driver check '{check}' failed: {json["Error"]}");
                    return (JObject)json["Data"]!;
                }
                catch (IOException) { }
                catch (Newtonsoft.Json.JsonException) { }
            }
            Thread.Sleep(30);
        }
        throw new TimeoutException($"Driver check '{check}' didn't answer.");
    }

    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
