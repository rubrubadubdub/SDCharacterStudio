using Microsoft.Xna.Framework.Graphics;
using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Loading;
using SDCharacterStudio.Core.Models;
using StardewModdingAPI;
using StardewValley;

namespace SDCharacterStudio.Runtime.Rendering;

/// <summary>Resolves source pixels only; all masks, colours, flips, anchors and blending remain in Core.</summary>
internal sealed class RuntimeLayerSourceResolver : ILayerSourceResolver
{
    private readonly IModHelper helper;
    private readonly IContentPack pack;
    private readonly LoadedPack library;
    private readonly Pose pose;
    private readonly Func<string,NPC?> findNpc;
    private readonly NpcSource source;
    private readonly FarmerPartsRenderer farmerRenderer;
    private readonly Farmer farmer;
    private readonly Func<Farmer,string,string> filterParts;
    private readonly Func<NPC,Texture2D> portraitSource;
    private readonly Func<string,(Texture2D Texture,string Asset)>? outfits;
    private readonly string actorFacing;
    private readonly Dictionary<string,RgbaImage> packImages=new(StringComparer.OrdinalIgnoreCase);
    public List<TextureDependency> Dependencies { get; }=new();
    public bool UsesFarmer { get; private set; }
    internal PixelPoint FarmerNeck(int frame,int facing)=>farmerRenderer.GetNeckAnchor(farmer,frame,facing);
    internal bool FarmerHeadVisible(LiveLayer layer)=>FarmerParts.Parse(filterParts(farmer,layer.Part)).HasFlag(FarmerPart.Head);
    internal int[] FarmerHeadBounds(int frame,int facing)
    {
        var current=FarmerNeck(frame,facing);var standard=farmerRenderer.GetNeckAnchor(frame,facing);
        return new[]{8+current.X-standard.X,16+current.Y-standard.Y,16,Math.Clamp(16+FarmerRenderer.featureYOffsetPerFrame[frame],0,32)};
    }

    public RuntimeLayerSourceResolver(IModHelper helper,IContentPack pack,LoadedPack library,Pose pose,Func<string,NPC?> findNpc,NpcSource source,int facing,FarmerPartsRenderer farmerRenderer,Farmer farmer,Func<Farmer,string,string> filterParts,Func<NPC,Texture2D> portraitSource,Func<string,(Texture2D Texture,string Asset)>? outfits=null)
    { this.helper=helper;this.pack=pack;this.library=library;this.pose=pose;this.findNpc=findNpc;this.source=source;actorFacing=FacingName(facing);this.farmerRenderer=farmerRenderer;this.farmer=farmer;this.filterParts=filterParts;this.portraitSource=portraitSource;this.outfits=outfits; }

    public PoseFrame Normalize(PoseFrame frame)
    {
        var layers=new List<Layer>(frame.Layers.Count);
        foreach(var layer in frame.Layers)
        {
            if(layer is LiveLayer live && live.Visible && live.Part.Equals("Head",StringComparison.OrdinalIgnoreCase) && live.Mask==null && !Same(live.Source?.Kind,"Farmer"))
            {
                var rig=FindRig(live);
                var facing=SourceFacing(live,rig);
                string? mask;
                if(IsPortrait(live)) mask=rig.Portrait?.HeadMask;
                else rig.HeadMask.TryGetValue(facing,out mask);
                if(mask==null) throw new ArgumentException($"Rig '{rig.Id}' has no head mask for '{facing}'.");
                // Clone only the changed layer; arrays/dictionaries are read-only throughout composition.
                layers.Add(new LiveLayer {Source=live.Source,Part=live.Part,SourceFrame=live.SourceFrame,Visible=live.Visible,
                    Offset=live.Offset,Flip=live.Flip,Mask=mask,ColorSlots=live.ColorSlots,AnchorTo=live.AnchorTo,Opacity=live.Opacity,Map=live.Map});
            }
            else layers.Add(layer);
        }
        return new PoseFrame {Duration=frame.Duration,Layers=layers,Anchors=frame.Anchors,Sound=frame.Sound,Dust=frame.Dust};
    }

