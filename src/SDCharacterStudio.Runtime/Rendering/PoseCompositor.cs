using System.Diagnostics;
using Microsoft.Xna.Framework.Graphics;
using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Loading;
using SDCharacterStudio.Core.Models;
using StardewModdingAPI;
using StardewValley;

namespace SDCharacterStudio.Runtime.Rendering;

/// <summary>An owned generated texture. Remove every referencing sprite before disposing it.</summary>
public sealed class ComposedPose : IDisposable
{
    private readonly Func<bool> isCurrent;
    private bool disposed;
    public Texture2D Texture { get; }
    public int FrameWidth { get; }
    public int FrameHeight { get; }
    public int Count { get; }
    public PixelPoint Pivot { get; }
    internal bool Current=>!disposed && !Texture.IsDisposed && isCurrent();

    internal ComposedPose(Texture2D texture,int frameWidth,int frameHeight,int count,PixelPoint pivot,Func<bool> isCurrent)
    { Texture=texture;FrameWidth=frameWidth;FrameHeight=frameHeight;Count=count;Pivot=pivot;this.isCurrent=isCurrent; }
    public void Dispose() { if(disposed)return;disposed=true;Texture.Dispose(); }
}

/// <summary>Composes Core layer stacks, uploads premultiplied strips and tracks borrowed source versions.</summary>
public sealed class PoseCompositor
{
    private readonly IModHelper helper;
    private readonly IMonitor monitor;
    private readonly NpcSource source;
    private long generation;
    public FarmerPartsRenderer Farmers { get; }
    public Func<Farmer> FarmerSource { get; set; }=()=>Game1.player;
    public Func<Farmer,string,string> FilterFarmerParts { get; set; }=(_,parts)=>parts;
    public Func<Farmer,long> FarmerVisibilityVersion { get; set; }=_=>0;
    public Func<NPC,Texture2D> PortraitSource { get; set; }=npc=>npc.Portrait;
    public Action<IEnumerable<string>>? PortraitSourcesInvalidated {get;set;}

    public PoseCompositor(IModHelper helper,IMonitor monitor,NpcSource? source=null)
    { this.helper=helper;this.monitor=monitor;this.source=source ?? new NpcSource();Farmers=new(helper); }

    public ComposedPose Compose(IContentPack pack,LoadedPack library,Pose pose,Func<string,NPC?> findNpc,int facing,ExpressionRender? expression=null)
        =>ComposeLayout(pack,library,pose,findNpc,facing,false,expression);

    public ComposedPose ComposeSheet(IContentPack pack,LoadedPack library,Pose pose,Func<string,NPC?> findNpc,int facing,ExpressionRender? expression=null)
        =>ComposeLayout(pack,library,pose,findNpc,facing,true,expression);

    public bool IsCurrent(ComposedPose pose)
    {
        try { return pose.Current; }
        catch(Exception ex)
        {
            monitor.Log($"A pose source is no longer available: {ex.Message}",LogLevel.Trace);
            return false;
        }
    }

    public void InvalidateAssets(IEnumerable<string> assets)
    {
        var names=assets.ToArray();
        if(names.Length==0)return;
        source.InvalidateAssets(names);
        PortraitSourcesInvalidated?.Invoke(names);
        Farmers.Invalidate();
        // Pack PNGs are copied and released during composition. Conservatively mark their strips stale too;
        // content packs can reference arbitrary asset names, including internal Mods/<id>/ paths.
        generation++;
    }

    /// <summary>Invalidate CPU caches; consumers retain ownership of their live strips until sprite cleanup.</summary>
    public void Clear() { generation++;source.Clear();Farmers.Clear(); }

    private ComposedPose ComposeLayout(IContentPack pack,LoadedPack library,Pose pose,Func<string,NPC?> findNpc,int facing,bool sheet,ExpressionRender? expression)
    {
        var watch=Stopwatch.StartNew();
        if(pose.FrameSize is not {Length:2} size || size[0]<=0 || size[1]<=0 || pose.Frames.Count==0)
            throw new ArgumentException($"Pose '{pose.Id}' requires positive frame dimensions and at least one frame.");
        var width=sheet ? pose.SheetSize is {Length:2} output ? output[0] : throw new ArgumentException("Sheet pose requires SheetSize.")
            : checked(size[0]*pose.Frames.Count);
        var height=sheet ? pose.SheetSize![1] : size[1];
        if(width<=0 || height<=0 || width%size[0]!=0 || height%size[1]!=0 || (long)(width/size[0])*(height/size[1])<pose.Frames.Count)
            throw new ArgumentException("Pose sheet dimensions must contain complete cells for every frame.");
        var canvas=new RgbaImage(width,height,new uint[checked(width*height)]);
        var farmer=FarmerSource();
        var resolver=new RuntimeLayerSourceResolver(helper,pack,library,pose,findNpc,source,facing,Farmers,farmer,FilterFarmerParts,PortraitSource);
        bool farmerColors=false;
        var colors=new ColorSlotResolver(library,resolver.SampleFrame,message=>monitor.Log(message,LogLevel.Trace),sample=>{farmerColors=true;return FarmerPalette.Resolve(Farmers,farmer,sample);});
        var transform=expression!=null?new ExpressionSourceTransform(pack,library,pose,resolver,colors.Resolve,expression,facing):null;
        for(var i=0;i<pose.Frames.Count;i++)
        {
            var frame=Core.Compositing.Compositor.Compose(size[0],size[1],resolver.Normalize(pose.Frames[i]),resolver,colors.Resolve,transformSource:transform==null?null:transform.Apply);
            Core.Compositing.Compositor.Blit(canvas,frame,i%(width/size[0])*size[0],i/(width/size[0])*size[1]);
        }
        var dependencies=resolver.Dependencies.ToArray();
        var version=generation;
        bool usesFarmer=resolver.UsesFarmer||farmerColors;
        FarmerAppearanceKey? farmerVersion=usesFarmer?Farmers.AppearanceKey(farmer):null;
        long visibilityVersion=FarmerVisibilityVersion(farmer);
        var pivot=pose.GetPivot();
        var texture=TexturePixels.Upload(Game1.graphics.GraphicsDevice,canvas);
        bool Current()
        {
            if(generation!=version)return false;
            for(int i=0;i<dependencies.Length;i++)if(!source.IsCurrent(dependencies[i]))return false;
            return !usesFarmer || (ReferenceEquals(FarmerSource(),farmer) && Farmers.AppearanceKey(farmer)==farmerVersion && FarmerVisibilityVersion(farmer)==visibilityVersion);
        }
        var result=new ComposedPose(texture,size[0],size[1],pose.Frames.Count,new PixelPoint(pivot[0],pivot[1]),Current);
        monitor.Log($"Composed pose '{pose.Id}' ({pose.Frames.Count} frames) in {watch.Elapsed.TotalMilliseconds:F2} ms.",LogLevel.Trace);
        return result;
    }
}
