using System.Text.RegularExpressions;
using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Loading;
using SDCharacterStudio.Core.Models;

namespace SDCharacterStudio.Editor.Studio;

public enum LibraryKind { Pose, Clip, Mask, Rig, Expression, Portrait, Costume }

public sealed record LibraryItem(LibraryKind Kind, string Id, string File, string Title, string? Detail);

/// <summary>
/// Reads and writes the pack's library (doc 03 §8–§13, §17) through the document's asset store, so every change is one
/// undo step, autosaved, validated and previewed before it's saved. Readers return copies: change them, then Save.
/// Writers must run inside <see cref="PackDocument.Edit"/>.
/// </summary>
public sealed class PackLibrary
{
    private readonly PackDocument doc;
    public PackLibrary(PackDocument doc) => this.doc = doc;
    public LoadedPack Pack => doc.Library;

    // ---------------- reading ----------------

    public IEnumerable<LibraryItem> Items(LibraryKind kind) => kind switch
    {
        LibraryKind.Pose => Pack.Poses.Where(p => p.Value.Id != null).Select(p => new LibraryItem(kind, p.Value.Id!, p.File, p.Value.Id!, $"{(p.Value.Kind == "sheet" ? "outfit" : p.Value.Kind)} · {p.Value.Frames.Count} frame{(p.Value.Frames.Count == 1 ? "" : "s")}" + (p.Value.For != null ? " · " + p.Value.For : ""))),
        LibraryKind.Clip => Pack.Clips.Where(c => c.Value.Id != null).Select(c => new LibraryItem(kind, c.Value.Id!, c.File, string.IsNullOrWhiteSpace(c.Value.Title) ? c.Value.Id! : c.Value.Title!, $"{c.Value.Duration} ms · " + string.Join(", ", c.Value.Roles.Select(r => r.Id)))),
        LibraryKind.Mask => (Pack.Masks?.Value.Masks ?? new()).Where(m => m?.Id != null).Select(m => new LibraryItem(kind, m.Id!, Pack.Masks!.File, string.IsNullOrWhiteSpace(m.DisplayName) ? m.Id! : m.DisplayName!, string.Join(", ", m.Tags))),
        LibraryKind.Rig => Pack.Characters.Where(r => r.Value.Id != null).Select(r => new LibraryItem(kind, r.Value.Id!, r.File, r.Value.Id!, r.Value.Kind == "Farmer" ? "player" : r.Value.Npc)),
        LibraryKind.Expression => (Pack.Expressions?.Value.Expressions ?? new()).Where(e => e?.Id != null).Select(e => new LibraryItem(kind, e.Id!, Pack.Expressions!.File, e.Id!, $"{e.Target}{(e.Facing != null ? " " + e.Facing : "")} · {e.For}")),
        LibraryKind.Portrait => Pack.Portraits.Where(p => p.Value.Id != null).Select(p => new LibraryItem(kind, p.Value.Id!, p.File, p.Value.Id!, string.Join(", ", p.Value.Emotions.Keys))),
        LibraryKind.Costume => (Pack.Costumes?.Value.Costumes ?? new()).Where(c => c?.Id != null).Select(c => new LibraryItem(kind, c.Id!, Pack.Costumes!.File, c.Id!, string.Join(", ", c.Set.Keys))),
        _ => Enumerable.Empty<LibraryItem>()
    };

