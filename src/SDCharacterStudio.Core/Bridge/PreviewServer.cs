using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;

namespace SDCharacterStudio.Core.Bridge;

public sealed class PreviewRequest
{
    public JObject Request { get; }
    private readonly Action<JObject, bool> respond;
    private readonly object gate = new();
    private bool complete;
    internal bool IsComplete { get { lock (gate) return complete; } }
    internal PreviewRequest(JObject request, Action<JObject, bool> respond) { Request = request; this.respond = respond; }
    public void Reply(JObject response, bool final = true)
    {
        lock (gate)
        {
            if (complete) return;
            var copy = (JObject)response.DeepClone();
            copy["Id"] ??= Request["Id"]?.DeepClone();
            copy["Op"] ??= Request["Op"]?.DeepClone();
            copy["Final"] = final;
            if (final) complete = true;
            // The operation closes once; its transport retains ownership until delivery.
            respond(copy, final);
        }
    }
}

/// <summary>Transport only. Consumers drain requests and perform all game work on their own thread.</summary>
public sealed class PreviewServer : IDisposable
{
    private const int MaximumRequestLength = 128 * 1024;
    private const int MaximumPending = 128;
    private readonly string name, requests;
    private readonly string responses;
    private readonly CancellationTokenSource stopping = new();
    private readonly ConcurrentQueue<PreviewRequest> queue = new();
    private readonly ConcurrentDictionary<Guid, PreviewRequest> pending = new();
    private readonly ConcurrentDictionary<Guid, PipeOutput> clients = new();
    private readonly SemaphoreSlim requestSlots = new(MaximumPending, MaximumPending);
    private readonly SemaphoreSlim connectionSlots = new(16, 16);
    private readonly object filesGate = new();
    private readonly Dictionary<string, FileReply> claimed = new(StringComparer.OrdinalIgnoreCase);
    private Task? accept;
    private bool disposed;
    public string? LastError { get; private set; }
    public bool IsListening { get; private set; }
    /// <summary>
    /// Answers a request at once on the pipe thread, or returns null to queue it for the game thread. For status only:
    /// it must read nothing but thread-safe snapshots, so a game thread busy with an export still answers.
    /// </summary>
    public Func<JObject, JObject?>? Immediate { get; set; }

