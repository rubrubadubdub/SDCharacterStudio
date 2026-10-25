using System.Globalization;
using SDCharacterStudio.Core.Models;

namespace SDCharacterStudio.Core.Compositing;

/// <summary>Maps portrait emotion stacks to a vanilla two-column sheet.</summary>
public static class PortraitLayout
{
    // 128 cells produce at most a 128x4096 texture (2 MiB of RGBA pixels).
    // Validate before allocation so an arbitrary numeric emotion cannot exhaust memory.
    public const int MaximumEmotionIndex = 127;

    private static readonly Dictionary<string, int> NamedIndices = new(StringComparer.OrdinalIgnoreCase)
    {
        ["neutral"] = 0, ["happy"] = 1, ["sad"] = 2, ["unique"] = 3, ["love"] = 4, ["angry"] = 5
    };

    /// <summary>
    /// Builds a sheet pose without changing the portrait model. Every physical cell is populated;
    /// missing emotions reuse neutral's complete stack and source indices. Neutral (or numeric 0)
    /// is required, and dimensions must match vanilla's 64x64 portrait contract.
    /// </summary>
    public static Pose Create(Portrait portrait)
    {
        ArgumentNullException.ThrowIfNull(portrait);
        if (portrait.FrameSize is not { Length: 2 } size || size[0] != 64 || size[1] != 64)
            throw new ArgumentException($"Portrait '{portrait.Id}' requires 64x64 frames.", nameof(portrait));
        if (portrait.Emotions is null)
            throw new ArgumentException($"Portrait '{portrait.Id}' requires a neutral emotion.", nameof(portrait));

        var emotions = new Dictionary<int, PortraitFrame>();
        foreach (var pair in portrait.Emotions)
        {
            if (!NamedIndices.TryGetValue(pair.Key, out var index)
                && !int.TryParse(pair.Key, NumberStyles.None, CultureInfo.InvariantCulture, out index))
                throw new ArgumentException($"Portrait '{portrait.Id}' has unknown emotion '{pair.Key}'.", nameof(portrait));
            if (index < 0 || index > MaximumEmotionIndex)
                throw new ArgumentException($"Portrait '{portrait.Id}' emotion '{pair.Key}' must be between 0 and {MaximumEmotionIndex}.", nameof(portrait));
            if (pair.Value is null || !emotions.TryAdd(index, pair.Value))
                throw new ArgumentException($"Portrait '{portrait.Id}' has an empty or duplicate emotion '{pair.Key}'.", nameof(portrait));
        }
        if (!emotions.TryGetValue(0, out var neutral))
            throw new ArgumentException($"Portrait '{portrait.Id}' requires a neutral emotion (index 0).", nameof(portrait));

        // Six standard emotions remain addressable even when only neutral is supplied.
        var count = Math.Max(6, (emotions.Keys.Max() / 2 + 1) * 2);
        var pose = new Pose
        {
            Id = portrait.Id, Format = portrait.Format, For = portrait.For, Kind = "sheet",
            FrameSize = new[] { 64, 64 }, SheetSize = new[] { 128, count / 2 * 64 },
            MirrorForOppositeFacing = false
        };
        for (var index = 0; index < count; index++)
        {
            var defined = emotions.TryGetValue(index, out var frame);
            frame ??= neutral;
            pose.Frames.Add(new PoseFrame
            {
                Layers = frame.Layers.Select(layer => DefaultPortraitIndex(layer, defined ? index : 0)).ToList(),
                Anchors = frame.Anchors.ToDictionary(pair => pair.Key, pair => (int[])pair.Value.Clone(), StringComparer.OrdinalIgnoreCase)
            });
        }
        return pose;
    }

    private static Layer DefaultPortraitIndex(Layer layer, int index)
    {
        if (layer is not LiveLayer live || live.SourceFrame?.Index is not null
            || !(string.Equals(live.Source?.Kind, "Portrait", StringComparison.OrdinalIgnoreCase)
                || string.Equals(live.Source?.Kind, "PortraitSheet", StringComparison.OrdinalIgnoreCase)))
            return layer;
        // Clone only the layer being normalized. Composition treats shared source models as read-only.
        return new LiveLayer
        {
            Source = live.Source, Part = live.Part,
            SourceFrame = new SourceFrame { Index = index, Facing = live.SourceFrame?.Facing, Stand = live.SourceFrame?.Stand ?? false },
            Visible = live.Visible, Offset = live.Offset, Flip = live.Flip, Mask = live.Mask,
            ColorSlots = live.ColorSlots, AnchorTo = live.AnchorTo, Opacity = live.Opacity, Extra = live.Extra
        };
    }
}