    public Pose? Pose(string? id) => Copy(Pack.Poses.FirstOrDefault(p => Same(p.Value.Id, id))?.Value);
    public LibraryClip? Clip(string? id) => Copy(Pack.Clips.FirstOrDefault(c => Same(c.Value.Id, id))?.Value);
    public Portrait? Portrait(string? id) => Copy(Pack.Portraits.FirstOrDefault(p => Same(p.Value.Id, id))?.Value);
    public CharacterRig? Rig(string? id) => Copy(Pack.Characters.FirstOrDefault(r => Same(r.Value.Id, id))?.Value);
    public CharacterRig? RigFor(string? npc) => Copy(Pack.Characters.Select(r => r.Value).FirstOrDefault(r => Same(r.Npc, npc) || Same(r.Id, npc)));
    public CharacterRig? FarmerRig() => Copy(Pack.Characters.Select(r => r.Value).FirstOrDefault(r => Same(r.Kind, "Farmer")));
    public MaskLibrary Masks() => Copy(Pack.Masks?.Value) ?? new MaskLibrary { Format = "1.0" };
    public Mask? Mask(string? id) => Masks().Masks.FirstOrDefault(m => Same(m?.Id, id));
    public ExpressionLibrary Expressions() => Copy(Pack.Expressions?.Value) ?? new ExpressionLibrary { Format = "1.0" };
    public CostumeLibrary Costumes() => Copy(Pack.Costumes?.Value) ?? new CostumeLibrary { Format = "1.0" };

    public string? FileOf(LibraryKind kind, string id) => kind switch
    {
        LibraryKind.Pose => Pack.Poses.FirstOrDefault(p => Same(p.Value.Id, id))?.File,
        LibraryKind.Clip => Pack.Clips.FirstOrDefault(c => Same(c.Value.Id, id))?.File,
        LibraryKind.Rig => Pack.Characters.FirstOrDefault(r => Same(r.Value.Id, id))?.File,
        LibraryKind.Portrait => Pack.Portraits.FirstOrDefault(p => Same(p.Value.Id, id))?.File,
        LibraryKind.Mask => Pack.Masks?.File,
        LibraryKind.Expression => Pack.Expressions?.File,
        LibraryKind.Costume => Pack.Costumes?.File,
        _ => null
    };

    public bool Exists(LibraryKind kind, string id) => Items(kind).Any(i => Same(i.Id, id));

    /// <summary>A content ID not used by that kind yet: base, base-2, base-3…</summary>
    public string UniqueId(LibraryKind kind, string wanted)
    {
        string id = PackDocument.ToContentId(wanted), candidate = id;
        for (int n = 2; Exists(kind, candidate); n++) candidate = $"{id}-{n}";
        return candidate;
    }

    /// <summary>A pack path not used yet (art/x.png, art/x-2.png…).</summary>
    public string UniquePath(string wanted)
    {
        var files = doc.CreateOverlayFileSystem();
        if (!files.Exists(wanted)) return wanted;
        string stem = wanted[..wanted.LastIndexOf('.')], extension = wanted[wanted.LastIndexOf('.')..];
        for (int n = 2; ; n++) if (!files.Exists($"{stem}-{n}{extension}")) return $"{stem}-{n}{extension}";
    }

    // ---------------- writing (inside Edit) ----------------

    public void SavePose(Pose pose) => SaveFile(LibraryKind.Pose, pose.Id!, "library/poses", pose, i => i.Poses, (i, v) => i.Poses = v);
    public void SaveClip(LibraryClip clip) => SaveFile(LibraryKind.Clip, clip.Id!, "library/clips", clip, i => i.Clips, (i, v) => i.Clips = v);
    public void SaveRig(CharacterRig rig) => SaveFile(LibraryKind.Rig, rig.Id!, "library/characters", rig, i => i.Characters, (i, v) => i.Characters = v);
    public void SavePortrait(Portrait portrait) => SaveFile(LibraryKind.Portrait, portrait.Id!, "library/portraits", portrait, i => i.Portraits, (i, v) => i.Portraits = v);
    public void SaveMasks(MaskLibrary masks) { masks.Format ??= "1.0"; doc.SetTextAsset(Pack.Masks?.File ?? doc.Studio.Library?.Masks ?? "library/masks.json", PackWriter.Serialize(masks)); }
    public void SaveExpressions(ExpressionLibrary expressions) { expressions.Format ??= "1.0"; doc.SetTextAsset(Pack.Expressions?.File ?? doc.Studio.Library?.Expressions ?? "library/expressions.json", PackWriter.Serialize(expressions)); }
    public void SaveCostumes(CostumeLibrary costumes) { costumes.Format ??= "1.0"; doc.SetTextAsset(Pack.Costumes?.File ?? doc.Studio.Library?.Costumes ?? "library/costumes.json", PackWriter.Serialize(costumes)); }

