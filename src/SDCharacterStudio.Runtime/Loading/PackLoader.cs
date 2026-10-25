using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SDCharacterStudio.Core.Validation;
using StardewModdingAPI;
using StardewValley;
using SDCharacterStudio.Runtime.Media;

namespace SDCharacterStudio.Runtime.Loading;

public sealed record PackStatus(string PackId, string PackName, int LoadedCount,
    IReadOnlyList<SkippedCutscene> Skipped, IReadOnlyList<ValidationIssue> Issues);

public sealed class PackLoader
{
    private readonly IModHelper helper;
    private readonly IMonitor monitor;
    private readonly CutsceneRegistry registry;
    private readonly Func<bool> logCompiledScripts;
    private readonly Dictionary<string, IContentPack> packs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IContentPack> registered = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PackStatus> statuses = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyCollection<PackStatus> Statuses => statuses.Values.ToArray();

    public PackLoader(IModHelper helper, IMonitor monitor, CutsceneRegistry registry, Func<bool> logCompiledScripts)
    {
        this.helper = helper; this.monitor = monitor; this.registry = registry; this.logCompiledScripts = logCompiledScripts;
    }

    public IContentPack? FindPack(string packId) => packs.TryGetValue(packId, out var pack) ? pack : null;
    public void LoadAll() => Reload();

