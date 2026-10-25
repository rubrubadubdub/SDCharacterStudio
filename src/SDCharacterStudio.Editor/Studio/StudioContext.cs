using System.IO;
using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Loading;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Palette;
using SDCharacterStudio.Runtime.Rendering;

namespace SDCharacterStudio.Editor.Studio;

public sealed record RenderResult(RgbaImage Image, IReadOnlyList<string> Problems);

/// <summary>
/// Shared state for drawing pack art in the editor: the open pack (with unsaved edits), the game export, decoded image
/// caches and in-progress paint strokes. One per editor window.
/// </summary>
public sealed class StudioContext
{
    private readonly Func<PackDocument?> document;
    private readonly Func<ExportCatalog?> catalog;
    private readonly Dictionary<string, RgbaImage?> packImages = new(StringComparer.OrdinalIgnoreCase);
    private ExportCatalog? farmerCatalog;
    private FarmerExport? farmer;

    public StudioContext(Func<PackDocument?> document, Func<ExportCatalog?> catalog) { this.document = document; this.catalog = catalog; }

    public PackDocument? Document => document();
    public ExportCatalog? Catalog => catalog();
    public LoadedPack Library => Document?.Library ?? new LoadedPack();

    /// <summary>Pixels being painted that aren't committed yet (pack path → image). They win over the pack's file.</summary>
    public Dictionary<string, RgbaImage> Overrides { get; } = new(StringComparer.OrdinalIgnoreCase);

    public FarmerExport? Farmer
    {
        get
        {
            if (!ReferenceEquals(farmerCatalog, Catalog)) { farmerCatalog = Catalog; farmer = FarmerExport.From(Catalog); }
            return farmer;
        }
    }

    /// <summary>Forget decoded pack images (after edits, undo, save or reopening).</summary>
    public void InvalidatePack() => packImages.Clear();

    /// <summary>A pack PNG as it would be saved now (edits included), or null when missing.</summary>
    public RgbaImage? PackImage(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (Overrides.TryGetValue(path, out var working)) return working;
        if (packImages.TryGetValue(path, out var cached)) return cached;
        RgbaImage? image = null;
        try
        {
            if (Document is { } doc && doc.CreateOverlayFileSystem() is OverlayPackFileSystem files && files.Exists(path))
                image = StudioImages.Decode(files.ReadBytes(path));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException) { image = null; }
        packImages[path] = image;
        return image;
    }

