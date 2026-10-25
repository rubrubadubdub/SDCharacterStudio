using System.IO.Pipes;
using System.Text;
using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core.Bridge;
using Xunit;

namespace SDCharacterStudio.Core.Tests.Bridge;

public sealed class PreviewServerTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "sdcs-preview-" + Guid.NewGuid().ToString("N"), "requests");

    [Fact]
    public void FileDropIsQueuedOnceAndCompletesInSiblingResponsesFolder()
    {
        using var server = new PreviewServer("sdcs-test-" + Guid.NewGuid().ToString("N"), folder);
        Directory.CreateDirectory(folder);
        string id = Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(folder, id + ".json"), "{\"Op\":\"Ping\"}");
        server.PollFiles(); Assert.True(server.TryDequeue(out var request));
        server.PollFiles(); Assert.False(server.TryDequeue(out _));
        request!.Reply(new JObject { ["Ok"] = true });
        Assert.True((bool)JObject.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(folder)!, "responses", id + ".json")))["Ok"]!);
        Assert.False(File.Exists(Path.Combine(folder, id + ".json")));
    }

    [Fact]
    public void PartialFileCanFinishBeforeBeingClaimed()
    {
        using var server = new PreviewServer("sdcs-test-" + Guid.NewGuid().ToString("N"), folder);
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(file, "{\"Op\":"); server.PollFiles(); Assert.False(server.TryDequeue(out _));
        File.WriteAllText(file, "{\"Op\":\"Ping\"}"); server.PollFiles(); Assert.True(server.TryDequeue(out var request));
        request!.Reply(new JObject { ["Ok"] = true });
    }

    [Fact]
    public async Task PipeRejectsMalformedJsonThenAcceptsRequestOnSameConnection()
    {
        string name = "sdcs-test-" + Guid.NewGuid().ToString("N");
        using var server = new PreviewServer(name, folder); server.Start();
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(3000);
        using var writer = new StreamWriter(client, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
        using var reader = new StreamReader(client, Encoding.UTF8, false, 1024, true);
        await writer.WriteLineAsync("broken");
        Assert.False((bool)JObject.Parse((await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3)))!)["Ok"]!);
        await writer.WriteLineAsync("{\"Op\":\"Ping\",\"Id\":\"mine\"}");
        PreviewRequest? request = null;
        for (int i = 0; i < 300 && !server.TryDequeue(out request); i++) await Task.Delay(10);
        Assert.NotNull(request);
        request!.Reply(new JObject { ["Ok"] = true });
        var reply = JObject.Parse((await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3)))!);
        Assert.True((bool)reply["Ok"]!); Assert.Equal("mine", (string?)reply["Id"]);
    }

    [Fact]
    public async Task StatusIsAnsweredOnThePipeThreadWhileTheGameThreadIsBusy()
    {
        string name = "sdcs-test-" + Guid.NewGuid().ToString("N");
        using var server = new PreviewServer(name, folder)
        {
            Immediate = request => (string?)request["Op"] == "Ping" ? new JObject { ["Ok"] = true, ["Exporting"] = true } : null
        };
        server.Start();
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(3000);
        using var writer = new StreamWriter(client, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
        using var reader = new StreamReader(client, Encoding.UTF8, false, 1024, true);
        // Nobody drains the queue (the game thread is busy): the ping is still answered, and never queued.
        await writer.WriteLineAsync("{\"Op\":\"Ping\",\"Id\":\"status\"}");
        var reply = JObject.Parse((await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3)))!);
        Assert.True((bool)reply["Exporting"]!); Assert.Equal("status", (string?)reply["Id"]); Assert.True((bool)reply["Final"]!);
        Assert.False(server.TryDequeue(out _));
        // Everything else still waits for the game thread.
        await writer.WriteLineAsync("{\"Op\":\"Export\"}");
        PreviewRequest? queued = null;
        for (int i = 0; i < 300 && !server.TryDequeue(out queued); i++) await Task.Delay(10);
        Assert.Equal("Export", (string?)queued?.Request["Op"]);
        queued!.Reply(new JObject { ["Ok"] = true });
    }

    [Fact]
    public void FinalFileReplySurvivesALockedResponseAndIsRetriedByTheNextPoll()
    {
        using var server = new PreviewServer("sdcs-test-" + Guid.NewGuid().ToString("N"), folder);
        string id = Guid.NewGuid().ToString("N");
        string requestFile = Path.Combine(folder, id + ".json");
        string responseFile = Path.Combine(Path.GetDirectoryName(folder)!, "responses", id + ".json");
        File.WriteAllText(requestFile, "{\"Op\":\"Ping\",\"Id\":\"retry\"}");
        File.WriteAllText(responseFile, "temporary prior response");
        server.PollFiles(); Assert.True(server.TryDequeue(out var request));
        using (var locked = new FileStream(responseFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Null(Record.Exception(() => request!.Reply(new JObject { ["Ok"] = true, ["Result"] = "original" })));
            Assert.Null(Record.Exception(server.PollFiles));
            Assert.True(File.Exists(requestFile));
            Assert.False(server.TryDequeue(out _));
        }
        server.PollFiles();
        var reply = JObject.Parse(File.ReadAllText(responseFile));
        Assert.True((bool)reply["Ok"]!);
        Assert.Equal("original", (string?)reply["Result"]);
        Assert.Equal("retry", (string?)reply["Id"]);
        Assert.True((bool)reply["Final"]!);
        Assert.False(File.Exists(requestFile));
    }

    [Fact]
    public void StrayFileNamesDoNotUseTheEligibleRequestBudget()
    {
        using var server = new PreviewServer("sdcs-test-" + Guid.NewGuid().ToString("N"), folder);
        for (int i = 0; i < 160; i++) File.WriteAllText(Path.Combine(folder, "0000-stray-" + i + ".json"), "{}");
        File.WriteAllText(Path.Combine(folder, "ffffffffffffffffffffffffffffffff.json"), "{\"Op\":\"Ping\",\"Id\":\"eligible\"}");
        server.PollFiles();
        Assert.True(server.TryDequeue(out var request));
        Assert.Equal("eligible", (string?)request!.Request["Id"]);
    }

    [Fact]
    public async Task OversizedPipeInputIsRejectedWithoutWaitingForANewline()
    {
        string name = "sdcs-test-" + Guid.NewGuid().ToString("N");
        using var server = new PreviewServer(name, folder); server.Start();
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(3000);
        using var reader = new StreamReader(client, Encoding.UTF8, false, 1024, true);
        await client.WriteAsync(Enumerable.Repeat((byte)'x', 128 * 1024 + 1).ToArray());
        var reply = JObject.Parse((await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3)))!);
        Assert.False((bool)reply["Ok"]!);
        Assert.Contains("128", (string?)reply["Error"]);
        Assert.False(server.TryDequeue(out _));
    }

    [Fact]
    public async Task AFinalPipeReplyCannotBeEvictedByLogsWhileTheClientIsNotReading()
    {
        string name = "sdcs-test-" + Guid.NewGuid().ToString("N");
        using var server = new PreviewServer(name, folder); server.Start();
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(3000);
        using var writer = new StreamWriter(client, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
        using var reader = new StreamReader(client, Encoding.UTF8, false, 1024, true);
        await writer.WriteLineAsync("{\"Op\":\"Ping\",\"Id\":\"must-arrive\"}");
        PreviewRequest? request = null;
        for (int i = 0; i < 300 && !server.TryDequeue(out request); i++) await Task.Delay(10);
        Assert.NotNull(request);
        // The first diagnostic fills the native pipe buffer and blocks the writer. The final
        // reply is then waiting in the outbound queue while later diagnostic traffic arrives.
        server.Publish(new JObject { ["Op"] = "Log", ["Text"] = new string('b', 2 * 1024 * 1024) });
        Assert.Equal(1, await reader.ReadAsync(new char[1], 0, 1).WaitAsync(TimeSpan.FromSeconds(3)));
        request!.Reply(new JObject { ["Ok"] = true });
        for (int i = 0; i < 600; i++) server.Publish(new JObject { ["Op"] = "Log", ["Index"] = i });
        await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3)); // Remainder of the blocking diagnostic.
        JObject? final = null;
        for (int i = 0; i < 260; i++)
        {
            var line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3));
            Assert.NotNull(line);
            var reply = JObject.Parse(line!);
            if ((string?)reply["Id"] != "must-arrive") continue;
            final = reply; break;
        }
        Assert.NotNull(final);
        Assert.True((bool)final!["Ok"]!);
        Assert.True((bool)final["Final"]!);
    }

    [Fact]
    public void Utf8FilePreambleDoesNotPreventAFileRequestFromBeingAccepted()
    {
        using var server = new PreviewServer("sdcs-test-" + Guid.NewGuid().ToString("N"), folder);
        string file = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(file, "{\"Op\":\"Ping\",\"Id\":\"with-preamble\"}", new UTF8Encoding(true));
        server.PollFiles();
        Assert.True(server.TryDequeue(out var request));
        Assert.Equal("with-preamble", (string?)request!.Request["Id"]);
    }

    [Fact]
    public void DisposeStillClosesTransportWhenAFileResponseIsLocked()
    {
        using var server = new PreviewServer("sdcs-test-" + Guid.NewGuid().ToString("N"), folder);
        string id = Guid.NewGuid().ToString("N");
        string file = Path.Combine(folder, id + ".json");
        string response = Path.Combine(Path.GetDirectoryName(folder)!, "responses", id + ".json");
        File.WriteAllText(file, "{\"Op\":\"Ping\"}"); File.WriteAllText(response, "old");
        server.PollFiles(); Assert.True(server.TryDequeue(out _));
        using var locked = new FileStream(response, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Null(Record.Exception(server.Dispose));
        Assert.False(server.IsListening);
        Assert.False(server.TryDequeue(out _));
        Assert.True(File.Exists(file));
        Assert.Throws<ObjectDisposedException>(server.Start);
    }

    [Fact]
    public async Task DisconnectedClientsDoNotBreakRepliesOrTheNextConnection()
    {
        string name = "sdcs-test-" + Guid.NewGuid().ToString("N");
        using var server = new PreviewServer(name, folder); server.Start();
        PreviewRequest? abandoned = null;
        using (var first = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            await first.ConnectAsync(3000);
            using var writer = new StreamWriter(first, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
            await writer.WriteLineAsync("{\"Op\":\"Ping\",\"Id\":\"abandoned\"}");
            for (int i = 0; i < 300 && !server.TryDequeue(out abandoned); i++) await Task.Delay(10);
            Assert.NotNull(abandoned);
        }
        await Task.Delay(100);
        Assert.Null(Record.Exception(() => abandoned!.Reply(new JObject { ["Ok"] = true })));
        using var second = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await second.ConnectAsync(3000);
        using var nextWriter = new StreamWriter(second, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
        using var nextReader = new StreamReader(second, Encoding.UTF8, false, 1024, true);
        await nextWriter.WriteLineAsync("{\"Op\":\"Ping\",\"Id\":\"reconnected\"}");
        PreviewRequest? next = null;
        for (int i = 0; i < 300 && !server.TryDequeue(out next); i++) await Task.Delay(10);
        Assert.NotNull(next); next!.Reply(new JObject { ["Ok"] = true });
        var reply = JObject.Parse((await nextReader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3)))!);
        Assert.Equal("reconnected", (string?)reply["Id"]);
        Assert.True((bool)reply["Ok"]!);
    }

    [Fact]
    public async Task FullPendingQueueReturnsAFinalErrorWithTheRejectedRequestsIdAndOperation()
    {
        string name = "sdcs-test-" + Guid.NewGuid().ToString("N");
        using var server = new PreviewServer(name, folder); server.Start();
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(3000);
        using var writer = new StreamWriter(client, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
        using var reader = new StreamReader(client, Encoding.UTF8, false, 1024, true);
        await writer.WriteLineAsync(string.Join("\n", Enumerable.Range(0, 128)
            .Select(i => new JObject { ["Op"] = "Ping", ["Id"] = "pending-" + i }.ToString(Newtonsoft.Json.Formatting.None))));
        var accepted = new List<PreviewRequest>();
        for (int i = 0; i < 300 && accepted.Count < 128; i++)
        {
            while (server.TryDequeue(out var request)) accepted.Add(request!);
            if (accepted.Count < 128) await Task.Delay(10);
        }
        Assert.Equal(128, accepted.Count); // Dequeued work stays pending until its final reply.
        await writer.WriteLineAsync("{\"Op\":\"RenderFarmer\",\"Id\":\"rejected-129\"}");
        var reply = JObject.Parse((await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3)))!);
        Assert.False((bool)reply["Ok"]!);
        Assert.True((bool)reply["Final"]!);
        Assert.Equal("rejected-129", (string?)reply["Id"]);
        Assert.Equal("RenderFarmer", (string?)reply["Op"]);
        Assert.Contains("queue is full", (string?)reply["Error"]);
        Assert.False(server.TryDequeue(out _));
    }

    [Fact]
    public async Task ReliableBacklogOverflowExplicitlyClosesASlowConnection()
    {
        string name = "sdcs-test-" + Guid.NewGuid().ToString("N");
        using var server = new PreviewServer(name, folder); server.Start();
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(3000);
        var writer = new StreamWriter(client, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
        using var reader = new StreamReader(client, Encoding.UTF8, false, 1024, true);
        await writer.WriteLineAsync(string.Join("\n", Enumerable.Range(0, 128).Select(i => "{\"Op\":\"Ping\",\"Id\":\"" + i + "\"}")));
        var work = new List<PreviewRequest>();
        for (int i = 0; i < 300 && work.Count < 128; i++)
        {
            while (server.TryDequeue(out var request)) work.Add(request!);
            if (work.Count < 128) await Task.Delay(10);
        }
        Assert.Equal(128, work.Count);
        server.Publish(new JObject { ["Op"] = "Log", ["Text"] = new string('b', 2 * 1024 * 1024) });
        Assert.Equal(1, await reader.ReadAsync(new char[1], 0, 1).WaitAsync(TimeSpan.FromSeconds(3)));
        foreach (var request in work) request.Reply(new JObject { ["Ok"] = true });
        await writer.WriteLineAsync("{\"Op\":\"Ping\",\"Id\":\"overflow\"}");
        PreviewRequest? overflow = null;
        for (int i = 0; i < 300 && !server.TryDequeue(out overflow); i++) await Task.Delay(10);
        Assert.NotNull(overflow);
        overflow!.Reply(new JObject { ["Ok"] = true });
        try { await reader.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
        catch (IOException) { /* Broken pipe is an explicit failure rather than silent eviction. */ }
        try { writer.Dispose(); } catch (IOException) { }
        Assert.Null(Record.Exception(server.Dispose));
    }

    public void Dispose() { string root = Path.GetDirectoryName(folder)!; if (Directory.Exists(root)) Directory.Delete(root, true); }
}
