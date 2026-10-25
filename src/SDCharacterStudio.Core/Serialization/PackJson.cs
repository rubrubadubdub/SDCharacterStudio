using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core.Models;

namespace SDCharacterStudio.Core.Serialization;

public static class PackJson
{
    public static JsonSerializer CreateSerializer()
    {
        var serializer = new JsonSerializer { NullValueHandling = NullValueHandling.Include, Formatting = Formatting.Indented, DateParseHandling = DateParseHandling.None };
        serializer.Converters.Add(new BeatConverter());
        serializer.Converters.Add(new LayerConverter());
        serializer.Converters.Add(new TextConverter());
        return serializer;
    }
    public static string Serialize(object value)
    {
        using var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        using var json = new JsonTextWriter(writer);
        CreateSerializer().Serialize(json, value);
        return writer.ToString();
    }
    public static T? Deserialize<T>(string json)
    {
        using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None };
        var serializer = CreateSerializer();
        var value = serializer.Deserialize<T>(reader);
        while (reader.Read()) if (reader.TokenType != JsonToken.Comment) throw new JsonSerializationException("Unexpected trailing JSON content.");
        return value;
    }
}
internal class TextConverter : JsonConverter<TextValue>
{
    public override TextValue? ReadJson(JsonReader reader, Type objectType, TextValue? existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null) return null;
        if (reader.TokenType == JsonToken.String) return new TextValue { Literal = (string?)reader.Value };
        var data = JObject.Load(reader);
        if (data["i18n"]?.Type != JTokenType.String) throw new JsonSerializationException("Text must be a string or an i18n key.");
        return new TextValue { I18n = (string?)data["i18n"] };
    }
    public override void WriteJson(JsonWriter writer, TextValue? value, JsonSerializer serializer)
    {
        if (value?.I18n is { } key) { writer.WriteStartObject(); writer.WritePropertyName("i18n"); writer.WriteValue(key); writer.WriteEndObject(); }
        else writer.WriteValue(value?.Literal);
    }
}
internal class BeatConverter : JsonConverter
{
    private static readonly Dictionary<string,Type> Types = typeof(Beat).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(Beat)) && t != typeof(UnknownBeat)).ToDictionary(t => ((Beat)Activator.CreateInstance(t)!).Type, StringComparer.OrdinalIgnoreCase);
    public override bool CanConvert(Type objectType) => objectType == typeof(Beat) || objectType == typeof(UnknownBeat);
    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null) return null;
        var data = JObject.Load(reader);
        var type = data["Type"]?.Type == JTokenType.String ? (string?)data["Type"] : null;
        if (type == null || !Types.TryGetValue(type, out var concrete)) return new UnknownBeat { Data = data };
        var value = (Beat)Activator.CreateInstance(concrete)!;
        using var nested = data.CreateReader(); serializer.Populate(nested,value); return value;
    }
    public override bool CanWrite => true;
    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value is UnknownBeat unknown) unknown.Data.WriteTo(writer); else serializer.Serialize(writer,value,value!.GetType());
    }
}
internal class LayerConverter : JsonConverter
{
    private static readonly Dictionary<string,Type> Types = new(StringComparer.OrdinalIgnoreCase) { ["Live"]=typeof(LiveLayer),["Image"]=typeof(ImageLayer),["Paint"]=typeof(PaintLayer),["Effect"]=typeof(EffectLayer) };
    public override bool CanConvert(Type objectType) => objectType == typeof(Layer) || objectType == typeof(UnknownLayer);
    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null) return null;
        var data=JObject.Load(reader); var type=data["Type"]?.Type==JTokenType.String ? (string?)data["Type"] : null;
        if(type==null || !Types.TryGetValue(type,out var concrete)) return new UnknownLayer {Data=data};
        var value=(Layer)Activator.CreateInstance(concrete)!; using var nested=data.CreateReader(); serializer.Populate(nested,value); return value;
    }
    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer) { if(value is UnknownLayer unknown) unknown.Data.WriteTo(writer); else serializer.Serialize(writer,value,value!.GetType()); }
}
