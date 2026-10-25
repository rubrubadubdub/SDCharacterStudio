using SDCharacterStudio.Editor.Audio;
using Newtonsoft.Json;
using System.Diagnostics;
using System.IO;

namespace SDCharacterStudio.Editor.Tests;

public sealed class AudioPreviewTests
{
    [Fact]
    public async Task NativeServiceSerializesPlayStopCancelAndDispose()
    {
        if (Environment.GetEnvironmentVariable("SDCS_AUDIO_TESTS") != "1") return;
        string game = @"C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley";
        if (!Directory.Exists(game)) return;
        Assert.Empty(Process.GetProcessesByName("Stardew Valley"));
        string host = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "SDCharacterStudio.AudioHost", "bin", "Debug", "net8.0-windows", "SDCharacterStudio.AudioHost.exe"));
        Assert.True(File.Exists(host), host);
        await using var service = new AudioPreviewService(host, Path.GetTempPath(), game);
        var first = await service.PlayAsync("coin"); Assert.True(first.Ok, first.Error);
        Assert.True((await service.PlayAsync("coin")).Ok);
        await service.StopAsync();
        Assert.True((await service.PlayAsync("coin")).Ok);
        using var cancelled = new CancellationTokenSource();
        var pending = service.PlayAsync("coin", cancellation: cancelled.Token);
        cancelled.Cancel();
        Assert.False((await pending).Ok);
        Assert.True((await service.PlayAsync("coin")).Ok);
        await service.DisposeAsync();
        Assert.False((await service.PlayAsync("coin")).Ok);
    }
    [Fact]
    public void SupplementalCatalogDeserializesCaseInsensitiveCueMap()
    {
        var catalog = JsonConvert.DeserializeObject<AudioPreviewCatalog>("""{"Format":1,"Cues":{"Mod.Cue":{"Looped":true,"Variants":[{"File":"audio/Mod.Cue/0.ogg"}]}}}""")!;
        Assert.Equal(1, catalog.Format);
        Assert.True(catalog.Cues["mod.cue"].Looped);
        Assert.Equal("audio/Mod.Cue/0.ogg", catalog.Cues["MOD.CUE"].Variants[0].File);
    }

    [Fact]
    public async Task EmptyCueFailsWithoutStartingHost()
    {
        var service = new AudioPreviewService("missing-host.exe", "missing", "missing");
        var result = await service.PlayAsync(" ");
        Assert.False(result.Ok);
        Assert.Contains("empty", result.Error!, StringComparison.OrdinalIgnoreCase);
    }
}
