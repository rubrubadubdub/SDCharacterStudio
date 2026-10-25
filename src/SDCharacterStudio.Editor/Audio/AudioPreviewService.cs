using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SDCharacterStudio.Editor.Audio;

/// <summary>One owned audio process, with serialized correlated requests and bounded playback.</summary>
public sealed class AudioPreviewService(string hostPath, string catalogDirectory, string gameDirectory) : IAudioPreviewService
{
    private readonly object gate = new();
    private readonly SemaphoreSlim io = new(1, 1);
    private Process? process;
    private CancellationTokenSource? active;
    private bool disposed;
    private long sequence;
    private DateTime playingUntil;
    private AudioHostStatus status = new(AudioHostState.Unavailable, "Choose a sound to preview.");
    public AudioHostStatus Status
    {
        get { lock (gate) return status.State == AudioHostState.Playing && DateTime.UtcNow >= playingUntil ? new(AudioHostState.Ready, "Ready") : status; }
    }

    public async Task<AudioPreviewResult> PlayAsync(string cue, int? pitch = null, CancellationToken cancellation = default)
    {
        if (string.IsNullOrWhiteSpace(cue)) return AudioPreviewResult.Failure("Sound cue is empty.");
        CancellationTokenSource request;
        lock (gate)
        {
            if (disposed) return AudioPreviewResult.Failure("Audio preview is closed.");
            active?.Cancel();
            active = request = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        }
        bool entered = false;
        try
        {
            await io.WaitAsync(request.Token).ConfigureAwait(false); entered = true;
            request.Token.ThrowIfCancellationRequested();
            var reply = await SendAsync(new JObject { ["Op"] = "play", ["Cue"] = cue, ["Pitch"] = pitch, ["Catalog"] = catalogDirectory, ["MaxSeconds"] = 10 }, request.Token).ConfigureAwait(false);
            if (reply["Ok"]?.Value<bool>() != true)
            {
                string error = (string?)reply["Error"] ?? "Audio preview failed.";
                SetStatus(AudioHostState.Faulted, error);
                return AudioPreviewResult.Failure(error);
            }
            lock (gate) { status = new(AudioHostState.Playing, cue); playingUntil = DateTime.UtcNow.AddSeconds(10); }
            return AudioPreviewResult.Success(cue);
        }
        catch (OperationCanceledException)
        {
            // A partial exchange must not leave a reply for the next caller.
            if (entered) ResetHost();
            return AudioPreviewResult.Failure("Audio preview cancelled.");
        }
        catch (Exception ex)
        {
            if (entered) ResetHost();
            SetStatus(AudioHostState.Faulted, ex.Message);
            return AudioPreviewResult.Failure(ex.Message);
        }
        finally
        {
            lock (gate) { if (ReferenceEquals(active, request)) active = null; request.Dispose(); }
            if (entered) io.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellation = default)
    {
        lock (gate) active?.Cancel();
        await io.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            if (process is { HasExited: false }) await SendAsync(new JObject { ["Op"] = "stop" }, cancellation).ConfigureAwait(false);
            SetStatus(AudioHostState.Ready, "Stopped");
        }
        catch (Exception ex) { ResetHost(); SetStatus(AudioHostState.Faulted, ex.Message); }
        finally { io.Release(); }
    }

    private async Task<JObject> SendAsync(JObject command, CancellationToken cancellation)
    {
        EnsureStarted();
        command["Id"] = ++sequence;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        await process!.StandardInput.WriteLineAsync(command.ToString(Formatting.None)).WaitAsync(timeout.Token).ConfigureAwait(false);
        await process.StandardInput.FlushAsync(timeout.Token).ConfigureAwait(false);
        string? line = await process.StandardOutput.ReadLineAsync(timeout.Token).ConfigureAwait(false);
        if (line == null) throw new IOException("The audio host exited. Check the installed game and audio device.");
        var reply = JObject.Parse(line);
        if ((long?)reply["Id"] != sequence) throw new IOException("Audio host returned an unrelated response.");
        return reply;
    }

    private void EnsureStarted()
    {
        if (process is { HasExited: false }) return;
        ResetHost();
        if (!File.Exists(hostPath)) throw new FileNotFoundException("The editor's AudioHost is missing. Rebuild or reinstall the editor.", hostPath);
        if (!File.Exists(Path.Combine(gameDirectory, "MonoGame.Framework.dll"))) throw new DirectoryNotFoundException("The installed Stardew Valley directory is unavailable. Refresh the Bridge export.");
        var info = new ProcessStartInfo(hostPath) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        info.ArgumentList.Add("--game-dir"); info.ArgumentList.Add(gameDirectory);
        process = new Process { StartInfo = info };
        if (!process.Start()) throw new IOException("Could not start audio host.");
        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        SetStatus(AudioHostState.Ready, "Ready");
    }

    private void SetStatus(AudioHostState state, string message) { lock (gate) status = new(state, message); }
    private void ResetHost()
    {
        if (process == null) return;
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        process.Dispose(); process = null;
    }
    public async ValueTask DisposeAsync()
    {
        lock (gate) { if (disposed) return; disposed = true; active?.Cancel(); }
        await io.WaitAsync().ConfigureAwait(false);
        try { ResetHost(); SetStatus(AudioHostState.Unavailable, "Audio preview is closed."); }
        finally { io.Release(); }
    }
}
