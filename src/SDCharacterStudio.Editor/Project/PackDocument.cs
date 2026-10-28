using System.IO;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core;
using SDCharacterStudio.Core.Loading;
using SDCharacterStudio.Core.Migration;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Serialization;
using SDCharacterStudio.Core.Validation;

namespace SDCharacterStudio.Editor.Project;

/// <summary>One editable cutscene and the pack-relative file it lives in.</summary>
public sealed class CutsceneDocument
{
    public string File { get; set; }
    public Cutscene Scene { get; set; }
    public CutsceneDocument(string file, Cutscene scene) { File = file; Scene = scene; }
}

/// <summary>A PNG-sequence video cutaway (doc 03 §14).</summary>
public sealed class VideoEntry
{
    public string Id { get; set; } = "";
    public int FrameCount { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public VideoSettings Settings { get; set; } = new();
    public string FirstFrame { get; set; } = "";
}

public sealed class NewProjectOptions
{
    public string Name { get; set; } = "My Cutscenes";
    public string Author { get; set; } = "Player";
    public string UniqueId { get; set; } = "Player.MyCutscenes";
    public string ParentFolder { get; set; } = "";
    public string FirstCutsceneId { get; set; } = "my-first-scene";
    public string? FirstCutsceneTitle { get; set; }
    public string Location { get; set; } = "Town";
}

/// <summary>
/// The open project: editable manifest, studio index and cutscenes (Core models), plus the read-only library
/// (rigs, masks, poses, portraits, expressions, costumes; edited from M8) and the pack's videos.
/// Every user action goes through <see cref="Edit"/>, which records one undo step.
/// </summary>
public sealed class PackDocument
{
    private static readonly Regex IdChars = new("[^a-z0-9._-]+", RegexOptions.CultureInvariant);
    private readonly HashSet<string> loadedCutsceneFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> media = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> binaryAssets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> textAssets = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> removedAssets = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> removedMedia = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> persistedMedia = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> persistedAssets = new(StringComparer.OrdinalIgnoreCase);
    private string libraryContentState = "";
    private string MediaRoot => ProjectPath + ".media";

    public string ProjectPath { get; private set; }
    public string PackFolder { get; private set; }
    public ProjectFile Project { get; private set; }
    public Manifest Manifest { get; private set; } = new();
    public StudioIndex Studio { get; private set; } = new();
    public List<CutsceneDocument> Cutscenes { get; } = new();
    public List<VideoEntry> Videos { get; } = new();
    public LoadedPack Library { get; private set; } = new();
    /// <summary>Problems found while reading files from disk (parse errors etc.).</summary>
    public IReadOnlyList<ValidationIssue> LoadIssues { get; private set; } = Array.Empty<ValidationIssue>();
    public History History { get; }
    public bool IsDirty { get; private set; }
    public string PackId => Manifest.UniqueID ?? "";
    public string DisplayName => Studio.Pack?.DisplayName ?? Manifest.Name ?? Path.GetFileName(PackFolder);

    /// <summary>Raised after any edit, undo, redo or reload. The string is the action label.</summary>
    public event EventHandler<string>? Changed;

    private PackDocument(string projectPath, string packFolder, ProjectFile project)
    {
        ProjectPath = projectPath; PackFolder = packFolder; Project = project;
        LoadFromDisk();
        History = new History(CaptureState());
    }

    public static PackDocument Open(string projectPath)
    {
        projectPath = Path.GetFullPath(projectPath);
        var project = ProjectFile.Read(projectPath);
        string pack = project.ResolvePackFolder(projectPath);
        if (!File.Exists(Path.Combine(pack, "studio.json"))) throw new FileNotFoundException($"The project's pack folder has no studio.json: {pack}");
        return new PackDocument(projectPath, pack, project);
    }

    /// <summary>Open a pack folder that has no project yet. The project file goes to the per-user projects folder.</summary>
    public static PackDocument OpenPackFolder(string packFolder, string projectsFolder)
    {
        packFolder = Path.GetFullPath(packFolder);
        if (!File.Exists(Path.Combine(packFolder, "studio.json"))) throw new FileNotFoundException("That folder isn't an SDCS pack (no studio.json).");
        Directory.CreateDirectory(projectsFolder);
        string name = SafeFileName(Path.GetFileName(packFolder.TrimEnd('\\', '/')).Replace("[SDCS]", "").Trim());
        string projectPath = Path.Combine(projectsFolder, (name.Length == 0 ? "pack" : name) + ".sdcs");
        var project = File.Exists(projectPath) ? ProjectFile.Read(projectPath) : new ProjectFile();
        project.PackFolder = packFolder;
        File.WriteAllText(projectPath, project.ToJson());
        return new PackDocument(projectPath, packFolder, project);
    }

