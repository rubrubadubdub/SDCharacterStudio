using System.IO;
using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Runtime.Rendering;

namespace SDCharacterStudio.Editor.Studio;

/// <summary>
/// The editor's side of doc 05 §1: resolves layer sources from the Bridge export and the pack, mirroring
/// <c>RuntimeLayerSourceResolver</c> rule for rule (rig selection, default head masks, source facing and frame, neck
/// anchors). Masks, colour slots, anchors and blending stay in Core's <see cref="Compositor"/>. Unlike the runtime it
/// never throws for missing art: it draws a visible placeholder and reports what's wrong, so the designer can keep working.
/// </summary>
public sealed class EditorLayerSourceResolver : ILayerSourceResolver
{
    private readonly StudioContext context;
    private readonly Pose pose;
    private readonly string actorFacing;
    private readonly ICollection<string> problems;

    public EditorLayerSourceResolver(StudioContext context, Pose pose, string actorFacing, ICollection<string> problems)
    { this.context = context; this.pose = pose; this.actorFacing = actorFacing; this.problems = problems; }

    private IEnumerable<CharacterRig> Rigs => context.Library.Characters.Select(x => x.Value);

    /// <summary>Runtime normalisation (head masks from the rig) plus editor safety: drops masks and anchors that can't resolve.</summary>
    public PoseFrame Normalize(PoseFrame frame, Func<Layer, bool>? include = null)
    {
        var layers = new List<Layer>(frame.Layers.Count);
        foreach (var source in frame.Layers)
        {
            if (source == null || (include != null && !include(source))) continue;
            var layer = Clone(source);
            if (layer is LiveLayer live && live.Visible && live.Part.Equals("Head", StringComparison.OrdinalIgnoreCase) && live.Mask == null && !Same(live.Source?.Kind, "Farmer"))
            {
                var rig = TryRig(live);
                if (rig != null)
                {
                    var facing = SourceFacing(live, rig);
                    live.Mask = IsPortrait(live) ? rig.Portrait?.HeadMask : rig.HeadMask.TryGetValue(facing, out var m) ? m : null;
                    if (live.Mask == null) Problem($"Rig '{rig.Id}' has no head mask for '{facing}'. Make one in the rig or mask editor.");
                }
            }
            if (layer.Mask != null && context.FindMask(layer.Mask) is not { Image: not null })
            {
                Problem($"Mask '{layer.Mask}' isn't in the pack yet.");
                layer.Mask = null;
            }
            if (layer.AnchorTo?.Anchor is { } anchor && !frame.Anchors.Keys.Contains(anchor, StringComparer.OrdinalIgnoreCase))
            {
                Problem($"This frame has no '{anchor}' anchor. Place it with the anchor tool.");
                layer.AnchorTo = null;
            }
            layers.Add(layer);
        }
        return new PoseFrame { Duration = frame.Duration, Layers = layers, Anchors = frame.Anchors, Sound = frame.Sound, Dust = frame.Dust };
    }

    public ResolvedLayerSource Resolve(Layer layer)
    {
        try
        {
            return layer switch
            {
                ImageLayer image => ResolveImage(image),
                PaintLayer paint => ResolvePaint(paint),
                EffectLayer => new(new RgbaImage(1, 1, new uint[1])),
                LiveLayer live => ResolveLive(live),
                _ => throw new NotSupportedException($"Layer type '{layer.Type}' cannot be drawn.")
            };
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or InvalidOperationException or IOException)
        {
            Problem(ex.Message);
            var size = pose.FrameSize is { Length: 2 } s ? s : new[] { 16, 32 };
            return new(StudioImages.Missing(size[0], size[1]));
        }
    }

    public Mask GetMask(string id) => context.FindMask(id) ?? throw new ArgumentException($"No mask named '{id}'.");

    public RgbaImage ResolveMaskImage(string path) => context.PackImage(path) ?? throw new ArgumentException($"Mask image '{path}' is missing.");

    /// <summary>The live frame colour samples read (ColorSlotResolver), like the runtime's SampleFrame.</summary>
    public RgbaImage SampleFrame(CharacterRig rig, int index)
    {
        if (rig.Kind.Equals("Farmer", StringComparison.OrdinalIgnoreCase))
            return context.Farmer?.Render(index, 2, FarmerPart.Full) ?? throw new ArgumentException("The game export has no player.");
        var sheet = context.NpcTexture(rig.Npc ?? rig.Id ?? "", false) ?? throw new ArgumentException($"The game export has no sprite for '{rig.Npc}'.");
        return StudioImages.Frame(sheet, rig.FrameSize[0], rig.FrameSize[1], index) ?? throw new ArgumentException($"'{rig.Npc}' has no frame {index}.");
    }

    private ResolvedLayerSource ResolveImage(ImageLayer image)
    {
        var size = image.FrameSize ?? pose.FrameSize ?? throw new ArgumentException("Image layer needs a frame size.");
        var sheet = context.PackImage(image.Image) ?? throw new ArgumentException($"Image '{image.Image}' is missing.");
        return new(StudioImages.Frame(sheet, size[0], size[1], image.Frame) ?? throw new ArgumentException($"Image '{image.Image}' has no frame {image.Frame}."), image.Frame);
    }

