using SDCharacterStudio.Core.Bridge;
using Xunit;

namespace SDCharacterStudio.Core.Tests.Bridge;

public sealed class ExportTransactionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "sdcs-txn-" + Guid.NewGuid().ToString("N"));
    [Fact]
    public void DisposeBeforeCommitPreservesPublishedFiles()
    {
        Directory.CreateDirectory(root); File.WriteAllText(Path.Combine(root, "a.txt"), "old");
        using (var transaction = new ExportTransaction(new ExportStore(root))) transaction.Write("a.txt", new byte[] { 1 });
        Assert.Equal("old", File.ReadAllText(Path.Combine(root, "a.txt")));
    }
    [Fact]
    public void CommitPublishesAllFilesAndPreservesUnchangedTimestamp()
    {
        Directory.CreateDirectory(root); File.WriteAllBytes(Path.Combine(root, "a.txt"), new byte[] { 1 });
        File.SetLastWriteTimeUtc(Path.Combine(root, "a.txt"), new DateTime(2020, 1, 1));
        using var transaction = new ExportTransaction(new ExportStore(root));
        transaction.Write("a.txt", new byte[] { 1 }); transaction.Write("b.txt", new byte[] { 2 });
        Assert.False(File.Exists(Path.Combine(root, "b.txt")));
        transaction.Commit();
        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(Path.Combine(root, "b.txt")));
        Assert.Equal(2020, File.GetLastWriteTimeUtc(Path.Combine(root, "a.txt")).Year);
    }
    [Fact]
    public void LaterCommitFailureRollsBackEarlierReplacements()
    {
        Directory.CreateDirectory(root); File.WriteAllText(Path.Combine(root, "a.txt"), "old");
        Directory.CreateDirectory(Path.Combine(root, "z.txt"));
        using var transaction = new ExportTransaction(new ExportStore(root));
        transaction.Write("a.txt", new byte[] { 1 }); transaction.Write("z.txt", new byte[] { 2 });
        Assert.ThrowsAny<IOException>(transaction.Commit);
        Assert.Equal("old", File.ReadAllText(Path.Combine(root, "a.txt")));
    }
    [Fact]
    public void IncompleteRollbackRestoresOtherFilesAndPreservesRecoveryBackupsAfterDispose()
    {
        if (!OperatingSystem.IsWindows()) return; // Windows rejects deletion of a read-only published file.
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "a.txt"), "old-a");
        File.WriteAllText(Path.Combine(root, "b.txt"), "old-b");
        Directory.CreateDirectory(Path.Combine(root, "z.txt"));
        var transaction = new ExportTransaction(new ExportStore(root));
        transaction.Write("a.txt", new byte[] { 1 }); transaction.Write("b.txt", new byte[] { 2 }); transaction.Write("z.txt", new byte[] { 3 });
        string recovery = Directory.GetDirectories(root, ".sdcs-txn-*").Single();
        // Move publishes this attribute too. A later directory conflict triggers rollback,
        // whose delete of b fails deterministically while a remains independently restorable.
        File.SetAttributes(Path.Combine(recovery, "b.txt"), FileAttributes.ReadOnly);
        var error = Record.Exception(transaction.Commit);
        transaction.Dispose();
        Assert.NotNull(error);
        Assert.Equal("old-a", File.ReadAllText(Path.Combine(root, "a.txt")));
        Assert.True(Directory.Exists(recovery), "Incomplete rollback must retain its recovery directory after Dispose.");
        Assert.Equal("old-b", File.ReadAllText(Path.Combine(recovery, "backup", "b.txt")));
        Assert.Contains(recovery, error!.Message);
    }
    public void Dispose()
    {
        if (!Directory.Exists(root)) return;
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(root, true);
    }
}
