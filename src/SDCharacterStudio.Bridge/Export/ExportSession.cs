using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core.Bridge;
using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Runtime.Api;
using StardewModdingAPI;

namespace SDCharacterStudio.Bridge.Export;

/// <summary>A single game-thread export with incremental writes and a completion boundary.</summary>
public sealed class ExportSession : IDisposable
{
    public IModHelper Helper { get; }
    public IMonitor Monitor { get; }
    public ISdcsApi Runtime { get; }
    public JObject Catalog { get; }
    public string Root => store.Root;
    public List<string> Errors { get; } = new();
    public int Completed { get; private set; }
    public int Scheduled { get; private set; }
    public bool Done => jobs.Count == 0;
    private readonly ExportStore store;
    private readonly ExportTransaction transaction;
    private readonly OriginIndex origins;
    private readonly Queue<(string Label, Action Work)> jobs = new();
    private readonly HashSet<string> textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Action> cleanup = new();
    public ExportSession(IModHelper helper, IMonitor monitor, ISdcsApi runtime, ExportStore store, OriginIndex origins, JObject catalog)
    { Helper = helper; Monitor = monitor; Runtime = runtime; this.store = store; transaction = new(store); this.origins = origins; Catalog = catalog; Catalog["Textures"] ??= new JArray(); }
    public void Queue(string label, Action work) { jobs.Enqueue((label, work)); Scheduled++; }
    public void RegisterCleanup(Action action) => cleanup.Add(action);
    public void Dispose()
    {
        jobs.Clear();
        foreach (var action in cleanup.AsEnumerable().Reverse())
            try { action(); } catch (Exception ex) { Monitor.Log($"Export cleanup failed: {ex.Message}", LogLevel.Warn); }
        cleanup.Clear();
        try { transaction.Dispose(); } catch (Exception ex) { Monitor.Log($"Could not remove export staging files: {ex.Message}", LogLevel.Warn); }
    }
    public void Tick(double budgetMilliseconds = 4)
    {
        var watch = Stopwatch.StartNew();
        do
        {
            if (!jobs.TryDequeue(out var job)) return;
            try { job.Work(); }
            catch (Exception ex) { string error = $"{job.Label}: {ex.Message}"; Errors.Add(error); Monitor.Log(error, LogLevel.Warn); }
            Completed++;
        } while (watch.Elapsed.TotalMilliseconds < budgetMilliseconds);
    }
    public JObject Texture(string asset, Texture2D? texture = null)
    {
        texture ??= Helper.GameContent.Load<Texture2D>(asset);
        var image = Read(texture);
        string relative = "textures/" + asset.Replace('\\', '/') + ".png";
        Image(relative, image);
        string hash = Convert.ToHexString(SHA256.HashData(System.Runtime.InteropServices.MemoryMarshal.AsBytes(image.Pixels.AsSpan())));
        var descriptor = new JObject { ["Asset"] = asset, ["File"] = relative, ["Size"] = new JArray(image.Width, image.Height), ["Hash"] = hash };
        var entries = (JArray)Catalog["Textures"]!;
        entries.FirstOrDefault(x => string.Equals((string?)x["Asset"], asset, StringComparison.OrdinalIgnoreCase))?.Remove();
        entries.Add(descriptor);
        origins.Asset(asset);
        return descriptor;
    }
    public void ScheduleTexture(string asset, Texture2D? texture = null)
    { if (!string.IsNullOrWhiteSpace(asset) && textures.Add(asset.Replace('\\', '/'))) Queue("texture " + asset, () => Texture(asset, texture)); }
    public void Image(string relativePath, RgbaImage image) => transaction.Write(relativePath, PngEncoder.Encode(image));
    public void Bytes(string relativePath, byte[] bytes) => transaction.Write(relativePath, bytes);
    public void Json(string relativePath, object value) => transaction.Json(relativePath, value);
    public JObject Origin(string asset, string? key = null) => key == null ? origins.Asset(asset) : origins.DataKey(asset, key);
    public void Finish(bool publishCatalog = true)
    {
        if (!Done) throw new InvalidOperationException("Export still has queued work.");
        if (Errors.Count != 0) { store.Json("last-export-error.json", new { Complete = false, Completed, Scheduled, Errors }); return; }
        Json("origins.json", origins.ToJson());
        if (publishCatalog) { Catalog["ExportedAt"] = DateTime.UtcNow.ToString("O"); Json("catalog.json", Catalog); }
        Json("export-report.json", new { Complete = true, Completed, Scheduled, Errors });
        transaction.Commit();
    }
    public static RgbaImage Read(Texture2D texture)
    {
        var colors = new Color[checked(texture.Width * texture.Height)]; texture.GetData(colors);
        var pixels = new uint[colors.Length];
        for (int i = 0; i < colors.Length; i++)
        {
            var c = colors[i];
            uint Channel(byte channel) => c.A == 0 ? 0u : Math.Min(255u, ((uint)channel * 255 + c.A / 2u) / c.A);
            pixels[i] = (uint)c.A << 24 | Channel(c.R) << 16 | Channel(c.G) << 8 | Channel(c.B);
        }
        return new(texture.Width, texture.Height, pixels);
    }
}