    /// <summary>Reload from disk; removing a scene invalidates its former location too.</summary>
    public bool Reload(string? packId = null)
    {
        try
        {
            if (Game1.eventUp)
            {
                monitor.Log("Cannot reload cutscenes while an event is playing.", LogLevel.Warn);
                return false;
            }
            VideoMenu.ClearPackCache(packId);
            var available = helper.ContentPacks.GetOwned().ToDictionary(x => x.Manifest.UniqueID, StringComparer.OrdinalIgnoreCase);
            foreach (var entry in registered) available[entry.Key] = entry.Value;
            if (packId != null)
            {
                if (!available.TryGetValue(packId, out var pack))
                {
                    Remove(packId);
                    monitor.Log($"No SDCS pack named '{packId}' is available.", LogLevel.Warn);
                    return false;
                }
                return Load(pack, registered.ContainsKey(packId));
            }
            foreach (var removed in packs.Keys.Where(x => !available.ContainsKey(x)).ToArray()) Remove(removed);
            // Judge a full reload against the new batch, not registrations that a
            // later pack in the batch is about to relinquish.
            foreach (var previous in registry.All.Select(x => x.PackId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
                Invalidate(registry.RemovePack(previous));
            var success = true;
            foreach (var pack in available.Values.OrderBy(x => x.Manifest.UniqueID, StringComparer.OrdinalIgnoreCase))
                success = Load(pack, registered.ContainsKey(pack.Manifest.UniqueID)) && success;
            return success;
        }
        catch (Exception ex)
        {
            monitor.Log($"Could not reload SDCS packs: {ex}", LogLevel.Error);
            return false;
        }
    }

    public bool RegisterPackFolder(IManifest owner, string absoluteFolder)
    {
        try
        {
            if (Game1.eventUp) { monitor.Log("Cannot register a pack while an event is playing.", LogLevel.Warn); return false; }
            if (!Path.IsPathFullyQualified(absoluteFolder) || !Directory.Exists(absoluteFolder))
                throw new ArgumentException("Pack folder must be an existing absolute directory.", nameof(absoluteFolder));
            if (helper.ContentPacks.GetOwned().Any(x => string.Equals(x.Manifest.UniqueID, owner.UniqueID, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"An owned content pack already uses ID '{owner.UniqueID}'.");
            var pack = helper.ContentPacks.CreateTemporary(absoluteFolder, owner.UniqueID, owner.Name, owner.Description, owner.Author, owner.Version);
            VideoMenu.ClearPackCache(owner.UniqueID);
            registered[owner.UniqueID] = pack;
            return Load(pack, true);
        }
        catch (Exception ex)
        {
            monitor.Log($"Could not register SDCS pack folder '{absoluteFolder}': {ex}", LogLevel.Error);
            return false;
        }
    }

    public void GrantDayAfterFlags()
    {
        try
        {
            if (!Context.IsWorldReady) return;
            foreach (var flag in registry.All.Select(x => x.Source.Trigger?.OnDayAfterFlag).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal))
                if (Game1.player.mailReceived.Contains(flag!) && !Game1.player.mailReceived.Contains(flag + "_Ready"))
                    Game1.player.mailReceived.Add(flag + "_Ready");
        }
        catch (Exception ex) { monitor.Log($"Could not grant SDCS day-after flags: {ex}", LogLevel.Error); }
    }

    /// <summary>NPCs and locations become available after launch, when a world has loaded.</summary>
    public void RevalidateReferences(string? packId = null)
    {
        if (!Context.IsWorldReady) return;
        var catalog = new LiveCatalog(helper);
        foreach (var id in registry.All.Where(x => packId == null || string.Equals(x.PackId, packId, StringComparison.OrdinalIgnoreCase)).Select(x => x.PackId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
        {
            var result = PackReferenceValidation.Validate(registry.ForPack(id), catalog);
            if (result.Skipped.Count == 0) continue;
            Invalidate(registry.ReplacePack(id, result.Valid));
            // Usually a mod this save doesn't have (e.g. a scene for SVE, Ridgeside Village or East Scarp): skipped content, so Warn (doc 04 §9).
            string name = statuses.TryGetValue(id, out var known) ? known.PackName : id;
            foreach (var skipped in result.Skipped) monitor.Log($"Skipped cutscene '{skipped.CutsceneId}' in '{name}' because it uses something this game doesn't have.", LogLevel.Warn);
            foreach (var issue in result.Issues) monitor.Log($"[{id}] {issue.File}:{issue.JsonPath}: {issue.Message}", LogLevel.Warn);
            if (statuses.TryGetValue(id, out var status))
                statuses[id] = status with { LoadedCount = result.Valid.Count, Skipped = status.Skipped.Concat(result.Skipped).ToArray(), Issues = status.Issues.Concat(result.Issues).ToArray() };
        }
    }

    /// <summary>The loaded world, including every installed content mod's NPCs, locations, assets and items.</summary>
    private sealed class LiveCatalog : ICatalog
    {
        private readonly IModHelper helper;
        public LiveCatalog(IModHelper helper) => this.helper = helper;
        public bool NpcExists(string name) => FindNpc(name) != null;
        public bool LocationExists(string name) => Game1.locations.Any(x => string.Equals(x.NameOrUniqueName, name, StringComparison.OrdinalIgnoreCase)) || Game1.getLocationFromName(name) != null;

        // Event actors and getCharacterFromName match exact names; return the real casing so a mismatch is reported.
        public string? FindNpc(string name)
        {
            if (Game1.characterData.ContainsKey(name) || Game1.getCharacterFromName(name, mustBeVillager: false) != null) return name;
            return Game1.characterData.Keys.FirstOrDefault(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
        }

        // Mod NPCs often have internal names unlike their display names (East Scarp's JadeMalic is "Jade", SVE_Henchman is "Henchman").
        public string? SuggestNpc(string name)
        {
            foreach (var (key, data) in Game1.characterData)
            {
                string? display = null;
                try { display = data?.DisplayName is { } raw ? StardewValley.TokenizableStrings.TokenParser.ParseText(raw) : null; } catch { /* a broken display name shouldn't stop validation */ }
                if (string.Equals(display, name, StringComparison.OrdinalIgnoreCase)) return key;
            }
            // "Kataryna" → "KatarynaLK"; too short a name would match almost anything.
            return name.Length < 4 ? null : Game1.characterData.Keys.FirstOrDefault(x => x.Contains(name, StringComparison.OrdinalIgnoreCase));
        }

        public bool AssetExists(string asset)
        {
            try { return helper.GameContent.DoesAssetExist<Microsoft.Xna.Framework.Graphics.Texture2D>(helper.GameContent.ParseAssetName(asset)); }
            catch (Exception) { return false; }
        }
        public bool ItemExists(string qualifiedItemId) => ItemRegistry.Exists(qualifiedItemId);
        public bool HairstyleExists(int index) => Farmer.GetAllHairstyleIndices().Contains(index);
    }

    private bool Load(IContentPack pack, bool ownerFolder)
    {
        var id = pack.Manifest.UniqueID;
        packs[id] = pack;
        try
        {
            string? Translate(string key) { var text = pack.Translation.Get(key); return text.HasValue() ? text.ToString() : null; }
            var result = PackCompilation.Compile(new ContentPackFileSystem(pack, ownerFolder), id, pack.Manifest.Name, Translate, helper.ModRegistry.IsLoaded, registry.All);
            Invalidate(registry.ReplacePack(id, result.Cutscenes));
            statuses[id] = new(id, pack.Manifest.Name, result.Cutscenes.Count, result.Skipped, result.Issues);
            RevalidateReferences(id);
            foreach (var issue in result.Issues.GroupBy(x => (x.File, x.JsonPath, x.Message)).Select(x => x.First()))
                monitor.Log($"[{pack.Manifest.Name}] {issue.File}:{issue.JsonPath}: {issue.Message}", issue.Severity == ValidationSeverity.Error ? LogLevel.Error : LogLevel.Warn);
            foreach (var skipped in result.Skipped.Where(x => x.Reason.StartsWith("Needs installed mod(s):", StringComparison.Ordinal)))
                monitor.Log($"Skipped cutscene '{skipped.CutsceneId}' in '{pack.Manifest.Name}'. {skipped.Reason}.", LogLevel.Warn);
            var status = statuses[id];
            var suffix = status.Skipped.Count == 0 ? "" : $" ({status.Skipped.Count} skipped: {string.Join("; ", status.Skipped.Select(x => x.CutsceneId + ": " + x.Reason))})";
            monitor.Log($"Loaded {pack.Manifest.Name}: {status.LoadedCount} cutscenes{suffix}.", LogLevel.Info);
            if (logCompiledScripts())
                foreach (var scene in registry.ForPack(id)) monitor.Log(scene.Compiled.ToGoldenText(), LogLevel.Debug);
            return status.LoadedCount > 0 || !status.Issues.Any(x => x.Severity == ValidationSeverity.Error) && status.Skipped.Count == 0;
        }
        catch (Exception ex)
        {
            Invalidate(registry.RemovePack(id));
            var issue = new ValidationIssue(ValidationSeverity.Error, "studio.json", "", ex.Message, "Runtime");
            statuses[id] = new(id, pack.Manifest.Name, 0, Array.Empty<SkippedCutscene>(), new[] { issue });
            monitor.Log($"Could not load SDCS pack '{pack.Manifest.Name}' ({id}): {ex}", LogLevel.Error);
            return false;
        }
    }

    private void Remove(string packId)
    {
        Invalidate(registry.RemovePack(packId));
        packs.Remove(packId); statuses.Remove(packId);
    }

    private void Invalidate(IEnumerable<string> locations)
    {
        foreach (var location in locations)
            helper.GameContent.InvalidateCache(info => info.NameWithoutLocale.IsEquivalentTo("Data/Events/" + location));
    }
}
