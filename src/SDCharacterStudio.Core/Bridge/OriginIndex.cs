using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;

namespace SDCharacterStudio.Core.Bridge;

public sealed record ModSource(string UniqueID, string Folder, string? ContentPackFor);

public sealed class OriginIndex
{
    private static readonly Regex Tokens = new(@"\{\{.*?\}\}", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private readonly HashSet<string> vanillaAssets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> vanillaKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> assets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> keys = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Patch> patches = new();

    public OriginIndex(JObject vanillaIndex, IEnumerable<ModSource> mods)
    {
        foreach (string asset in (vanillaIndex["Assets"] as JArray ?? new JArray()).Values<string>().OfType<string>())
        {
            vanillaAssets.Add(Normalize(asset));
            assets.Add(Normalize(asset));
        }
        foreach (var data in (vanillaIndex["DataKeys"] as JObject ?? new JObject()).Properties())
        {
            var values = new HashSet<string>((data.Value as JArray ?? new JArray()).Values<string>().OfType<string>(), StringComparer.Ordinal);
            vanillaKeys[Normalize(data.Name)] = values;
            keys[Normalize(data.Name)] = new HashSet<string>(values, StringComparer.Ordinal);
        }
        foreach (var mod in mods.Where(m => string.Equals(m.ContentPackFor, "Pathoschild.ContentPatcher", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                string folder = Path.GetFullPath(mod.Folder);
                int budget = 512;
                Scan(mod, folder, "content.json", true, new HashSet<string>(StringComparer.OrdinalIgnoreCase), ref budget);
            }
            catch (Exception ex) when (IsInvalidPack(ex)) { /* One bad pack must not hide the others. */ }
        }
    }

    public JObject Asset(string asset)
    {
        asset = Normalize(asset);
        assets.Add(asset);
        return Resolve(asset, null, vanillaAssets.Contains(asset));
    }

    public JObject DataKey(string dataAsset, string key)
    {
        dataAsset = Normalize(dataAsset);
        RememberKey(dataAsset, key);
        return Resolve(dataAsset, key, vanillaKeys.TryGetValue(dataAsset, out var values) && values.Contains(key));
    }

    public JObject ToJson()
    {
        var assetJson = new JObject();
        foreach (string asset in assets.OrderBy(a => a, StringComparer.Ordinal))
            assetJson[asset] = Resolve(asset, null, vanillaAssets.Contains(asset));
        var keyJson = new JObject();
        foreach (var data in keys.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var entries = new JObject();
            foreach (string key in data.Value.OrderBy(k => k, StringComparer.Ordinal))
                entries[key] = Resolve(data.Key, key, vanillaKeys.TryGetValue(data.Key, out var values) && values.Contains(key));
            keyJson[data.Key] = entries;
        }
        return new JObject { ["Assets"] = assetJson, ["DataKeys"] = keyJson };
    }

    private JObject Resolve(string asset, string? key, bool vanilla)
    {
        var providers = new SortedSet<string>(StringComparer.Ordinal);
        var editors = new SortedSet<string>(StringComparer.Ordinal);
        bool certain = true;
        foreach (var patch in patches)
        {
            if ((key is null) != (patch.Key is null) || !Matches(patch.Target, asset, true)
                || (key is not null && !Matches(patch.Key!, key, false)))
                continue;
            if (patch.Provides && !vanilla) providers.Add(patch.Mod);
            else editors.Add(patch.Mod);
            certain &= patch.Certain;
        }
        bool unknown = !vanilla && providers.Count == 0;
        var result = new JObject
        {
            ["Vanilla"] = vanilla, ["ProvidedBy"] = new JArray(providers),
            ["EditedBy"] = new JArray(editors), ["Certain"] = certain && !unknown
        };
        if (unknown) result["Unknown"] = true;
        return result;
    }

    private void Scan(ModSource mod, string folder, string relative, bool certain, HashSet<string> active, ref int budget)
    {
        if (budget-- <= 0 || active.Count >= 64 || !SafeInclude(folder, relative, out string file) || !active.Add(file)) return;
        try
        {
            if (!File.Exists(file) || new FileInfo(file).Length > 8 * 1024 * 1024) return;
            JToken document = JToken.Parse(File.ReadAllText(file));
            var changes = document as JArray ?? (document as JObject)?["Changes"] as JArray;
            if (changes is null) return;
            foreach (var change in changes.OfType<JObject>())
            {
                string? action = Text(change["Action"]);
                bool unconditional = certain && (change["When"] is null || change["When"] is JObject { Count: 0 });
                if (string.Equals(action, "Include", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (string from in Split(Text(change["FromFile"])))
                    foreach (string expanded in Expand(from, mod.UniqueID))
                        if (!Tokens.IsMatch(expanded)) Scan(mod, folder, expanded, unconditional, active, ref budget);
                    continue;
                }
                if (action is not ("Load" or "EditImage" or "EditData")) continue;
                foreach (string target in Split(Text(change["Target"])))
                foreach (string expanded in Expand(Normalize(target), mod.UniqueID))
                {
                    bool targetCertain = unconditional && !Tokens.IsMatch(expanded);
                    AddPatch(expanded, null, mod.UniqueID, action == "Load", targetCertain);
                    if (action != "EditData" || change["Entries"] is not JObject entries) continue;
                    foreach (var entry in entries.Properties().Where(e => e.Value.Type != JTokenType.Null))
                    foreach (string key in Expand(entry.Name, mod.UniqueID))
                        AddPatch(expanded, key, mod.UniqueID, true, targetCertain && !Tokens.IsMatch(key));
                }
            }
        }
        catch (Exception ex) when (IsInvalidPack(ex)) { }
        finally { active.Remove(file); }
    }

    private void AddPatch(string target, string? key, string mod, bool provides, bool certain)
    {
        patches.Add(new Patch(target, key, mod, provides, certain));
        if (Tokens.IsMatch(target)) return;
        if (key is null) assets.Add(target);
        else if (!Tokens.IsMatch(key)) RememberKey(target, key);
    }

    private void RememberKey(string asset, string key)
    {
        if (!keys.TryGetValue(asset, out var values)) keys[asset] = values = new HashSet<string>(StringComparer.Ordinal);
        values.Add(key);
    }

    private static bool SafeInclude(string folder, string relative, out string path)
    {
        path = "";
        relative = relative.Replace('\\', '/');
        if (Path.IsPathRooted(relative) || relative.Contains(':')) return false;
        path = Path.GetFullPath(Path.Combine(folder, relative));
        if (!path.StartsWith(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return false;
        // Reject reparse points on every component, so a symlink cannot escape the lexical boundary.
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
            if (string.Equals(current, folder, StringComparison.OrdinalIgnoreCase)) break;
        }
        return true;
    }

    private static IEnumerable<string> Expand(string value, string mod)
    {
        value = Regex.Replace(value, @"\{\{\s*ModId\s*\}\}", _ => mod, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!Regex.IsMatch(value, @"\{\{\s*Season\s*\}\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            return new[] { value };
        return new[] { "spring", "summer", "fall", "winter" }.Select(season => Regex.Replace(value, @"\{\{\s*Season\s*\}\}", season, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)));
    }

    private static bool Matches(string pattern, string value, bool ignoreCase)
    {
        if (!Tokens.IsMatch(pattern)) return string.Equals(pattern, value, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        string regex = "\\A" + string.Concat(Tokens.Split(pattern).Select((text, i) => (i == 0 ? "" : ".*") + Regex.Escape(text))) + "\\z";
        try { return Regex.IsMatch(value, regex, RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None), TimeSpan.FromMilliseconds(100)); }
        catch (RegexMatchTimeoutException) { return false; }
    }

    private static string Normalize(string value) => value.Replace('\\', '/').Trim().Trim('/');
    private static string? Text(JToken? token) => token?.Type == JTokenType.String ? (string?)token : null;
    private static IEnumerable<string> Split(string? value) => (value ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    private static bool IsInvalidPack(Exception ex) => ex is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException or ArgumentException or NotSupportedException or RegexMatchTimeoutException;
    private sealed record Patch(string Target, string? Key, string Mod, bool Provides, bool Certain);
}
