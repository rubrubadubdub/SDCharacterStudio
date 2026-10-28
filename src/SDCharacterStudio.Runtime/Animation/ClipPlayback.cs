using SDCharacterStudio.Core;
using SDCharacterStudio.Core.Models;
using StardewValley;
using SDCharacterStudio.Runtime.Costumes;

namespace SDCharacterStudio.Runtime.Animation;

/// <summary>Evaluates authored clip values while raw cue time remains independent of stepped visuals.</summary>
public sealed class ClipPlayback
{
    private readonly AnimationRuntime owner;
    private readonly Clip clip;
    private readonly ClipClock clock;
    private readonly bool hold;
    private readonly List<TrackState> tracks=new();
    private readonly List<Cue> cues=new();
    private readonly Dictionary<Farmer,PartsState> partsStates=new();
    private readonly List<ExpressionTrack> expressionTracks=new();
    private readonly WorldPoint cameraStart,cameraCentre;
    private readonly bool cameraFrozen,hasCamera;
    private double crossed=-1;
    public bool Finished { get; private set; }

    public ClipPlayback(AnimationRuntime owner,Clip clip,bool hold)
    {
        // Reject unsupported tracks before changing any actor, stand-in, viewport or cue state.
        this.owner=owner;this.clip=clip;this.hold=hold;
        clock=new(clip.Duration,clip.StepFps,clip.Loop);
        cameraStart=new(Game1.viewport.X,Game1.viewport.Y);
        cameraCentre=new(cameraStart.X+Game1.viewport.Width/2d,cameraStart.Y+Game1.viewport.Height/2d);
        cameraFrozen=Game1.viewportFreeze;
        hasCamera=clip.Tracks.Any(x=>x.Camera?.Count>0);
        foreach(var definition in clip.Tracks)
        {
            var actor=definition.Actor!=null ? owner.Actor(definition.Actor) : null;
            if(actor==null && (definition.Position?.Count>0 || definition.Facing?.Count>0 || definition.Pose?.Count>0 ||
                definition.Visible?.Count>0 || definition.Emote?.Count>0 || definition.Parts?.Count>0 || definition.Expression?.Count>0)) throw new ArgumentException("Actor tracks require an Actor.");
            if(definition.Parts?.Count>0 && actor is not Farmer)throw new ArgumentException("Parts tracks require a Farmer actor.");
            foreach(var key in definition.Parts??new())
            {
                if(key.Set!=null)owner.Costumes.Validate(key.Set);
                if(key.Visible!=null)FarmerPartVisibility.Validate(key.Visible);
            }
            var track=new TrackState(definition,actor,actor==null?default:new(actor.Position.X,actor.Position.Y),
                actor!=null && owner.LogicalVisible(definition.Actor!));
            tracks.Add(track);
            if(actor is Farmer farmer && definition.Parts?.Count>0)
            {
                if(!partsStates.TryGetValue(farmer,out var state))
                {
                    state=new(owner.Costumes.BeginScope(farmer),owner.PartVisibility(farmer).BeginScope());
                    partsStates.Add(farmer,state);
                    var shared=state;
                    if(clip.Loop)cues.Add(new(0,()=>
                    {
                        shared.Costume.Dispose();shared.Visibility.Dispose();
                        shared.Costume=owner.Costumes.BeginScope(farmer);
                        shared.Visibility=owner.PartVisibility(farmer).BeginScope();
                    }));
                }
                foreach(var key in definition.Parts)
                    cues.Add(new(key.At,()=>
                    {
                        if(key.Set!=null)state.Costume.Apply(key.Set,key.Persist);
                        if(key.Visible!=null)state.Visibility.Apply(key.Visible,key.Persist);
                    }));
            }
            foreach(var key in definition.Emote ?? new())
            {
                if(!Ids.TryEmote(key.Emote?.ToString(),out int id)) throw new ArgumentException($"Unknown emote '{key.Emote}'.");
                cues.Add(new(key.At,()=>track.Actor!.doEmote(id,playSound:true,nextEventCommand:false)));
            }
            foreach(var key in definition.Sound ?? new())
                cues.Add(new(key.At,()=> {if(!CueSounds.Muted)Game1.currentLocation.playSound(key.Cue!,pitch:key.Pitch);}));
            foreach(var key in definition.Effect ?? new())
                cues.Add(new(key.At,()=>owner.Spawn(new EffectBeat {Actor=definition.Actor,Effect=key.Effect,Color=key.Color,Count=key.Count,Duration=key.Duration})));
        }
        foreach(var group in clip.Tracks.Where(x=>x.Expression?.Count>0).GroupBy(x=>x.Actor!,StringComparer.OrdinalIgnoreCase))
        {
            var keys=group.SelectMany(x=>x.Expression!).OrderBy(x=>x.At).ToList();
            foreach(var key in keys){if(key.Expression!=null)_=owner.GetExpression(key.Expression);if(key.Duration<0)throw new ArgumentException("Expression Duration cannot be negative.");}
            expressionTracks.Add(new(group.Key,keys));
        }
    }