    private ResolvedLayerSource ResolvePaint(PaintLayer paint)
    {
        var pixels = context.PackImage(paint.Image) ?? throw new ArgumentException($"Paint image '{paint.Image}' is missing.");
        if (pose.FrameSize is not { Length: 2 } size || pixels.Width != size[0] || pixels.Height != size[1])
            throw new ArgumentException("Paint image dimensions must match the pose frame size.");
        return new(pixels);
    }

    private ResolvedLayerSource ResolveLive(LiveLayer live)
    {
        var spec = live.Source ?? throw new ArgumentException("Live layer has no source.");
        if (Same(spec.Kind, "Farmer"))
        {
            var farmer = context.Farmer ?? throw new ArgumentException("The game export has no player yet. Load a save with the Bridge running.");
            string facingName = live.SourceFrame?.Facing ?? pose.Facing ?? actorFacing;
            if (!Core.Ids.Facings.TryGetValue(facingName, out int direction)) throw new ArgumentException("Unknown farmer facing.");
            int frame = live.SourceFrame?.Index ?? direction switch { 0 => 12, 1 or 3 => 6, _ => 0 };
            var parts = FarmerParts.Parse(live.Part);
            return new(farmer.Render(frame, direction, parts, problems), frame, FarmerExport.Neck(frame, direction));
        }
        if (Same(spec.Kind, "Asset"))
        {
            var texture = context.AssetTexture(spec.Asset ?? throw new ArgumentException("Asset layer has no asset name.")) ?? throw new ArgumentException($"The game export has no texture '{spec.Asset}'.");
            var rect = spec.Rect is { Length: 4 } r ? r : throw new ArgumentException("Asset layer requires a source rectangle.");
            return new(StudioImages.Crop(texture, rect[0], rect[1], rect[2], rect[3]));
        }
        if (!Same(spec.Kind, "Npc") && !Same(spec.Kind, "NpcSheet") && !Same(spec.Kind, "Portrait") && !Same(spec.Kind, "PortraitSheet"))
            throw new NotSupportedException($"Live source '{spec.Kind}' cannot be drawn.");
        if (!Same(live.Part, "Full") && !Same(live.Part, "Head") && !Same(live.Part, "Mask"))
            throw new NotSupportedException($"NPC part '{live.Part}' isn't available; NPC parts are Full, Head or Mask.");
        var rig = TryRig(live) ?? throw new ArgumentException($"No rig for '{spec.Npc ?? pose.For}' yet. Create one in the Character Studio (Rig).");
        var facing = SourceFacing(live, rig);
        bool portrait = IsPortrait(live);
        int index = live.SourceFrame?.Index ?? (portrait ? 0 : rig.Frames.TryGetValue(facing, out var frames) ? frames.Stand
            : throw new ArgumentException($"Rig '{rig.Id}' has no frames for '{facing}'."));
        RgbaImage sheet = Same(spec.Kind, "Npc") || Same(spec.Kind, "Portrait")
            ? context.NpcTexture(spec.Npc ?? rig.Npc ?? "", portrait) ?? throw new ArgumentException($"The game export has no {(portrait ? "portrait" : "sprite")} for '{spec.Npc ?? rig.Npc}'.")
            : context.AssetTexture(spec.Asset ?? throw new ArgumentException("Sheet source has no asset name.")) ?? throw new ArgumentException($"The game export has no texture '{spec.Asset}'.");
        var size = portrait ? rig.Portrait?.FrameSize ?? new[] { 64, 64 } : rig.FrameSize;
        var image = StudioImages.Frame(sheet, size[0], size[1], index) ?? throw new ArgumentException($"Source frame {index} is outside the sheet.");
        var neck = portrait ? rig.Portrait?.NeckPoint : rig.NeckPoint.TryGetValue(facing, out var point) ? point : null;
        return new(image, index, neck is { Length: 2 } ? new PixelPoint(neck[0], neck[1]) : null);
    }

    private CharacterRig? TryRig(LiveLayer layer)
    {
        try { return RigSelection.Select(Rigs, pose.For, layer.Source?.Npc); }
        catch (ArgumentException) { return null; }
    }

    private string SourceFacing(LiveLayer layer, CharacterRig rig)
    {
        if (layer.SourceFrame?.Facing is { } facing) return facing;
        if (layer.SourceFrame?.Index is { } index)
        {
            var match = rig.Frames.FirstOrDefault(x => x.Value.Stand == index || x.Value.Walk.Contains(index));
            if (match.Key != null) return match.Key;
        }
        return pose.Facing ?? actorFacing;
    }

    private void Problem(string message) { if (!problems.Contains(message)) problems.Add(message); }

    private static Layer Clone(Layer layer) => Core.Serialization.PackJson.Deserialize<Layer>(Core.Serialization.PackJson.Serialize(layer))!;
    private static bool IsPortrait(LiveLayer layer) => Same(layer.Source?.Kind, "Portrait") || Same(layer.Source?.Kind, "PortraitSheet");
    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