    public ResolvedLayerSource Resolve(Layer layer)
    {
        switch(layer)
        {
            case ImageLayer image:
                var size=image.FrameSize ?? pose.FrameSize ?? throw new ArgumentException("Image layer needs a frame size.");
                if(size.Length!=2) throw new ArgumentException("Image frame size needs width and height.");
                return new(PixelOperations.CropFrame(ReadPackImage(image.Image),size[0],size[1],image.Frame),image.Frame);
            case PaintLayer paint:
                var pixels=ReadPackImage(paint.Image);
                if(pose.FrameSize is not {Length:2} paintSize || pixels.Width!=paintSize[0] || pixels.Height!=paintSize[1])
                    throw new ArgumentException("Paint image dimensions must match the pose frame size.");
                return new(pixels);
            case EffectLayer:
                // The animation player schedules EffectLayer emitters; they are never baked into the strip.
                return new(new RgbaImage(1,1,new uint[1]));
            case LiveLayer live: return ResolveLive(live);
            default: throw new NotSupportedException($"Layer type '{layer.Type}' cannot be rendered.");
        }
    }

    public Mask GetMask(string id)=>library.Masks?.Value.Masks.FirstOrDefault(x=>Same(x.Id,id))
        ?? throw new ArgumentException($"No mask named '{id}'.");
    public RgbaImage ResolveMaskImage(string path)=>ReadPackImage(path);

    public RgbaImage SampleFrame(CharacterRig rig,int index)
    {
        if(rig.Kind.Equals("Farmer",StringComparison.OrdinalIgnoreCase))
        {UsesFarmer=true;return farmerRenderer.Render(farmer,index,2,"Full");}
        var npc=FindNpc(rig.Npc);
        var snapshot=source.CaptureNpc(npc,rig,index);
        Dependencies.Add(new(snapshot,()=>NpcSource.Current(npc)));
        return snapshot.Image;
    }

    private ResolvedLayerSource ResolveLive(LiveLayer live)
    {
        var spec=live.Source ?? throw new ArgumentException("Live layer has no source.");
        if(Same(spec.Kind,"Farmer"))
        {
            UsesFarmer=true;
            string farmerFacing=live.SourceFrame?.Facing??pose.Facing??actorFacing;
            if(!Core.Ids.Facings.TryGetValue(farmerFacing,out int direction))throw new ArgumentException("Unknown farmer facing.");
            int farmerFrame=live.SourceFrame?.Index??(direction switch{0=>12,1 or 3=>6,_=>0});
            return new(farmerRenderer.Render(farmer,farmerFrame,direction,filterParts(farmer,live.Part)),farmerFrame,farmerRenderer.GetNeckAnchor(farmer,farmerFrame,direction));
        }
        if(Same(spec.Kind,"Asset"))
        {
            var asset=spec.Asset ?? throw new ArgumentException("Asset layer has no asset name.");
            var texture=helper.GameContent.Load<Texture2D>(asset);
            var snapshot=source.Capture(texture,asset,texture.Width,texture.Height,0);
            Dependencies.Add(new(snapshot,()=> (helper.GameContent.Load<Texture2D>(asset),asset)));
            var rect=spec.Rect ?? throw new ArgumentException("Asset layer requires a source rectangle.");
            if(rect.Length!=4) throw new ArgumentException("Asset source rectangle needs x, y, width and height.");
            return new(PixelOperations.Crop(snapshot.Image,rect[0],rect[1],rect[2],rect[3]));
        }
        if(!Same(spec.Kind,"Npc") && !Same(spec.Kind,"NpcSheet") && !Same(spec.Kind,"Portrait") && !Same(spec.Kind,"PortraitSheet") && !Same(spec.Kind,Outfits.SourceKind))
            throw new NotSupportedException($"Live source '{spec.Kind}' cannot be rendered.");
        if(!Same(live.Part,"Full") && !Same(live.Part,"Head") && !Same(live.Part,"Mask"))
            throw new NotSupportedException($"NPC part '{live.Part}' is unavailable. Farmer parts require M4.");
        if(Same(live.Part,"Mask") && live.Mask==null) throw new ArgumentException("Part Mask needs a named mask.");
        var rig=FindRig(live);
        var facing=SourceFacing(live,rig);
        var portrait=IsPortrait(live);
        var index=live.SourceFrame?.Index ?? (portrait ? 0 : rig.Frames.TryGetValue(facing,out var frames) ? frames.Stand
            : throw new ArgumentException($"Rig '{rig.Id}' has no frames for '{facing}'."));
        SourceFrameSnapshot captured;
        if(Same(spec.Kind,"Npc") || Same(spec.Kind,"Portrait"))
        {
            var npc=FindNpc(spec.Npc ?? rig.Npc);
            if(portrait)
            {
                var texture=portraitSource(npc);var size=rig.Portrait?.FrameSize??new[]{64,64};
                captured=source.Capture(texture,texture.Name??"Portraits/"+npc.Name,size[0],size[1],index);
                Dependencies.Add(new(captured,()=>{var current=portraitSource(npc);return(current,current.Name??"Portraits/"+npc.Name);}));
            }
            else
            {
                captured=source.CaptureNpc(npc,rig,index);
                Dependencies.Add(new(captured,()=>NpcSource.Current(npc)));
            }
        }
        else if(Same(spec.Kind,Outfits.SourceKind))
        {
            // One of the pack's outfits: its generated sheet, composed once per scene (an outfit never wears itself).
            var id=spec.Outfit ?? throw new ArgumentException("Outfit source names no outfit.");
            if(outfits==null) throw new ArgumentException($"Outfit '{id}' can only be worn in a scene.");
            var (texture,asset)=outfits(id);
            captured=source.Capture(texture,asset,rig.FrameSize[0],rig.FrameSize[1],index);
            Dependencies.Add(new(captured,()=>outfits(id)));
        }
        else
        {
            var asset=spec.Asset ?? throw new ArgumentException("Sheet source has no asset name.");
            var texture=helper.GameContent.Load<Texture2D>(asset);
            var size=portrait ? rig.Portrait?.FrameSize ?? new[]{64,64} : rig.FrameSize;
            if(size.Length!=2) throw new ArgumentException("Rig frame size needs width and height.");
            captured=source.Capture(texture,asset,size[0],size[1],index);
            Dependencies.Add(new(captured,()=> (helper.GameContent.Load<Texture2D>(asset),asset)));
        }
        var neck=portrait ? rig.Portrait?.NeckPoint : rig.NeckPoint.TryGetValue(facing,out var point) ? point : null;
        return new(captured.Image,index,neck is {Length:2} ? new PixelPoint(neck[0],neck[1]) : null);
    }

