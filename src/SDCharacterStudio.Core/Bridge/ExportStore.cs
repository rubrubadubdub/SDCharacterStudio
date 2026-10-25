using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace SDCharacterStudio.Core.Bridge;

public sealed class ExportStore
{
    public string Root { get; }
    private readonly Dictionary<string, string> hashes = new(StringComparer.OrdinalIgnoreCase);

    public ExportStore(string root)
    {
        Root = Path.GetFullPath(root);
        Directory.CreateDirectory(Root);
        try
        {
            string path = PathFor("export-state.json");
            if (File.Exists(path))
                foreach (var entry in JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path)) ?? new())
                    hashes[entry.Key] = entry.Value;
        }
        catch (JsonException) { /* Existing complete outputs can rebuild a corrupt state file. */ }
    }

    public string PathFor(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':'))
            throw new ArgumentException("Export paths must be relative to the selected profile.", nameof(relative));
        var segments = relative.Replace('\\', '/').Split('/');
        if (segments.Any(x => x is ".." or "." or "" || x.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new ArgumentException("Export paths contain an invalid segment.", nameof(relative));
        string path = Path.GetFullPath(Path.Combine(Root, Path.Combine(segments)));
        if (!path.StartsWith(Root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Export path escapes the selected profile.", nameof(relative));
        return path;
    }

    public bool Write(string relative, byte[] bytes)
    {
        string path = PathFor(relative);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        hashes[relative.Replace('\\', '/')] = hash;
        if (File.Exists(path) && Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) == hash) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return true;
    }

    public void Json(string relative, object value) => Write(relative, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value, Formatting.Indented) + "\n"));
    public void SaveState() => Json("export-state.json", hashes.Where(x => x.Key != "export-state.json").OrderBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Value));
}
