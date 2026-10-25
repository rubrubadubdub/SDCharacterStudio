using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core.Models;

namespace SDCharacterStudio.Core.Validation;

/// <summary>Checks JSON token kinds before Json.NET's permissive scalar coercion can hide malformed content.</summary>
internal static class JsonShapeValidator
{
    private static readonly Dictionary<string,Type> BeatTypes=typeof(Beat).Assembly.GetTypes().Where(t=>t.IsSubclassOf(typeof(Beat))&&t!=typeof(UnknownBeat)).ToDictionary(t=>((Beat)Activator.CreateInstance(t)!).Type,StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string,Type> LayerTypes=typeof(Layer).Assembly.GetTypes().Where(t=>t.IsSubclassOf(typeof(Layer))&&t!=typeof(UnknownLayer)).ToDictionary(t=>((Layer)Activator.CreateInstance(t)!).Type,StringComparer.OrdinalIgnoreCase);
    public static IEnumerable<ValidationIssue> Validate(JToken token,Type expected,string file)
    {
        var issues=new List<ValidationIssue>();var pending=new Stack<(JToken Token,Type Type)>();pending.Push((token,expected));
        void Invalid(JToken value,string message) => issues.Add(new(ValidationSeverity.Error,file,value.Path,message,"Schema"));
        while(pending.Count>0)
        {
            var (value,type)=pending.Pop();var nullable=Nullable.GetUnderlyingType(type);
            if(value.Type==JTokenType.Null){if(type.IsValueType&&nullable==null)Invalid(value,"Value may not be null.");continue;}
            type=nullable??type;
            if(typeof(JToken).IsAssignableFrom(type))continue;
            if(type==typeof(string)){if(value.Type!=JTokenType.String)Invalid(value,"Expected a string.");continue;}
            if(type==typeof(int)){if(value.Type!=JTokenType.Integer)Invalid(value,"Expected an integer.");continue;}
            if(type==typeof(double)){if(value.Type is not (JTokenType.Integer or JTokenType.Float))Invalid(value,"Expected a number.");continue;}
            if(type==typeof(bool)){if(value.Type!=JTokenType.Boolean)Invalid(value,"Expected true or false.");continue;}
            if(type==typeof(TextValue))
            {
                if(value.Type==JTokenType.String)continue;
                if(value is not JObject text||text["i18n"]?.Type!=JTokenType.String)Invalid(value,"Expected a literal string or an object with a string i18n key.");
                continue;
            }
            if(type.IsArray||type.IsGenericType&&type.GetGenericTypeDefinition()==typeof(List<>))
            {
                if(value is not JArray array){Invalid(value,"Expected an array.");continue;}
                var element=type.IsArray?type.GetElementType()!:type.GetGenericArguments()[0];foreach(var item in array)pending.Push((item,element));continue;
            }
            if(type.IsGenericType&&type.GetGenericTypeDefinition()==typeof(Dictionary<,>))
            {
                if(value is not JObject dictionary){Invalid(value,"Expected an object.");continue;}
                foreach(var property in dictionary.Properties())pending.Push((property.Value,type.GetGenericArguments()[1]));continue;
            }
            if(value is not JObject obj){Invalid(value,"Expected an object.");continue;}
            if(type==typeof(Beat)||type==typeof(Layer))
            {
                var map=type==typeof(Beat)?BeatTypes:LayerTypes;
                if(obj["Type"]?.Type!=JTokenType.String||!map.TryGetValue((string)obj["Type"]!,out var concrete))continue; // Unknown discriminators are preserved and reported by the model validator.
                type=concrete;
            }
            foreach(var property in type.GetProperties(BindingFlags.Public|BindingFlags.Instance))
            {
                if(property.GetCustomAttribute<JsonIgnoreAttribute>()!=null||property.GetCustomAttribute<JsonExtensionDataAttribute>()!=null)continue;
                var name=property.GetCustomAttribute<JsonPropertyAttribute>()?.PropertyName??property.Name;
                if(!obj.TryGetValue(name,StringComparison.OrdinalIgnoreCase,out var child))continue;
                if(child.Type!=JTokenType.Null&&property.PropertyType==typeof(JToken))
                {
                    if(name is "Emotion" or "Emote" or "Speed" && child.Type is not (JTokenType.String or JTokenType.Integer or JTokenType.Float))Invalid(child,"Expected a name or numeric value.");
                    if(name=="Camera"&&child.Type is not (JTokenType.String or JTokenType.Object))Invalid(child,"Expected a camera position or 'follow'.");
                    if(name=="Costume"&&child.Type is not (JTokenType.String or JTokenType.Object))Invalid(child,"Expected a preset name or inline costume.");
                    if(name=="From"&&child.Type is not (JTokenType.String or JTokenType.Array))Invalid(child,"Expected 'rig' or a point array.");
                }
                pending.Push((child,property.PropertyType));
            }
        }
        return issues;
    }
}