    public void Update(double ms)
    {
        if(Finished)return;
        clock.Advance(ms,Game1.activeClickableMenu!=null || Game1.dialogueUp);
        foreach(var track in tracks) Evaluate(track);
        foreach(var track in expressionTracks)
        {
            var key=ExpressionKeyTimeline.Select(track.Keys,clock.Time);
            if(!ReferenceEquals(key,track.Key)||track.Cycle!=clock.Cycle)
            {
                owner.ClearExpression(track.Actor,track.Owner);
                if(key?.Expression!=null)owner.SetExpression(track.Actor,key.Expression,track.Owner,clock.Time-key.At,key.Duration,true);
                track.Key=key;track.Cycle=clock.Cycle;
            }
            else if(key?.Expression!=null)owner.SeekExpression(track.Actor,track.Owner,clock.Time-key.At);
        }
        double to=clip.Loop?clock.Elapsed:Math.Min(clock.Elapsed,clip.Duration);
        foreach(var cue in ClipClock.Crossings(cues,crossed,to,clip.Duration,clip.Loop)) cue.Fire();
        crossed=to;
        if(!clock.Finished)return;
        Finish();
    }

    private void Evaluate(TrackState track)
    {
        var definition=track.Definition;
        bool cycleChanged=track.Cycle!=clock.Cycle;
        if(track.Actor!=null)
        {
            if(definition.Position is {Count:>0} positions)
                track.Actor.Position=AnimationRuntime.ToVector(AnimationMath.PositionAt(positions,clock.VisualTime,track.Origin));
            var facing=Latest(definition.Facing,clock.Time);
            if(facing!=null && (!ReferenceEquals(facing,track.Facing) || cycleChanged))
            {
                if(!Ids.Facings.TryGetValue(facing.Facing??"",out int direction)) throw new ArgumentException("Unknown clip facing.");
                track.Actor.faceDirection(direction);
            }
            track.Facing=facing;
            var pose=Latest(definition.Pose,clock.Time);
            if(!ReferenceEquals(pose,track.Pose) || cycleChanged)
            {
                if(pose!=null)
                {
                    if(pose.Pose==null) {owner.Release(definition.Actor!,false);track.Stand=null;}
                    else track.Stand=owner.SetPose(definition.Actor!,pose.Pose,pose.Play,pose.Speed,external:true);
                }
                else if(cycleChanged && track.Stand!=null && ReferenceEquals(owner.Find(definition.Actor!),track.Stand))
                {owner.Remove(track.Stand);track.Stand=null;}
                track.Pose=pose;
            }
            if(track.Stand!=null && pose!=null && ReferenceEquals(owner.Find(definition.Actor!),track.Stand))
                track.Stand.SetTime(Math.Max(0,clock.VisualTime-pose.At));
            var visible=Latest(definition.Visible,clock.Time);
            if(visible!=null && (!ReferenceEquals(visible,track.Visible) || cycleChanged)) owner.SetVisible(definition.Actor!,visible.Value);
            else if(visible==null && cycleChanged && definition.Visible?.Count>0) owner.SetVisible(definition.Actor!,track.OriginalVisible);
            track.Visible=visible;
        }
        if(definition.Camera is {Count:>0} cameras)
            CameraMotion.Apply(CameraMotion.Clamp(CameraAt(cameras,clock.VisualTime,cameraCentre)));
        track.Cycle=clock.Cycle;
    }