    public static PackDocument Create(NewProjectOptions options)
    {
        string projectFolder = Path.Combine(options.ParentFolder, SafeFileName(options.Name));
        string packFolder = Path.Combine(projectFolder, "[SDCS] " + SafeFileName(options.Name));
        if (Directory.Exists(packFolder) && Directory.EnumerateFileSystemEntries(packFolder).Any()) throw new IOException($"The folder already exists and isn't empty: {packFolder}");
        Directory.CreateDirectory(packFolder);
        var manifest = new Manifest
        {
            Name = options.Name, Author = options.Author, Version = "1.0.0", Description = "Cutscenes made with SDCharacterStudio.",
            UniqueID = options.UniqueId, ContentPackFor = new ContentPackTarget { UniqueID = Ids.RuntimeModId, MinimumVersion = "0.1.0" }
        };
        var studio = new StudioIndex { Format = "1.0", Pack = new PackInfo { DisplayName = options.Name } };
        var scene = NewCutscene(ToContentId(options.FirstCutsceneId), options.Location, playsByItself: true);
        scene.Title = options.FirstCutsceneTitle;
        PackWriter.WriteIfChanged(Path.Combine(packFolder, "manifest.json"), PackWriter.Serialize(manifest));
        PackWriter.WriteIfChanged(Path.Combine(packFolder, "studio.json"), PackWriter.Serialize(studio));
        PackWriter.WriteIfChanged(Path.Combine(packFolder, "cutscenes", scene.Id + ".json"), PackWriter.Serialize(scene));
        string projectPath = Path.Combine(projectFolder, SafeFileName(options.Name) + ".sdcs");
        var project = new ProjectFile { PackFolder = Path.GetRelativePath(projectFolder, packFolder), OpenCutscene = scene.Id };
        File.WriteAllText(projectPath, project.ToJson());
        return new PackDocument(projectPath, packFolder, project);
    }

    /// <summary>
    /// A new scene. Scenes the designer creates play by themselves when the player enters the location (once, no
    /// conditions yet): an exported mod has nothing else to start them (D-46). Internal preview scenes stay manual.
    /// </summary>
    public static Cutscene NewCutscene(string id, string location, bool playsByItself = false) => new()
    {
        Format = "1.0", Id = id, Location = location, Title = null, Trigger = playsByItself ? new Trigger() : null,
        OnEnd = playsByItself ? new EndSettings { End = "stay" } : new EndSettings(),
        Actors = new() { new Actor { Id = "farmer", Kind = "Farmer", Start = new Position { Tile = new[] { 10, 10 } }, Facing = "down" } },
        Beats = new()
    };

    /// <summary>Lowercase kebab-case content ID (doc 03 conventions: a-z 0-9 - _ .).</summary>
    public static string ToContentId(string text)
    {
        string id = IdChars.Replace(text.Trim().ToLowerInvariant().Replace(' ', '-'), "-").Trim('-', '.', '_');
        return id.Length == 0 ? "scene" : char.IsLetterOrDigit(id[0]) ? id : "s" + id;
    }
    public static string SafeFileName(string text) => string.Concat(text.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)).Trim();

    private void LoadFromDisk()
    {
        var read = new PackReader(new DiskPackFileSystem(PackFolder), new PackMigrator()).Read(exportValidation: false);
        Library = read.Pack;
        Manifest = read.Pack.Manifest ?? new Manifest();
        Studio = read.Pack.Index ?? new StudioIndex { Format = "1.0" };
        Cutscenes.Clear(); loadedCutsceneFiles.Clear();
        foreach (var entry in read.Pack.Cutscenes) { Cutscenes.Add(new CutsceneDocument(entry.File, entry.Value)); loadedCutsceneFiles.Add(entry.File); }
        // Only file-level problems matter here; the live validator reports content problems.
        LoadIssues = read.Issues.Where(i => i.Rule is "Schema" or "V1" or "V4" && i.Severity == ValidationSeverity.Error && i.JsonPath.Length == 0).ToArray();
        LoadVideos();
        LoadMediaIndex();
        LoadAssetIndex();
        libraryContentState = LibraryContentState();
    }

