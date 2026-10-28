using System.IO;
using Newtonsoft.Json.Linq;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>Real-game hold-through-black checks for both engine copies, end policies, branch End and camera forms.</summary>
public sealed class NativeCameraTests
{
    [Fact]
    public async Task TheAuthoredCameraStaysUntilFullyBlackInBothEngines()
    {
        string? driver = Environment.GetEnvironmentVariable("SDCS_NATIVE_DRIVER");
        if (string.IsNullOrEmpty(driver) || Environment.GetEnvironmentVariable("SDCS_NATIVE_SUITE") != "camera") return;
        string evidence = Environment.GetEnvironmentVariable("SDCS_NATIVE_EVIDENCE") ?? driver;
        var failures = new List<string>();
        foreach (string engine in new[] { "embedded", "framework" })
        foreach (string end in new[] { "stay", "default" })
        foreach (string path in new[] { "main", "branch" })
        foreach (string form in new[] { "fixed", "beat" })
        {
            string scene = $"camera-{end}-{path}-{form}";
            string name = engine + "-" + scene;
            var warp = await Ask(driver, "warp", new JObject { ["Location"] = "Town", ["X"] = 44, ["Y"] = 60, ["Facing"] = 1 });
            int warpTick = (int)warp["Data"]!["Tick"]!;
            await Until(driver, s => (int)s["Tick"]! > warpTick + 30 && (string?)s["Location"] == "Town" && (bool?)s["Settled"] == true && (int?)s["PlayerTile"]?[0] == 44, "Town ready");
            int start = (int)(await Ask(driver, "state", new JObject()))["Data"]!["Tick"]!;
            var played = await Ask(driver, engine == "embedded" ? "play" : "play-framework", new JObject { ["Mod"] = "Bench.Camera", ["Pack"] = "Bench.CameraFramework", ["Cutscene"] = scene });
            Assert.True((bool?)played["Data"]?["Started"] == true, name + " did not start");
            await Until(driver, s => (bool?)s["DialogueUp"] == true, name + " ending dialogue");
            await Ask(driver, "close-dialogue", new JObject());
            await Until(driver, s => (bool?)s["Settled"] == true && (double?)s["Fade"] <= 0.01, name + " recovery");
            var samples = ReadSamples(driver).Where(s => (int)s["Tick"]! > start).ToList();
            File.WriteAllLines(Path.Combine(evidence, name + ".jsonl"), samples.Select(s => s.ToString(Newtonsoft.Json.Formatting.None)));
            try
            {
                Assert.NotEmpty(samples);
                int boundary = samples.FindLastIndex(s => (bool?)s["DialogueUp"] == true);
                Assert.True(boundary >= 0, name + " missing final dialogue sample");
                int black = samples.FindIndex(boundary, s => (double)s["Fade"]! >= 1.0);
                Assert.True(black > boundary, name + " never reached full black");
                var held = samples.Skip(boundary).Take(black - boundary + 1).ToList();
                Assert.All(held.Zip(held.Skip(1)), pair => Assert.Equal((int)pair.First["Tick"]! + 1, (int)pair.Second["Tick"]!));
                int x = (int)held[0]["ViewportX"]!, y = (int)held[0]["ViewportY"]!;
                Assert.All(held, s => Assert.True((int)s["ViewportX"]! == x && (int)s["ViewportY"]! == y,
                    $"{name}: viewport moved from {x},{y} to {s["ViewportX"]},{s["ViewportY"]} at tick {s["Tick"]}, fade {s["Fade"]}"));
                Assert.All(held.Take(held.Count - 1), s => Assert.True((int)s["FurnitureSprites"]! > 0, name + " prop vanished before black"));
                Assert.Equal(0, (int)samples[^1]["FurnitureSprites"]!);
                Assert.False((bool)samples[^1]["Frozen"]!);
                Assert.True((bool)samples[^1]["Settled"]!);
                Assert.Equal(end == "stay" ? 52 : 44, (int)samples[^1]["PlayerTile"]![0]!);
            }
            catch (Exception ex) { failures.Add(name + ": " + ex.Message); }
        }
        File.WriteAllLines(Path.Combine(evidence, "camera-verdict.txt"), failures.Count == 0 ? new[] { "PASS: 16 camera cases" } : failures);
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static List<JObject> ReadSamples(string driver)
    {
        using var stream = new FileStream(Path.Combine(driver, "camera-samples.jsonl"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        string text = new StreamReader(stream).ReadToEnd();
        return text.Split('\n').SkipLast(1).Where(s => !string.IsNullOrWhiteSpace(s)).Select(JObject.Parse).ToList();
    }

    private static async Task Until(string driver, Func<JObject, bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var stream = new FileStream(Path.Combine(driver, "state.json"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (condition(JObject.Parse(await new StreamReader(stream).ReadToEndAsync()))) return;
            }
            catch (Exception ex) when (ex is IOException or Newtonsoft.Json.JsonException) { }
            await Task.Delay(30);
        }
        throw new TimeoutException("Never saw " + what);
    }

    private static async Task<JObject> Ask(string driver, string check, JObject request)
    {
        string id = "camera-" + Guid.NewGuid().ToString("N");
        request["Check"] = check;
        string path = Path.Combine(driver, "assertions", id + ".json");
        File.WriteAllText(path + ".tmp", request.ToString());
        File.Move(path + ".tmp", path);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            string result = Path.Combine(driver, "assertion-results", id + ".json");
            if (File.Exists(result))
            {
                var answer = JObject.Parse(File.ReadAllText(result));
                Assert.True((bool?)answer["Ok"] == true, answer.ToString());
                return answer;
            }
            await Task.Delay(30);
        }
        throw new TimeoutException("Driver did not answer " + check);
    }
}
