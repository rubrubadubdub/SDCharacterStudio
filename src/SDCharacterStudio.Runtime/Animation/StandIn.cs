using Microsoft.Xna.Framework;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Runtime.Rendering;
using StardewModdingAPI;
using StardewValley;

namespace SDCharacterStudio.Runtime.Animation;

/// <summary>A manually timed native sprite. Its strip belongs to the cutscene, never to the TAS.</summary>
public sealed class StandIn
{
    private readonly AnimationRuntime owner;
    private readonly GameLocation location;
    private readonly IReflectedField<int> emoteOffset;
    private readonly bool originalHidden;
    private readonly int originalEmoteOffset;
    private readonly Dictionary<string,double> effectTimes=new(StringComparer.OrdinalIgnoreCase);
    private readonly List<EffectLayer> visibleEffects=new();
    private readonly List<string> effectKeys=new();
    private ComposedPose composed;
    private int composedFacing;
    private int effectsFrame=-1;
    private double elapsed,externalTime;
    private bool removed;
    private (Expression? Definition, int Frame) expressionStamp;
    private int headFrame=-1,headFacing=-1;
    private double headOffset;
    public Character Actor { get; }
    public Pose Definition { get; }
    public PoseTimeline Timeline { get; }
    public TemporaryAnimatedSprite Sprite { get; }
    public bool ExternalClock { get; set; }
    public bool Visible { get; set; }=true;
    public bool Finished=>Timeline.Finished;
    public Vector2 HeadPosition { get; private set; }
    public StandIn(AnimationRuntime owner,Character actor,Pose pose,ComposedPose composed,string play,double speed,bool external)
    {
        this.owner=owner;Actor=actor;Definition=pose;this.composed=composed;ExternalClock=external;composedFacing=actor.FacingDirection;
        expressionStamp=owner.ExpressionStamp(actor);
        location=Game1.currentLocation;
        originalHidden=AnimationRuntime.Hidden(actor);
        emoteOffset=owner.Reflection.GetField<int>(actor,"emoteYOffset");originalEmoteOffset=emoteOffset.GetValue();
        Timeline=new(pose,play,speed);
        Sprite=new TemporaryAnimatedSprite("",new Rectangle(0,0,composed.FrameWidth,composed.FrameHeight),999999f,1,999999,actor.Position,false,false,0,0,Color.White,4,0,0,0){texture=composed.Texture};
        location.temporarySprites.Add(Sprite);
        EnterFrame(Timeline.Frame);
        Update(0);
    }
    public void SetTime(double time)
    {
        Advance(Math.Max(0,time-externalTime));externalTime=time;
    }
    public void Release()
    {
        Timeline.Release(); ExternalClock=false;
        if(!Timeline.Finished)EnterFrame(Timeline.Frame);
    }
    private void Advance(double ms)
    {
        elapsed+=ms;
        var entered=Timeline.Advance(ms);
        for(int i=0;i<entered.Count;i++)EnterFrame(entered[i]);
    }
    private void EnterFrame(int frame)
    {
        var definition=Definition.Frames[frame];
        if(definition.Sound!=null&&!CueSounds.Muted) location.playSound(definition.Sound);
        if(definition.Dust) owner.Effects.Spawn("dust",location,Actor.Position,Actor);
    }
    public void Update(double ms)
    {
        if(removed)return;
        if(!ExternalClock)Advance(ms);
        if(Timeline.Releasing&&Timeline.Finished){owner.Remove(this);return;}
        bool sourceFrameChanged=owner.UpdateExpressionFallback(this);
        var nextExpression=owner.ExpressionStamp(Actor);
        bool recomposed=sourceFrameChanged||expressionStamp!=nextExpression||(Definition.Facing==null&&composedFacing!=Actor.FacingDirection)||!owner.Compositor.IsCurrent(composed);
        if(recomposed)
        {composed=owner.Compose(Definition,Actor.FacingDirection,Actor);composedFacing=Actor.FacingDirection;expressionStamp=nextExpression;}
        bool mirror=Definition.MirrorForOppositeFacing && ((Definition.Facing=="left"&&Actor.FacingDirection==1)||(Definition.Facing=="right"&&Actor.FacingDirection==3));
        var pivot=composed.Pivot;
        var point=AnimationMath.StandInTopLeft(new(Actor.Position.X+Actor.drawOffset.X,Actor.Position.Y+Actor.drawOffset.Y),Actor.GetSpriteWidthForPositioning(),Actor.Sprite.SpriteHeight,Actor.GetBoundingBox().Height,composed.FrameWidth,composed.FrameHeight,pivot.X,pivot.Y,mirror,Actor.yJumpOffset,Actor.Sprite.SpriteWidth);
        Sprite.position=new Vector2((float)point.X,(float)point.Y)+(Actor is NPC npc&&npc.shakeTimer>0?new Vector2(Game1.random.Next(-2,3),Game1.random.Next(-1,2)):Vector2.Zero);
        if(Actor is Farmer farmer)
            Sprite.position=FarmerPartsSprite.BaseTopLeft(farmer)+new Vector2(32,124)-new Vector2((mirror?composed.FrameWidth-1-pivot.X:pivot.X)*4,pivot.Y*4);
        Sprite.texture=composed.Texture;Sprite.sourceRect=new Rectangle(Timeline.Frame*composed.FrameWidth,0,composed.FrameWidth,composed.FrameHeight);
        Sprite.flipped=mirror;Sprite.layerDepth=Actor.StandingPixel.Y/10000f+.0001f;Sprite.alpha=Visible?1:0;
        AnimationRuntime.SetHidden(Actor,true);
        var current=Definition.Frames[Timeline.Frame];
        if(recomposed||headFrame!=Timeline.Frame||headFacing!=Actor.FacingDirection)
        {headOffset=owner.HeadOffset(Definition,current,Actor.FacingDirection);headFrame=Timeline.Frame;headFacing=Actor.FacingDirection;}
        double top=headOffset;
        HeadPosition=Sprite.position+new Vector2((mirror?composed.FrameWidth-1-7:7)*4,(float)top*4);
        if(Actor is Farmer)HeadPosition=Sprite.position+new Vector2(composed.FrameWidth*2,(float)top*4);
        if(Actor.IsEmoting)emoteOffset.SetValue(AnimationMath.EmoteOffset(composed.Pivot.Y,top));
        if(effectsFrame!=Timeline.Frame)
        {
            visibleEffects.Clear();effectKeys.Clear();
            foreach(var layer in current.Layers)
                if(layer is EffectLayer effect&&effect.Visible&&effect.Effect!=null)
                { visibleEffects.Add(effect);effectKeys.Add(effect.Effect+":"+string.Join(",",effect.Offset)); }
            effectsFrame=Timeline.Frame;
        }
        for(var effectIndex=0;effectIndex<visibleEffects.Count;effectIndex++)
        {
            var effect=visibleEffects[effectIndex];
            string key=effectKeys[effectIndex];
            double previous=effectTimes.GetValueOrDefault(key,elapsed);
            if(!effectTimes.ContainsKey(key))effectTimes[key]=elapsed;
            double interval=effect.Every>0?effect.Every:double.MaxValue;
            while(elapsed-previous>=interval)
            {
                previous+=interval;
                var at=Sprite.position+new Vector2((mirror?composed.FrameWidth-1-effect.Offset[0]:effect.Offset[0])*4,effect.Offset[1]*4);
                owner.Effects.Spawn(effect.Effect!,location,at,Actor,mirror:mirror);
            }
            effectTimes[key]=previous;
        }
    }
    public void Remove()
    {
        if(removed)return;removed=true;
        location.temporarySprites.Remove(Sprite);
        AnimationRuntime.SetHidden(Actor,originalHidden);
        emoteOffset.SetValue(originalEmoteOffset);
    }
}
