using Microsoft.Xna.Framework;
using Newtonsoft.Json.Linq;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Objects;
using SDCharacterStudio.Core;

namespace SDCharacterStudio.Runtime.Costumes;

/// <summary>Owns actor-specific costume backups until explicit restoration, including across normal scene ends.</summary>
public sealed class CostumeManager
{
    /// <summary>The save-data key holding the player's look during a scene; per engine copy (<see cref="EngineNames"/>).</summary>
    public static string BackupKey => EngineNames.DataKey("CostumeBackup");
    private readonly IMonitor monitor;
    private readonly Dictionary<Farmer, Session> sessions = new(ReferenceEqualityComparer.Instance);

    public CostumeManager(IMonitor monitor) => this.monitor = monitor;
    public bool IsActive(Farmer farmer) => sessions.ContainsKey(farmer);

    /// <summary>Validate and resolve every requested value without mutating the actor.</summary>
    public void Validate(Dictionary<string, JToken> values) => Prepare(values, new CostumeSnapshot(), new CostumeSnapshot());

    public void Apply(Farmer farmer, Dictionary<string, JToken> values)
    {
        if (values.Count == 0) return;
        sessions.TryGetValue(farmer, out var session);
        if (session?.Suspended != null) throw new InvalidOperationException("Cannot apply a costume while saving.");
        // An unprocessed crash backup must never be overwritten with the damaged current look.
        if (session == null && farmer.modData.ContainsKey(BackupKey))
            throw new InvalidOperationException("Repair the existing costume backup before applying another costume.");
        var before = CostumeSnapshot.Capture(farmer);
        var (target, keys) = Prepare(values, before, session?.Original ?? before);
        bool created = session == null;
        if (created)
        {
            string json = CostumeBackup.Serialize(before); // Fail before changing fields or modData.
            session = new Session(before, json);
            farmer.modData[BackupKey] = json;
            sessions.Add(farmer, session);
        }
        try { target.Write(farmer, keys); }
        catch
        {
            // Keep the recovery record if rollback itself fails.
            before.Write(farmer, keys);
            if (created) { sessions.Remove(farmer); farmer.modData.Remove(BackupKey); }
            throw;
        }
    }

    public void Restore(Farmer farmer)
    {
        if (!sessions.TryGetValue(farmer, out var session))
        {
            if (farmer.modData.ContainsKey(BackupKey)) RepairAfterCrash(farmer);
            return;
        }
        session.Original.Write(farmer);
        sessions.Remove(farmer);
        farmer.modData.Remove(BackupKey);
    }

    public void RestoreAll()
    {
        foreach (var farmer in sessions.Keys.ToArray())
            TryLifecycle(() => Restore(farmer), "restore costume");
    }

    public void RepairAfterCrash(Farmer farmer)
    {
        if (sessions.ContainsKey(farmer) || !farmer.modData.TryGetValue(BackupKey, out string json)) return;
        TryLifecycle(() =>
        {
            var original = CostumeBackup.Deserialize(json);
            original.Write(farmer);
            farmer.modData.Remove(BackupKey);
            monitor.Log("Restored the player's original appearance from an interrupted SDCharacterStudio costume.", LogLevel.Info);
        }, "repair costume backup");
    }

    /// <summary>Save real equipment/appearance while retaining the original recovery record in the save.</summary>
    public void SuspendForSave()
    {
        foreach (var (farmer, session) in sessions.ToArray())
        {
            if (session.Suspended != null) continue;
            TryLifecycle(() =>
            {
                var current = CostumeSnapshot.Capture(farmer);
                session.Original.Write(farmer);
                session.Suspended = current;
                farmer.modData[BackupKey] = session.Json;
            }, "suspend costume for saving");
        }
    }

    public void ResumeAfterSave()
    {
        foreach (var (farmer, session) in sessions.ToArray())
        {
            if (session.Suspended == null) continue;
            TryLifecycle(() =>
            {
                session.Suspended.Write(farmer);
                session.Suspended = null;
                farmer.modData[BackupKey] = session.Json;
            }, "resume costume after saving");
        }
    }

    public CostumeScope BeginScope(Farmer farmer) => new(this, farmer, CostumeSnapshot.Capture(farmer));

