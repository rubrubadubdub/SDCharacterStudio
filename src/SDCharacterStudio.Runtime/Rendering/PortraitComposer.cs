using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Loading;
using SDCharacterStudio.Core.Models;
using StardewModdingAPI;
using StardewValley;

namespace SDCharacterStudio.Runtime.Rendering;

/// <summary>Builds native portrait sheets through the same masks, anchors and colours as poses.</summary>
public sealed class PortraitComposer
{
    private readonly IMonitor monitor;
    private readonly PoseCompositor compositor;

    public PortraitComposer(IModHelper helper, IMonitor monitor, PoseCompositor compositor)
    {
        ArgumentNullException.ThrowIfNull(helper);
        this.monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        this.compositor = compositor ?? throw new ArgumentNullException(nameof(compositor));
    }

    /// <summary>The caller owns the generated texture and must release dialogue references before disposal.</summary>
    public ComposedPose Compose(IContentPack pack, LoadedPack library, Portrait portrait, Func<string, NPC?> findNpc,
        ExpressionRender? expression = null)
    {
        var pose = PortraitLayout.Create(portrait);
        var result = compositor.ComposeSheet(pack, library, pose, findNpc, facing: 2, expression: expression);
        monitor.Log($"Composed portrait '{portrait.Id}' ({result.Count} emotion cells).", LogLevel.Trace);
        return result;
    }
}
