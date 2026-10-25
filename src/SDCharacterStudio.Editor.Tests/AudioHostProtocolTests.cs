using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace SDCharacterStudio.Editor.Tests;

public sealed class AudioHostProtocolTests
{
    [Fact]
    public async Task HostBoundsPlaybackAndRejectsCatalogErrorsAndTraversal()
    {
        string host = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "SDCharacterStudio.AudioHost", "bin", "Debug", "net8.0-windows", "SDCharacterStudio.AudioHost.exe"));
        Assert.True(File.Exists(host), host);
        string dir = Path.Combine(Path.GetTempPath(), "sdcs-audio-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "sound-previews.json"), "{\"Format\":1,\"Cues\":{\"bad\":{\"Error\":\"source unavailable\"},\"escape\":{\"Variants\":[{\"File\":\"../outside.ogg\"}]}}}");
            using var p = Process.Start(new ProcessStartInfo(host, "--game-dir \"" + @"C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley" + "\"") { RedirectStandardInput=true, RedirectStandardOutput=true, UseShellExecute=false, CreateNoWindow=true })!;
            async Task<JsonDocument> Send(string json){await p.StandardInput.WriteLineAsync(json);return JsonDocument.Parse((await p.StandardOutput.ReadLineAsync())!);}
            Assert.False((await Send("{\"Id\":1,\"Op\":\"play\",\"Cue\":\"bad\",\"Catalog\":\""+dir.Replace("\\","\\\\")+"\"}")).RootElement.GetProperty("Ok").GetBoolean());
            Assert.False((await Send("{\"Id\":2,\"Op\":\"play\",\"Cue\":\"escape\",\"Catalog\":\""+dir.Replace("\\","\\\\")+"\"}")).RootElement.GetProperty("Ok").GetBoolean());
            Assert.True((await Send("{\"Id\":3,\"Op\":\"shutdown\"}")).RootElement.GetProperty("Ok").GetBoolean()); await p.WaitForExitAsync(); Assert.Equal(0,p.ExitCode);
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact]
    public async Task HostRejectsMalformedAndUnknownRequestsAndExitsCleanly()
    {
        string host = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "SDCharacterStudio.AudioHost", "bin", "Debug", "net8.0-windows", "SDCharacterStudio.AudioHost.dll"));
        Assert.True(File.Exists(host), host);
        using var p = Process.Start(new ProcessStartInfo("dotnet", $"\"{host}\" --game-dir \"{Path.GetTempPath()}\"") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true })!;
        await p.StandardInput.WriteLineAsync("not-json");
        var malformed = JsonDocument.Parse((await p.StandardOutput.ReadLineAsync())!);
        Assert.False(malformed.RootElement.GetProperty("Ok").GetBoolean());
        await p.StandardInput.WriteLineAsync("{\"Id\":2,\"Op\":\"unknown\"}");
        var unknown = JsonDocument.Parse((await p.StandardOutput.ReadLineAsync())!);
        Assert.False(unknown.RootElement.GetProperty("Ok").GetBoolean());
        await p.StandardInput.WriteLineAsync("{\"Id\":3,\"Op\":\"ping\"}");
        var ping = JsonDocument.Parse((await p.StandardOutput.ReadLineAsync())!);
        Assert.True(ping.RootElement.GetProperty("Ok").GetBoolean());
        await p.StandardInput.WriteLineAsync("{\"Id\":4,\"Op\":\"shutdown\"}");
        var shutdown = JsonDocument.Parse((await p.StandardOutput.ReadLineAsync())!);
        Assert.True(shutdown.RootElement.GetProperty("Ok").GetBoolean());
        await p.WaitForExitAsync();
        Assert.Equal(0, p.ExitCode);
    }
}