    private void Finish()
    {
        foreach(var track in expressionTracks){if(hold)owner.HoldExpression(track.Actor,track.Owner);else owner.ClearExpression(track.Actor,track.Owner);}
        foreach(var state in partsStates.Values)
        {
            if(hold){state.Costume.KeepCurrent();state.Visibility.KeepCurrent();}
            state.Costume.Dispose();state.Visibility.Dispose();
        }
        foreach(var track in tracks.Where(x=>x.Actor!=null))
        {
            if(track.Stand!=null && ReferenceEquals(owner.Find(track.Definition.Actor!),track.Stand))
            {
                if(hold)track.Stand.ExternalClock=false;
                else owner.Remove(track.Stand);
            }
            if(!hold)owner.SetVisible(track.Definition.Actor!,track.OriginalVisible);
        }
        if(!hold && hasCamera)
        {Game1.viewport.X=(int)cameraStart.X;Game1.viewport.Y=(int)cameraStart.Y;Game1.viewportFreeze=cameraFrozen;}
        Finished=true;
    }
    public void Cancel()
    {
        foreach(var track in expressionTracks)owner.ClearExpression(track.Actor,track.Owner);
        foreach(var state in partsStates.Values){state.Costume.Dispose();state.Visibility.Dispose();}
        Finished=true;
    }

    private static T? Latest<T>(List<T>? keys,double time) where T:Keyframe
        =>keys?.LastOrDefault(x=>x.At<=time);

    private WorldPoint CameraAt(List<CameraKey> keys,double time,WorldPoint origin)
    {
        if(time<keys[0].At)return origin;
        var i=0;while(i+1<keys.Count && keys[i+1].At<=time)i++;
        // A key centred on a place (D-52) follows where the place is in this game.
        WorldPoint Centre(CameraKey key) {var p=AnimationMath.Position(owner.Places.AbsoluteOrNull(key.Center));return new(p.X+32,p.Y+32);}
        var point=Centre(keys[i]);
        if(i==keys.Count-1)return point;
        var next=keys[i+1];
        return AnimationMath.Lerp(point,Centre(next),AnimationMath.Ease(next.Ease,(time-keys[i].At)/(next.At-keys[i].At)));
    }

    private sealed class Cue:Keyframe
    {
        public Action Fire {get;}
        public Cue(int at,Action fire){At=at;Fire=fire;}
    }

    private sealed class TrackState(ClipTrack definition,Character? actor,WorldPoint origin,bool originalVisible)
    {
        public ClipTrack Definition {get;}=definition;
        public Character? Actor {get;}=actor;
        public WorldPoint Origin {get;}=origin;
        public bool OriginalVisible {get;}=originalVisible;
        public long Cycle {get;set;}=-1;
        public PoseKey? Pose {get;set;}
        public FacingKey? Facing {get;set;}
        public VisibleKey? Visible {get;set;}
        public StandIn? Stand {get;set;}
    }
    private sealed class PartsState(CostumeManager.CostumeScope costume,FarmerPartVisibility.Scope visibility)
    {
        public CostumeManager.CostumeScope Costume=costume;
        public FarmerPartVisibility.Scope Visibility=visibility;
    }
    private sealed class ExpressionTrack(string actor,List<ExpressionKey> keys)
    {
        public string Actor=actor;
        public List<ExpressionKey> Keys=keys;
        public object Owner=new();
        public ExpressionKey? Key;
        public long Cycle=-1;
    }
}