    private CharacterRig FindRig(LiveLayer layer)
        =>RigSelection.Select(library.Characters.Select(x=>x.Value),pose.For,layer.Source?.Npc);

    private string SourceFacing(LiveLayer layer,CharacterRig rig)
    {
        if(layer.SourceFrame?.Facing is {} facing) return facing;
        if(layer.SourceFrame?.Index is {} index)
        {
            var match=rig.Frames.FirstOrDefault(x=>x.Value.Stand==index || x.Value.Walk.Contains(index));
            if(match.Key!=null) return match.Key;
        }
        return pose.Facing ?? actorFacing;
    }

    private NPC FindNpc(string? name)=>name!=null ? findNpc(name) ?? throw new ArgumentException($"No live NPC '{name}'.")
        : throw new ArgumentException("NPC source requires an NPC name.");

    private RgbaImage ReadPackImage(string? path)
    {
        if(path==null || !PackPath.IsSafe(path)) throw new ArgumentException($"Invalid pack image path '{path}'.");
        if(packImages.TryGetValue(path,out var cached)) return cached;
        // The installed SMAPI raw-data path uses the same premultiplied PNG decoder, without allocating
        // a GPU texture only to read it back and dispose it. Live NPC/GameContent textures stay borrowed.
        return packImages[path]=TexturePixels.Read(pack.ModContent.Load<IRawTextureData>(path));
    }

    private static bool IsPortrait(LiveLayer layer)=>Same(layer.Source?.Kind,"Portrait") || Same(layer.Source?.Kind,"PortraitSheet");
    private static bool Same(string? a,string? b)=>string.Equals(a,b,StringComparison.OrdinalIgnoreCase);
    private static string FacingName(int facing)=>facing switch {0=>"up",1=>"right",2=>"down",3=>"left",_=>throw new ArgumentOutOfRangeException(nameof(facing))};
}
