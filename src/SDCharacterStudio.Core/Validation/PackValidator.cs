using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core.Loading;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Serialization;

namespace SDCharacterStudio.Core.Validation;

public static class PackValidator
{
    private static readonly Regex IdPattern=new("^[a-z0-9][a-z0-9._-]*$",RegexOptions.CultureInvariant);
    private static readonly HashSet<string> Slots=new(Palette.ColorSlots.Names,StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> FarmerSamples=new(new[]{"Skin","Hair","Eyes","Shirt","Pants","Shoes"},StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ArgumentFields=new(new[]{"Actor","Npc","Facing","Cue","Add","Remove","Item","Preset","Portrait","Expression","Pose","Clip","Video","Effect","Branch","Then","Command","Music","Conditions","OnDayAfterFlag","Kind","Play","Path","Ease","Emotion","Emote","Speed","End","Camera"},StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ContentReferenceFields=new(new[]{"Preset","Portrait","Expression","Pose","Clip","Video","Mask","Branch","Then"},StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> BeatTypes=new(typeof(Beat).Assembly.GetTypes().Where(t=>t.IsSubclassOf(typeof(Beat)) && t!=typeof(UnknownBeat)).Select(t=>((Beat)Activator.CreateInstance(t)!).Type),StringComparer.OrdinalIgnoreCase);
    public static IReadOnlyList<ValidationIssue> ValidateCutscene(Cutscene scene,string file,ICatalog? catalog=null)
    {
        var issues=new List<ValidationIssue>();
        void Error(string path,string message,string rule="Schema") => issues.Add(new(ValidationSeverity.Error,file,path,message,rule));
        if(scene==null){Error("","Cutscene must be an object.");return issues;}
        CheckFormat(scene.Format,file,"Format",issues);CheckId(scene.Id,file,"Id",issues);
        if(string.IsNullOrWhiteSpace(scene.Location))Error("Location","Location is required.","V15");
        else if(catalog!=null&&!catalog.LocationExists(scene.Location))Error("Location",$"Unknown location '{scene.Location}'.","V15");
        if(scene.Actors==null)Error("Actors","Actors must be an array.");
        else
        {
            var ids=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for(var i=0;i<scene.Actors.Count;i++)
            {
                var actor=scene.Actors[i];var path=$"Actors[{i}]";
                if(actor==null){Error(path,"Actor must be an object.");continue;}
                if(string.IsNullOrWhiteSpace(actor.Id))Error(path+".Id","Actor name is required.","V7");
                else if(!ids.Add(actor.Id))Error(path+".Id","Duplicate actor name.","V3");
                if(string.Equals(actor.Kind,"Npc",StringComparison.OrdinalIgnoreCase))
                {
                    if(!string.Equals(actor.Id,actor.Npc??actor.Id,StringComparison.OrdinalIgnoreCase))Error(path+".Id","NPC actor ID must equal an existing NPC internal name.","V7");
                    else if(catalog!=null && !string.IsNullOrWhiteSpace(actor.Id) && NpcNameProblem(catalog,actor.Id) is { } problem)Error(path+".Id",problem,"V7");
                }
                else if(string.Equals(actor.Kind,"Farmer",StringComparison.OrdinalIgnoreCase)){if(!string.Equals(actor.Id,"farmer",StringComparison.OrdinalIgnoreCase))Error(path+".Id","Farmer actor ID must be farmer.","V7");}
                else if(!string.Equals(actor.Kind,"Temporary",StringComparison.OrdinalIgnoreCase))Error(path+".Kind","Expected Npc, Farmer, or Temporary.");
                else if(string.IsNullOrWhiteSpace(actor.Sprite?.Asset))Error(path+".Sprite.Asset","Temporary actors require a sprite asset.");
                CheckPosition(actor.Start,path+".Start",Error);
            }
        }
        var branches=scene.Branches??new(StringComparer.OrdinalIgnoreCase);
        CheckDictionaryIds(branches.Keys,file,"Branches",issues);
        if(scene.Branches==null)Error("Branches","Branches must be an object.");
        void Beats(List<Beat>? beats,string basePath,bool onSkip=false)
        {
            if(beats==null){Error(basePath,"Beat list must be an array.");return;}
            for(var i=0;i<beats.Count;i++)
            {
                var beat=beats[i];var path=$"{basePath}[{i}]";
                if(beat==null){Error(path,"Beat must be an object.");continue;}
                if(beat is UnknownBeat unknown){Error(path+".Type",$"Unknown beat type '{unknown.Data["Type"]}'.");continue;}
                if(!BeatTypes.Contains(beat.Type))Error(path+".Type","Unknown beat type.");
                var actorName=beat.GetType().GetProperty("Actor")?.GetValue(beat) as string;
                if(beat is SayBeat or MoveBeat or FaceBeat or EmoteBeat or ShakeBeat or JumpBeat or PoseBeat or CostumeBeat && string.IsNullOrWhiteSpace(actorName))Error(path+".Actor","Actor is required.");
                else if(actorName!=null && !(scene.Actors?.Any(a=>a!=null&&string.Equals(a.Id,actorName,StringComparison.OrdinalIgnoreCase))??false))Error(path+".Actor",$"Unknown actor '{actorName}'.","Reference");
                if(beat is EffectBeat effect && string.IsNullOrWhiteSpace(effect.Actor)&&effect.At==null)Error(path+".At","Effect requires an Actor or At position.");
                if(beat is PropBeat prop)
                {
                    if(!prop.Remove&&string.IsNullOrWhiteSpace(prop.Item))Error(path+".Item","Prop requires an item.");
                    if(prop.At==null)Error(path+".At","Prop requires an At tile.");else CheckPosition(prop.At,path+".At",Error);
                }
                if(onSkip && beat is not (MailBeat or FriendshipBeat or MoneyBeat or ItemBeat or CostumeBeat or NumberBeat))Error(path+".Type","OnSkip permits Mail, Friendship, Money, Item, Costume and Number only.");
                if(beat is NumberBeat number)
                {
                    if(number.Name==null||!IdPattern.IsMatch(number.Name))Error(path+".Name","A story number needs a name like jokes-told (lowercase letters, digits, dots and dashes).");
                    if(number.Change is not ("add" or "subtract" or "set"))Error(path+".Change","Change must be add, subtract or set.");
                    if(number.Amount<0)Error(path+".Amount","The amount can't be negative: use subtract.");
                    if(number.UpTo is { } upTo && upTo<number.Amount)Error(path+".UpTo","The random amount's top must be at least the amount.");
                }
                if(beat is ChoiceBeat choice)
                {
                    if(choice.Options==null||choice.Options.Count==0)Error(path+".Options","Choice requires at least one option.");
                    else for(var j=0;j<choice.Options.Count;j++)
                    {
                        var option=choice.Options[j];var optionPath=$"{path}.Options[{j}]";
                        if(option==null){Error(optionPath,"Option must be an object.");continue;}
                        CheckBranch(option.Then,optionPath+".Then");if(option.Text==null)Error(optionPath+".Text","Option text is required.");
                        if(option.When!=null&&string.IsNullOrWhiteSpace(option.When))Error(optionPath+".When","An answer's condition can't be empty.");
                    }
                }
                if(beat is GotoBeat go)CheckBranch(go.Branch,path+".Branch");
                if(beat is ChanceBeat chance)
                {
                    if(chance.Outcomes==null||chance.Outcomes.Count==0)Error(path+".Outcomes","Chance requires at least one outcome.");
                    else
                    {
                        for(var j=0;j<chance.Outcomes.Count;j++)
                        {
                            var outcome=chance.Outcomes[j];var outcomePath=$"{path}.Outcomes[{j}]";
                            if(outcome==null){Error(outcomePath,"Outcome must be an object.");continue;}
                            if(outcome.Weight<0)Error(outcomePath+".Weight","A chance can't be negative.");
                            CheckBranch(outcome.Then,outcomePath+".Then");
                        }
                        if(chance.Outcomes.Where(o=>o!=null).Sum(o=>Math.Max(0,o.Weight))==0)Error(path+".Outcomes","At least one outcome needs a chance above 0.");
                    }
                }
                if(beat is ItemBeat { OneOf: {Count:>0} oneOf })
                {
                    if(onSkip)Error(path+".OneOf","A random item can't be given on skip: pick one item.");
                    for(var j=0;j<oneOf.Count;j++)
                    {
                        var pick=oneOf[j];var pickPath=$"{path}.OneOf[{j}]";
                        if(pick==null){Error(pickPath,"Item must be an object.");continue;}
                        if(string.IsNullOrWhiteSpace(pick.Item))Error(pickPath+".Item","Pick an item.");
                        if(pick.Count<1)Error(pickPath+".Count","Give at least one.");
                        if(pick.Weight<0)Error(pickPath+".Weight","A chance can't be negative.");
                    }
                    if(oneOf.Where(c=>c!=null).Sum(c=>Math.Max(0,c.Weight))==0)Error(path+".OneOf","At least one item needs a chance above 0.");
                }
                if(beat is CheckBeat check)
                {
                    if(check.Cases==null||check.Cases.Count==0)Error(path+".Cases","Check requires at least one case.");
                    else for(var j=0;j<check.Cases.Count;j++)
                    {
                        var @case=check.Cases[j];var casePath=$"{path}.Cases[{j}]";
                        if(@case==null){Error(casePath,"Case must be an object.");continue;}
                        if(string.IsNullOrWhiteSpace(@case.When))Error(casePath+".When","A case needs a condition.");
                        CheckBranch(@case.Then,casePath+".Then");
                    }
                    if(check.Otherwise!=null)CheckBranch(check.Otherwise,path+".Otherwise");
                }
                if(beat is SayBeat say && say.Text==null)Error(path+".Text","Text is required.");
                if(beat is MessageBeat message && message.Text==null)Error(path+".Text","Text is required.");
                if(beat is MoveBeat move)CheckPosition(move.To,path+".To",Error);
            }
        }
        void CheckBranch(string? name,string path){if(string.IsNullOrEmpty(name)||!branches.Keys.Contains(name,StringComparer.OrdinalIgnoreCase))Error(path,$"Unknown branch '{name}'.","V8");}
        Beats(scene.Beats,"Beats");foreach(var branch in branches)Beats(branch.Value,"Branches."+branch.Key);if(scene.OnSkip!=null)Beats(scene.OnSkip,"OnSkip",true);
        CheckCycles(branches,file,issues);
        if(scene.Clips==null)Error("Clips","Clips must be an object.");
        else
        {
            CheckDictionaryIds(scene.Clips.Keys,file,"Clips",issues);
            foreach(var clip in scene.Clips)
            {
                var path="Clips."+clip.Key;
                if(clip.Value==null){Error(path,"Clip must be an object.");continue;}
                if(clip.Value.Duration<0)Error(path+".Duration","Clip duration cannot be negative.");
                CheckClipTracks(clip.Value.Tracks,clip.Value.Duration,file,path,issues);
            }
        }
        foreach(var (value,path) in Walk(scene))
        {
            if(value is not TextValue text)continue;
            if(text.Literal is { } literal)
            {
                if(literal.Contains('/')||literal.Contains('"'))issues.Add(new(ValidationSeverity.Error,file,path,"Text may not contain '/' or straight double quotes.","V5",()=>text.Literal=FixText(text.Literal??"")));
                if((path.Contains(".Options[",StringComparison.Ordinal)||path.EndsWith(".Prompt",StringComparison.Ordinal))&&(literal.Contains('#')||literal.Contains('\\')||literal.Contains("(break)",StringComparison.OrdinalIgnoreCase)))Error(path,"Choice text may not contain '#', '\\', or '(break)'.","V6");
            }
            else if(string.IsNullOrWhiteSpace(text.I18n))Error(path,"Text must contain a literal or i18n key.");
        }
        // File/asset paths are registry inputs, not event arguments. Check only schema fields emitted as arguments.
        foreach(var (token,path) in Tokens(JObject.FromObject(scene,PackJson.CreateSerializer())))
        {
            if(token.Type!=JTokenType.String)continue;
            var name=(token.Parent as JProperty)?.Name??"";
            if((ArgumentFields.Contains(name)||path.StartsWith("Trigger.Preconditions[",StringComparison.Ordinal)) && token.Value<string>()!.Contains('/'))Error(path,"Event arguments may not contain '/'.","V5");
            if(ContentReferenceFields.Contains(name))CheckId(token.Value<string>(),file,path,issues);
        }
        return issues;
    }

    /// <summary>Vanilla event commands find actors by exact internal name, so a case mismatch is an error too.</summary>
    internal static string? NpcNameProblem(ICatalog catalog,string name)
    {
        var actual=catalog.FindNpc(name);
        if(actual==null)
        {
            var hint=catalog.SuggestNpc(name);
            return $"No NPC has the internal name '{name}'."+(hint!=null?$" Did you mean '{hint}'?":"")+" If the NPC comes from another mod, that mod isn't installed.";
        }
        return string.Equals(actual,name,StringComparison.Ordinal) ? null : $"NPC internal names are case-sensitive in events: use '{actual}', not '{name}'.";
    }

    public static IReadOnlyList<ValidationIssue> ValidateEventKey(string packUniqueId,Cutscene scene,string file)
    {
        var key=packUniqueId+"."+scene?.Id;
        return string.IsNullOrWhiteSpace(packUniqueId)||string.IsNullOrWhiteSpace(scene?.Id)||key.Contains('/')||key.All(char.IsDigit) ? new[]{new ValidationIssue(ValidationSeverity.Error,file,"Id","Event key must contain a nonnumeric ID and no '/'.","V17")} : Array.Empty<ValidationIssue>();
    }
    public static IReadOnlyList<ValidationIssue> ValidateScriptLength(string script,string file,string path="") => System.Text.Encoding.UTF8.GetByteCount(script)>=65536 ? new[]{new ValidationIssue(ValidationSeverity.Warning,file,path,"Compiled event is at least 64 KB.","V18")} : Array.Empty<ValidationIssue>();

    public static IReadOnlyList<ValidationIssue> ValidatePack(LoadedPack pack,IPackFileSystem? files=null,ICatalog? catalog=null,bool exportValidation=true)
    {
        var issues=new List<ValidationIssue>();
        if(pack.Index is { } index)CheckFormat(index.Format,"studio.json","Format",issues);
        if(pack.Manifest is { } manifest && !string.Equals(manifest.ContentPackFor?.UniqueID,"Player.SDCharacterStudio",StringComparison.OrdinalIgnoreCase))issues.Add(new(ValidationSeverity.Error,"manifest.json","ContentPackFor.UniqueID","ContentPackFor must identify Player.SDCharacterStudio.","Schema"));
        CheckDuplicates(pack.Cutscenes.Select(x=>(x.Value.Id,x.File,"Id")),issues);CheckDuplicates(pack.Characters.Select(x=>(x.Value.Id,x.File,"Id")),issues);CheckDuplicates(pack.Poses.Select(x=>(x.Value.Id,x.File,"Id")),issues);CheckDuplicates(pack.Portraits.Select(x=>(x.Value.Id,x.File,"Id")),issues);
        foreach(var scene in pack.Cutscenes){issues.AddRange(ValidateCutscene(scene.Value,scene.File,catalog));issues.AddRange(ValidateEventKey(pack.Manifest?.UniqueID??"",scene.Value,scene.File));}
        foreach(var rig in pack.Characters)
        {
            CheckFormat(rig.Value.Format,rig.File,"Format",issues);CheckId(rig.Value.Id,rig.File,"Id",issues);
            foreach(var map in rig.Value.BodyMap??new())CheckImage(map.Value,rig.Value.FrameSize,false,rig.File,"BodyMap."+map.Key,"V12",files,issues);
            foreach(var sample in rig.Value.ColorSamples??new())
            {
                if(sample.Value?.Fallback is { } fallback && !(rig.Value.ColorSamples?.Keys.Contains(fallback,StringComparer.OrdinalIgnoreCase)??false))issues.Add(new(ValidationSeverity.Error,rig.File,"ColorSamples."+sample.Key+".Fallback",$"Unknown colour sample '{fallback}'.","Reference"));
            }
        }
        if(pack.Masks is { } masks)
        {
            CheckFormat(masks.Value.Format,masks.File,"Format",issues);
            CheckDuplicates((masks.Value.Masks??new()).Where(m=>m!=null).Select((m,i)=>(m.Id,masks.File,$"Masks[{i}].Id")),issues);
            for(var i=0;i<(masks.Value.Masks?.Count??0);i++)
            {
                var mask=masks.Value.Masks![i];var path=$"Masks[{i}]";
                if(mask==null){issues.Add(new(ValidationSeverity.Error,masks.File,path,"Mask must be an object.","Schema"));continue;}
                CheckId(mask.Id,masks.File,path+".Id",issues);CheckImage(mask.Image,mask.FrameSize,false,masks.File,path+".Image","V12",files,issues);
                foreach(var frame in mask.PerFrame??new())CheckImage(frame.Value,mask.FrameSize,false,masks.File,path+".PerFrame."+frame.Key,"V12",files,issues);
            }
        }
        foreach(var pose in pack.Poses)
        {
            CheckFormat(pose.Value.Format,pose.File,"Format",issues);CheckId(pose.Value.Id,pose.File,"Id",issues);
            if(!ValidSize(pose.Value.FrameSize))issues.Add(new(ValidationSeverity.Error,pose.File,"FrameSize","FrameSize must have two positive dimensions.","V11"));
            if(pose.Value.Frames==null||pose.Value.Frames.Count==0)issues.Add(new(ValidationSeverity.Error,pose.File,"Frames","Pose requires frames.","V11"));
            for(var i=0;i<(pose.Value.Frames?.Count??0);i++)
            {
                var frame=pose.Value.Frames![i];var path=$"Frames[{i}]";
                if(frame==null){issues.Add(new(ValidationSeverity.Error,pose.File,path,"Frame must be an object.","Schema"));continue;}
                if(frame.Duration<=0)issues.Add(new(ValidationSeverity.Error,pose.File,path+".Duration","Pose frame duration must be positive.","V11"));
                CheckAnchors(frame.Anchors,pose.File,path+".Anchors",issues);
                ValidateLayers(frame.Layers,frame.Anchors,pose.Value.FrameSize,pose.File,path+".Layers",pack,files,catalog,issues);
                CheckOutfitSources(frame.Layers,pose.File,path+".Layers",pack,true,issues);
            }
            if(Compositing.Outfits.IsOutfit(pose.Value)&&pose.Value.Id!=null&&Compositing.Outfits.Cycle(pack,pose.Value.Id) is {} cycle)
                issues.Add(new(ValidationSeverity.Error,pose.File,"Frames",$"Outfit '{pose.Value.Id}' is made from itself: {string.Join(" → ",cycle)}. Make one of them from her own look.","Reference"));
        }
        CheckDuplicates(pack.Clips.Select(x=>(x.Value.Id,x.File,"Id")),issues);
        foreach(var clip in pack.Clips)ValidateLibraryClip(clip.Value,clip.File,issues);
        foreach(var scene in pack.Cutscenes)ValidateClipCasts(scene.Value,scene.File,pack,issues);
        foreach(var portrait in pack.Portraits)
        {
            CheckFormat(portrait.Value.Format,portrait.File,"Format",issues);CheckId(portrait.Value.Id,portrait.File,"Id",issues);
            foreach(var emotion in portrait.Value.Emotions??new())
            {
                CheckAnchors(emotion.Value?.Anchors,portrait.File,"Emotions."+emotion.Key+".Anchors",issues);
                ValidateLayers(emotion.Value?.Layers,emotion.Value?.Anchors,portrait.Value.FrameSize,portrait.File,"Emotions."+emotion.Key+".Layers",pack,files,catalog,issues);
                CheckOutfitSources(emotion.Value?.Layers,portrait.File,"Emotions."+emotion.Key+".Layers",pack,false,issues);
            }
        }
        if(pack.Expressions is { } expressions)
        {
            CheckFormat(expressions.Value.Format,expressions.File,"Format",issues);CheckDuplicates((expressions.Value.Expressions??new()).Where(e=>e!=null).Select((e,i)=>(e.Id,expressions.File,$"Expressions[{i}].Id")),issues);
            for(var i=0;i<(expressions.Value.Expressions?.Count??0);i++)
            {
                var expression=expressions.Value.Expressions![i];var path=$"Expressions[{i}]";if(expression==null){issues.Add(new(ValidationSeverity.Error,expressions.File,path,"Expression must be an object.","Schema"));continue;}
                CheckId(expression.Id,expressions.File,path+".Id",issues);CheckColorSlots(expression.ColorSlots,expressions.File,path+".ColorSlots",pack,catalog,issues);
                for(var f=0;f<(expression.Frames?.Count??0);f++)CheckImage(expression.Frames![f]?.Image,null,true,expressions.File,$"{path}.Frames[{f}].Image","V4",files,issues);
            }
        }
        if(pack.Costumes is { } costumes)
        {
            CheckFormat(costumes.Value.Format,costumes.File,"Format",issues);CheckDuplicates((costumes.Value.Costumes??new()).Where(c=>c!=null).Select((c,i)=>(c.Id,costumes.File,$"Costumes[{i}].Id")),issues);
            for(var i=0;i<(costumes.Value.Costumes?.Count??0);i++)CheckId(costumes.Value.Costumes![i]?.Id,costumes.File,$"Costumes[{i}].Id",issues);
        }
        ValidateReferencesAndDependencies(pack,files,catalog,exportValidation,issues);
        return issues;
    }

    /// <summary>Track and keyframe checks shared by scene clips (§7) and library clips (§17.1).</summary>
    private static void CheckClipTracks(List<ClipTrack>? tracks,int duration,string file,string path,List<ValidationIssue> issues)
    {
        string Prefix(string rest) => path.Length==0 ? rest : path+"."+rest;
        void Error(string at,string message,string rule="Schema") => issues.Add(new(ValidationSeverity.Error,file,at,message,rule));
        if(tracks==null){Error(Prefix("Tracks"),"Tracks must be an array.");return;}
        for(var i=0;i<tracks.Count;i++)
        {
            var track=tracks[i];var trackPath=Prefix($"Tracks[{i}]");
            if(track==null){Error(trackPath,"Track must be an object.");continue;}
            foreach(var property in typeof(ClipTrack).GetProperties().Where(p=>p.PropertyType.IsGenericType&&p.PropertyType.GetGenericTypeDefinition()==typeof(List<>)))
            {
                if(property.GetValue(track) is not IList keys)continue;
                var keyPath=trackPath+"."+property.Name;var previous=int.MinValue;var unsorted=false;
                for(var k=0;k<keys.Count;k++)
                {
                    if(keys[k] is not Keyframe key){Error($"{keyPath}[{k}]","Keyframe must be an object.");continue;}
                    if(key.At<previous)unsorted=true;previous=key.At;
                    if(key.At<0||key.At>duration)issues.Add(new(ValidationSeverity.Warning,file,$"{keyPath}[{k}].At","Keyframe time must be within the clip duration.","V10"));
                }
                if(unsorted)issues.Add(new(ValidationSeverity.Warning,file,keyPath,"Keyframes must be sorted by At.","V10",()=>{var ordered=keys.Cast<Keyframe?>().OrderBy(k=>k?.At??int.MinValue).ToArray();for(var k=0;k<ordered.Length;k++)keys[k]=ordered[k];}));
            }
        }
    }

    /// <summary>Library clips (doc 03 §17.1): roles, role-addressed tracks, no camera.</summary>
    private static void ValidateLibraryClip(LibraryClip clip,string file,List<ValidationIssue> issues)
    {
        void Error(string at,string message,string rule="Schema") => issues.Add(new(ValidationSeverity.Error,file,at,message,rule));
        CheckFormat(clip.Format,file,"Format",issues);CheckId(clip.Id,file,"Id",issues);
        if(clip.Duration<0)Error("Duration","Clip duration cannot be negative.");
        var roles=new Dictionary<string,ClipRole>(StringComparer.OrdinalIgnoreCase);
        if(clip.Roles==null||clip.Roles.Count==0)Error("Roles","A library clip needs at least one role.");
        for(var i=0;i<(clip.Roles?.Count??0);i++)
        {
            var role=clip.Roles![i];var path=$"Roles[{i}]";
            if(role==null){Error(path,"Role must be an object.");continue;}
            CheckId(role.Id,file,path+".Id",issues);
            if(role.Id!=null&&!roles.TryAdd(role.Id,role))Error(path+".Id","Duplicate role ID.","V3");
            if(role.Kind is not ("Npc" or "Farmer" or "Any"))Error(path+".Kind","Role kind must be Npc, Farmer or Any.");
        }
        CheckClipTracks(clip.Tracks,clip.Duration,file,"",issues);
        for(var i=0;i<(clip.Tracks?.Count??0);i++)
        {
            var track=clip.Tracks![i];if(track==null)continue;var path=$"Tracks[{i}]";
            if(track.Actor!=null)Error(path+".Actor","Library clip tracks name a Role, not an Actor.");
            if(track.Role==null)Error(path+".Role","Track needs a role.");
            else if(!roles.ContainsKey(track.Role))Error(path+".Role",$"Unknown role '{track.Role}'.","Reference");
            if(track.Camera is {Count:>0})Error(path+".Camera","Library clips have no Camera track; scenes control the camera.");
        }
    }

    /// <summary>A Clip beat playing a library clip must cast every role to a scene actor of the right kind.</summary>
    private static void ValidateClipCasts(Cutscene scene,string file,LoadedPack pack,List<ValidationIssue> issues)
    {
        var poses=new HashSet<string>(pack.Poses.Where(x=>x.Value.Id!=null).Select(x=>x.Value.Id!),StringComparer.OrdinalIgnoreCase);
        foreach(var (value,path) in Walk(scene))
        {
            if(value is not ClipBeat beat||beat.Clip==null)continue;
            bool sceneClip=scene.Clips?.ContainsKey(beat.Clip)??false;
            var library=pack.Clips.Select(x=>x.Value).FirstOrDefault(x=>string.Equals(x.Id,beat.Clip,StringComparison.OrdinalIgnoreCase));
            if(sceneClip||library==null)
            {
                if(sceneClip&&(beat.Cast is {Count:>0}||beat.PoseMap is {Count:>0}||beat.Mirror))issues.Add(new(ValidationSeverity.Warning,file,Join(path,"Cast"),$"'{beat.Clip}' is this scene's own clip, so Cast, Mirror and PoseMap are ignored.","Reference"));
                continue;
            }
            foreach(var role in library.Roles.Where(r=>r?.Id!=null))
            {
                if(beat.Cast==null||!beat.Cast.TryGetValue(role.Id!,out var actorId)||string.IsNullOrWhiteSpace(actorId)){issues.Add(new(ValidationSeverity.Error,file,Join(path,"Cast."+role.Id),$"Role '{role.Id}' of clip '{beat.Clip}' isn't cast.","Reference"));continue;}
                var actor=scene.Actors?.FirstOrDefault(a=>string.Equals(a?.Id,actorId,StringComparison.OrdinalIgnoreCase));
                if(actor==null)issues.Add(new(ValidationSeverity.Error,file,Join(path,"Cast."+role.Id),$"No actor '{actorId}' in this scene.","Reference"));
                else if(role.Kind!="Any"&&!string.Equals(actor.Kind,role.Kind,StringComparison.OrdinalIgnoreCase))issues.Add(new(ValidationSeverity.Error,file,Join(path,"Cast."+role.Id),$"Role '{role.Id}' needs a {role.Kind} actor; '{actorId}' is {actor.Kind}.","Reference"));
            }
            foreach(var extra in (beat.Cast??new()).Keys.Where(k=>!library.Roles.Any(r=>string.Equals(r?.Id,k,StringComparison.OrdinalIgnoreCase))))
                issues.Add(new(ValidationSeverity.Warning,file,Join(path,"Cast."+extra),$"Clip '{beat.Clip}' has no role '{extra}'.","Reference"));
            foreach(var swap in beat.PoseMap??new())
                if(!poses.Contains(swap.Value??""))issues.Add(new(ValidationSeverity.Error,file,Join(path,"PoseMap."+swap.Key),$"Unknown pose '{swap.Value}'.","Reference"));
        }
    }

    private static void CheckAnchors(Dictionary<string,int[]>? anchors,string file,string path,List<ValidationIssue> issues)
    {
        if(anchors==null){issues.Add(new(ValidationSeverity.Error,file,path,"Anchors must be an object.","Schema"));return;}
        foreach(var anchor in anchors)if(anchor.Value is not {Length:2})issues.Add(new(ValidationSeverity.Error,file,path+"."+anchor.Key,"Anchor must contain two integer coordinates.","Schema"));
    }
    private static void ValidateLayers(List<Layer>? layers,Dictionary<string,int[]>? anchors,int[]? frameSize,string file,string path,LoadedPack pack,IPackFileSystem? files,ICatalog? catalog,List<ValidationIssue> issues)
    {
        if(layers==null){issues.Add(new(ValidationSeverity.Error,file,path,"Layers must be an array.","Schema"));return;}
        for(var i=0;i<layers.Count;i++)
        {
            var layer=layers[i];var p=$"{path}[{i}]";
            if(layer==null){issues.Add(new(ValidationSeverity.Error,file,p,"Layer must be an object.","Schema"));continue;}
            if(layer is UnknownLayer unknown){issues.Add(new(ValidationSeverity.Error,file,p+".Type",$"Unknown layer type '{unknown.Data["Type"]}'.","Schema"));continue;}
            if(layer.Offset is not {Length:2})issues.Add(new(ValidationSeverity.Error,file,p+".Offset","Offset must contain two integer coordinates.","Schema"));
            if(layer.AnchorTo is { } anchor)
            {
                if(string.IsNullOrWhiteSpace(anchor.Anchor)||!(anchors?.Keys.Contains(anchor.Anchor,StringComparer.OrdinalIgnoreCase)??false))issues.Add(new(ValidationSeverity.Error,file,p+".AnchorTo.Anchor",$"Unknown frame anchor '{anchor.Anchor}'.","Reference"));
                if(anchor.From is not JArray {Count:2} point || point.Any(t=>t.Type!=JTokenType.Integer))
                {
                    if(layer is not LiveLayer || anchor.From?.Type!=JTokenType.String || !string.Equals((string?)anchor.From,"rig",StringComparison.OrdinalIgnoreCase))issues.Add(new(ValidationSeverity.Error,file,p+".AnchorTo.From","Anchor source must be a two-integer point or 'rig' for a Live layer.","Schema"));
                }
            }
            if(layer is LiveLayer live && live.Source==null)issues.Add(new(ValidationSeverity.Error,file,p+".Source","Live layers require a source.","Schema"));
            if(layer is LiveLayer {Recolor:{} recolor})
                for(var r=0;r<recolor.Count;r++)
                {
                    var rule=recolor[r];var rp=$"{p}.Recolor[{r}]";
                    if(rule==null){issues.Add(new(ValidationSeverity.Error,file,rp,"Recolour rule must be an object.","Schema"));continue;}
                    if(rule.Range<1||rule.Range>180)issues.Add(new(ValidationSeverity.Error,file,rp+".Range","Range is degrees of hue, 1 to 180.","Schema"));
                    foreach(var (value,name) in new[]{(rule.From,"From"),(rule.To,"To")})
                        if(string.IsNullOrWhiteSpace(value))issues.Add(new(ValidationSeverity.Error,file,rp+"."+name,$"A recolour needs {name}: a colour such as #C03030, or one of her sampled colours.","Schema"));
                        else if(!ColorSourceKnown(value,pack,catalog))issues.Add(new(ValidationSeverity.Error,file,rp+"."+name,$"Unknown colour source '{value}'.","V13"));
                }
            if(layer is ImageLayer image)CheckImage(image.Image,image.FrameSize??frameSize,true,file,p+".Image","V11",files,issues,image.Frame);
            if(layer is PaintLayer paint)CheckImage(paint.Image,frameSize,false,file,p+".Image","V11",files,issues);
            if(layer is LiveLayer {Map:{} map})CheckImage(map,frameSize,false,file,p+".Map","V11",files,issues);
            if(layer.Opacity<0||layer.Opacity>1||double.IsNaN(layer.Opacity))issues.Add(new(ValidationSeverity.Error,file,p+".Opacity","Opacity must be between zero and one.","Schema"));
            CheckColorSlots(layer.ColorSlots,file,p+".ColorSlots",pack,catalog,issues);
        }
    }
    /// <summary>A layer that wears an outfit names one of the pack's outfits (sheet poses); portraits can't wear one.</summary>
    private static void CheckOutfitSources(List<Layer>? layers,string file,string path,LoadedPack pack,bool sprite,List<ValidationIssue> issues)
    {
        for(var i=0;i<(layers?.Count??0);i++)
        {
            if(layers![i] is not LiveLayer {Source:{} source}||!Compositing.Outfits.IsOutfitSource(source))continue;
            var at=$"{path}[{i}].Source";
            if(!sprite){issues.Add(new(ValidationSeverity.Error,file,at+".Kind","Outfits are sprite sheets; a portrait can't wear one.","Schema"));continue;}
            if(string.IsNullOrWhiteSpace(source.Outfit)){issues.Add(new(ValidationSeverity.Error,file,at+".Outfit","Which outfit? Outfit names one of the pack's outfits.","Schema"));continue;}
            var target=pack.Poses.Select(x=>x.Value).FirstOrDefault(x=>string.Equals(x?.Id,source.Outfit,StringComparison.OrdinalIgnoreCase));
            if(target!=null&&!Compositing.Outfits.IsOutfit(target))issues.Add(new(ValidationSeverity.Error,file,at+".Outfit",$"'{source.Outfit}' is a pose, not an outfit (a walk sheet).","Reference"));
        }
    }
    private static void CheckImage(string? image,int[]? size,bool multiple,string file,string path,string rule,IPackFileSystem? files,List<ValidationIssue> issues,int frame=0)
    {
        if(!PackPath.IsSafe(image)){issues.Add(new(ValidationSeverity.Error,file,path,"Image path must remain inside the pack and use '/'.","V4"));return;}
        if(files==null)return;
        try
        {
            if(!files.Exists(image!)){issues.Add(new(ValidationSeverity.Error,file,path,$"Image '{image}' does not exist.","V4"));return;}
            if(files is not IImageMetadataProvider metadata)return;
            var actual=metadata.GetImageSize(image!);
            if(actual==null){issues.Add(new(ValidationSeverity.Error,file,path,"Image does not contain a valid PNG header.",rule));return;}
            if(size==null)return;
            if(!ValidSize(size)||(!multiple&&(actual.Value.Width!=size[0]||actual.Value.Height!=size[1]))||(multiple&&(actual.Value.Width%size[0]!=0||actual.Value.Height%size[1]!=0)) || frame<0 || frame>=actual.Value.Width/size[0]*(actual.Value.Height/size[1]))issues.Add(new(ValidationSeverity.Error,file,path,"Image dimensions or frame index do not match FrameSize.",rule));
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or KeyNotFoundException){issues.Add(new(ValidationSeverity.Error,file,path,ex.Message,rule));}
    }
    private static void CheckColorSlots(Dictionary<string,string>? slots,string file,string path,LoadedPack pack,ICatalog? catalog,List<ValidationIssue> issues)
    {
        foreach(var pair in slots??new())
        {
            if(!Slots.Contains(pair.Key)){issues.Add(new(ValidationSeverity.Error,file,path+"."+pair.Key,"Unknown reserved colour slot.","V13"));continue;}
            if(!ColorSourceKnown(pair.Value,pack,catalog))issues.Add(new(ValidationSeverity.Error,file,path+"."+pair.Key,$"Unknown colour source '{pair.Value}'.","V13"));
        }
    }
    /// <summary>A colour source the runtime can read: #RRGGBB, a player colour, or a sample of one of the pack's rigs.</summary>
    private static bool ColorSourceKnown(string? source,LoadedPack pack,ICatalog? catalog)
    {
        var dot=source?.LastIndexOf('.')??-1;var known=source!=null&&Regex.IsMatch(source,"^#[0-9a-fA-F]{6}$");
        if(dot>0)
        {
            var who=source![..dot];var sample=source[(dot+1)..];
            known|=who.Equals("Farmer",StringComparison.OrdinalIgnoreCase)&&FarmerSamples.Contains(sample);
            known|=pack.Characters.Any(r=>(string.Equals(r.Value.Id,who,StringComparison.OrdinalIgnoreCase)||string.Equals(r.Value.Npc,who,StringComparison.OrdinalIgnoreCase))&&(r.Value.ColorSamples?.Keys.Contains(sample,StringComparer.OrdinalIgnoreCase)??false));
        }
        return known||(source!=null&&(catalog?.ColorSourceExists(source)??false));
    }
    private static void ValidateReferencesAndDependencies(LoadedPack pack,IPackFileSystem? files,ICatalog? catalog,bool export,List<ValidationIssue> issues)
    {
        IEnumerable<(object Value,string File)> entries=pack.Cutscenes.Select(e=>((object)e.Value,e.File)).Concat(pack.Poses.Select(e=>((object)e.Value,e.File))).Concat(pack.Portraits.Select(e=>((object)e.Value,e.File))).Concat(pack.Characters.Select(e=>((object)e.Value,e.File)));
        if(pack.Expressions is { } expressionLibrary)entries=entries.Append((expressionLibrary.Value,expressionLibrary.File));
        entries=entries.Concat(pack.Clips.Select(e=>((object)e.Value,e.File)));
        var libraryClips=new HashSet<string>(pack.Clips.Where(x=>x.Value.Id!=null).Select(x=>x.Value.Id!),StringComparer.OrdinalIgnoreCase);
        var rigs=new HashSet<string>(pack.Characters.Where(x=>x.Value.Id!=null).Select(x=>x.Value.Id!),StringComparer.OrdinalIgnoreCase);
        var masks=new HashSet<string>((pack.Masks?.Value.Masks??new()).Where(x=>x?.Id!=null).Select(x=>x.Id!),StringComparer.OrdinalIgnoreCase);
        var poses=new HashSet<string>(pack.Poses.Where(x=>x.Value.Id!=null).Select(x=>x.Value.Id!),StringComparer.OrdinalIgnoreCase);
        var portraits=new HashSet<string>(pack.Portraits.Where(x=>x.Value.Id!=null).Select(x=>x.Value.Id!),StringComparer.OrdinalIgnoreCase);
        var expressions=new HashSet<string>((pack.Expressions?.Value.Expressions??new()).Where(x=>x?.Id!=null).Select(x=>x.Id!),StringComparer.OrdinalIgnoreCase);
        var costumes=new HashSet<string>((pack.Costumes?.Value.Costumes??new()).Where(x=>x?.Id!=null).Select(x=>x.Id!),StringComparer.OrdinalIgnoreCase);
        var videos=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Reference(string? value,HashSet<string> known,string file,string path,string kind)
        {
            if(value==null)return;
            CheckId(value,file,path,issues);
            if(!known.Contains(value))issues.Add(new(ValidationSeverity.Error,file,path,$"Unknown {kind} '{value}'.","Reference"));
        }
        foreach(var (entry,file) in entries)
        foreach(var (value,path) in Walk(entry))
        {
            if(value is CharacterRig rig)
            {
                if(rig.HeadMask==null)issues.Add(new(ValidationSeverity.Error,file,Join(path,"HeadMask"),"HeadMask must be an object.","Schema"));
                foreach(var mask in rig.HeadMask??new())Reference(mask.Value,masks,file,Join(path,"HeadMask."+mask.Key),"mask");
                Reference(rig.Portrait?.HeadMask,masks,file,Join(path,"Portrait.HeadMask"),"mask");
            }
            if(value is Actor actor && actor.Costume?.Type==JTokenType.String)Reference((string?)actor.Costume,costumes,file,Join(path,"Costume"),"costume");
            var rigId=value switch {Pose pose=>pose.For,Portrait portrait=>portrait.For,Expression expression=>expression.For,_=>null};
            if(rigId!=null)Reference(rigId,rigs,file,Join(path,"For"),"rig");
        }
        foreach(var (entry,file) in entries)
        foreach(var (token,path) in Tokens(JObject.FromObject(entry,PackJson.CreateSerializer())))
        {
            if(token.Type!=JTokenType.String)continue;var name=(token.Parent as JProperty)?.Name??"";var value=token.Value<string>()!;
            HashSet<string>? set=name switch {"Mask"=>masks,"Pose"=>poses,"Outfit"=>poses,"Portrait"=>portraits,"Expression"=>expressions,"Preset"=>costumes,_=>null};
            if(set!=null&&!set.Contains(value))issues.Add(new(ValidationSeverity.Error,file,path,$"Unknown {name.ToLowerInvariant()} '{value}'.","Reference"));
            if(name=="Clip" && entry is Cutscene scene && !(scene.Clips?.Keys.Contains(value,StringComparer.OrdinalIgnoreCase)??false) && !libraryClips.Contains(value))issues.Add(new(ValidationSeverity.Error,file,path,$"Unknown clip '{value}'.","Reference"));
            if(name=="Video"&&videos.Add(value)&&files!=null)CheckVideo(value,file,path,files,issues);
            if(name=="Asset"&&catalog?.FindAsset(value) is {Vanilla:false} origin)
            {
                // The provider must be a manifest dependency. Optional is allowed ("only some scenes need it", doc 15 §3.2,
                // Q-15); a required one must also appear in Requires, which mirrors required dependencies (doc 12 §3).
                var requirement=pack.Index?.Requires?.Any(r=>r!=null&&string.Equals(r.Mod,origin.ProvidedBy,StringComparison.OrdinalIgnoreCase))??false;
                var listed=pack.Manifest?.Dependencies?.FirstOrDefault(d=>d!=null&&string.Equals(d.UniqueID,origin.ProvidedBy,StringComparison.OrdinalIgnoreCase));
                if(string.IsNullOrEmpty(origin.ProvidedBy)||listed==null)issues.Add(new(export?ValidationSeverity.Error:ValidationSeverity.Warning,file,path,$"Asset '{value}' comes from '{origin.ProvidedBy ?? "an unknown mod"}', which isn't a manifest dependency.","V14"));
                else if(listed.IsRequired&&!requirement)issues.Add(new(export?ValidationSeverity.Error:ValidationSeverity.Warning,file,path,$"'{origin.ProvidedBy}' is a required dependency but isn't listed in studio.json Requires.","V14"));
            }
        }
    }
    private static void CheckVideo(string id,string file,string path,IPackFileSystem files,List<ValidationIssue> issues)
    {
        if(!IdPattern.IsMatch(id)){issues.Add(new(ValidationSeverity.Error,file,path,"Video ID must be a content ID, not a file path.","V2"));return;}
        try
        {
            var frames=files.ListFiles("videos/"+id+"/*.png").ToArray();
            if(frames.Length==0){issues.Add(new(ValidationSeverity.Error,file,path,"Video requires at least one PNG frame.","V16"));return;}
            ImageSize? expected=null;
            foreach(var frame in frames)
            {
                if(!PackPath.IsSafe(frame)){issues.Add(new(ValidationSeverity.Error,file,path,"Unsafe video frame path.","V4"));continue;}
                if(files is not IImageMetadataProvider metadata)continue;
                var size=metadata.GetImageSize(frame);
                if(size==null||(expected!=null&&expected!=size))issues.Add(new(ValidationSeverity.Error,file,path,$"Video frame '{frame}' has invalid or inconsistent dimensions.","V16"));
                expected??=size;
            }
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or KeyNotFoundException){issues.Add(new(ValidationSeverity.Error,file,path,ex.Message,"V16"));}
    }
    private static bool ValidSize(int[]? size) => size is {Length:2}&&size[0]>0&&size[1]>0;
    private static void CheckPosition(Position? position,string path,Action<string,string,string> error)
    {
        if(position?.Tile is not {Length:2})error(path+".Tile","Tile must contain two integer coordinates.","Schema");
        if(position!=null&&position.Px is not {Length:2})error(path+".Px","Px must contain two integer coordinates.","Schema");
    }
    private static void CheckFormat(string? format,string file,string path,List<ValidationIssue> issues){if(string.IsNullOrWhiteSpace(format)||format.Split('.')[0]!="1")issues.Add(new(ValidationSeverity.Error,file,path,"Format must be present with major version 1.","V1"));}
    private static void CheckId(string? id,string file,string path,List<ValidationIssue> issues){if(id==null||!IdPattern.IsMatch(id))issues.Add(new(ValidationSeverity.Error,file,path,"Content ID must match ^[a-z0-9][a-z0-9._-]*$.","V2"));}
    private static void CheckDictionaryIds(IEnumerable<string> ids,string file,string path,List<ValidationIssue> issues){var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(var id in ids){CheckId(id,file,path+"."+id,issues);if(!seen.Add(id))issues.Add(new(ValidationSeverity.Error,file,path+"."+id,"Duplicate ID (case-insensitive).","V3"));}}
    private static void CheckDuplicates(IEnumerable<(string? Id,string File,string Path)> values,List<ValidationIssue> issues){var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(var (id,file,path) in values)if(id!=null&&!seen.Add(id))issues.Add(new(ValidationSeverity.Error,file,path,$"Duplicate ID '{id}' (case-insensitive).","V3"));}
    private static string FixText(string text){var open=true;return string.Concat(text.Select(c=>c=='/'?'⁄':c=='"'?(open=!open)?'”':'“':c));}
    private static IEnumerable<(object Value,string Path)> Walk(object root,string path="")
    {
        var pending=new Stack<(object Value,string Path)>();var seen=new HashSet<object>(ReferenceEqualityComparer.Instance);pending.Push((root,path));
        while(pending.Count>0)
        {
            var current=pending.Pop();if(!seen.Add(current.Value))continue;yield return current;
            if(current.Value is TextValue or JToken or string)continue;
            if(current.Value is IDictionary dictionary){foreach(DictionaryEntry entry in dictionary)if(entry.Value!=null)pending.Push((entry.Value,Join(current.Path,entry.Key.ToString()!)));}
            else if(current.Value is IList list){for(var i=0;i<list.Count;i++)if(list[i]!=null)pending.Push((list[i]!,$"{current.Path}[{i}]"));}
            else if(!current.Value.GetType().IsValueType)foreach(var property in current.Value.GetType().GetProperties(BindingFlags.Public|BindingFlags.Instance).Where(p=>p.CanRead&&p.GetIndexParameters().Length==0&&p.GetCustomAttribute<Newtonsoft.Json.JsonIgnoreAttribute>()==null))if(property.GetValue(current.Value) is { } value)pending.Push((value,Join(current.Path,property.Name)));
        }
    }
    private static string Join(string path,string name)=>path.Length==0?name:path+"."+name;
    private static IEnumerable<(JToken Token,string Path)> Tokens(JToken root)
    {
        var pending=new Stack<JToken>();pending.Push(root);while(pending.Count>0){var token=pending.Pop();yield return(token,token.Path);foreach(var child in token.Children())pending.Push(child);}
    }
    private static void CheckCycles(Dictionary<string,List<Beat>> branches,string file,List<ValidationIssue> issues)
    {
        var graph=new Dictionary<string,HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach(var branch in branches)graph[branch.Key]=new((branch.Value??new()).Where(b=>b!=null).SelectMany(b=>BeatFlow.Targets(b)).Where(x=>branches.Keys.Contains(x,StringComparer.OrdinalIgnoreCase)),StringComparer.OrdinalIgnoreCase);
        var reach=new Dictionary<string,HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach(var key in graph.Keys){var visited=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var queue=new Queue<string>(graph[key]);while(queue.Count>0){var item=queue.Dequeue();if(visited.Add(item)&&graph.TryGetValue(item,out var next))foreach(var edge in next)queue.Enqueue(edge);}reach[key]=visited;}
        var reported=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var key in graph.Keys)
        {
            if(reported.Contains(key)||!reach[key].Contains(key))continue;
            var component=graph.Keys.Where(k=>reach[key].Contains(k)&&reach[k].Contains(key)).ToArray();foreach(var k in component)reported.Add(k);
            if(!component.Any(k=>(branches[k]??new()).Any(b=>b is ChoiceBeat)))issues.Add(new(ValidationSeverity.Warning,file,"Branches."+key,"Branch cycle has no Choice and may never terminate.","V9"));
        }
    }
}