    public void LoadVideos()
    {
        Videos.Clear();
        string root = Path.Combine(PackFolder, "videos");
        if (!Directory.Exists(root)) return;
        foreach (var folder in Directory.EnumerateDirectories(root).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var frames = Directory.EnumerateFiles(folder, "*.png").OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            if (frames.Length == 0) continue;
            var settings = new VideoSettings();
            string json = Path.Combine(folder, "video.json");
            if (File.Exists(json)) try { settings = JsonConvert.DeserializeObject<VideoSettings>(File.ReadAllText(json)) ?? settings; } catch (JsonException) { }
            var size = new DiskPackFileSystem(PackFolder).GetImageSize("videos/" + Path.GetFileName(folder) + "/" + Path.GetFileName(frames[0]));
            Videos.Add(new VideoEntry { Id = Path.GetFileName(folder), FrameCount = frames.Length, Settings = settings, FirstFrame = frames[0], Width = size?.Width ?? 0, Height = size?.Height ?? 0 });
        }
    }

    private void LoadMediaIndex()
    {
        media.Clear(); removedMedia.Clear();
        persistedMedia.Clear();
        foreach (var file in Directory.Exists(Path.Combine(PackFolder, "videos")) ? Directory.EnumerateFiles(Path.Combine(PackFolder, "videos"), "*", SearchOption.AllDirectories) : Array.Empty<string>())
        {
            var rel = Path.GetRelativePath(PackFolder, file).Replace('\\', '/');
            media[rel] = PutBlob(File.ReadAllBytes(file));
            persistedMedia.Add(rel);
        }
    }

    private void LoadAssetIndex()
    {
        foreach (var file in Directory.Exists(PackFolder) ? Directory.EnumerateFiles(PackFolder, "*", SearchOption.AllDirectories) : Array.Empty<string>())
        {
            var rel = Path.GetRelativePath(PackFolder, file).Replace('\\', '/');
            if (!IsAssetPath(rel)) continue;
            persistedAssets.Add(rel);
            if (rel.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) textAssets[rel] = File.ReadAllText(file);
            else binaryAssets[rel] = PutBlob(File.ReadAllBytes(file));
        }
    }

    private string PutBlob(byte[] bytes)
    {
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        string path = Path.Combine(MediaRoot, hash);
        Directory.CreateDirectory(MediaRoot);
        if (!File.Exists(path)) File.WriteAllBytes(path, bytes);
        return hash;
    }

