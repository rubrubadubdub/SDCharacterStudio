using System.IO;
using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Serialization;
using SDCharacterStudio.Editor.Project;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Preview;

public enum BridgeState { NotRunning, NoSave, Ready, Busy }

/// <param name="Note">From the Bridge: e.g. that mods changed since the game export (the export never repeats by itself, D-47).</param>
public sealed record BridgeStatus(BridgeState State, string Text, string? GameVersion = null, string? Note = null);

/// <summary>
/// "Preview in game" (doc 07 §9): mirror the pack, unsaved edits included, into a preview folder under a separate
/// UniqueID (so an installed copy of the same pack can't clash), register it with the Bridge and play a cutscene.
/// </summary>
public sealed class PreviewService : IDisposable
{
    public const string PreviewSuffix = Core.Ids.PreviewSuffix;
    private readonly BridgeClient client;
    private readonly string previewRoot;
    public event Action<string, string>? Log;   // level, message

    public PreviewService(BridgeClient client, string previewRoot)
    {
        this.client = client; this.previewRoot = previewRoot;
        client.Notification += message =>
        {
            string op = (string?)message["Op"] ?? "";
            if (op == "Log") Log?.Invoke((string?)message["Level"] ?? "Info", (string?)message["Message"] ?? "");
            else if (op is "CutsceneStarted" or "CutsceneEnded") Log?.Invoke("Info", $"{op}: {message["Cutscene"] ?? message["Key"]}");
        };
    }

    public BridgeClient Client => client;

    public async Task<BridgeStatus> PingAsync()
    {
        if (!await client.TryConnectAsync().ConfigureAwait(false)) return new BridgeStatus(BridgeState.NotRunning, "Game not running");
        try
        {
            var reply = await client.SendAsync(new JObject { ["Op"] = "Ping" }, timeout: TimeSpan.FromSeconds(4)).ConfigureAwait(false);
            string version = (string?)reply["Game"] ?? "";
            string? note = (string?)reply["ExportNote"];
            if ((bool?)reply["Exporting"] == true) return new BridgeStatus(BridgeState.Busy, "Game exporting…", version, note);
            return (bool?)reply["SaveLoaded"] == true ? new BridgeStatus(BridgeState.Ready, "Game ready", version, note) : new BridgeStatus(BridgeState.NoSave, "Load a save", version, note);
        }
        // The pipe is there, so the game is running; it just didn't answer in time (an older Bridge answers on the game thread).
        catch (BridgeException) { return new BridgeStatus(BridgeState.Busy, "Game busy…"); }
    }

    /// <summary>Write the preview copy and return its folder and pack ID.</summary>
    /// <param name="adjust">Changes a scene's preview copy ("Try it as…" forces how Checks and Chances land, D-51).</param>
    public (string Folder, string PackId, string Cutscene) PreparePreviewPack(PackDocument document, string cutsceneId, string? lane = null, int fromIndex = -1,
        Func<Cutscene, Cutscene>? adjust = null)
    {
        string packId = document.PackId + PreviewSuffix;
        string folder = Path.Combine(previewRoot, PackDocument.SafeFileName(packId));
        Mirror(document.PackFolder, folder, document.RenderFiles(), document.RemovedFiles, document);
        // A preview copy plays when asked, never by itself: with a trigger it would start again whenever the player
        // walks into the location (and mark itself seen in the test save), blocking the next preview.
        foreach (var cutscene in document.Cutscenes.Where(c => c.Scene.Trigger != null || adjust != null))
        {
            var copy = PackJson.Deserialize<Cutscene>(PackWriter.Serialize(adjust?.Invoke(cutscene.Scene) ?? cutscene.Scene))!;
            copy.Trigger = null;
            File.WriteAllText(Path.Combine(folder, cutscene.File.Replace('/', Path.DirectorySeparatorChar)), PackWriter.Serialize(copy));
        }
        var manifest = PackJson.Deserialize<Manifest>(PackWriter.Serialize(document.Manifest))!;
        manifest.UniqueID = packId;
        manifest.Name = (manifest.Name ?? document.DisplayName) + " (preview)";
        // Preview runs whatever is installed; a missing optional mod should skip scenes, not the whole preview pack.
        manifest.Dependencies = new();
        File.WriteAllText(Path.Combine(folder, "manifest.json"), PackWriter.Serialize(manifest));
        var studio = PackJson.Deserialize<StudioIndex>(PackWriter.Serialize(document.Studio))!;
        studio.Requires = new();
        string playId = cutsceneId;
        var source = document.FindCutscene(cutsceneId) ?? throw new ArgumentException($"No cutscene '{cutsceneId}'.");
        if (lane != null && fromIndex >= 0)
        {
            var variant = FromBeat(adjust?.Invoke(source.Scene) ?? source.Scene, lane, fromIndex);
            playId = variant.Id!;
            string file = "cutscenes/" + playId + ".json";
            File.WriteAllText(Path.Combine(folder, file.Replace('/', Path.DirectorySeparatorChar)), PackWriter.Serialize(variant));
            if (studio.Cutscenes != null) studio.Cutscenes.Add(file);
        }
        File.WriteAllText(Path.Combine(folder, "studio.json"), PackWriter.Serialize(studio));
        return (folder, packId, playId);
    }

