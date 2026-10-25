using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace SDCharacterStudio.Runtime.Animation;

/// <summary>Factories transfer fresh texture ownership to SMAPI; source strips remain scene-owned.</summary>
public sealed class GeneratedAssets(IModHelper helper)
{
    private readonly Dictionary<string,Func<Texture2D>> factories=new(StringComparer.OrdinalIgnoreCase);
    public void Register(string name,Texture2D source)
    {
        var pixels=new Microsoft.Xna.Framework.Color[source.Width*source.Height];source.GetData(pixels);
        // Capture size and device now: the scene disposes the source strip, but this factory outlives it (see Clear).
        var device=source.GraphicsDevice;int width=source.Width,height=source.Height;
        factories[name]=()=>
        {
            var copy=new Texture2D(device,width,height);
            try{copy.SetData(pixels);return copy;}catch{copy.Dispose();throw;}
        };
        helper.GameContent.InvalidateCache(name);
    }
    public void OnAssetRequested(AssetRequestedEventArgs e)
    {
        foreach(var entry in factories)
            if(e.NameWithoutLocale.IsEquivalentTo(entry.Key)){e.LoadFrom(entry.Value,AssetLoadPriority.Exclusive);return;}
    }
    /// <summary>
    /// Scene end. Vanilla changeSprite caches each sheet in the game's content manager, and SMAPI reloads a cached
    /// texture in place whenever its name is invalidated, warning "the underlying asset no longer exists" if no loader
    /// remains. So the last pixels stay loadable (one small array per sheet pose) and a later Register replaces them.
    /// </summary>
    public void Clear() { }
}
