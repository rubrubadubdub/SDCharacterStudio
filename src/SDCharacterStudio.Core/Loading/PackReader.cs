using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core.Migration;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Serialization;
using SDCharacterStudio.Core.Validation;

namespace SDCharacterStudio.Core.Loading;

public sealed class PackReader
{
    private readonly IPackFileSystem files;
    private readonly PackMigrator? migrations;
    public PackReader(IPackFileSystem files,PackMigrator? migrations=null) { this.files=files??throw new ArgumentNullException(nameof(files));this.migrations=migrations; }
    public PackReadResult Read(ICatalog? catalog=null,bool exportValidation=true)
    {
        var issues=new List<ValidationIssue>();var pack=new LoadedPack();
        pack.Manifest=ReadFile<Manifest>("manifest.json","manifest.json","",true,issues);
        pack.Index=ReadFile<StudioIndex>("studio.json","studio.json","",true,issues);
        if(pack.Index is { } index)
        {
            LoadMany(index.Cutscenes,new[]{"cutscenes/*.json"},"Cutscenes",pack.Cutscenes,issues);
            LoadMany(index.Library?.Characters,new[]{"library/characters/*.json"},"Library.Characters",pack.Characters,issues);
            LoadMany(index.Library?.Poses,new[]{"library/poses/*.json"},"Library.Poses",pack.Poses,issues);
            LoadMany(index.Library?.Portraits,new[]{"library/portraits/*.json"},"Library.Portraits",pack.Portraits,issues);
            LoadMany(index.Library?.Clips,new[]{"library/clips/*.json"},"Library.Clips",pack.Clips,issues);
            var masks=index.Library?.Masks??"library/masks.json";var mask=ReadFile<MaskLibrary>(masks,"studio.json","Library.Masks",index.Library?.Masks!=null,issues); if(mask!=null)pack.Masks=new(masks,mask);
            var expressions=index.Library?.Expressions??"library/expressions.json";var expression=ReadFile<ExpressionLibrary>(expressions,"studio.json","Library.Expressions",index.Library?.Expressions!=null,issues);if(expression!=null)pack.Expressions=new(expressions,expression);
            var costumes=index.Library?.Costumes??"library/costumes.json";var costume=ReadFile<CostumeLibrary>(costumes,"studio.json","Library.Costumes",index.Library?.Costumes!=null,issues);if(costume!=null)pack.Costumes=new(costumes,costume);
        }
        issues.AddRange(PackValidator.ValidatePack(pack,files,catalog,exportValidation));
        return new(pack,issues);
    }
    private T? ReadFile<T>(string path,string source,string jsonPath,bool required,List<ValidationIssue> issues) where T:class
    {
        if(!PackPath.IsSafe(path)){issues.Add(new(ValidationSeverity.Error,source,jsonPath,"File path must remain inside the pack and use '/'.","V4"));return null;}
        try
        {
            if(!files.Exists(path)){if(required)issues.Add(new(ValidationSeverity.Error,source,jsonPath,$"Referenced file '{path}' does not exist.","V4"));return null;}
            var json=files.ReadText(path);
            var raw=JObject.Parse(json,new JsonLoadSettings {DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});
            if(migrations!=null && raw["Format"]?.Type==JTokenType.String && ((string)raw["Format"]!).Split('.')[0]!="1")
            {
                var migrated=migrations.Migrate(raw,path);issues.AddRange(migrated.Issues);if(migrated.Data==null)return null;raw=migrated.Data;
            }
            InspectDocument(raw,typeof(T),path,issues);
            issues.AddRange(JsonShapeValidator.Validate(raw,typeof(T),path));
            var value=PackJson.Deserialize<T>(raw.ToString());
            if(value==null)issues.Add(new(ValidationSeverity.Error,path,"","Expected a JSON object, found null.","Schema"));
            return value;
        }
        catch(JsonException ex){issues.Add(new(ValidationSeverity.Error,path,ex is JsonSerializationException se?se.Path??"":ex is JsonReaderException re?re.Path??"":"",ex.Message,"Schema"));return null;}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or KeyNotFoundException){issues.Add(new(ValidationSeverity.Error,path,"",$"Could not read file: {ex.Message}","V4"));return null;}
    }
    private static void InspectDocument(JObject raw,Type type,string file,List<ValidationIssue> issues)
    {
        void Required(JObject obj,params string[] properties)
        {
            foreach(var property in properties)if(obj[property]==null || obj[property]!.Type==JTokenType.Null)issues.Add(new(ValidationSeverity.Error,file,obj.Path.Length==0?property:obj.Path+"."+property,$"Required field '{property}' is missing or null.",property=="Format"?"V1":"Schema"));
        }
        if(type!=typeof(Manifest))Required(raw,"Format");
        if(raw["Format"] is {Type:not JTokenType.String and not JTokenType.Null})issues.Add(new(ValidationSeverity.Error,file,"Format","Format must be a string.","V1"));
        if(type==typeof(Cutscene))
        {
            Required(raw,"Id","Location","Beats");
            foreach(var name in new[]{"Branches","Clips"})if(raw[name] is JObject dictionary)
            {
                var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(var property in dictionary.Properties())if(!seen.Add(property.Name))issues.Add(new(ValidationSeverity.Error,file,property.Path,"Duplicate content ID (case-insensitive).","V3"));
            }
            foreach(var actor in (raw["Actors"] as JArray??new()).OfType<JObject>())Required(actor,"Id","Kind","Start");
        }
        if(type==typeof(Pose)){Required(raw,"Id","FrameSize","Frames");foreach(var frame in (raw["Frames"] as JArray??new()).OfType<JObject>())Required(frame,"Duration","Layers");}
        if(type==typeof(CharacterRig)){Required(raw,"Id");if(raw["Kind"] is {Type:not JTokenType.String and not JTokenType.Null})issues.Add(new(ValidationSeverity.Error,file,"Kind","Kind must be a string.","Schema"));else if(string.Equals((string?)raw["Kind"]??"Npc","Npc",StringComparison.OrdinalIgnoreCase))Required(raw,"Npc");}
        if(type==typeof(Portrait))Required(raw,"Id","Emotions");
        if(type==typeof(LibraryClip)){Required(raw,"Id","Roles","Tracks");foreach(var role in (raw["Roles"] as JArray??new()).OfType<JObject>())Required(role,"Id");}
        if(type==typeof(MaskLibrary)){Required(raw,"Masks");foreach(var mask in (raw["Masks"] as JArray??new()).OfType<JObject>())Required(mask,"Id","FrameSize","Image");}
        if(type==typeof(ExpressionLibrary)){Required(raw,"Expressions");foreach(var expression in (raw["Expressions"] as JArray??new()).OfType<JObject>())Required(expression,"Id","Frames");}
        if(type==typeof(CostumeLibrary)){Required(raw,"Costumes");foreach(var costume in (raw["Costumes"] as JArray??new()).OfType<JObject>())Required(costume,"Id","Set");}
        var pending=new Stack<JToken>();pending.Push(raw);
        while(pending.Count>0)
        {
            var current=pending.Pop();foreach(var child in current.Children())pending.Push(child);
            if(current is JObject obj && obj.Parent is JArray array && (ReferenceEquals(array,raw["Beats"])||ReferenceEquals(array,raw["OnSkip"])||array.Parent is JProperty branchProperty && ReferenceEquals(branchProperty.Parent,raw["Branches"])))Required(obj,"Type");
            if(current is JObject option && option.Parent is JArray options && options.Path.EndsWith(".Options",StringComparison.Ordinal))Required(option,"Text","Then");
            if(current is JObject key && key.Parent is JArray keys && (keys.Path.Contains(".Tracks[",StringComparison.Ordinal)||keys.Path.StartsWith("Tracks[",StringComparison.Ordinal)))Required(key,"At");
        }
    }
    private void LoadMany<T>(List<string>? patterns,string[] defaults,string jsonPath,List<SourceEntry<T>> destination,List<ValidationIssue> issues) where T:class
    {
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var entries=patterns??defaults.ToList();
        for(var i=0;i<entries.Count;i++)
        {
            var pattern=entries[i];var path=$"{jsonPath}[{i}]";
            if(!PackPath.IsSafe(pattern)){issues.Add(new(ValidationSeverity.Error,"studio.json",path,"File path must remain inside the pack and use '/'.","V4"));continue;}
            IEnumerable<string> matches;
            try
            {
                matches=pattern.IndexOfAny(new[]{'*','?'})>=0 ? files.ListFiles(pattern).OrderBy(p=>p,StringComparer.Ordinal).ToArray() : new[]{pattern};
                if(!matches.Any() && patterns!=null)issues.Add(new(ValidationSeverity.Error,"studio.json",path,$"Pattern '{pattern}' matched no files.","V4"));
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException){issues.Add(new(ValidationSeverity.Error,"studio.json",path,ex.Message,"V4"));continue;}
            foreach(var file in matches)
            {
                if(!seen.Add(file))continue;
                var value=ReadFile<T>(file,"studio.json",path,true,issues);if(value!=null)destination.Add(new(file,value));
            }
        }
    }
}
