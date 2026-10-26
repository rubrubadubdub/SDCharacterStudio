using SDCharacterStudio.Core;
using SDCharacterStudio.Core.Migration;
using System.IO;
using Newtonsoft.Json;
using SDCharacterStudio.Core.Loading;
using SDCharacterStudio.Core.Models;

namespace SDCharacterStudio.Editor.Studio;

/// <summary>item.json: what the personal library shows about an item (doc 03 §17.3).</summary>
public sealed class LibraryEntry
{
    public string Kind { get; set; } = "Pose";
    public string Id { get; set; } = "";
    /// <summary>The pose or animation's own ID inside the item (the one scenes name).</summary>
    public string Item { get; set; } = "";
    public string Title { get; set; } = "";
    public List<string> Tags { get; set; } = new();
    public string? Detail { get; set; }
    public string? SourcePack { get; set; }
    public DateTime SavedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    [JsonIgnore] public string Folder { get; set; } = "";
    [JsonIgnore] public LibraryKind LibraryKind => Kind == "Clip" ? LibraryKind.Clip : LibraryKind.Pose;
    [JsonIgnore] public string? Thumbnail => File.Exists(Path.Combine(Folder, "thumbnail.png")) ? Path.Combine(Folder, "thumbnail.png") : null;
}

/// <summary>
/// The designer's own library across projects, in %APPDATA%\SDCharacterStudio\Library (doc 03 §17.3). Each item is a
/// folder laid out like a tiny pack (manifest, studio.json, library files, art), holding the item and everything it
/// needs: its art, masks, rigs, and for animations the poses and expressions they play. The runtime never reads it.
/// </summary>
public sealed class PersonalLibrary
{
    public PersonalLibrary(string? root = null) => Root = root ?? Path.Combine(EditorSettings.AppDataFolder, "Library");
    public string Root { get; }

    private static string KindFolder(LibraryKind kind) => kind == LibraryKind.Clip ? "clips" : "poses";

    public IReadOnlyList<LibraryEntry> Entries()
    {
        var list = new List<LibraryEntry>();
        foreach (var kind in new[] { LibraryKind.Pose, LibraryKind.Clip })
        {
            string folder = Path.Combine(Root, KindFolder(kind));
            if (!Directory.Exists(folder)) continue;
            foreach (var item in Directory.EnumerateDirectories(folder))
            {
                try
                {
                    var entry = JsonConvert.DeserializeObject<LibraryEntry>(File.ReadAllText(Path.Combine(item, "item.json")));
                    if (entry == null) continue;
                    entry.Folder = item;
                    list.Add(entry);
                }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
            }
        }
        return list.OrderBy(e => e.Kind).ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public LibraryEntry? Find(LibraryKind kind, string id) => Entries().FirstOrDefault(e => e.LibraryKind == kind && string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));

    // ---------------- saving an item out of a pack ----------------

