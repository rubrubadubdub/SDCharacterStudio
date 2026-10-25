using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using SDCharacterStudio.Core.Serialization;

namespace SDCharacterStudio.Editor.Project;

/// <summary>
/// Writes pack JSON the way a person would: values equal to the model's defaults are left out, required
/// fields are always written, and Type/Format/Id come first. Unknown fields (extension data) survive.
/// </summary>
public static class PackWriter
{
    private static readonly UTF8Encoding Utf8 = new(false);

    public static string Serialize(object value)
    {
        var serializer = PackJson.CreateSerializer();
        serializer.ContractResolver = CleanContractResolver.Instance;
        serializer.NullValueHandling = NullValueHandling.Ignore;
        var token = JToken.FromObject(value, serializer);
        RemoveEmptyObjects(token);
        return token.ToString(Formatting.Indented).Replace("\r\n", "\n") + "\n";
    }

    // An all-default nested object ("OnEnd": {}) is noise. An empty Trigger is not: it means "triggers with no conditions".
    private static void RemoveEmptyObjects(JToken token)
    {
        foreach (var child in token.Children().ToArray()) RemoveEmptyObjects(child);
        if (token is JProperty { Value: JObject { Count: 0 } } property && property.Name != "Trigger") property.Remove();
    }

    public static T Clone<T>(T value) where T : class => PackJson.Deserialize<T>(Serialize(value))!;

    /// <summary>Write only when the content changed, so file timestamps (and incremental tools) stay honest.</summary>
    public static bool WriteIfChanged(string path, string content)
    {
        if (File.Exists(path) && File.ReadAllText(path) == content) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, content, Utf8);
        File.Move(temporary, path, true);
        return true;
    }

    private sealed class CleanContractResolver : DefaultContractResolver
    {
        public static readonly CleanContractResolver Instance = new();
        // Fields the reader requires or that carry meaning even at their default value.
        private static readonly HashSet<string> AlwaysWrite = new(StringComparer.Ordinal)
        { "Format", "Id", "Type", "Location", "Beats", "Kind", "Start", "Tile", "Text", "Options", "Then", "At", "Frames", "Layers",
          "Masks", "Costumes", "Expressions", "Emotions", "Set", "FrameSize", "Image", "Branch", "Actor", "To", "UniqueID", "ContentPackFor",
          "Name", "Author", "Version", "Description", "Duration", "Mod" };
        private static readonly ConcurrentDictionary<Type, object?> Defaults = new();
        private static readonly string[] Leading = { "Type", "Format", "Id" };

        protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
        {
            var properties = base.CreateProperties(type, memberSerialization);
            int Depth(Type? t) { int d = 0; while (t != null) { d++; t = t.BaseType; } return d; }
            int index = 0;
            foreach (var property in properties
                .OrderBy(p => Array.IndexOf(Leading, p.PropertyName) is var i && i >= 0 ? i : Leading.Length)
                .ThenBy(p => Depth(p.DeclaringType))
                .ThenBy(p => p.DeclaringType?.GetProperty(p.UnderlyingName ?? "", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)?.MetadataToken ?? int.MaxValue))
                property.Order = index++;
            return properties;
        }

        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            var property = base.CreateProperty(member, memberSerialization);
            if (property.PropertyName is { } name && !AlwaysWrite.Contains(name) && property.ValueProvider != null)
            {
                var provider = property.ValueProvider;
                property.ShouldSerialize = instance =>
                {
                    object? value = provider.GetValue(instance);
                    if (value == null || IsEmpty(value)) return false;
                    object? @default = DefaultValue(instance.GetType(), provider);
                    return !Same(value, @default);
                };
            }
            return property;
        }

        private static object? DefaultValue(Type type, IValueProvider provider)
        {
            var instance = Defaults.GetOrAdd(type, t => t.GetConstructor(Type.EmptyTypes) != null && !t.IsAbstract ? Activator.CreateInstance(t) : null);
            return instance == null ? null : provider.GetValue(instance);
        }

        private static bool IsEmpty(object value) => value switch
        {
            string => false,
            JArray array => array.Count == 0,
            JObject obj => obj.Count == 0,
            IDictionary dictionary => dictionary.Count == 0,
            ICollection collection => collection.Count == 0,
            _ => false
        };

        private static bool Same(object value, object? @default)
        {
            if (@default == null) return false;
            if (value is JToken token) return @default is JToken other && JToken.DeepEquals(token, other);
            if (value is string || value.GetType().IsValueType) return value.Equals(@default);
            if (value is IEnumerable sequence && @default is IEnumerable defaults && value is not IDictionary)
                return sequence.Cast<object?>().SequenceEqual(defaults.Cast<object?>());
            return false;
        }
    }
}