    public async Task PlayAsync(PackDocument document, string cutsceneId, string? lane = null, int fromIndex = -1, CancellationToken cancellation = default,
        Func<Cutscene, Cutscene>? adjust = null)
    {
        var (folder, packId, playId) = PreparePreviewPack(document, cutsceneId, lane, fromIndex, adjust);
        Log?.Invoke("Info", $"Registering {packId} from {folder}");
        var registered = await client.SendAsync(new JObject { ["Op"] = "RegisterDevPack", ["Folder"] = folder }, timeout: TimeSpan.FromSeconds(30), cancellation: cancellation).ConfigureAwait(false);
        var scenes = ((JArray?)registered["Cutscenes"])?.Select(x => (string?)x).ToArray() ?? Array.Empty<string?>();
        if (!scenes.Contains(playId, StringComparer.OrdinalIgnoreCase))
            throw new BridgeException($"The game loaded the pack but skipped '{playId}'. The Output panel shows why (usually something this game doesn't have).");
        Log?.Invoke("Info", $"Starting {playId}…");
        await client.SendAsync(new JObject { ["Op"] = "PlayCutscene", ["Pack"] = packId, ["Cutscene"] = playId, ["IgnoreConditions"] = true },
            progress => Log?.Invoke("Info", progress.ToString(Newtonsoft.Json.Formatting.None)), TimeSpan.FromSeconds(45), cancellation).ConfigureAwait(false);
        Log?.Invoke("Info", $"Playing {playId} in the game.");
    }

    /// <summary>
    /// The preview copy plus a cutscene that isn't in the pack (Character Studio previews a pose or animation in a
    /// temporary scene, doc 07 §15). The pack's own files are mirrored exactly as for a normal preview.
    /// </summary>
    public (string Folder, string PackId) PrepareTemporaryPreview(PackDocument document, Cutscene temporary)
    {
        if (document.Cutscenes.Count == 0) throw new ArgumentException("The pack has no cutscene to mirror.");
        var (folder, packId, _) = PreparePreviewPack(document, document.Cutscenes[0].Scene.Id!);
        string file = "cutscenes/" + temporary.Id + ".json";
        File.WriteAllText(Path.Combine(folder, file.Replace('/', Path.DirectorySeparatorChar)), PackWriter.Serialize(temporary));
        var studio = PackJson.Deserialize<StudioIndex>(File.ReadAllText(Path.Combine(folder, "studio.json")))!;
        if (studio.Cutscenes != null && !studio.Cutscenes.Contains(file, StringComparer.OrdinalIgnoreCase)) studio.Cutscenes.Add(file);
        File.WriteAllText(Path.Combine(folder, "studio.json"), PackWriter.Serialize(studio));
        return (folder, packId);
    }

    public async Task PlayTemporaryAsync(PackDocument document, Cutscene temporary, CancellationToken cancellation = default)
    {
        var (folder, packId) = PrepareTemporaryPreview(document, temporary);
        string playId = temporary.Id!;
        Log?.Invoke("Info", $"Registering {packId} from {folder}");
        var registered = await client.SendAsync(new JObject { ["Op"] = "RegisterDevPack", ["Folder"] = folder }, timeout: TimeSpan.FromSeconds(30), cancellation: cancellation).ConfigureAwait(false);
        var scenes = ((JArray?)registered["Cutscenes"])?.Select(x => (string?)x).ToArray() ?? Array.Empty<string?>();
        if (!scenes.Contains(playId, StringComparer.OrdinalIgnoreCase))
            throw new BridgeException($"The game loaded the pack but skipped the preview scene. The Output panel shows why.");
        await client.SendAsync(new JObject { ["Op"] = "PlayCutscene", ["Pack"] = packId, ["Cutscene"] = playId, ["IgnoreConditions"] = true },
            progress => Log?.Invoke("Info", progress.ToString(Newtonsoft.Json.Formatting.None)), TimeSpan.FromSeconds(45), cancellation).ConfigureAwait(false);
        Log?.Invoke("Info", $"Playing the Character Studio preview in the game.");
    }

