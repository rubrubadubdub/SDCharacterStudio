using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace SDCharacterStudio.Core.Bridge;

public sealed class ExportTransaction : IDisposable
{
    private readonly ExportStore store;
    private readonly ExportStore staged;
    private readonly HashSet<string> files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> hashes;
    private bool committed;
    private bool recoveryRequired;
    public ExportTransaction(ExportStore store)
    {
        this.store = store;
        staged = new ExportStore(store.PathFor(".sdcs-txn-" + Guid.NewGuid().ToString("N")));
        try { hashes = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(store.PathFor("export-state.json"))) ?? new(); }
        catch (Exception ex) when (ex is IOException or JsonException) { hashes = new(); }
        hashes.Remove("export-state.json");
    }
    public void Write(string path, byte[] bytes)
    {
        if (committed) throw new InvalidOperationException("Export is already committed.");
        if (recoveryRequired) throw new InvalidOperationException("Export requires manual recovery from " + staged.Root);
        string destination = store.PathFor(path);
        hashes[path] = Convert.ToHexString(SHA256.HashData(bytes));
        if (File.Exists(destination) && File.ReadAllBytes(destination).AsSpan().SequenceEqual(bytes))
        { files.Remove(path); if (File.Exists(staged.PathFor(path))) File.Delete(staged.PathFor(path)); return; }
        staged.Write(path, bytes); files.Add(path);
    }
    public void Json(string path, object value) => Write(path, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value, Formatting.Indented) + "\n"));
    public void Commit()
    {
        Json("export-state.json", hashes.OrderBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Value));
        var changed = new List<(string Target, string Backup, bool HadOriginal)>();
        try
        {
            // The catalog is the publication marker. All its dependencies precede it.
            foreach (string path in files.OrderBy(x => x.Equals("catalog.json", StringComparison.OrdinalIgnoreCase) ? 1 : 0).ThenBy(x => x, StringComparer.Ordinal))
            {
                string target = store.PathFor(path), backup = staged.PathFor("backup/" + path);
                if (Directory.Exists(target)) throw new IOException("A directory blocks export file " + path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                bool exists = File.Exists(target);
                if (exists) { Directory.CreateDirectory(Path.GetDirectoryName(backup)!); File.Move(target, backup); }
                changed.Add((target, backup, exists));
                File.Move(staged.PathFor(path), target);
            }
            committed = true;
        }
        catch (Exception failure)
        {
            var rollbackErrors = new List<Exception>();
            foreach (var file in changed.AsEnumerable().Reverse())
            {
                try
                {
                    if (File.Exists(file.Target)) File.Delete(file.Target);
                    if (file.HadOriginal) File.Move(file.Backup, file.Target);
                }
                catch (Exception restoreFailure)
                {
                    rollbackErrors.Add(new IOException("Could not restore export file " + file.Target +
                        (file.HadOriginal ? " from backup " + file.Backup : " by removing its new file"), restoreFailure));
                }
            }
            if (rollbackErrors.Count != 0)
            {
                recoveryRequired = true;
                throw new IOException($"Export failed and rollback is incomplete ({rollbackErrors.Count} restore error(s)). " +
                    $"Recovery files were retained at '{staged.Root}'. " + string.Join("; ", rollbackErrors.Select(error => error.Message)),
                    new AggregateException(new[] { failure }.Concat(rollbackErrors)));
            }
            throw;
        }
    }
    public void Dispose() { if (!recoveryRequired && Directory.Exists(staged.Root)) Directory.Delete(staged.Root, true); }
}
