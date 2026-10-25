using Microsoft.Xna.Framework;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Runtime.Rendering;
using StardewValley;

namespace SDCharacterStudio.Runtime.Animation;

public sealed partial class AnimationRuntime
{
    private readonly Dictionary<Character,ExpressionStack> expressionStates=new();
    private readonly HashSet<StandIn> expressionFallbacks=new();
    public PortraitComposer PortraitComposer {get;}
    public PortraitCostumes PortraitCostumes {get;}=new();
    public event Action? Releasing;
    public event Action<double>? Updating;
    public Expression GetExpression(string id)=>scene?.Pack.Expressions?.Value.Expressions.FirstOrDefault(x=>string.Equals(x.Id,id,StringComparison.OrdinalIgnoreCase))
        ??throw new ArgumentException($"Unknown expression '{id}'.");
    public Portrait GetPortrait(string id)=>scene?.Pack.Portraits.FirstOrDefault(x=>string.Equals(x.Value.Id,id,StringComparison.OrdinalIgnoreCase))?.Value
        ??throw new ArgumentException($"Unknown portrait '{id}'.");
    public ExpressionRender? ExpressionFor(Character actor)
    {
        var active=expressionStates.GetValueOrDefault(actor)?.Active;
        return active==null?null:new(active.Definition,active.Frame);
    }
    public (Expression? Definition,int Frame) ExpressionStamp(Character actor)
    {var value=expressionStates.GetValueOrDefault(actor)?.Active;return(value?.Definition,value?.Frame??0);}
    public void SetExpression(string actor,string id,object owner,double elapsed=0,int? duration=null,bool external=false)
    {
        var target=Actor(actor);var definition=GetExpression(id);
        if(!definition.Target.Equals("Sprite",StringComparison.OrdinalIgnoreCase)&&!definition.Target.Equals("Portrait",StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Expression Target must be Sprite or Portrait.");
        var rig=scene!.Pack.Characters.FirstOrDefault(x=>string.Equals(x.Value.Id,definition.For,StringComparison.OrdinalIgnoreCase))?.Value
            ??throw new ArgumentException($"Expression '{id}' must select a valid For rig.");
        if(target is NPC npc&&!string.Equals(npc.Name,rig.Npc??rig.Id,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Expression rig does not match its actor.");
        if(target is Farmer&&!rig.Kind.Equals("Farmer",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Farmer expression requires a Farmer rig.");
        if(!expressionStates.TryGetValue(target,out var state))expressionStates[target]=state=new();
        state.Set(owner,definition,elapsed,duration,external);
    }
    public void SeekExpression(string actor,object owner,double elapsed)=>expressionStates.GetValueOrDefault(Actor(actor))?.Seek(owner,elapsed);
    public void ClearExpression(string actor,object owner)=>expressionStates.GetValueOrDefault(Actor(actor))?.Remove(owner);
    public void HoldExpression(string actor,object owner)=>expressionStates.GetValueOrDefault(Actor(actor))?.ReleaseClock(owner);

    private void UpdateExpressions(double ms)
    {
        foreach(var pair in expressionStates)
        {
            pair.Value.Advance(ms);
            bool sprite=pair.Value.Active?.Definition.Target.Equals("Sprite",StringComparison.OrdinalIgnoreCase)==true;
            if(!sprite)
            {
                if(standIns.TryGetValue(pair.Key,out var old)&&expressionFallbacks.Contains(old))Remove(old);
                continue;
            }
            if(standIns.ContainsKey(pair.Key))continue;
            var rig=scene!.Pack.Characters.First(x=>string.Equals(x.Value.Id,pair.Value.Active!.Definition.For,StringComparison.OrdinalIgnoreCase)).Value;
            bool farmer=pair.Key is Farmer;
            var pose=new Pose{Id="__expression_"+pair.Key.Name,Kind="character",For=rig.Id,FrameSize=farmer?new[]{32,48}:rig.FrameSize,
                Pivot=farmer?new[]{16,47}:new[]{rig.FrameSize[0]/2,rig.FrameSize[1]-1},
                Frames=new(){new PoseFrame{Duration=1000,Layers=new(){new LiveLayer{Source=new(){Kind=farmer?"Farmer":"Npc",Npc=farmer?null:pair.Key.Name},Part="Full",SourceFrame=new(){Index=pair.Key.Sprite.CurrentFrame}}}}}};
            var stand=new StandIn(this,pair.Key,pose,Compose(pose,pair.Key.FacingDirection,pair.Key),"hold-first",1,false);
            stand.Visible=LogicalVisible(farmer?"farmer":pair.Key.Name);standIns[pair.Key]=stand;expressionFallbacks.Add(stand);
        }
    }
    internal bool UpdateExpressionFallback(StandIn stand)
    {
        if(!expressionFallbacks.Contains(stand))return false;
        var source=((LiveLayer)stand.Definition.Frames[0].Layers[0]).SourceFrame!;
        bool changed=source.Index!=stand.Actor.Sprite.CurrentFrame;
        source.Index=stand.Actor.Sprite.CurrentFrame;return changed;
    }
    private void ClearExpressions(){foreach(var state in expressionStates.Values)state.Clear();expressionStates.Clear();expressionFallbacks.Clear();}

    public ComposedPose ComposePortrait(string id,ExpressionRender? expression=null)
        =>ComposePortraitDefinition(GetPortrait(id),expression);
    private ComposedPose ComposePortraitDefinition(Portrait definition,ExpressionRender? expression)
    {
        string key="portrait:"+definition.Id+":"+expression?.Definition.Id+":"+expression?.Frame;
        if(cache.TryGetValue(key,out var existing)&&Compositor.IsCurrent(existing))return existing;
        var pack=packs.FindPack(scene!.PackId)??throw new InvalidOperationException("Pack unavailable.");
        var result=PortraitComposer.Compose(pack,scene.Pack,definition,Npc,expression);
        cache[key]=result;textures.Add(result);return result;
    }
    public ComposedPose ComposeLivePortrait(NPC npc,ExpressionRender expression)
    {
        var source=PortraitCostumes.Source(npc);
        int count=Math.Max(6,source.Width/64*(source.Height/64));
        var definition=new Portrait{Id="__live_"+npc.Name,For=expression.Definition.For};
        for(int i=0;i<count;i++)definition.Emotions[i.ToString(System.Globalization.CultureInfo.InvariantCulture)]=new(){Layers=new(){new LiveLayer{Source=new(){Kind="Portrait",Npc=npc.Name},Part="Full",SourceFrame=new(){Index=Math.Min(i,source.Width/64*(source.Height/64)-1)}}}};
        return ComposePortraitDefinition(definition,expression);
    }
}