    public Task StopAsync() => client.SendAsync(new JObject { ["Op"] = "StopCutscene" }, timeout: TimeSpan.FromSeconds(15));

    public Task<JObject> ExportAsync(Action<JObject> progress) =>
        client.SendAsync(new JObject { ["Op"] = "Export", ["Phase"] = "all" }, progress, TimeSpan.FromMinutes(10));

    /// <summary>Start at the selected beat with the first reachable caller context.
    /// Future choices retain all answers; their branch bodies are never flattened into the main lane.</summary>
    public static Cutscene FromBeat(Cutscene scene, string lane, int index)
    {
        var copy = PackWriter.Clone(scene);
        var lists = new Dictionary<string, List<Beat>>(StringComparer.Ordinal) { [LaneNames.Main] = copy.Beats };
        foreach (var pair in copy.Branches) lists[pair.Key] = pair.Value;
        if (copy.OnSkip != null) lists[LaneNames.Skip] = copy.OnSkip;
        lane = ResolveKey(lists, lane) ?? throw new ArgumentException("Choose an existing beat to preview.");
        if (!lists.TryGetValue(lane, out var selected) || selected.Count == 0) throw new ArgumentException("Choose an existing beat to preview.");
        index = Math.Clamp(index, 0, selected.Count - 1);
        var route = FindEntry(lists, lane, index);
        // An unreferenced branch can still be auditioned on its own.
        var prefix = route?.Prefix ?? selected.Take(index).ToList();
        foreach (var beat in prefix)
        {
            var actor = copy.Actors.FirstOrDefault(a => string.Equals(a.Id, (beat as MoveBeat)?.Actor ?? (beat as FaceBeat)?.Actor, StringComparison.OrdinalIgnoreCase));
            if (actor == null) continue;
            if (beat is MoveBeat { To.Tile: { Length: 2 } to } move)
            {
                // Compared where both would start in the compiled event: a walk to a place (D-52) and a start on the map line up.
                actor.Facing = MoveFacing(move, Places.FallbackTile(actor.Start), Places.FallbackTile(move.To)!) ?? actor.Facing;
                actor.Start = new Position { Tile = (int[])to.Clone(), Px = move.To.Px.ToArray(), From = move.To.From };
            }
            if (beat is FaceBeat { Facing: { } facing }) actor.Facing = facing;
        }
        var continuation = new List<Beat>();
        foreach (var frame in route?.Callers ?? Array.Empty<PreviewFrame>())
            continuation = AttachContinuation(lists[frame.Lane].Skip(frame.Index), continuation, copy.Branches, new(StringComparer.Ordinal));
        copy.Beats = AttachContinuation(selected.Skip(index), continuation, copy.Branches, new(StringComparer.Ordinal));
        if (copy.Beats.Count == 0 || copy.Beats[^1] is not EndBeat) copy.Beats.Add(new EndBeat());
        string laneId = lane == LaneNames.Main ? "main" : lane == LaneNames.Skip ? "skip" : "branch-" + lane;
        copy.Id = PackDocument.ToContentId($"{scene.Id}-from-{laneId}-{index}");
        copy.Trigger = null; copy.Skippable = true;
        return copy;
    }

    private sealed record PreviewFrame(string Lane, int Index);
    private sealed record PreviewEntry(List<Beat> Prefix, IReadOnlyList<PreviewFrame> Callers);

    private static PreviewEntry? FindEntry(Dictionary<string, List<Beat>> lists, string targetLane, int targetIndex)
    {
        var active = new HashSet<string>(StringComparer.Ordinal);
        int remaining = 4096;
        return Walk(targetLane == LaneNames.Skip ? LaneNames.Skip : LaneNames.Main, 0, new(), new());

        PreviewEntry? Walk(string lane, int index, List<PreviewFrame> callers, List<Beat> prefix)
        {
            if (--remaining < 0 || callers.Count > 64 || active.Count > 256 || !lists.TryGetValue(lane, out var beats)) return null;
            if (index >= beats.Count)
            {
                if (callers.Count == 0) return null;
                var frame = callers[^1];
                return Walk(frame.Lane, frame.Index, callers.Take(callers.Count - 1).ToList(), prefix);
            }
            if (lane == targetLane && index == targetIndex) return new(prefix, callers);
            string identity = lane + ":" + index + "|" + string.Join("|", callers.Select(f => f.Lane + ":" + f.Index));
            if (!active.Add(identity)) return null;
            try
            {
                var beat = beats[index];
                if (beat is EndBeat) return null;
                if (beat is GotoBeat go) return ResolveKey(lists, go.Branch) is { } target ? Walk(target, 0, callers, prefix) : null;
                if (BeatFlow.IsFork(beat))
                {
                    // Any of a fork's ways may lead to the beat: a Choice answer, a Check case, or a Check's "keep going".
                    foreach (var way in BeatFlow.Ways(beat))
                    {
                        if (way.KeepGoing) { if (Walk(lane, index + 1, callers, new(prefix)) is { } next) return next; continue; }
                        var nested = callers.Append(new PreviewFrame(lane, index + 1)).ToList();
                        if (ResolveKey(lists, way.Branch) is { } target && Walk(target, 0, nested, new(prefix)) is { } entry) return entry;
                    }
                    return null;
                }
                return Walk(lane, index + 1, callers, prefix.Append(beat).ToList());
            }
            finally { active.Remove(identity); }
        }
    }