    public void SaveMask(Mask mask)
    {
        var masks = Masks();
        int index = masks.Masks.FindIndex(m => Same(m?.Id, mask.Id));
        if (index >= 0) masks.Masks[index] = mask; else masks.Masks.Add(mask);
        SaveMasks(masks);
    }

    public void SaveExpression(Expression expression)
    {
        var expressions = Expressions();
        int index = expressions.Expressions.FindIndex(e => Same(e?.Id, expression.Id));
        if (index >= 0) expressions.Expressions[index] = expression; else expressions.Expressions.Add(expression);
        SaveExpressions(expressions);
    }

    public void Delete(LibraryKind kind, string id)
    {
        switch (kind)
        {
            case LibraryKind.Mask: var masks = Masks(); masks.Masks.RemoveAll(m => Same(m?.Id, id)); SaveMasks(masks); break;
            case LibraryKind.Expression: var expressions = Expressions(); expressions.Expressions.RemoveAll(e => Same(e?.Id, id)); SaveExpressions(expressions); break;
            case LibraryKind.Costume: var costumes = Costumes(); costumes.Costumes.RemoveAll(c => Same(c?.Id, id)); SaveCostumes(costumes); break;
            default:
                if (FileOf(kind, id) is { } file) doc.RemoveAsset(file);
                break;
        }
    }

    /// <summary>A pack image as it would be saved now, or null when missing or not a PNG.</summary>
    public RgbaImage? ReadImage(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        try
        {
            return doc.CreateOverlayFileSystem() is OverlayPackFileSystem files && files.Exists(path) ? StudioImages.Decode(files.ReadBytes(path)) : null;
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.IO.InvalidDataException or ArgumentException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Write a pack image (PNG) as it will be saved.</summary>
    public void WriteImage(string path, RgbaImage image) => doc.SetBinaryAsset(path, StudioImages.Encode(image));

    /// <summary>A pack file's bytes as they would be saved now, or null when missing.</summary>
    public byte[]? ReadBytes(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        try { return doc.CreateOverlayFileSystem() is OverlayPackFileSystem files && files.Exists(path) ? files.ReadBytes(path) : null; }
        catch (Exception ex) when (ex is System.IO.IOException or ArgumentException or UnauthorizedAccessException) { return null; }
    }

    public void WriteBytes(string path, byte[] bytes) => doc.SetBinaryAsset(path, bytes);

    /// <summary>Remove a pack file (an image nothing uses any more).</summary>
    public void RemoveFile(string path) => doc.RemoveAsset(path);

    private void SaveFile<T>(LibraryKind kind, string id, string folder, T value, Func<LibraryIndex, List<string>?> list, Action<LibraryIndex, List<string>> setList) where T : class
    {
        string file = FileOf(kind, id) ?? $"{folder}/{PackDocument.ToContentId(id)}.json";
        if (value is VersionedObject versioned) versioned.Format ??= "1.0";
        doc.SetTextAsset(file, PackWriter.Serialize(value));
        // A pack whose studio.json lists files explicitly needs the new one listed too (defaults cover the folder).
        if (doc.Studio.Library is { } index && list(index) is { } patterns && !patterns.Any(p => Matches(p, file)))
        {
            setList(index, patterns.Append(file).ToList());
            doc.SetTextAsset(file, PackWriter.Serialize(value));   // re-read the library with the updated index
        }
    }

    private static bool Matches(string pattern, string file) =>
        Regex.IsMatch(file, "^" + Regex.Escape(pattern).Replace(@"\*\*", ".*").Replace(@"\*", "[^/]*").Replace(@"\?", "[^/]") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static T? Copy<T>(T? value) where T : class => value == null ? null : PackWriter.Clone(value);
    private static bool Same(string? a, string? b) => a != null && b != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
