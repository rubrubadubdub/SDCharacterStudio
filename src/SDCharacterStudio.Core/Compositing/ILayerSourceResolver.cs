using SDCharacterStudio.Core.Models;

namespace SDCharacterStudio.Core.Compositing;

public readonly record struct PixelPoint(int X, int Y);
/// <summary>A cropped source frame, its original sheet index, and an optional anchor in source coordinates.</summary>
public sealed record ResolvedLayerSource(RgbaImage Image, int SourceFrameIndex = 0, PixelPoint? RigAnchor = null);

/// <summary>Adapters own decoding, sheet selection and rig lookup; Core owns all subsequent pixel operations.</summary>
public interface ILayerSourceResolver
{
    /// <summary>Returns a source that may be cached and reused. Compose never modifies its pixels.</summary>
    ResolvedLayerSource Resolve(Layer layer);
    Mask GetMask(string id);
    /// <summary>Resolves the mask's chosen base or per-frame image at the source frame size.</summary>
    RgbaImage ResolveMaskImage(string path);
}
