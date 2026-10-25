using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SDCharacterStudio.Editor.Preview;

/// <summary>
/// Client for the Bridge's preview pipe (doc 06 §7, docs/bridge-preview.md): UTF-8 JSON lines both ways. Requests carry
/// an Id; replies with Final=false are progress, the Final reply completes the request. Op "Log" lines are Runtime diagnostics.
/// </summary>
public sealed class BridgeClient : IDisposable
{
    public const string DefaultPipe = "SDCharacterStudio.Preview";
    private readonly string pipeName;
    private readonly ConcurrentDictionary<string, Pending> pending = new();
    private readonly SemaphoreSlim connectGate = new(1, 1);
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private NamedPipeClientStream? pipe;
    private StreamWriter? writer;
    private CancellationTokenSource? reading;

    private sealed record Pending(TaskCompletionSource<JObject> Completion, Action<JObject>? Progress);

    public BridgeClient(string pipeName = DefaultPipe) => this.pipeName = pipeName;
    public bool IsConnected => pipe?.IsConnected == true;
    /// <summary>Runtime warnings/errors and other unsolicited lines.</summary>
    public event Action<JObject>? Notification;
    public event Action? Disconnected;

    public async Task<bool> TryConnectAsync(int timeoutMs = 300)
    {
        if (IsConnected) return true;
        await connectGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (IsConnected) return true;
            Close();
            var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try { await client.ConnectAsync(timeoutMs).ConfigureAwait(false); }
            catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException) { client.Dispose(); return false; }
            pipe = client;
            writer = new StreamWriter(client, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
            reading = new CancellationTokenSource();
            _ = Task.Run(() => ReadLoop(client, reading.Token));
            return true;
        }
        finally { connectGate.Release(); }
    }

    /// <summary>Send a request and wait for its final reply. Throws <see cref="BridgeException"/> when the Bridge reports a failure.</summary>
    public async Task<JObject> SendAsync(JObject request, Action<JObject>? progress = null, TimeSpan? timeout = null, CancellationToken cancellation = default)
    {
        if (!await TryConnectAsync(1000).ConfigureAwait(false)) throw new BridgeException("The game isn't running with the SDCS Bridge, or its preview pipe isn't ready.");
        string id = Guid.NewGuid().ToString("N");
        request = (JObject)request.DeepClone();
        request["Id"] = id;
        var entry = new Pending(new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously), progress);
        pending[id] = entry;
        try
        {
            await writeGate.WaitAsync(cancellation).ConfigureAwait(false);
            try { await writer!.WriteLineAsync(request.ToString(Formatting.None)).ConfigureAwait(false); }
            finally { writeGate.Release(); }
            using var timer = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(60));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(timer.Token, cancellation);
            using (linked.Token.Register(() => entry.Completion.TrySetCanceled()))
            {
                JObject reply;
                try { reply = await entry.Completion.Task.ConfigureAwait(false); }
                catch (TaskCanceledException) when (timer.IsCancellationRequested) { throw new BridgeException($"The game didn't answer '{request["Op"]}' in time."); }
                if ((bool?)reply["Ok"] == false) throw new BridgeException((string?)reply["Error"] ?? "The Bridge reported an error.");
                return reply;
            }
        }
        catch (IOException ex) { throw new BridgeException("Lost the connection to the game: " + ex.Message); }
        finally { pending.TryRemove(id, out _); }
    }

    private async Task ReadLoop(NamedPipeClientStream client, CancellationToken token)
    {
        try
        {
            using var reader = new StreamReader(client, new UTF8Encoding(false), false, 4096, leaveOpen: true);
            while (!token.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                if (line == null) break;
                if (line.Length == 0) continue;
                JObject message;
                try { message = JObject.Parse(line); } catch (JsonException) { continue; }
                string? id = (string?)message["Id"];
                if (id != null && pending.TryGetValue(id, out var entry))
                {
                    if ((bool?)message["Final"] == false) entry.Progress?.Invoke(message);
                    else entry.Completion.TrySetResult(message);
                }
                else Notification?.Invoke(message);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
        foreach (var entry in pending.Values) entry.Completion.TrySetException(new IOException("The game closed the preview connection."));
        if (!token.IsCancellationRequested) Disconnected?.Invoke();
    }

    private void Close()
    {
        reading?.Cancel();
        writer?.Dispose(); writer = null;
        pipe?.Dispose(); pipe = null;
    }

    public void Dispose() { Close(); connectGate.Dispose(); writeGate.Dispose(); }
}

public sealed class BridgeException : Exception { public BridgeException(string message) : base(message) { } }
