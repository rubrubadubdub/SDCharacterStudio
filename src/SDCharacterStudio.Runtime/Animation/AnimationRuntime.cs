using System.Diagnostics;
using Microsoft.Xna.Framework;
using SDCharacterStudio.Core;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Runtime.Loading;
using SDCharacterStudio.Runtime.Playback;
using SDCharacterStudio.Runtime.Rendering;
using Newtonsoft.Json.Linq;
using StardewModdingAPI;
using GameEvent=StardewValley.Event;
using StardewValley;
using SDCharacterStudio.Runtime.Costumes;

namespace SDCharacterStudio.Runtime.Animation;

public sealed partial class AnimationRuntime
{
    private readonly PlaybackSession session;
    private readonly PackLoader packs;
    private readonly IMonitor monitor;
    private readonly Dictionary<Character,StandIn> standIns=new();
    private readonly Dictionary<Character,bool> visibility=new();
    private readonly Dictionary<Farmer,FarmerPartVisibility> partVisibility=new();
    private readonly Dictionary<Farmer,FarmerPartsSprite> partSprites=new();
    private readonly Dictionary<string,ComposedPose> cache=new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ComposedPose> textures=new();
    private readonly List<StandIn> updateBuffer=new();
    private readonly List<ComposedPose> disposeBuffer=new();
    private RegisteredCutscene? scene;
    private GameEvent? ev;
    /// <summary>
    /// An ended scene's costume restore, waiting for its event to finish (the game's end fade), so the player's clothes
    /// never change on screen. The owner saw earmuffs vanish the moment the End beat came up.
    /// </summary>
    private GameEvent? restoreCostumesAfter;
    /// <summary>
    /// An ended scene's camera lock, waiting for its event to finish. The camera stays where the scene left it until the
    /// game's end fade; the owner saw it jump back to the scene's start before the fade.
    /// </summary>
    private (GameEvent Event,bool Frozen)? releaseCameraAfter;
    /// <summary>An ended scene's props (Prop beats), staying on the map until its event has finished fading out.</summary>
    private GameEvent? clearPropsAfter;
    private ClipPlayback? clip;
    private CameraMotion? camera;
    private bool cameraFrozen;
    private int cameraX,cameraY;
    public IReflectionHelper Reflection { get; }
    public PoseCompositor Compositor { get; }
    public EffectManager Effects { get; }
    /// <summary>Items the scene put on the map (Prop beats, D-50).</summary>
    public PropManager Props { get; } = new();
    public GeneratedAssets Generated { get; }
    public CostumeManager Costumes { get; }
    public AnimationRuntime(IModHelper helper,IMonitor monitor,PlaybackSession session,PackLoader packs)
    {
        this.session=session;this.packs=packs;this.monitor=monitor;Reflection=helper.Reflection;
        Costumes=new(monitor);
        Compositor=new(helper,monitor);Generated=new(helper);
        PortraitComposer=new(helper,monitor,Compositor);
        Compositor.PortraitSource=PortraitCostumes.Source;
        PortraitCostumes.Reload=asset=>helper.GameContent.Load<Microsoft.Xna.Framework.Graphics.Texture2D>(asset);
        Compositor.PortraitSourcesInvalidated=PortraitCostumes.Invalidate;
        Releasing+=ReleaseObservedDialogue;
        Compositor.FarmerSource=()=>ev?.farmer??Game1.player;
        Compositor.FilterFarmerParts=FilterParts;
        Compositor.FarmerVisibilityVersion=farmer=>PartVisibility(farmer).Version;
        Effects=new(monitor,id=>Compose(GetPose(id),3),GetPose);
        session.Cleaning+=()=>Clear(deferAssets:true);
        session.ActorsRestored+=restore=>
        {
            try { if(restore)PortraitCostumes.RestoreAll(); }
            finally { ReleaseSceneAssets(); }
        };
        session.VisibilityOverride=VisibilityFor;
        session.DrawHiddenFarmer=DrawFarmerEmote;
    }
    public void Begin(GameEvent gameEvent,RegisteredCutscene entry)
    {
        if(ReferenceEquals(ev,gameEvent)&&ReferenceEquals(scene,entry))return;
        FinishCostumeRestore();   // a new scene starts from the player's own look
        if(releaseCameraAfter is {} release){releaseCameraAfter=null;Game1.viewportFreeze=release.Frozen;}
        clearPropsAfter=null;Props.Clear();
        ev=gameEvent;scene=entry;
        cameraFrozen=Game1.viewportFreeze;cameraX=Game1.viewport.X;cameraY=Game1.viewport.Y;
        foreach(var actor in entry.Source.Actors.Where(x=>x.Costume!=null))
        {
            if(Actor(actor.Id!) is not Farmer farmer)throw new ArgumentException("Initial costumes require a Farmer actor.");
            ApplyCostume(farmer,actor.Costume!);
        }
        var watch=Stopwatch.StartNew();
        var beats=entry.Compiled.Beats.Values.Select(x=>x.Beat).ToArray();
        var names=beats.OfType<PoseBeat>().Select(x=>x.Pose)
            .Concat(entry.Source.Clips.Values.SelectMany(x=>x.Tracks).SelectMany(x=>x.Pose??new()).Select(x=>x.Pose))
            .Concat(beats.OfType<CostumeBeat>().Select(x=>x.Sprite?.Pose))
            .Concat(entry.Source.Actors.Select(x=>x.Sprite?.Pose));
        // Precomposition is an optimisation: one unusable pose logs once and fails again only at its own beat (doc 12 §4.3).
        foreach(string id in names.Where(x=>x!=null).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try{var pose=GetPose(id);if(pose.Kind=="sheet")Sheet(pose);else Compose(pose,3);}
            catch(Exception ex){monitor.Log($"{entry.Compiled.Key}: couldn't prepare pose '{id}'. {ex.Message}",LogLevel.Error);}
        }
        foreach(var actor in entry.Source.Actors.Where(x=>x.Sprite?.Pose!=null))
        {
            try{var target=(NPC)Actor(actor.Id!);target.Sprite.LoadTexture(Sheet(GetPose(actor.Sprite!.Pose!)),syncTextureName:false);target.spriteOverridden=true;}
            catch(Exception ex){monitor.Log($"{entry.Compiled.Key}: couldn't apply sheet pose '{actor.Sprite!.Pose}' to '{actor.Id}'; using the normal sprite. {ex.Message}",LogLevel.Error);}
        }
        foreach(string id in beats.OfType<SayBeat>().Select(x=>x.Portrait).Concat(beats.OfType<CostumeBeat>().Select(x=>x.Portrait)).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try{ComposePortrait(id);}
            catch(Exception ex){monitor.Log($"{entry.Compiled.Key}: couldn't prepare portrait '{id}'. {ex.Message}",LogLevel.Error);}
        }
        monitor.Log($"Precomposed cutscene '{entry.Compiled.Key}' in {watch.Elapsed.TotalMilliseconds:F2} ms.",LogLevel.Trace);
    }
    /// <summary>Pay first-use JIT/graphics setup once when a world becomes ready, before scene entry.</summary>
    public void Prepare(IEnumerable<RegisteredCutscene> entries)
    {
        var watch=Stopwatch.StartNew();
        foreach(var entry in entries)
        {
            var pack=packs.FindPack(entry.PackId);
            if(pack==null)continue;
            foreach(var pose in entry.Pack.Poses.Select(x=>x.Value).Where(x=>x.Kind=="character"))
            {
                try
                {
                    using var composed=Compositor.Compose(pack,entry.Pack,pose,name=>Game1.getCharacterFromName(name),3);
                    monitor.Log($"Prepared animation pipeline in {watch.Elapsed.TotalMilliseconds:F2} ms.",LogLevel.Trace);
                    return;
                }
                catch(Exception ex){monitor.Log($"Pose '{pose.Id}' is not available for early preparation: {ex.Message}",LogLevel.Trace);}
            }
        }
    }
    public Character Actor(string id)=>PlaybackSession.ResolveActor(ev??throw new InvalidOperationException("No active animation event."),id);
    public Dictionary<string,JToken> CostumeValues(string preset)=>scene?.Pack.Costumes?.Value.Costumes.FirstOrDefault(x=>string.Equals(x.Id,preset,StringComparison.OrdinalIgnoreCase))?.Set
        ??throw new ArgumentException($"Unknown costume preset '{preset}'.");
    private void ApplyCostume(Farmer farmer,JToken spec)
    {
        if(spec.Type==JTokenType.String)Costumes.Apply(farmer,CostumeValues(spec.Value<string>()!));
        else if(spec is JObject obj)Costumes.Apply(farmer,(obj["Set"]??obj).ToObject<Dictionary<string,JToken>>()??throw new ArgumentException("Invalid inline costume."));
        else throw new ArgumentException("Initial costume must be a preset ID or an object.");
    }
    public FarmerPartVisibility PartVisibility(Farmer farmer)
    {
        if(!partVisibility.TryGetValue(farmer,out var state))partVisibility[farmer]=state=new();
        return state;
    }
    public string FilterParts(Farmer farmer,string requested)
    {
        var parts=FarmerParts.Parse(requested);
        var state=PartVisibility(farmer);
        foreach(string name in new[]{"Base","Head","Body","Eyes","Pants","Shirt","Accessory","Hair","Hat","Arms","Shoes"})
            if(!state.Visible(name))parts&=~FarmerParts.Parse(name);
        return string.Join("+",new[]{FarmerPart.Head,FarmerPart.Body,FarmerPart.Pants,FarmerPart.Eyes,FarmerPart.Shirt,FarmerPart.Accessory,FarmerPart.Hair,FarmerPart.Hat,FarmerPart.Arms,FarmerPart.Shoes}.Where(x=>parts.HasFlag(x)).Select(x=>x.ToString()).DefaultIfEmpty("None"));
    }
    public Pose GetPose(string id)=>scene?.Pack.Poses.FirstOrDefault(x=>string.Equals(x.Value.Id,id,StringComparison.OrdinalIgnoreCase))?.Value??throw new ArgumentException("Unknown pose '"+id+"'.");
    private NPC? Npc(string name)=>ev?.actors.FirstOrDefault(x=>string.Equals(x.Name,name,StringComparison.OrdinalIgnoreCase))??Game1.getCharacterFromName(name);
    public ComposedPose Compose(Pose pose,int facing,Character? actor=null)
    {
        var expression=actor!=null?ExpressionFor(actor):null;
        if(expression?.Definition.Target.Equals("Portrait",StringComparison.OrdinalIgnoreCase)==true)expression=null;
        string key=pose.Id+":"+facing+":"+(actor==null?0:System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(actor))+":"+(expression?.Definition.Id??"")+":"+(expression?.Frame??0);
        if(pose.Id?.StartsWith("__expression_",StringComparison.Ordinal)==true)key+=":"+pose.Frames[0].Layers.OfType<LiveLayer>().First().SourceFrame?.Index;
        if(cache.TryGetValue(key,out var result)&&Compositor.IsCurrent(result))return result;
        var pack=packs.FindPack(scene!.PackId)??throw new InvalidOperationException("Pack unavailable.");
        result=Compositor.Compose(pack,scene.Pack,pose,Npc,facing,expression);cache[key]=result;textures.Add(result);return result;
    }
    private string Sheet(Pose pose)
    {
        string key="sheet:"+pose.Id;
        string npc=scene!.Pack.Characters.FirstOrDefault(x=>string.Equals(x.Value.Id,pose.For,StringComparison.OrdinalIgnoreCase))?.Value.Npc??pose.For??throw new ArgumentException("Sheet needs a rig.");
        string name="Characters/"+NPC.getTextureNameForCharacter(npc)+"_SDCS_"+Ids.PackHash(scene.PackId)+"_"+pose.Id;
        if(cache.ContainsKey(key))return name;
        var composed=Compositor.ComposeSheet(packs.FindPack(scene.PackId)!,scene.Pack,pose,Npc,3);
        textures.Add(composed);cache[key]=composed;Generated.Register(name,composed.Texture);return name;
    }
    public string SheetSuffix(string id){Sheet(GetPose(id));return "SDCS_"+Ids.PackHash(scene!.PackId)+"_"+id;}
    public StandIn SetPose(string actor,string id,string play,double speed=1,bool external=false)
    {
        var target=Actor(actor);if(standIns.TryGetValue(target,out var old))Remove(old);
        var pose=GetPose(id);if(pose.Kind!="character")throw new ArgumentException("Actor pose must have Kind:character.");
        var stand=new StandIn(this,target,pose,Compose(pose,target.FacingDirection,target),play,speed,external);
        stand.Visible=LogicalVisible(actor);standIns[target]=stand;return stand;
    }
    public StandIn? Find(string actor)=>standIns.GetValueOrDefault(Actor(actor));
    public void Remove(StandIn stand){if(standIns.GetValueOrDefault(stand.Actor)==stand)standIns.Remove(stand.Actor);expressionFallbacks.Remove(stand);stand.Remove();}
    public void Release(string actor,bool outro)
    {if(Find(actor) is{} stand){if(outro)stand.Release();else Remove(stand);}}
    public void SetVisible(string actor,bool value)
    {var target=Actor(actor);visibility[target]=value;if(standIns.TryGetValue(target,out var stand))stand.Visible=value;else SetHidden(target,!value);}
    public bool LogicalVisible(string actor)
    {
        if(visibility.TryGetValue(Actor(actor),out var value))return value;
        foreach(var entry in scene!.Source.Actors)if(string.Equals(entry.Id,actor,StringComparison.OrdinalIgnoreCase))return entry.Visible;
        return true;
    }
    public double HeadOffset(Pose pose,PoseFrame frame,int facing)
    {
        LiveLayer? head=null;
        for(var i=frame.Layers.Count-1;i>=0;i--)
            if(frame.Layers[i] is LiveLayer live&&live.Visible&&HasPart(live.Part,"Head")){head=live;break;}
        if(head==null)return 0;
        bool farmerSource=string.Equals(head.Source?.Kind,"Farmer",StringComparison.OrdinalIgnoreCase);
        double offset=head.Offset[1]+(farmerSource?16:0);
        if(head.AnchorTo?.Anchor is not{} anchor||!frame.Anchors.TryGetValue(anchor,out var target))return offset;
        double fromY;
        if(head.AnchorTo.From is JArray point)fromY=(double)point[1];
        else
        {
            string direction=head.SourceFrame?.Facing??pose.Facing??(facing switch{0=>"up",1=>"right",2=>"down",_=>"left"});
            if(string.Equals(head.Source?.Kind,"Farmer",StringComparison.OrdinalIgnoreCase))
            {
                int d=Ids.Facings[direction];int index=head.SourceFrame?.Index??(d switch{0=>12,1 or 3=>6,_=>0});
                fromY=Compositor.Farmers.GetNeckAnchor(ev?.farmer??Game1.player,index,d).Y;
            }
            else
            {
                var rig=RigSelection.Select(scene!.Pack.Characters.Select(x=>x.Value),pose.For,head.Source?.Npc);
                if(head.SourceFrame?.Index is{} index)direction=rig.Frames.FirstOrDefault(x=>x.Value.Stand==index||x.Value.Walk.Contains(index)).Key??direction;
                fromY=rig.NeckPoint.TryGetValue(direction,out var neck)?neck[1]:0;
            }
        }
        return offset+target[1]-fromY;
    }
    private static bool HasPart(string parts,string wanted)
    {
        var start=0;
        while(start<parts.Length)
        {
            var end=parts.IndexOf('+',start);
            if(end<0)end=parts.Length;
            if(parts.AsSpan(start,end-start).Equals(wanted.AsSpan(),StringComparison.OrdinalIgnoreCase))return true;
            start=end+1;
        }
        return false;
    }
    public static bool Hidden(Character actor)=>actor is NPC npc?npc.IsInvisible:((Farmer)actor).hidden.Value;
    public static void SetHidden(Character actor,bool value){if(actor is NPC npc)npc.IsInvisible=value;else ((Farmer)actor).hidden.Value=value;}
    public void StartClip(string id,bool hold)
    {if(scene==null||!scene.Source.Clips.TryGetValue(id,out var definition))throw new ArgumentException("Unknown clip '"+id+"'.");if(definition.Tracks.Any(x=>x.Camera?.Count>0))camera=null;clip=new(this,definition,hold);}
    public bool ClipFinished=>clip?.Finished!=false;
    public void SettleClipParts()=>clip?.Cancel();
    public CameraMotion StartCamera(CameraBeat beat){camera=new(beat);return camera;}
    public void Spawn(EffectBeat beat)
    {
        var actor=beat.Actor!=null?Actor(beat.Actor):null;
        Vector2 point=beat.At!=null?ToVector(AnimationMath.Position(beat.At)):actor!=null?actor.Position+new Vector2(32,-72):Vector2.Zero;
        if(beat.Actor!=null&&Find(beat.Actor) is{} stand)point=stand.HeadPosition;
        Effects.Spawn(beat.Effect!,Game1.currentLocation,point,actor,beat.Count,beat.Color,beat.Duration,actor?.FacingDirection==1);
    }
    public void Update(double ms)
    {
        if(restoreCostumesAfter!=null&&!ReferenceEquals(Game1.CurrentEvent,restoreCostumesAfter))FinishCostumeRestore();
        if(releaseCameraAfter is {} release&&!ReferenceEquals(Game1.CurrentEvent,release.Event)){releaseCameraAfter=null;Game1.viewportFreeze=release.Frozen;}
        if(clearPropsAfter!=null&&!ReferenceEquals(Game1.CurrentEvent,clearPropsAfter)){clearPropsAfter=null;Props.Clear();}
        if(scene==null)return;
        UpdateExpressions(ms);
        clip?.Update(ms);
        UpdateExpressions(0);
        foreach(var portrait in PortraitCostumes.Active)
        {
            if(!ReferenceEquals(portrait.Scope,scene))continue;
            if(!scene.Pack.Portraits.Any(x=>string.Equals(x.Value.Id,portrait.Id,StringComparison.OrdinalIgnoreCase)))continue;
            var current=ComposePortrait(portrait.Id);
            if(!ReferenceEquals(current.Texture,portrait.Generated))PortraitCostumes.Apply(portrait.Actor,portrait.Id,current.Texture,scene);
        }
        Updating?.Invoke(ms);
        UpdateObservedDialogue();
        if(camera!=null){camera.Update(ms,Game1.activeClickableMenu!=null||Game1.dialogueUp);if(camera.Finished)camera=null;}
        updateBuffer.Clear();
        foreach(var stand in standIns.Values)updateBuffer.Add(stand);
        foreach(var stand in updateBuffer)stand.Update(ms);
        foreach(var pair in partVisibility)
        {
            bool needed=!pair.Value.IsDefault&&!standIns.ContainsKey(pair.Key);
            if(needed)
            {
                if(!partSprites.TryGetValue(pair.Key,out var sprite))partSprites[pair.Key]=sprite=new(this,pair.Key);
                sprite.Update(LogicalVisible("farmer"));
            }
            else if(partSprites.Remove(pair.Key,out var sprite))sprite.Dispose();
        }
        Effects.Update(ms);
        // Recomposition can happen every blink/prismatic update. Retain an old strip only while
        // one of our sprites/effects still borrows it; current cache entries remain reusable.
        disposeBuffer.Clear();
        for(var i=0;i<textures.Count;i++)
        {
            var texture=textures[i];
            if(cache.Values.Contains(texture))continue;
            var borrowed=false;
            foreach(var stand in standIns.Values)
                if(ReferenceEquals(stand.Sprite.texture,texture.Texture)){borrowed=true;break;}
            if(!borrowed&&!Effects.UsesTexture(texture.Texture))disposeBuffer.Add(texture);
        }
        foreach(var texture in disposeBuffer){texture.Dispose();textures.Remove(texture);}
    }
    public void Clear(bool deferAssets=false)
    {
        if(Releasing!=null)foreach(Action release in Releasing.GetInvocationList())
            try{release();}catch(Exception ex){monitor.Log($"Expression/dialogue cleanup failed: {ex}",LogLevel.Error);}
        ClearExpressions();
        clip?.Cancel();clip=null;camera=null;
        Effects.Clear();
        foreach(var sprite in partSprites.Values)sprite.Dispose();partSprites.Clear();
        foreach(var stand in standIns.Values.ToArray())stand.Remove();standIns.Clear();visibility.Clear();
        foreach(var state in partVisibility.Values)state.Clear();partVisibility.Clear();
        updateBuffer.Clear();disposeBuffer.Clear();
        if(session.ForceRestoreAppearance)Costumes.RestoreAll();
        else if(scene?.Source.OnEnd.RestoreCostume==true)
        {
            // The natural end runs before the game's fade: put the clothes back once the event is gone, behind the black.
            if(ev!=null&&ReferenceEquals(Game1.CurrentEvent,ev))restoreCostumesAfter=ev;
            else Costumes.RestoreAll();
        }
        // Props stay through the game's end fade on a natural end (nothing vanishes on screen); otherwise they go now.
        if(!session.ForceRestoreAppearance&&ev!=null&&ReferenceEquals(Game1.CurrentEvent,ev))clearPropsAfter=ev;
        else{clearPropsAfter=null;Props.Clear();}
        if(scene!=null&&(Game1.CurrentEvent==null||ReferenceEquals(Game1.CurrentEvent,ev)))
        {
            // A natural end runs before the game's fade: the camera stays where the scene left it (no jump on screen) and
            // the game recentres behind the black. A stopped preview or an unexpected end puts it back at once.
            if(!session.ForceRestoreAppearance&&ev!=null&&ReferenceEquals(Game1.CurrentEvent,ev))releaseCameraAfter=(ev,cameraFrozen);
            else{Game1.viewportFreeze=cameraFrozen;Game1.viewport.X=cameraX;Game1.viewport.Y=cameraY;}
        }
        scene=null;ev=null;
        if(!deferAssets)ReleaseSceneAssets();
    }
    private void ReleaseSceneAssets()
    {
        // Session snapshots and persistent portrait copies must stop borrowing generated
        // textures before invalidation/disposal, including forced preview cleanup.
        Generated.Clear();
        foreach(var texture in textures)texture.Dispose();textures.Clear();cache.Clear();Compositor.Clear();
    }
    private void FinishCostumeRestore()
    {
        if(restoreCostumesAfter==null)return;
        restoreCostumesAfter=null;
        Costumes.RestoreAll();
    }
    public void RestoreCostumes()
    {
        restoreCostumesAfter=null;
        clip?.Cancel();Costumes.RestoreAll();
        foreach(var state in partVisibility.Values)state.Clear();
    }
    private void DrawFarmerEmote(Farmer farmer,Microsoft.Xna.Framework.Graphics.SpriteBatch batch)
    {
        if(!farmer.IsEmoting)return;
        Vector2 head;
        if(standIns.TryGetValue(farmer,out var stand)&&stand.Visible)head=stand.HeadPosition;
        else if(partSprites.ContainsKey(farmer)&&LogicalVisible("farmer"))head=FarmerPartsSprite.BaseTopLeft(farmer)+new Vector2(32,0);
        else return;
        int index=farmer.CurrentEmoteIndex;
        batch.Draw(Game1.emoteSpriteSheet,Game1.GlobalToLocal(Game1.viewport,head-new Vector2(32,64)),
            new Rectangle(index*16%Game1.emoteSpriteSheet.Width,index*16/Game1.emoteSpriteSheet.Width*16,16,16),Color.White,0,Vector2.Zero,4,Microsoft.Xna.Framework.Graphics.SpriteEffects.None,farmer.getDrawLayer());
    }
    public static Vector2 ToVector(WorldPoint point)=>new((float)point.X,(float)point.Y);
    private bool? VisibilityFor(Character actor)=>standIns.ContainsKey(actor)||(actor is Farmer farmer&&partSprites.ContainsKey(farmer))?true:visibility.TryGetValue(actor,out var visible)?!visible:null;
}