    /// <summary>Save (or replace) a pack item and what it needs. <paramref name="thumbnail"/> is a PNG.</summary>
    public LibraryEntry Save(PackLibrary from, LibraryKind kind, string id, string? sourcePack, byte[]? thumbnail = null, string? title = null)
    {
        var bundle = Collect(from, kind, id);
        string personalId = PackDocument.ToContentId(id);
        string folder = Path.Combine(Root, KindFolder(kind), personalId);
        var previous = File.Exists(Path.Combine(folder, "item.json")) ? JsonConvert.DeserializeObject<LibraryEntry>(File.ReadAllText(Path.Combine(folder, "item.json"))) : null;
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(folder);
        void Write(string path, string text) { string full = Path.Combine(folder, path.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(full)!); File.WriteAllText(full, text); }
        Write("manifest.json", PackWriter.Serialize(new Manifest { Name = title ?? id, Author = "SDCharacterStudio library", Version = "1.0.0", UniqueID = "SDCS.Library." + personalId,
            ContentPackFor = new ContentPackTarget { UniqueID = Ids.RuntimeModId } }));
        Write("studio.json", PackWriter.Serialize(new StudioIndex { Format = "1.0", Pack = new PackInfo { DisplayName = title ?? id } }));
        foreach (var pose in bundle.Poses) Write($"library/poses/{pose.Id}.json", PackWriter.Serialize(pose));
        foreach (var clip in bundle.Clips) Write($"library/clips/{clip.Id}.json", PackWriter.Serialize(clip));
        foreach (var rig in bundle.Rigs) Write($"library/characters/{rig.Id}.json", PackWriter.Serialize(rig));
        if (bundle.Masks.Count > 0) Write("library/masks.json", PackWriter.Serialize(new MaskLibrary { Format = "1.0", Masks = bundle.Masks }));
        if (bundle.Expressions.Count > 0) Write("library/expressions.json", PackWriter.Serialize(new ExpressionLibrary { Format = "1.0", Expressions = bundle.Expressions }));
        foreach (var (path, bytes) in bundle.Files)
        {
            string full = Path.Combine(folder, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, bytes);
        }
        if (thumbnail != null) File.WriteAllBytes(Path.Combine(folder, "thumbnail.png"), thumbnail);
        var now = DateTime.UtcNow;
        var main = kind == LibraryKind.Clip ? (object?)bundle.Clips.FirstOrDefault() : bundle.Poses.FirstOrDefault();
        var entry = new LibraryEntry
        {
            Kind = kind == LibraryKind.Clip ? "Clip" : "Pose", Id = personalId, Item = id,
            Title = title ?? (main as LibraryClip)?.Title ?? id, Tags = (main as LibraryClip)?.Tags.ToList() ?? new(),
            Detail = kind == LibraryKind.Clip ? $"{bundle.Clips[0].Duration} ms · {string.Join(", ", bundle.Clips[0].Roles.Select(r => r.Id))} · {bundle.Poses.Count} pose(s)"
                : $"{bundle.Poses[0].Kind} · {bundle.Poses[0].Frames.Count} frame(s)" + (bundle.Poses[0].For != null ? " · " + bundle.Poses[0].For : ""),
            SourcePack = sourcePack, SavedAt = previous?.SavedAt ?? now, UpdatedAt = now, Folder = folder
        };
        File.WriteAllText(Path.Combine(folder, "item.json"), JsonConvert.SerializeObject(entry, Formatting.Indented));
        return entry;
    }

    public void Delete(LibraryEntry entry) { if (Directory.Exists(entry.Folder)) Directory.Delete(entry.Folder, recursive: true); }