    private static (CostumeSnapshot Target, string[] Keys) Prepare(Dictionary<string, JToken> values, CostumeSnapshot current, CostumeSnapshot original)
    {
        var parsed = CostumeSet.Parse(values);
        var target = current.Clone();
        foreach (var (key, value) in parsed)
        {
            if (value.Kind == CostumeValueKind.Original) { target.CopyPart(original, key); continue; }
            if (value.Kind == CostumeValueKind.None)
            {
                if (key == "Hat") target.Hat = null;
                else if (key == "Accessory") target.Accessory = -1;
                else { target.Boots = null; target.Shoes = "12"; }
                continue;
            }
            if (value.Kind == CostumeValueKind.Item)
            {
                var item = ItemRegistry.Create(value.ItemId!, allowNull: true)
                    ?? throw new ArgumentException($"Costume item '{value.ItemId}' doesn't exist.");
                switch (key)
                {
                    case "Hat" when item is Hat hat: target.Hat = hat; break;
                    case "Shoes" when item is Boots boots: target.Boots = boots; target.Shoes = boots.GetBootsColorString(); break;
                    case "Shirt" when item is Clothing && item.TypeDefinitionId == "(S)": target.Shirt = item.ItemId; break;
                    case "Pants" when item is Clothing && item.TypeDefinitionId == "(P)": target.Pants = item.ItemId; break;
                    default: throw new ArgumentException($"Costume item '{value.ItemId}' has the wrong type for {key}.");
                }
                continue;
            }
            switch (key)
            {
                case "Hair":
                    if (!Farmer.GetAllHairstyleIndices().Contains(value.Index)) throw new ArgumentException($"Hairstyle {value.Index} doesn't exist.");
                    target.Hair = value.Index; break;
                case "Accessory": target.Accessory = value.Index; break;
                case "Skin": target.Skin = value.Index; break;
                case "HairColor": target.HairColor = ColorOf(value.Rgba); break;
                case "EyeColor": target.EyeColor = ColorOf(value.Rgba); break;
            }
        }
        return (target, parsed.Keys.ToArray());
    }

    private static Color ColorOf(uint rgba) => new((byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8), (byte)rgba);
    private void TryLifecycle(Action action, string operation)
    {
        try { action(); }
        catch (Exception ex) { monitor.Log($"Couldn't {operation}; the recovery backup was retained. {ex}", LogLevel.Error); }
    }

    private sealed class Session(CostumeSnapshot original, string json)
    {
        public CostumeSnapshot Original { get; } = original;
        public string Json { get; } = json;
        public CostumeSnapshot? Suspended { get; set; }
    }

    /// <summary>A clip's Set scope. Persist updates only the affected part's post-clip baseline.</summary>
    public sealed class CostumeScope : IDisposable
    {
        private readonly CostumeManager owner;
        private readonly Farmer farmer;
        private readonly CostumeSnapshot baseline;
        private readonly HashSet<string> touched = new(StringComparer.OrdinalIgnoreCase);
        private Session? session;
        private bool disposed;

        internal CostumeScope(CostumeManager owner, Farmer farmer, CostumeSnapshot baseline)
        { this.owner = owner; this.farmer = farmer; this.baseline = baseline; }

        public void Apply(Dictionary<string, JToken> values, bool persist = false)
        {
            if (disposed) throw new ObjectDisposedException(nameof(CostumeScope));
            if (session != null && (!owner.sessions.TryGetValue(farmer, out var active) || !ReferenceEquals(active, session)))
                throw new InvalidOperationException("This clip's costume session has ended.");
            owner.Apply(farmer, values);
            if (!owner.sessions.TryGetValue(farmer, out var applied)) return;
            session = applied;
            var current = CostumeSnapshot.Capture(farmer);
            foreach (string key in CostumeSet.Parse(values).Keys)
            {
                touched.Add(key);
                if (persist) baseline.CopyPart(current, key);
            }
        }

        /// <summary>Retain the final clip appearance when Hold is requested, without changing earlier loop semantics.</summary>
        public void KeepCurrent()
        {
            if (disposed) throw new ObjectDisposedException(nameof(CostumeScope));
            if (session == null || !owner.sessions.TryGetValue(farmer, out var active) || !ReferenceEquals(active, session)) return;
            var current = session.Suspended ?? CostumeSnapshot.Capture(farmer);
            foreach (string key in touched) baseline.CopyPart(current, key);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (session == null || !owner.sessions.TryGetValue(farmer, out var active) || !ReferenceEquals(active, session)) return;
            if (session.Suspended != null)
            {
                foreach (string key in touched) session.Suspended.CopyPart(baseline, key);
                return;
            }
            baseline.Write(farmer, touched);
        }
    }
}
