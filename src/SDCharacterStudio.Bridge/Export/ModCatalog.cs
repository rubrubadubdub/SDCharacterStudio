using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core.Bridge;
using StardewModdingAPI;

namespace SDCharacterStudio.Bridge.Export;

public sealed class ModCatalog
{
    public JArray Mods { get; } = new();
    public IReadOnlyList<ModSource> Sources { get; }
    public ModCatalog(IModHelper helper, IMonitor monitor)
    {
        var installed = helper.ModRegistry.GetAll().ToDictionary(x => x.Manifest.UniqueID, StringComparer.OrdinalIgnoreCase);
        var folders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string modsPath = Path.Combine(Constants.GamePath, "Mods");
        var arguments = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < arguments.Length; i++)
            if (arguments[i].Equals("--mods-path", StringComparison.OrdinalIgnoreCase)) modsPath = Path.GetFullPath(arguments[i + 1]);
        if (Directory.Exists(modsPath))
            foreach (var path in Directory.EnumerateFiles(modsPath, "manifest.json", SearchOption.AllDirectories))
                try { if ((string?)JObject.Parse(File.ReadAllText(path))["UniqueID"] is { } id && installed.ContainsKey(id)) folders.TryAdd(id, Path.GetDirectoryName(path)!); }
                catch (Exception ex) when (ex is IOException or Newtonsoft.Json.JsonException) { monitor.Log($"Could not inspect mod manifest {path}: {ex.Message}", LogLevel.Debug); }
        var sources = new List<ModSource>();
        foreach (var mod in installed.Values.OrderBy(x => x.Manifest.UniqueID, StringComparer.Ordinal))
        {
            string id = mod.Manifest.UniqueID;
            string? owner = mod.Manifest.ContentPackFor?.UniqueID;
            string? folder = folders.GetValueOrDefault(id);
            Mods.Add(new JObject { ["UniqueID"] = id, ["Name"] = mod.Manifest.Name, ["Version"] = mod.Manifest.Version.ToString(), ["IsContentPack"] = mod.IsContentPack, ["ContentPackFor"] = owner, ["Folder"] = folder });
            if (folder != null) sources.Add(new(id, folder, owner));
        }
        Sources = sources;
    }
}