    /// <summary>An item and everything it needs, read from a pack.</summary>
    public sealed class Bundle
    {
        public List<Pose> Poses { get; } = new();
        public List<LibraryClip> Clips { get; } = new();
        public List<CharacterRig> Rigs { get; } = new();
        public List<Mask> Masks { get; } = new();
        public List<Expression> Expressions { get; } = new();
        public Dictionary<string, byte[]> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public static Bundle Collect(PackLibrary library, LibraryKind kind, string id)
    {
        var bundle = new Bundle();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void File_(string? path) { if (path != null && seen.Add("File:" + path) && library.ReadBytes(path) is { } bytes) bundle.Files[path] = bytes; }
        void Mask(string? maskId)
        {
            if (maskId == null || !seen.Add("Mask:" + maskId) || library.Mask(maskId) is not { } mask) return;
            bundle.Masks.Add(mask);
            File_(mask.Image);
            foreach (var path in mask.PerFrame.Values) File_(path);
        }
        void Rig(string? rigId)
        {
            if (rigId == null || !seen.Add("Rig:" + rigId) || library.Rig(rigId) is not { } rig) return;
            bundle.Rigs.Add(rig);
            foreach (var maskId in rig.HeadMask.Values) Mask(maskId);
            Mask(rig.Portrait?.HeadMask);
            foreach (var path in rig.BodyMap?.Values ?? Enumerable.Empty<string>()) File_(path);
        }
        void Slots(Dictionary<string, string>? slots) { foreach (var source in slots?.Values ?? Enumerable.Empty<string>()) if (source.Split('.') is { Length: 2 } parts && parts[0] != "Farmer") Rig(parts[0]); }
        void Pose(string? poseId)
        {
            if (poseId == null || !seen.Add("Pose:" + poseId) || library.Pose(poseId) is not { } pose) return;
            bundle.Poses.Add(pose);
            Rig(pose.For);
            foreach (var layer in pose.Frames.SelectMany(f => f.Layers))
            {
                Mask(layer.Mask);
                Slots(layer.ColorSlots);
                if (layer is LiveLayer { Source: { } worn } && Core.Compositing.Outfits.IsOutfitSource(worn)) Pose(worn.Outfit);   // the outfit it wears comes along
                if (layer is LiveLayer { Recolor: { } recolor }) Slots(recolor.SelectMany(r => new[] { r.From, r.To }).OfType<string>().Distinct().ToDictionary(s => s, s => s));   // her samples it recolours
                switch (layer)
                {
                    case PaintLayer paint: File_(paint.Image); break;
                    case ImageLayer image: File_(image.Image); break;
                    case LiveLayer { Map: { } map }: File_(map); break;
                    case EffectLayer effect when library.Pose(effect.Effect)?.Kind == "effect": Pose(effect.Effect); break;
                }
            }
        }
        void Expression(string? expressionId)
        {
            if (expressionId == null || !seen.Add("Expression:" + expressionId) || library.Expressions().Expressions.FirstOrDefault(e => string.Equals(e.Id, expressionId, StringComparison.OrdinalIgnoreCase)) is not { } expression) return;
            bundle.Expressions.Add(expression);
            Rig(expression.For);
            Slots(expression.ColorSlots);
            foreach (var frame in expression.Frames) File_(frame.Image);
        }
        if (kind == LibraryKind.Clip)
        {
            var clip = library.Clip(id) ?? throw new ArgumentException($"No animation '{id}'.");
            bundle.Clips.Add(clip);
            foreach (var track in clip.Tracks)
            {
                foreach (var key in track.Pose ?? new()) Pose(key.Pose);
                foreach (var key in track.Expression ?? new()) Expression(key.Expression);
                foreach (var key in track.Effect ?? new()) if (library.Pose(key.Effect)?.Kind == "effect") Pose(key.Effect);
            }
        }
        else
        {
            if (library.Pose(id) == null) throw new ArgumentException($"No pose '{id}'.");
            Pose(id);
        }
        return bundle;
    }

    // ---------------- using an item in a pack ----------------

    /// <summary>The bundle of a library item, read like a pack.</summary>
    public static (Bundle Bundle, LoadedPack Pack) Read(LibraryEntry entry)
    {
        var files = new DiskPackFileSystem(entry.Folder);
        var pack = new PackReader(files, new PackMigrator()).Read(exportValidation: false).Pack;
        var bundle = new Bundle();
        bundle.Poses.AddRange(pack.Poses.Select(p => p.Value));
        bundle.Clips.AddRange(pack.Clips.Select(c => c.Value));
        bundle.Rigs.AddRange(pack.Characters.Select(c => c.Value));
        bundle.Masks.AddRange(pack.Masks?.Value.Masks ?? new());
        bundle.Expressions.AddRange(pack.Expressions?.Value.Expressions ?? new());
        foreach (var file in Directory.EnumerateFiles(entry.Folder, "*.png", SearchOption.AllDirectories))
        {
            string path = Path.GetRelativePath(entry.Folder, file).Replace('\\', '/');
            if (path == "thumbnail.png") continue;
            bundle.Files[path] = File.ReadAllBytes(file);
        }
        return (bundle, pack);
    }

    /// <summary>
    /// Copy a library item into a pack (inside an edit). Items the pack already has unchanged are reused; ID or path
    /// clashes get new names, and every reference follows. With <paramref name="update"/>, the items that link made are
    /// overwritten in place (Update from library). Returns the item's ID in the pack and the link to record.
    /// </summary>
    public static (string Id, LibraryLink Link) Use(LibraryEntry entry, PackLibrary into, LibraryLink? update = null)
    {
        var (bundle, _) = Read(entry);
        var link = new LibraryLink { Kind = entry.Kind, Source = entry.Id, Version = entry.UpdatedAt };
        string? Linked(string key) => update?.Items.GetValueOrDefault(key);
        bool Same(object a, object? b) => b != null && PackWriter.Serialize(a) == PackWriter.Serialize(b);

        // Files: same bytes → reuse; clash → a new path.
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, bytes) in bundle.Files)
        {
            string target = Linked("File:" + path) ?? path;
            if (Linked("File:" + path) == null && into.ReadBytes(path) is { } existing)
                target = existing.AsSpan().SequenceEqual(bytes) ? path : into.UniquePath(path);
            if (into.ReadBytes(target) is not { } current || !current.AsSpan().SequenceEqual(bytes)) into.WriteBytes(target, bytes);
            files[path] = target;
            link.Items["File:" + path] = target;
        }
        string? F(string? path) => path == null ? null : files.GetValueOrDefault(path, path);