    public PreviewServer(string name, string requests)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0) throw new ArgumentException("Invalid preview pipe name.");
        this.name = name; this.requests = Path.GetFullPath(requests);
        Directory.CreateDirectory(this.requests);
        responses = Path.Combine(Path.GetDirectoryName(this.requests)!, "responses");
        Directory.CreateDirectory(responses);
    }
    public void Start() { if (disposed) throw new ObjectDisposedException(nameof(PreviewServer)); accept ??= Task.Run(AcceptLoop); }

    private async Task AcceptLoop()
    {
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                NamedPipeServerStream? pipe = null;
                bool slot = false;
                try
                {
                    await connectionSlots.WaitAsync(stopping.Token).ConfigureAwait(false); slot = true;
                    pipe = new NamedPipeServerStream(name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                        OperatingSystem.IsWindows() ? PipeTransmissionMode.Message : PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    IsListening = true;
                    await pipe.WaitForConnectionAsync(stopping.Token).ConfigureAwait(false);
                    _ = Connection(pipe); pipe = null; slot = false;
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException or ObjectDisposedException)
                {
                    pipe?.Dispose();
                    if (slot) connectionSlots.Release();
                    if (!stopping.IsCancellationRequested)
                    {
                        LastError = ex.Message;
                        await Task.Delay(1000, stopping.Token).ConfigureAwait(false);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        finally { IsListening = false; }
    }

    private async Task Connection(NamedPipeServerStream pipe)
    {
        Guid id = Guid.NewGuid();
        var output = new PipeOutput(pipe, stopping.Token);
        clients[id] = output;
        var write = output.Write();
        bool drain = false;
        try
        {
            await foreach (string line in ReadLines(pipe, output.Token))
            {
                try
                {
                    var request = JObject.Parse(line);
                    Enqueue(request, (reply, final, complete) =>
                    {
                        output.Send(reply, final);
                        if (final) complete();
                    });
                }
                catch (Exception ex) when (ex is JsonException or ArgumentException) { output.Send(Error(ex.Message), true); }
            }
            drain = true;
        }
        catch (JsonException ex) { output.Send(Error(ex.Message), true); drain = true; }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
        finally
        {
            clients.TryRemove(id, out _);
            output.Complete();
            if (drain)
                try { await write.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
                catch (Exception ex) when (ex is TimeoutException or IOException or OperationCanceledException or ObjectDisposedException) { }
            output.Abort();
            try { await write.ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
            output.Dispose();
            connectionSlots.Release();
        }
    }

    private static async IAsyncEnumerable<string> ReadLines(Stream pipe, [EnumeratorCancellation] CancellationToken cancellation)
    {
        // Bound bytes before decoding, including a peer that never sends a newline.
        var line = new byte[MaximumRequestLength];
        var chunk = new byte[4096];
        int length = 0;
        while (true)
        {
            int read = await pipe.ReadAsync(chunk.AsMemory(), cancellation).ConfigureAwait(false);
            if (read == 0) break;
            for (int i = 0; i < read; i++)
            {
                byte value = chunk[i];
                if (value == (byte)'\n')
                {
                    yield return Decode(line, length > 0 && line[length - 1] == (byte)'\r' ? length - 1 : length);
                    length = 0;
                }
                else
                {
                    if (length == line.Length) throw new JsonException("Preview request exceeds128KiB.");
                    line[length++] = value;
                }
            }
        }
        if (length > 0) yield return Decode(line, length);
    }

    private void Enqueue(JObject request, Action<JObject, bool, Action> respond)
    {
        if (Immediate?.Invoke(request) is { } answer)
        {
            new PreviewRequest(request, (reply, final) => respond(reply, final, () => { })).Reply(answer);
            return;
        }
        if (!requestSlots.Wait(0))
        {
            new PreviewRequest(request, (reply, final) => respond(reply, final, () => { }))
                .Reply(Error("Preview queue is full."));
            return;
        }
        Guid id = Guid.NewGuid();
        int completed = 0;
        void Complete()
        {
            if (Interlocked.Exchange(ref completed, 1) != 0) return;
            pending.TryRemove(id, out _); requestSlots.Release();
        }
        var work = new PreviewRequest(request, (reply, final) => respond(reply, final, Complete));
        pending[id] = work; queue.Enqueue(work);
    }

    public void PollFiles()
    {
        if (disposed) return;
        lock (filesGate)
        {
            foreach (var reply in claimed.Values.ToArray()) Flush(reply);
            // Filter before taking the budget. Delivery ownership is bounded as well.
            var files = Directory.EnumerateFiles(requests, "*.json")
                .Where(file => !claimed.ContainsKey(file) && Guid.TryParse(Path.GetFileNameWithoutExtension(file), out _))
                .Take(MaximumPending - claimed.Count).ToArray();
            foreach (string file in files)
            {
                try
                {
                    var request = JObject.Parse(ReadFile(file));
                    var state = new FileReply(file); claimed.Add(file, state);
                    Enqueue(request, (reply, final, complete) =>
                    {
                        lock (filesGate)
                        {
                            state.Response = reply; state.Final = final;
                            if (final) state.Complete = complete;
                            Flush(state);
                        }
                    });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LastError = ex.Message; }
                catch (JsonException ex)
                {
                    if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < TimeSpan.FromSeconds(2)) continue;
                    var state = new FileReply(file) { Response = Error(ex.Message), Final = true };
                    claimed.Add(file, state); Flush(state);
                }
            }
        }
    }

    private static string ReadFile(string file)
    {
        using var stream = File.OpenRead(file);
        var buffer = new byte[MaximumRequestLength + 1];
        int length = 0, read;
        while ((read = stream.Read(buffer, length, buffer.Length - length)) != 0)
        {
            length += read;
            if (length > MaximumRequestLength) throw new JsonException("Preview request exceeds128KiB.");
        }
        return Decode(buffer, length);
    }

    private static string Decode(byte[] buffer, int length)
    {
        int start = length >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF ? 3 : 0;
        return Encoding.UTF8.GetString(buffer, start, length - start);
    }

    private void Flush(FileReply state)
    {
        if (state.Response is null) return;
        try
        {
            WriteResponse(Path.GetFileName(state.File), state.Response);
            if (state.Final)
            {
                if (File.Exists(state.File)) File.Delete(state.File);
                claimed.Remove(state.File);
                state.Complete?.Invoke();
            }
            state.Response = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Preserve the exact reply and operation slot for the next poll, without reexecution.
            LastError = ex.Message;
        }
    }

    private void WriteResponse(string name, JObject response)
    {
        // Responses have unique GUID names. Do not retain an export hash entry per request.
        string destination = Path.Combine(responses, name);
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, response.ToString(Formatting.Indented) + "\n", new UTF8Encoding(false));
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public bool TryDequeue(out PreviewRequest? request)
    {
        while (queue.TryDequeue(out request)) if (!request.IsComplete) return true;
        request = null; return false;
    }
    public void Publish(JObject response)
    { foreach (var client in clients.Values) client.Send((JObject)response.DeepClone(), false); }
    private static JObject Error(string message) => new() { ["Ok"] = false, ["Error"] = message, ["Final"] = true };

    public void Dispose()
    {
        if (disposed) return; disposed = true;
        try
        {
            foreach (var request in pending.Values) request.Reply(Error("Preview server is closing."));
            lock (filesGate) foreach (var reply in claimed.Values.ToArray()) Flush(reply);
        }
        finally
        {
            stopping.Cancel();
            foreach (var client in clients.Values) client.Abort();
            while (queue.TryDequeue(out _)) { }
            IsListening = false;
        }
    }

    private sealed class FileReply
    {
        public string File { get; }
        public JObject? Response;
        public bool Final;
        public Action? Complete;
        public FileReply(string file) => File = file;
    }

    private sealed class PipeOutput : IDisposable
    {
        private readonly NamedPipeServerStream pipe;
        private readonly Channel<JObject> finals = Channel.CreateBounded<JObject>(new BoundedChannelOptions(MaximumPending) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
        private readonly Channel<JObject> progress = Channel.CreateBounded<JObject>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        private readonly SemaphoreSlim wake = new(0, 1);
        private readonly CancellationTokenSource closed;
        private int finished, aborted;
        public CancellationToken Token => closed.Token;
        public PipeOutput(NamedPipeServerStream pipe, CancellationToken cancellation)
        { this.pipe = pipe; closed = CancellationTokenSource.CreateLinkedTokenSource(cancellation); }
        public void Send(JObject response, bool final)
        {
            if (Volatile.Read(ref aborted) != 0) return;
            if (final && !finals.Writer.TryWrite(response))
            {
                // Full reliable backlog means explicit connection failure, never silent loss.
                Abort(); return;
            }
            if (!final) progress.Writer.TryWrite(response);
            Signal();
        }
        private void Signal() { try { wake.Release(); } catch (SemaphoreFullException) { } }
        public async Task Write()
        {
            try
            {
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
                while (!Token.IsCancellationRequested)
                {
                    if (finals.Reader.TryRead(out var final)) await writer.WriteLineAsync(final.ToString(Formatting.None).AsMemory(), Token).ConfigureAwait(false);
                    else if (progress.Reader.TryRead(out var update)) await writer.WriteLineAsync(update.ToString(Formatting.None).AsMemory(), Token).ConfigureAwait(false);
                    else if (Volatile.Read(ref finished) != 0) return;
                    else await wake.WaitAsync(Token).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { Abort(); }
        }
        public void Complete() { Interlocked.Exchange(ref finished, 1); finals.Writer.TryComplete(); progress.Writer.TryComplete(); Signal(); }
        public void Abort()
        {
            if (Interlocked.Exchange(ref aborted, 1) != 0) return;
            closed.Cancel(); pipe.Dispose(); Signal();
        }
        public void Dispose() { Abort(); closed.Dispose(); }
    }
}