    private byte[] GetBlob(string hash)
    {
        if (!Regex.IsMatch(hash, "^[0-9a-f]{64}$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)) throw new InvalidDataException("Invalid managed blob reference.");
        return File.ReadAllBytes(Path.Combine(MediaRoot, hash));
    }
    internal IReadOnlyDictionary<string, string> CaptureBlobPaths() => media.Concat(binaryAssets).ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
    internal byte[] ReadBlobReference(string hash) => GetBlob(hash);
    /// <summary>The current immutable PNG/source file for editor image caches, including unsaved imports.</summary>
    public string? AssetFile(string path)
    {
        if (Path.IsPathRooted(path)) path = Path.GetRelativePath(PackFolder, path);
        path = path.Replace('\\', '/');
        path = path.StartsWith("videos/", StringComparison.OrdinalIgnoreCase) ? NormalizeMediaPath(path) : NormalizeAssetPath(path);
        if (RemovedFiles.Contains(path, StringComparer.OrdinalIgnoreCase)) return null;
        if (media.TryGetValue(path, out var hash) || binaryAssets.TryGetValue(path, out hash))
        {
            if (!Regex.IsMatch(hash, "^[0-9a-f]{64}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) throw new InvalidDataException("Invalid managed blob reference.");
            return Path.Combine(MediaRoot, hash);
        }
        string disk = Path.Combine(PackFolder, path.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(disk) ? disk : null;
    }
    public byte[] ReadMediaBytes(string path)
    {
        path = NormalizeMediaPath(path);
        if (removedMedia.Contains(path) || persistedMedia.Contains(path) && !media.ContainsKey(path)) throw new FileNotFoundException(path);
        return media.TryGetValue(path, out var hash) ? GetBlob(hash) : File.ReadAllBytes(Path.Combine(PackFolder, path.Replace('/', Path.DirectorySeparatorChar)));
    }
    public void SetBinaryAsset(string path, byte[] bytes)
    {
        path = NormalizeAssetPath(path); removedAssets.Remove(path); textAssets.Remove(path); binaryAssets[path] = PutBlob(bytes);
    }
    public void SetTextAsset(string path, string content)
    {
        path = NormalizeAssetPath(path); removedAssets.Remove(path); binaryAssets.Remove(path); textAssets[path] = content;
        RefreshLibraryAfterAssetChange();
    }
    public void RemoveAsset(string path) { path = NormalizeAssetPath(path); binaryAssets.Remove(path); textAssets.Remove(path); removedAssets.Add(path); RefreshLibraryAfterAssetChange(); }
    public byte[] ReadBinaryAsset(string path)
    {
        path = NormalizeAssetPath(path); if (removedAssets.Contains(path) || persistedAssets.Contains(path) && !binaryAssets.ContainsKey(path) && !textAssets.ContainsKey(path)) throw new FileNotFoundException(path);
        if (textAssets.TryGetValue(path, out var text)) return System.Text.Encoding.UTF8.GetBytes(text);
        return binaryAssets.TryGetValue(path, out var hash) ? GetBlob(hash) : File.ReadAllBytes(Path.Combine(PackFolder, path.Replace('/', Path.DirectorySeparatorChar)));
    }
    internal string? ReadTextAsset(string path) => textAssets.TryGetValue(path, out var text) ? text : null;
    private static string NormalizeAssetPath(string path)
    {
        path = path.Replace('\\', '/');
        if (Path.IsPathRooted(path) || path.Contains("../", StringComparison.Ordinal) || path == ".." || path.Split('/').Any(p => p == "..") || path.StartsWith("/", StringComparison.Ordinal) || !IsAssetPath(path))
            throw new ArgumentException("Asset path must be a pack file outside manifest.json, studio.json, cutscenes/, videos/ and i18n/ (assets/, library/, art/, masks/…).", nameof(path));
        path = path.TrimStart('/');
        return path;
    }
    /// <summary>
    /// Pack files the asset store manages: library JSON and the art it uses (assets/, library/, art/, masks/, images/…).
    /// The document renders manifest.json, studio.json and cutscenes itself; videos are media; i18n is edited as text elsewhere.
    /// </summary>
    internal static bool IsAssetPath(string path)
    {
        path = path.Replace('\\', '/');
        if (path.Length == 0 || !path.Contains('/')) return false;
        string top = path[..path.IndexOf('/')];
        return !(top.Equals("cutscenes", StringComparison.OrdinalIgnoreCase) || top.Equals("videos", StringComparison.OrdinalIgnoreCase)
            || top.Equals("i18n", StringComparison.OrdinalIgnoreCase) || top.StartsWith(".", StringComparison.Ordinal));
    }
    internal IEnumerable<string> MediaPaths => media.Keys;
    internal IEnumerable<string> AssetPaths => binaryAssets.Keys.Concat(textAssets.Keys);
    internal ImageSize? TryGetMediaSize(string path)
    {
        try { var b = ReadMediaBytes(path); if (b.Length >= 24 && b[0] == 137 && b[1] == 80 && b[2] == 78 && b[3] == 71) return new ImageSize(System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(16, 4)), System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(20, 4))); } catch { }
        return null;
    }
    internal ImageSize? TryGetAssetSize(string path)
    {
        try { var b = ReadBinaryAsset(path); if (b.Length >= 24 && b[0] == 137 && b[1] == 80 && b[2] == 78 && b[3] == 71) return new ImageSize(System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(16, 4)), System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(20, 4))); } catch { }
        return null;
    }
    private static string NormalizeMediaPath(string path)
    {
        path = path.Replace('\\', '/');
        if (path.Contains("../", StringComparison.Ordinal) || path == ".." || Path.IsPathRooted(path) || path.StartsWith("/", StringComparison.Ordinal) || !path.StartsWith("videos/", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Media path must be contained in videos/.", nameof(path));
        path = path.TrimStart('/');
        return path;
    }
    internal void StageVideo(string id, IReadOnlyList<string> frames, VideoSettings settings)
    {
        var prefix = "videos/" + ToContentId(id) + "/";
        var staged = frames.Select((f, i) => (Path: prefix + $"frame-{i + 1:000}.png", Hash: PutBlob(File.ReadAllBytes(f)))).ToList();
        staged.Add((prefix + "video.json", PutBlob(System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(settings, Formatting.Indented)))));
        foreach (var old in media.Keys.Where(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray()) { removedMedia.Add(old); media.Remove(old); }
        foreach (var item in staged) { media[item.Path] = item.Hash; removedMedia.Remove(item.Path); }
    }

    // ---- editing ----

    /// <summary>Apply a user action and record it as one undo step.</summary>
    public void Edit(string label, Action mutate)
    {
        mutate();
        Commit(label);
    }

    /// <summary>Record changes already made to the models (e.g. by a bound field).</summary>
    public void Commit(string label)
    {
        if (History.Commit(label, CaptureState())) { RefreshLibraryAfterAssetChange(); IsDirty = true; Changed?.Invoke(this, label); }
    }

    public bool Undo() => Restore(History.Undo(), "Undo");
    public bool Redo() => Restore(History.Redo(), "Redo");
    private bool Restore(string? state, string label)
    {
        if (state == null) return false;
        ApplyState(state);
        IsDirty = true;
        Changed?.Invoke(this, label);
        return true;
    }

    /// <summary>The whole editable state as one string: the unit of undo, autosave and change detection.</summary>
    public string CaptureState()
    {
        var state = new JObject
        {
            ["Manifest"] = JToken.Parse(PackWriter.Serialize(Manifest)),
            ["Studio"] = JToken.Parse(PackWriter.Serialize(Studio)),
            ["Cutscenes"] = new JArray(Cutscenes.Select(c => new JObject { ["File"] = c.File, ["Scene"] = JToken.Parse(PackWriter.Serialize(c.Scene)) })),
            ["Videos"] = JToken.FromObject(Videos),
            ["Media"] = JToken.FromObject(media),
            ["BinaryAssets"] = JToken.FromObject(binaryAssets),
            ["TextAssets"] = JToken.FromObject(textAssets),
            ["RemovedAssets"] = JToken.FromObject(removedAssets),
            ["RemovedMedia"] = JToken.FromObject(removedMedia),
            ["Project"] = EditableProjectState()
        };
        return state.ToString(Formatting.None);
    }

    // View selection, zoom and layout are session state. They are persisted by Save,
    // but must not create an undo step or be reverted when content is undone.
    private JObject EditableProjectState()
    {
        var project = JToken.Parse(Project.ToJson()) as JObject ?? new JObject();
        foreach (var key in new[] { "OpenCutscene", "StageZoom", "ShowGrid", "ShowPassability", "StageLayouts", "DockLayoutXml", "StoryboardGraph" }) project.Remove(key);
        return project;
    }

    public void ApplyState(string json)
    {
        var state = JObject.Parse(json);
        // Recovery files and history references must never name files outside
        // their managed roots. Validate the entire mapping before applying it.
        foreach (string key in new[] { "Media", "BinaryAssets", "TextAssets" })
            foreach (var entry in (state[key] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
            {
                if (key == "Media") NormalizeMediaPath(entry.Name); else NormalizeAssetPath(entry.Name);
                if (key != "TextAssets" && !Regex.IsMatch((string?)entry.Value ?? "", "^[0-9a-f]{64}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    throw new InvalidDataException("Invalid managed blob reference.");
            }
        foreach (var entry in (JArray?)state["RemovedAssets"] ?? new()) NormalizeAssetPath((string)entry!);
        foreach (var entry in (JArray?)state["RemovedMedia"] ?? new()) NormalizeMediaPath((string)entry!);
        Manifest = PackJson.Deserialize<Manifest>(state["Manifest"]!.ToString())!;
        Studio = PackJson.Deserialize<StudioIndex>(state["Studio"]!.ToString())!;
        Cutscenes.Clear();
        foreach (var entry in (JArray)state["Cutscenes"]!)
            Cutscenes.Add(new CutsceneDocument((string)entry["File"]!, PackJson.Deserialize<Cutscene>(entry["Scene"]!.ToString())!));
        Videos.Clear(); foreach (var v in (JArray?)state["Videos"] ?? new JArray()) Videos.Add(v.ToObject<VideoEntry>()!);
        media.Clear(); foreach (var p in (state["Media"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>()) media[p.Name] = (string)p.Value!;
        binaryAssets.Clear(); foreach (var p in (state["BinaryAssets"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>()) binaryAssets[p.Name] = (string)p.Value!;
        textAssets.Clear(); foreach (var p in (state["TextAssets"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>()) textAssets[p.Name] = (string)p.Value!;
        removedAssets.Clear(); foreach (var p in (JArray?)state["RemovedAssets"] ?? new JArray()) removedAssets.Add((string)p!);
        removedMedia.Clear(); foreach (var p in (JArray?)state["RemovedMedia"] ?? new JArray()) removedMedia.Add((string)p!);
        var project = JsonConvert.DeserializeObject<ProjectFile>(state["Project"]!.ToString())!;
        // Keep current session view state while restoring editable project settings.
        project.PackFolder = Project.PackFolder;
        project.OpenCutscene = Project.OpenCutscene;
        project.StageZoom = Project.StageZoom;
        project.ShowGrid = Project.ShowGrid;
        project.ShowPassability = Project.ShowPassability;
        project.StageLayouts = Project.StageLayouts;
        project.DockLayoutXml = Project.DockLayoutXml;
        project.StoryboardGraph = Project.StoryboardGraph;
        Project = project;
        RefreshLibraryAfterAssetChange();
    }

    /// <summary>Replace the state wholesale (autosave recovery). The restored state becomes undoable.</summary>
    public void RestoreSnapshot(string state, string label)
    {
        ApplyState(state);
        Commit(label);
    }

    // ---- files ----

    /// <summary>The pack files as they would be written now (pack-relative path → content).</summary>
    public Dictionary<string, string> RenderFiles()
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["manifest.json"] = PackWriter.Serialize(Manifest),
            ["studio.json"] = PackWriter.Serialize(Studio),
        };
        foreach (var c in Cutscenes) files[c.File] = PackWriter.Serialize(c.Scene);
        foreach (var v in Videos) files["videos/" + v.Id + "/video.json"] = JsonConvert.SerializeObject(v.Settings, Formatting.Indented).Replace("\r\n", "\n") + "\n";
        foreach (var (path, content) in textAssets) files[path] = content;
        return files;
    }

    /// <summary>Cutscene files that existed on disk but are gone from the document (deleted or renamed).</summary>
    public IReadOnlyList<string> RemovedFiles => loadedCutsceneFiles.Where(f => !Cutscenes.Any(c => string.Equals(c.File, f, StringComparison.OrdinalIgnoreCase)))
        .Concat(removedMedia).Concat(removedAssets).Concat(persistedMedia.Except(media.Keys, StringComparer.OrdinalIgnoreCase))
        .Concat(persistedAssets.Except(binaryAssets.Keys.Concat(textAssets.Keys), StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public void Save()
    {
        SyncCutsceneIndex();
        var rendered = RenderFiles();
        foreach (var (file, content) in rendered)
            PackWriter.WriteIfChanged(Path.Combine(PackFolder, file.Replace('/', Path.DirectorySeparatorChar)), content);
        foreach (var path in media.Keys.Where(p => !rendered.ContainsKey(p)))
        {
            var target = Path.Combine(PackFolder, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllBytes(target, GetBlob(media[path]));
        }
        foreach (var (path, hash) in binaryAssets)
        { var target = Path.Combine(PackFolder, path.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllBytes(target, GetBlob(hash)); }
        foreach (var (path, content) in textAssets)
        { var target = Path.Combine(PackFolder, path.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); PackWriter.WriteIfChanged(target, content); }
        foreach (var path in removedAssets) { var target = Path.Combine(PackFolder, path.Replace('/', Path.DirectorySeparatorChar)); if (File.Exists(target)) File.Delete(target); }
        foreach (var path in removedMedia) { var target = Path.Combine(PackFolder, path.Replace('/', Path.DirectorySeparatorChar)); if (File.Exists(target)) File.Delete(target); }
        foreach (var removed in RemovedFiles)
        {
            string path = Path.Combine(PackFolder, removed.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path)) File.Delete(path);
        }
        loadedCutsceneFiles.Clear();
        foreach (var c in Cutscenes) loadedCutsceneFiles.Add(c.File);
        persistedMedia.Clear(); persistedMedia.UnionWith(media.Keys);
        persistedAssets.Clear(); persistedAssets.UnionWith(binaryAssets.Keys.Concat(textAssets.Keys));
        File.WriteAllText(ProjectPath, Project.ToJson());
        IsDirty = false;
        Autosave.Delete(ProjectPath);
        Changed?.Invoke(this, "Save");
    }

    /// <summary>Keep studio.json's explicit cutscene list (if it has one) in step with the files.</summary>
    public void SyncCutsceneIndex()
    {
        foreach (var c in Cutscenes)
        {
            string expected = "cutscenes/" + c.Scene.Id + ".json";
            if (!string.Equals(c.File, expected, StringComparison.OrdinalIgnoreCase) && c.File.StartsWith("cutscenes/", StringComparison.OrdinalIgnoreCase)
                && !Cutscenes.Any(o => o != c && string.Equals(o.File, expected, StringComparison.OrdinalIgnoreCase)))
                c.File = expected;
        }
        if (Studio.Cutscenes != null) Studio.Cutscenes = Cutscenes.Select(c => c.File).ToList();
    }

    public CutsceneDocument? FindCutscene(string? id) => Cutscenes.FirstOrDefault(c => string.Equals(c.Scene.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Re-read the library (poses, rigs, costumes…) after files changed on disk.</summary>
    public void ReloadLibrary()
    {
        // Reload through the same live overlay used by validation/preview.  Library
        // consumers must see unsaved text/binary assets and removals immediately.
        var read = new PackReader(CreateOverlayFileSystem(), new PackMigrator()).Read(exportValidation: false);
        Library = read.Pack;
        libraryContentState = LibraryContentState();
        Changed?.Invoke(this, "Reload library");
    }

    // studio.json's Library globs decide which files are read, so an index change refreshes the library too.
    private string LibraryContentState() => JsonConvert.SerializeObject(new { binaryAssets, textAssets, removedAssets, index = PackWriter.Serialize(Studio.Library ?? new LibraryIndex()) });
    private void RefreshLibraryAfterAssetChange()
    {
        string state = LibraryContentState();
        if (state == libraryContentState) return;
        Library = new PackReader(CreateOverlayFileSystem(), new PackMigrator()).Read(exportValidation: false).Pack;
        libraryContentState = state;
    }

    public IPackFileSystem CreateOverlayFileSystem() => new OverlayPackFileSystem(new DiskPackFileSystem(PackFolder), RenderFiles(), RemovedFiles.Concat(removedMedia).Concat(removedAssets).ToArray(), this);
}

/// <summary>The pack as the runtime would read it if saved now: in-memory JSON over the files on disk.</summary>
public sealed class OverlayPackFileSystem : IPackFileSystem, IImageMetadataProvider
{
    private readonly DiskPackFileSystem disk;
    private readonly Dictionary<string, string> files;
    private readonly HashSet<string> removed;
    private readonly PackDocument? document;
    private readonly IReadOnlyDictionary<string, byte[]>? binaryAssets;
    private readonly IReadOnlyDictionary<string, string>? textAssets;
    private readonly IReadOnlyDictionary<string, string>? blobPaths;
    private readonly string? blobRoot;

    public OverlayPackFileSystem(DiskPackFileSystem disk, Dictionary<string, string> files, IEnumerable<string> removed,
        PackDocument? document = null, IReadOnlyDictionary<string, byte[]>? binaryAssets = null,
        IReadOnlyDictionary<string, string>? textAssets = null, IReadOnlyDictionary<string, string>? blobPaths = null, string? blobRoot = null)
    {
        this.disk = disk; this.files = new(files, StringComparer.OrdinalIgnoreCase);
        this.removed = new(removed, StringComparer.OrdinalIgnoreCase);
        this.document = document; this.binaryAssets = binaryAssets; this.textAssets = textAssets;
        this.blobPaths = blobPaths; this.blobRoot = blobRoot;
    }

    public bool Exists(string path) => !removed.Contains(path) && (files.ContainsKey(path)
        || binaryAssets?.ContainsKey(path) == true || textAssets?.ContainsKey(path) == true || blobPaths?.ContainsKey(path) == true
        || document?.MediaPaths.Contains(path, StringComparer.OrdinalIgnoreCase) == true
        || document?.AssetPaths.Contains(path, StringComparer.OrdinalIgnoreCase) == true || disk.Exists(path));

    private string BlobFile(string path)
    {
        if (blobPaths == null || !blobPaths.TryGetValue(path, out string? hash)
            || !Regex.IsMatch(hash, "^[0-9a-f]{64}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new InvalidDataException("Invalid managed blob reference.");
        return Path.Combine(blobRoot!, hash);
    }

    public string ReadText(string path)
    {
        if (removed.Contains(path)) throw new FileNotFoundException(path);
        if (files.TryGetValue(path, out var text)) return text;
        if (textAssets?.TryGetValue(path, out text) == true) return text;
        if (binaryAssets?.TryGetValue(path, out var bytes) == true) return System.Text.Encoding.UTF8.GetString(bytes);
        if (blobPaths?.ContainsKey(path) == true) return File.ReadAllText(BlobFile(path));
        if (document?.MediaPaths.Contains(path, StringComparer.OrdinalIgnoreCase) == true)
            return System.Text.Encoding.UTF8.GetString(document.ReadMediaBytes(path));
        if (document?.AssetPaths.Contains(path, StringComparer.OrdinalIgnoreCase) == true)
            return document.ReadTextAsset(path) ?? System.Text.Encoding.UTF8.GetString(document.ReadBinaryAsset(path));
        return disk.ReadText(path);
    }

    /// <summary>A file's bytes as the pack would contain it now (images for the Character Studio).</summary>
    public byte[] ReadBytes(string path)
    {
        if (removed.Contains(path)) throw new FileNotFoundException(path);
        if (files.TryGetValue(path, out var text)) return System.Text.Encoding.UTF8.GetBytes(text);
        if (textAssets?.TryGetValue(path, out text) == true) return System.Text.Encoding.UTF8.GetBytes(text);
        if (binaryAssets?.TryGetValue(path, out var bytes) == true) return bytes;
        if (blobPaths?.ContainsKey(path) == true) return File.ReadAllBytes(BlobFile(path));
        if (document?.MediaPaths.Contains(path, StringComparer.OrdinalIgnoreCase) == true) return document.ReadMediaBytes(path);
        if (document?.AssetPaths.Contains(path, StringComparer.OrdinalIgnoreCase) == true) return document.ReadBinaryAsset(path);
        return disk.ReadBytes(path);
    }

    public IEnumerable<string> ListFiles(string glob)
    {
        var regex = new Regex("^" + Regex.Escape(glob).Replace(@"\*\*", ".*").Replace(@"\*", "[^/]*").Replace(@"\?", "[^/]") + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        IEnumerable<string> candidates = disk.ListFiles(glob).Concat(files.Keys)
            .Concat(binaryAssets?.Keys ?? Enumerable.Empty<string>()).Concat(textAssets?.Keys ?? Enumerable.Empty<string>())
            .Concat(blobPaths?.Keys ?? Enumerable.Empty<string>());
        if (document != null) candidates = candidates.Concat(document.MediaPaths).Concat(document.AssetPaths);
        return candidates.Where(p => !removed.Contains(p) && regex.IsMatch(p))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.Ordinal).ToArray();
    }

    public ImageSize? GetImageSize(string path)
    {
        if (removed.Contains(path)) return null;
        try
        {
            Span<byte> header = stackalloc byte[24];
            if (blobPaths?.ContainsKey(path) == true)
            {
                using var stream = File.OpenRead(BlobFile(path));
                stream.ReadExactly(header);
            }
            else if (binaryAssets?.TryGetValue(path, out var bytes) == true)
            {
                if (bytes.Length < 24) return null;
                bytes.AsSpan(0, 24).CopyTo(header);
            }
            else if (document != null && (document.MediaPaths.Contains(path, StringComparer.OrdinalIgnoreCase) || document.AssetPaths.Contains(path, StringComparer.OrdinalIgnoreCase)))
            {
                string? file = document.AssetFile(path);
                if (file == null) return null;
                using var stream = File.OpenRead(file);
                stream.ReadExactly(header);
            }
            else return disk.GetImageSize(path);
            ReadOnlySpan<byte> signature = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
            if (!header[..8].SequenceEqual(signature)) return null;
            int width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header[16..20]);
            int height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header[20..24]);
            return width > 0 && height > 0 ? new ImageSize(width, height) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }
}