        // Masks.
        var masks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in bundle.Masks.Where(m => m.Id != null))
        {
            var mask = PackWriter.Clone(source);
            mask.Image = F(mask.Image);
            mask.PerFrame = mask.PerFrame.ToDictionary(p => p.Key, p => F(p.Value)!);
            string id = Linked("Mask:" + source.Id) ?? Free(source.Id!, mask, i => into.Mask(i), i => into.Mask(i) == null);
            masks[source.Id!] = id;
            mask.Id = id;
            into.SaveMask(mask);
            link.Items["Mask:" + source.Id] = id;
        }
        string? M(string? id) => id == null ? null : masks.GetValueOrDefault(id, id);

        // Rigs: one rig per character, so a pack that already has one for that character keeps it.
        var rigs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in bundle.Rigs.Where(r => r.Id != null))
        {
            var rig = PackWriter.Clone(source);
            rig.HeadMask = rig.HeadMask.ToDictionary(p => p.Key, p => M(p.Value)!, StringComparer.OrdinalIgnoreCase);
            if (rig.Portrait != null) rig.Portrait.HeadMask = M(rig.Portrait.HeadMask);
            if (rig.BodyMap != null) rig.BodyMap = rig.BodyMap.ToDictionary(p => p.Key, p => F(p.Value)!, StringComparer.OrdinalIgnoreCase);
            string? id = Linked("Rig:" + source.Id);
            if (id == null)
            {
                var existing = rig.Kind == "Farmer" ? into.FarmerRig() : into.RigFor(rig.Npc);
                if (existing?.Id != null && (string.Equals(existing.Npc, rig.Npc, StringComparison.OrdinalIgnoreCase) || rig.Kind == "Farmer")) { rigs[source.Id!] = existing.Id; link.Items["Rig:" + source.Id] = existing.Id; continue; }
                id = Free(source.Id!, rig, i => into.Rig(i), i => into.Rig(i) == null);
            }
            rigs[source.Id!] = id;
            rig.Id = id;
            into.SaveRig(rig);
            link.Items["Rig:" + source.Id] = id;
        }
        string? R(string? id) => id == null ? null : rigs.GetValueOrDefault(id, id);
        Dictionary<string, string>? Slots(Dictionary<string, string>? slots) => slots?.ToDictionary(p => p.Key, p => Source(p.Value)!, StringComparer.OrdinalIgnoreCase);
        string? Source(string? source) => source?.Split('.') is { Length: 2 } parts && parts[0] != "Farmer" ? $"{R(parts[0])}.{parts[1]}" : source;

        // Expressions.
        var expressions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in bundle.Expressions.Where(e => e.Id != null))
        {
            var expression = PackWriter.Clone(source);
            expression.For = R(expression.For);
            expression.ColorSlots = Slots(expression.ColorSlots);
            foreach (var frame in expression.Frames) frame.Image = F(frame.Image);
            string id = Linked("Expression:" + source.Id) ?? Free(source.Id!, expression,
                i => into.Expressions().Expressions.FirstOrDefault(e => string.Equals(e.Id, i, StringComparison.OrdinalIgnoreCase)),
                i => !into.Expressions().Expressions.Any(e => string.Equals(e.Id, i, StringComparison.OrdinalIgnoreCase)));
            expressions[source.Id!] = id;
            expression.Id = id;
            into.SaveExpression(expression);
            link.Items["Expression:" + source.Id] = id;
        }

        // Poses: effects first, since other poses' effect layers name them; then outfits (an outfit made from another after
        // it), since poses wear them.
        var poses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int Depth(Pose pose, int guard = 0) => guard > 16 ? 0 : Core.Compositing.Outfits.Worn(pose)
            .Select(id => bundle.Poses.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) is { } worn ? 1 + Depth(worn, guard + 1) : 0).DefaultIfEmpty(0).Max();
        foreach (var source in bundle.Poses.Where(p => p.Id != null).OrderBy(p => p.Kind == "effect" ? 0 : p.Kind == "sheet" ? 1 : 2).ThenBy(p => Depth(p)))
        {
            var pose = PackWriter.Clone(source);
            pose.For = R(pose.For);
            foreach (var layer in pose.Frames.SelectMany(f => f.Layers))
            {
                layer.Mask = M(layer.Mask);
                layer.ColorSlots = Slots(layer.ColorSlots);
                if (layer is LiveLayer { Recolor: { } recolor })
                    foreach (var rule in recolor) { rule.From = Source(rule.From); rule.To = Source(rule.To); }
                if (layer is LiveLayer { Source: { Outfit: { } outfit } worn } && Core.Compositing.Outfits.IsOutfitSource(worn)) worn.Outfit = poses.GetValueOrDefault(outfit, outfit);
                switch (layer)
                {
                    case PaintLayer paint: paint.Image = F(paint.Image); break;
                    case ImageLayer image: image.Image = F(image.Image); break;
                    case LiveLayer { Map: not null } live: live.Map = F(live.Map); break;
                    case EffectLayer effect when effect.Effect != null: effect.Effect = poses.GetValueOrDefault(effect.Effect, effect.Effect); break;
                }
            }
            string id = Linked("Pose:" + source.Id) ?? Free(source.Id!, pose, i => into.Pose(i), i => !into.Exists(LibraryKind.Pose, i));
            poses[source.Id!] = id;
            pose.Id = id;
            into.SavePose(pose);
            link.Items["Pose:" + source.Id] = id;
        }

        // Clips.
        string? main = null;
        foreach (var source in bundle.Clips.Where(c => c.Id != null))
        {
            var clip = PackWriter.Clone(source);
            foreach (var track in clip.Tracks)
            {
                foreach (var key in track.Pose ?? new()) if (key.Pose != null) key.Pose = poses.GetValueOrDefault(key.Pose, key.Pose);
                foreach (var key in track.Expression ?? new()) if (key.Expression != null) key.Expression = expressions.GetValueOrDefault(key.Expression, key.Expression);
                foreach (var key in track.Effect ?? new()) if (key.Effect != null) key.Effect = poses.GetValueOrDefault(key.Effect, key.Effect);
            }
            string id = Linked("Clip:" + source.Id) ?? Free(source.Id!, clip, i => into.Clip(i), i => !into.Exists(LibraryKind.Clip, i));
            clip.Id = id;
            into.SaveClip(clip);
            link.Items["Clip:" + source.Id] = id;
            if (string.Equals(source.Id, entry.Item, StringComparison.OrdinalIgnoreCase) || bundle.Clips.Count == 1) main = id;
        }
        if (entry.LibraryKind == LibraryKind.Pose) main = poses.GetValueOrDefault(entry.Item) ?? poses.Values.FirstOrDefault();
        return (main ?? throw new InvalidDataException($"The library item '{entry.Id}' is empty."), link);

        // The wanted ID when it's free or the pack's item is identical; otherwise wanted-2, wanted-3…
        string Free<T>(string wanted, T value, Func<string, T?> existing, Func<string, bool> isFree) where T : class
        {
            var current = existing(wanted);
            if (current == null) return wanted;
            var copy = PackWriter.Clone(value);
            SetId(copy, wanted);
            if (Same(copy, current)) return wanted;
            for (int n = 2; ; n++) if (isFree($"{wanted}-{n}")) return $"{wanted}-{n}";
        }
        static void SetId(object value, string id)
        {
            switch (value)
            {
                case NamedObject named: named.Id = id; break;
                case Mask mask: mask.Id = id; break;
                case Expression expression: expression.Id = id; break;
            }
        }
    }
}
