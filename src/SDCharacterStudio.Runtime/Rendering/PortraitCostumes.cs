using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace SDCharacterStudio.Runtime.Rendering;

/// <summary>Owns persistent portrait copies, keeping their source separate from the generated result.</summary>
public sealed class PortraitCostumes
{
    private readonly Dictionary<NPC,Entry> active=new();
    public Func<string,Texture2D>? Reload {get;set;}
    public IEnumerable<(NPC Actor,string Id,Texture2D Generated,object? Scope)> Active=>active.Select(x=>(x.Key,x.Value.Id,x.Value.Generated,x.Value.Scope)).ToArray();
    public Texture2D Source(NPC npc)
    {
        if(!active.TryGetValue(npc,out var entry)||!ReferenceEquals(npc.Portrait,entry.Texture))return npc.Portrait;
        RefreshSource(entry);
        return entry.Live;
    }
    private void RefreshSource(Entry entry)
    {
        if(entry.Dirty&&entry.Live.Name is{} name&&Reload!=null)
        {
            var loaded=Reload(name);
            if(ReferenceEquals(entry.Live,entry.Original)||string.Equals(entry.Original.Name?.Replace('\\','/'),name.Replace('\\','/'),StringComparison.OrdinalIgnoreCase))entry.Original=loaded;
            entry.Live=loaded;entry.Dirty=false;
        }
    }
    public void Invalidate(IEnumerable<string> assets)
    {
        static string Normalize(string name)=>name.Replace('\\','/');
        var names=new HashSet<string>(assets.Select(Normalize),StringComparer.OrdinalIgnoreCase);
        foreach(var entry in active.Values)if(entry.Live.Name is{} name&&names.Contains(Normalize(name)))entry.Dirty=true;
    }
    public string? Definition(NPC npc)=>active.TryGetValue(npc,out var entry)&&ReferenceEquals(npc.Portrait,entry.Texture)?entry.Id:null;
    public bool Uses(Texture2D texture)=>active.Values.Any(x=>ReferenceEquals(x.Texture,texture));
    public void Apply(NPC npc,string id,Texture2D composed,object? scope=null)
    {
        active.TryGetValue(npc,out var old);
        if(old!=null)RefreshSource(old);
        var live=Source(npc);
        var original=old?.Original??npc.Portrait;
        bool overridden=old?.Overridden??npc.portraitOverridden;
        var pixels=new Color[checked(composed.Width*composed.Height)];composed.GetData(pixels);
        var copy=new Texture2D(composed.GraphicsDevice,composed.Width,composed.Height);
        try{copy.SetData(pixels);}
        catch{copy.Dispose();throw;}
        active[npc]=new(original,overridden,id,copy,live,composed,scope);npc.Portrait=copy;npc.portraitOverridden=true;
        old?.Texture.Dispose();
    }
    public void Restore(NPC npc)
    {
        if(!active.TryGetValue(npc,out var entry))return;
        RefreshSource(entry);
        if(entry.Original.IsDisposed&&entry.Original.Name is{} name&&Reload!=null)entry.Original=Reload(name);
        active.Remove(npc);
        npc.Portrait=entry.Original;npc.portraitOverridden=entry.Overridden;entry.Texture.Dispose();
    }
    public void RestoreAll(){foreach(var npc in active.Keys.ToArray())Restore(npc);}
    private sealed class Entry(Texture2D original,bool overridden,string id,Texture2D texture,Texture2D live,Texture2D generated,object? scope)
    {
        public Texture2D Original=original,Texture=texture,Live=live,Generated=generated;
        public bool Overridden=overridden,Dirty;
        public string Id=id;
        public object? Scope=scope;
    }
}