    /// <summary>Masks being edited that aren't committed yet (id → mask). They win over the pack's masks.json.</summary>
    public Dictionary<string, Mask> MaskOverrides { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Mask? FindMask(string? id) => id == null ? null : MaskOverrides.TryGetValue(id, out var working) ? working
        : Library.Masks?.Value.Masks.FirstOrDefault(m => string.Equals(m?.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The NPC's current sprite or portrait sheet from the export (what the runtime's Npc source reads).</summary>
    /// <summary>For the fit check: draw an NPC's live sprite from another installed variant (NPC → sprite asset).</summary>
    public Dictionary<string, string> SheetOverrides { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Draw <paramref name="npc"/> from <paramref name="asset"/> until disposed, then put back whatever was there (an outfit preview).</summary>
    public IDisposable SheetOverride(string npc, string asset)
    {
        bool had = SheetOverrides.TryGetValue(npc, out var before);
        SheetOverrides[npc] = asset;
        return new Restore(() => { if (had) SheetOverrides[npc] = before!; else SheetOverrides.Remove(npc); });
    }

    private sealed class Restore(Action undo) : IDisposable { public void Dispose() => undo(); }

    public RgbaImage? NpcTexture(string npc, bool portrait)
    {
        if (!portrait && SheetOverrides.TryGetValue(npc, out var variant)) return AssetTexture(variant);
        var character = Catalog?.Character(npc);
        if (character == null) return null;
        string? asset = portrait ? character.Current?.Portrait ?? character.Portrait : character.Current?.Sprite ?? character.Sprite;
        return StudioImages.LoadFile(Catalog!.TextureFile(asset));
    }

    public RgbaImage? AssetTexture(string? asset) => asset == null ? null : StudioImages.LoadFile(Catalog?.TextureFile(asset));

    /// <summary>Colour sources (rig samples and Farmer.*) like the runtime's ColorSlotResolver; failures fall back visibly.</summary>
    public Func<string, ColorRamp> Colors(EditorLayerSourceResolver resolver, ICollection<string> problems)
    {
        var colors = new ColorSlotResolver(Library, resolver.SampleFrame, null, sample => Farmer?.Palette(sample) ?? throw new ArgumentException("The game export has no player for Farmer colours."));
        return source =>
        {
            try { return colors.Resolve(source); }
            catch (ArgumentException ex)
            {
                string message = $"Colour '{source}': {ex.Message}";
                if (!problems.Contains(message)) problems.Add(message);
                return new ColorRamp(0xFF7F007F, 0xFFBF00BF, 0xFFFF00FF);
            }
        };
    }

    /// <summary>One pose frame as the runtime would compose it.</summary>
    public RenderResult RenderFrame(Pose pose, int index, string? actorFacing = null, Func<Layer, bool>? include = null)
    {
        var problems = new List<string>();
        var size = pose.FrameSize is { Length: 2 } s && s[0] > 0 && s[1] > 0 ? s : new[] { 16, 32 };
        if (index < 0 || index >= pose.Frames.Count) return new(StudioImages.Blank(size[0], size[1]), problems);
        var resolver = new EditorLayerSourceResolver(this, pose, actorFacing ?? pose.Facing ?? "down", problems);
        try
        {
            var frame = resolver.Normalize(pose.Frames[index], include);
            var image = Compositor.Compose(size[0], size[1], frame, resolver, Colors(resolver, problems));
            return new(image, problems);
        }
        catch (ArgumentException ex)
        {
            problems.Add(ex.Message);
            return new(StudioImages.Missing(size[0], size[1]), problems);
        }
    }

    /// <summary>
    /// Where a layer of a frame sits on the canvas, as composed: the top-left of its source image, flip, the source's
    /// size and frame, and which source pixels it shows now (its mask, including a Head layer's implicit rig head mask,
    /// as a keep image). Direct editing maps a click back to the source pixel with it (D-48).
    /// </summary>
    public LayerPlacement? Placement(Pose pose, int frameIndex, Layer layer)
    {
        if (frameIndex < 0 || frameIndex >= pose.Frames.Count || layer is LiveLayer { Map: not null }) return null;   // a flat picture maps cells itself
        var problems = new List<string>();
        var resolver = new EditorLayerSourceResolver(this, pose, pose.Facing ?? "down", problems);
        try
        {
            var frame = resolver.Normalize(pose.Frames[frameIndex], l => ReferenceEquals(l, layer));
            if (frame.Layers.Count != 1) return null;
            var normalized = frame.Layers[0];
            var resolved = resolver.Resolve(normalized);
            if (problems.Count > 0) return null;   // missing art: nothing real to edit
            var (x, y) = Compositor.Place(frame, normalized, resolved);
            int width = resolved.Image.Width, height = resolved.Image.Height;
            var keep = new RgbaImage(width, height, Enumerable.Repeat(0xFF000000u, width * height).ToArray());
            if (normalized.Mask != null)
            {
                var mask = resolver.GetMask(normalized.Mask);
                string? path = mask.PerFrame.TryGetValue(resolved.SourceFrameIndex.ToString(System.Globalization.CultureInfo.InvariantCulture), out var perFrame) ? perFrame : mask.Image;
                if (path == null) return null;
                var image = resolver.ResolveMaskImage(path);
                if (image.Width != width || image.Height != height) return null;
                bool remove = string.Equals(mask.Mode, "remove", StringComparison.OrdinalIgnoreCase);
                for (int i = 0; i < keep.Pixels.Length; i++) if ((image.Pixels[i] >> 24 != 0) == remove) keep.Pixels[i] = 0;
            }
            return new LayerPlacement(x, y, normalized.Flip, width, height, resolved.SourceFrameIndex, keep);
        }
        catch (ArgumentException) { return null; }
    }

    /// <summary>Just one layer of a frame, composed alone (layer thumbnails, mask editing).</summary>
    public RenderResult RenderLayer(Pose pose, int index, Layer layer) => RenderFrame(pose, index, null, l => ReferenceEquals(l, layer));
}

/// <summary>A layer's placement on the canvas (see <see cref="StudioContext.Placement"/>).</summary>
public sealed record LayerPlacement(long X, long Y, bool Flip, int SourceWidth, int SourceHeight, int SourceFrameIndex, RgbaImage Keep)
{
    /// <summary>Canvas pixel → source pixel, or null outside the source image.</summary>
    public (int X, int Y)? ToSource(int canvasX, int canvasY)
    {
        long sx = canvasX - X, sy = canvasY - Y;
        if (Flip) sx = SourceWidth - 1 - sx;
        return sx < 0 || sy < 0 || sx >= SourceWidth || sy >= SourceHeight ? null : ((int)sx, (int)sy);
    }
}
