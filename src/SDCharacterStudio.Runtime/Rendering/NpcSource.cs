using Microsoft.Xna.Framework.Graphics;
using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Models;
using StardewValley;

namespace SDCharacterStudio.Runtime.Rendering;

internal sealed record TextureDependency(SourceFrameSnapshot Snapshot,Func<(Texture2D Texture,string? Asset)> Current);

/// <summary>Caches CPU copies of borrowed game and content textures. Never disposes a source texture.</summary>
public sealed class NpcSource
{
    private readonly SourceVersionCache cache=new();

    internal SourceFrameSnapshot Capture(Texture2D texture,string? asset,int width,int height,int index)
        =>cache.GetFrame(texture,asset,width,height,index,()=>TexturePixels.Read(texture));

    internal SourceFrameSnapshot CaptureNpc(NPC npc,CharacterRig rig,int index,bool portrait=false)
    {
        var size=portrait ? rig.Portrait?.FrameSize ?? new[]{64,64} : rig.FrameSize;
        if(size.Length!=2) throw new ArgumentException("Rig frame size must contain width and height.");
        var current=Current(npc,portrait);
        return Capture(current.Texture,current.Asset,size[0],size[1],index);
    }

    internal static (Texture2D Texture,string? Asset) Current(NPC npc,bool portrait=false)
    {
        var texture=portrait ? npc.Portrait : npc.Sprite.Texture;
        var asset=portrait ? texture.Name ?? "Portraits/"+npc.Name : npc.Sprite.loadedTexture ?? texture.Name ?? "Characters/"+npc.Name;
        return(texture,asset);
    }

    internal bool IsCurrent(TextureDependency dependency)
    {
        var current=dependency.Current();
        return !current.Texture.IsDisposed && cache.IsCurrent(dependency.Snapshot,current.Texture) &&
            (string.Equals(dependency.Snapshot.Asset,current.Asset,StringComparison.OrdinalIgnoreCase) ||
             SourceVersionCache.SameAsset(dependency.Snapshot.Asset,current.Asset));
    }

    public void InvalidateAssets(IEnumerable<string> assets)=>cache.Invalidate(assets);
    public void Clear()=>cache.Clear();
}