    private static string? ResolveKey(Dictionary<string, List<Beat>> lists, string? target) => target == null ? null : lists.Keys.FirstOrDefault(k => string.Equals(k, target, StringComparison.OrdinalIgnoreCase));

    private static List<Beat> AttachContinuation(IEnumerable<Beat> suffix, List<Beat> continuation,
        Dictionary<string, List<Beat>> branches, HashSet<string> gotos)
    {
        var beats = suffix.ToList();
        var result = new List<Beat>();
        for (int i = 0; i < beats.Count; i++)
        {
            var beat = beats[i];
            if (beat is EndBeat) { result.Add(beat); return result; }
            if (beat is GotoBeat go)
            {
                // A Goto tail-calls its branch with the current return context.
                // Inline this tail path until a Choice (whose compiler then carries
                // the appended continuation), End, or non-returning Goto cycle.
                if (go.Branch is { } target && branches.Keys.FirstOrDefault(k => string.Equals(k, target, StringComparison.OrdinalIgnoreCase)) is { } key && branches.TryGetValue(key, out var body) && gotos.Add(key) && gotos.Count <= 256)
                    result.AddRange(AttachContinuation(body, continuation, branches, gotos));
                else result.Add(beat);
                return result;
            }
            if (BeatFlow.IsFork(beat))
            {
                result.AddRange(beats.Skip(i));
                result.AddRange(continuation);
                return result;
            }
            result.Add(beat);
        }
        result.AddRange(continuation);
        return result;
    }

    /// <summary>The facing a vanilla move leaves the actor in: the direction of its last leg (doc 04 §5.5).</summary>
    private static string? MoveFacing(MoveBeat move, int[]? from, int[] to)
    {
        if (from is not { Length: 2 }) return null;
        int dx = to[0] - from[0], dy = to[1] - from[1];
        string? horizontal = dx == 0 ? null : dx > 0 ? "right" : "left";
        string? vertical = dy == 0 ? null : dy > 0 ? "down" : "up";
        return move.Path == "y-first" ? horizontal ?? vertical : vertical ?? horizontal;
    }

    private static void Mirror(string source, string target, Dictionary<string, string> rendered, IReadOnlyList<string> removed, PackDocument? document = null)
    {
        Directory.CreateDirectory(target);
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, file).Replace('\\', '/');
            if (removed.Contains(relative, StringComparer.OrdinalIgnoreCase) || relative.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
            keep.Add(relative);
            string destination = Path.Combine(target, relative.Replace('/', Path.DirectorySeparatorChar));
            var from = new FileInfo(file); var to = new FileInfo(destination);
            if (to.Exists && to.Length == from.Length && to.LastWriteTimeUtc == from.LastWriteTimeUtc) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, true);
            File.SetLastWriteTimeUtc(destination, from.LastWriteTimeUtc);
        }
        foreach (var (relative, content) in rendered)
        {
            keep.Add(relative);
            PackWriter.WriteIfChanged(Path.Combine(target, relative.Replace('/', Path.DirectorySeparatorChar)), content);
        }
        if (document != null)
            foreach (var relative in document.MediaPaths.Concat(document.AssetPaths).Distinct(StringComparer.OrdinalIgnoreCase).Where(p => !rendered.ContainsKey(p)))
            {
                keep.Add(relative);
                var destination = Path.Combine(target, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.WriteAllBytes(destination, relative.StartsWith("videos/", StringComparison.OrdinalIgnoreCase) ? document.ReadMediaBytes(relative) : document.ReadBinaryAsset(relative));
            }
        foreach (var file in Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories))
            if (!keep.Contains(Path.GetRelativePath(target, file).Replace('\\', '/'))) File.Delete(file);
    }

    public void Dispose() => client.Dispose();
}
