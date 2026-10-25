using System;
using System.Collections.Generic;
using System.Linq;

namespace SDCharacterStudio.Runtime;

public sealed class CutsceneRegistry
{
    private readonly Dictionary<string, RegisteredCutscene> entries = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyCollection<RegisteredCutscene> All => entries.Values.ToArray();
    public RegisteredCutscene? Find(string key) => entries.TryGetValue(key, out var entry) ? entry : null;
    public IReadOnlyList<RegisteredCutscene> ForPack(string packId) => entries.Values.Where(x => Equal(x.PackId, packId)).ToArray();
    public IReadOnlyList<RegisteredCutscene> ForLocation(string location) => entries.Values.Where(x => Equal(x.Compiled.Location, location)).ToArray();

    /// <summary>Replace atomically, returning the union of old and new event locations to invalidate.</summary>
    public IReadOnlyCollection<string> ReplacePack(string packId, IEnumerable<RegisteredCutscene> replacements)
    {
        var incoming = replacements.ToArray();
        if (incoming.Any(x => !Equal(x.PackId, packId)))
            throw new ArgumentException("Every replacement must belong to the requested pack.", nameof(replacements));
        if (incoming.Select(x => x.Compiled.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != incoming.Length)
            throw new ArgumentException("Replacement cutscene keys must be unique.", nameof(replacements));
        if (incoming.Any(x => entries.TryGetValue(x.Compiled.Key, out var old) && !Equal(old.PackId, packId)))
            throw new ArgumentException("A replacement cutscene key is already owned by another pack.", nameof(replacements));
        var scriptOwners = new Dictionary<string, RegisteredCutscene>(StringComparer.OrdinalIgnoreCase);
        foreach (var scene in entries.Values.Where(x => !Equal(x.PackId, packId)).Concat(incoming))
        {
            var conflict = TryReserveEventIds(scene, scriptOwners);
            if (conflict != null) throw new ArgumentException(conflict, nameof(replacements));
        }
        var affected = new HashSet<string>(ForPack(packId).Select(x => x.Compiled.Location), StringComparer.OrdinalIgnoreCase);
        affected.UnionWith(incoming.Select(x => x.Compiled.Location));
        foreach (var old in ForPack(packId)) entries.Remove(old.Compiled.Key);
        foreach (var entry in incoming) entries.Add(entry.Compiled.Key, entry);
        return affected.ToArray();
    }

    public IReadOnlyCollection<string> RemovePack(string packId) => ReplacePack(packId, Array.Empty<RegisteredCutscene>());

    /// <summary>Reserve every emitted event ID as one atomic unit, ignoring preconditions and case.</summary>
    internal static string? TryReserveEventIds(RegisteredCutscene scene, IDictionary<string, RegisteredCutscene> owners)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in scene.Compiled.SubScripts.Keys.Prepend(scene.Compiled.MainKey))
        {
            var id = key.Split('/', 2)[0];
            if (!ids.Add(id))
                return $"Event ID '{id}' is emitted more than once by cutscene '{scene.Compiled.Key}' in pack '{scene.PackId}'.";
            if (owners.TryGetValue(id, out var owner))
                return $"Event ID '{id}' from cutscene '{scene.Compiled.Key}' in pack '{scene.PackId}' conflicts with cutscene '{owner.Compiled.Key}' in pack '{owner.PackId}'.";
        }
        foreach (var id in ids) owners.Add(id, scene);
        return null;
    }

    private static bool Equal(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
