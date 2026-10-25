using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SDCharacterStudio.Editor.Audio;

namespace SDCharacterStudio.Editor.Tests;

public sealed class AudioFactAttribute : FactAttribute
{
    public AudioFactAttribute()
    { if (Environment.GetEnvironmentVariable("SDCS_AUDIO_TESTS") != "1") Skip = "Set SDCS_AUDIO_TESTS=1 and SDCS_AUDIO_EXPORT for installed audio checks."; }
}

/// <summary>Runs the actual service/host with an exported mod cue and installed vanilla banks, with the game closed.</summary>
public sealed class AudioNativeTests
{
    private static (string Host, string Catalog, string Game, string Cue) Sources()
    {
        Assert.Empty(Process.GetProcessesByName("Stardew Valley"));
        Assert.Empty(Process.GetProcessesByName("StardewModdingAPI"));
        string root = TestEnvironment.RepoRoot ?? throw new InvalidOperationException("Repository root is required.");
        string host = Environment.GetEnvironmentVariable("SDCS_AUDIO_HOST") ?? Path.Combine(root, "src/SDCharacterStudio.AudioHost/bin/Debug/net8.0-windows/SDCharacterStudio.AudioHost.exe");
        string catalog = Environment.GetEnvironmentVariable("SDCS_AUDIO_EXPORT") ?? Path.Combine(root, ".superpowers/sdd/2026-10-24-m7-hardening/audio-native/Export/m7-audio");
        var metadata = JsonConvert.DeserializeObject<AudioPreviewCatalog>(File.ReadAllText(Path.Combine(catalog, "sound-previews.json")))!;
        string game = Environment.GetEnvironmentVariable("SDCS_GAME_DIR") ?? metadata.GameDirectory!;
        string cue = metadata.Cues.First(p => p.Value.Error == null && p.Value.Variants.Any(v => v.File.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))).Key;
        Assert.True(File.Exists(host)); Assert.True(Directory.Exists(game));
        return (host, catalog, game, cue);
    }

    [AudioFact]
    public async Task ExportedModAndVanillaPlayThroughServiceAndLatestRequestWins()
    {
        var sources = Sources();
        await using var service = new AudioPreviewService(sources.Host, sources.Catalog, sources.Game);
        static void Ok(AudioPreviewResult result) => Assert.True(result.Ok, result.Error);
        Ok(await service.PlayAsync("coin", 1200));
        Ok(await service.PlayAsync(sources.Cue, 1200));
        await service.StopAsync();
        Ok(await service.PlayAsync("coin"));
        var first = service.PlayAsync(sources.Cue);
        var second = service.PlayAsync("coin");
        await first;
        Ok(await second);
        Assert.Equal("coin", service.Status.Message);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.False((await service.PlayAsync(sources.Cue, cancellation: cancelled.Token)).Ok);
        Ok(await service.PlayAsync(sources.Cue, 1800));
        Assert.False((await service.PlayAsync("SDCS.DefinitelyMissingSoundCue")).Ok);
        Ok(await service.PlayAsync("coin"));
        await service.StopAsync();
        await service.DisposeAsync();
        Assert.False((await service.PlayAsync("coin")).Ok);
    }

    [AudioFact]
    public async Task NativeLoopStopsAtDeadlineAndShutdownAndEofExitCleanly()
    {
        var s = Sources();
        var info = new ProcessStartInfo(s.Host) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        info.ArgumentList.Add("--game-dir"); info.ArgumentList.Add(s.Game);
        using var host = Process.Start(info)!;
        var errors = host.StandardError.ReadToEndAsync();
        try
        {
            foreach (string cue in new[] { "coin", s.Cue })
            {
                var play = await Send(host, new JObject { ["Id"] = 1, ["Op"] = "play", ["Cue"] = cue, ["Catalog"] = s.Catalog, ["Pitch"] = 1200, ["MaxSeconds"] = .1 });
                Assert.True((bool?)play["Ok"], (string?)play["Error"]);
                await Task.Delay(400);
                var ping = await Send(host, new JObject { ["Id"] = 2, ["Op"] = "ping" });
                Assert.False((bool)ping["Playing"]!);
            }
            var shutdown = await Send(host, new JObject { ["Id"] = 3, ["Op"] = "shutdown" });
            Assert.True((bool)shutdown["Ok"]!);
            await host.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, host.ExitCode);
            Assert.Equal("", await errors);
        }
        finally { if (!host.HasExited) host.Kill(true); }

        using var eof = Process.Start(info)!;
        var eofErrors = eof.StandardError.ReadToEndAsync();
        try
        {
            Assert.True((bool)(await Send(eof, new JObject { ["Id"] = 4, ["Op"] = "play", ["Cue"] = "coin" }))["Ok"]!);
            eof.StandardInput.Close();
            await eof.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, eof.ExitCode);
            Assert.Equal("", await eofErrors);
        }
        finally { if (!eof.HasExited) eof.Kill(true); }
    }

    private static async Task<JObject> Send(Process host, JObject command)
    {
        await host.StandardInput.WriteLineAsync(command.ToString(Formatting.None));
        await host.StandardInput.FlushAsync();
        string? line = await host.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
        Assert.NotNull(line);
        var reply = JObject.Parse(line);
        Assert.Equal((long)command["Id"]!, (long)reply["Id"]!);
        return reply;
    }
}
