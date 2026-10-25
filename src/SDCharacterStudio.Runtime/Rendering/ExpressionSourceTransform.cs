using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Loading;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Palette;
using StardewModdingAPI;

namespace SDCharacterStudio.Runtime.Rendering;

/// <summary>Edits the cloned live head in source coordinates; Core masks and places the result afterwards.</summary>
internal sealed class ExpressionSourceTransform
{
    private readonly LoadedPack library;
    private readonly Pose pose;
    private readonly RuntimeLayerSourceResolver sources;
    private readonly Func<string,ColorRamp> colors;
    private readonly ExpressionRender render;
    private readonly string actorFacing;
    private readonly RgbaImage overlay;
    public ExpressionSourceTransform(IContentPack pack,LoadedPack library,Pose pose,RuntimeLayerSourceResolver sources,Func<string,ColorRamp> colors,ExpressionRender render,int facing)
    {
        this.library=library;this.pose=pose;this.sources=sources;this.colors=colors;this.render=render;
        actorFacing=facing switch{0=>"up",1=>"right",2=>"down",_=>"left"};
        if((uint)render.Frame>=render.Definition.Frames.Count)throw new ArgumentException("Invalid expression frame.");
        string path=render.Definition.Frames[render.Frame].Image??throw new ArgumentException("Expression frame has no image.");
        if(!PackPath.IsSafe(path))throw new ArgumentException("Expression image path must stay inside the pack.");
        overlay=TexturePixels.Read(pack.ModContent.Load<IRawTextureData>(path));
    }
    public void Apply(Layer layer,RgbaImage pixels,int index)
    {
        if(layer is not LiveLayer live)return;
        bool portrait=Same(live.Source?.Kind,"Portrait")||Same(live.Source?.Kind,"PortraitSheet");
        var expression=render.Definition;
        if(portrait!=Same(expression.Target,"Portrait"))return;
        bool farmer=Same(live.Source?.Kind,"Farmer");
        if(!farmer&&!portrait&&!Same(live.Source?.Kind,"Npc")&&!Same(live.Source?.Kind,"NpcSheet"))return;
        bool head=farmer?sources.FarmerHeadVisible(live):live.Part.Split('+').Any(x=>Same(x.Trim(),"Head"));
        if(farmer&&!head)return;
        bool full=Same(live.Part,"Full")||Same(live.Part,"Mask");
        if(!head&&!full)return;
        var rig=expression.For!=null?library.Characters.FirstOrDefault(x=>Same(x.Value.Id,expression.For))?.Value
            :RigSelection.Select(library.Characters.Select(x=>x.Value),pose.For,live.Source?.Npc);
        if(rig==null)throw new ArgumentException($"Expression '{expression.Id}' has no rig '{expression.For}'.");
        if(farmer!=Same(rig.Kind,"Farmer"))return;
        if(!farmer && live.Source?.Npc is{} npc && !Same(npc,rig.Npc)&&!Same(npc,rig.Id))return;
        var sourceRig=pose.For!=null?library.Characters.FirstOrDefault(x=>Same(x.Value.Id,pose.For))?.Value:null;
        if(!farmer&&live.Source?.Npc==null&&sourceRig?.Npc is{} sourceNpc&&!Same(sourceNpc,rig.Npc))return;
        string facing=farmer?live.SourceFrame?.Facing??pose.Facing??actorFacing
            :live.SourceFrame?.Facing??(rig.Frames.FirstOrDefault(x=>x.Value.Stand==index||x.Value.Walk.Contains(index)).Key)??pose.Facing??actorFacing;
        if(!portrait&&expression.Facing!=null&&!Same(expression.Facing,facing))return;
        if(!Same(expression.Anchor,"FaceBox"))throw new ArgumentException("Expressions must use the FaceBox anchor.");
        int[]? box=portrait?rig.Portrait?.FaceBox:rig.FaceBox.TryGetValue(facing,out var value)?value:null;
        if(box is not{Length:4})throw new ArgumentException($"Rig '{rig.Id}' has no {(portrait?"portrait":"sprite "+facing)} FaceBox.");
        int[]? farmerBounds=farmer?sources.FarmerHeadBounds(index,Core.Ids.Facings[facing]):null;
        if(farmer&&rig.FrameSize.SequenceEqual(new[]{16,32}))box=new[]{box[0]+farmerBounds![0],box[1]+farmerBounds[1],box[2],box[3]};
        else if(farmer)box=new[]{box[0]+farmerBounds![0]-8,box[1]+farmerBounds[1]-16,box[2],box[3]};
        uint[]? before=full||farmer?(uint[])pixels.Pixels.Clone():null;
        ExpressionOverlay.Apply(pixels,overlay,box,expression.Frames[render.Frame],expression.ColorSlots,colors);
        if(before==null)return; // Head layers receive their authored mask in Core after this callback.
        if(farmer)
        {
            for(int y=0;y<pixels.Height;y++)for(int x=0;x<pixels.Width;x++)
                if(x<farmerBounds![0]||x>=farmerBounds[0]+farmerBounds[2]||y<farmerBounds[1]||y>=farmerBounds[1]+farmerBounds[3])pixels.Pixels[y*pixels.Width+x]=before[y*pixels.Width+x];
            return;
        }
        string? mask=portrait?rig.Portrait?.HeadMask:rig.HeadMask.GetValueOrDefault(facing);
        if(mask!=null)
        {
            var definition=sources.GetMask(mask);
            string? path=definition.PerFrame.GetValueOrDefault(index.ToString(System.Globalization.CultureInfo.InvariantCulture))??definition.Image;
            if(path==null)throw new ArgumentException("Expression head mask has no image.");
            var keep=sources.ResolveMaskImage(path);
            if(keep.Width!=pixels.Width||keep.Height!=pixels.Height)throw new ArgumentException("Expression head mask dimensions must match its source.");
            bool remove=Same(definition.Mode,"remove");
            for(int i=0;i<pixels.Pixels.Length;i++)if(((keep.Pixels[i]>>24)!=0)==remove)pixels.Pixels[i]=before[i];
        }
        else throw new ArgumentException($"Full live source needs a head mask for expression '{expression.Id}'.");
    }
    private static bool Same(string? a,string? b)=>string.Equals(a,b,StringComparison.OrdinalIgnoreCase);
}
