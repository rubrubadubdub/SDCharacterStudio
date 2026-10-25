using System.Globalization;
using System.Xml;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using StardewValley.Objects;
using StardewValley.SaveSerialization;

namespace SDCharacterStudio.Runtime.Costumes;

/// <summary>JSON envelope with the game's full XML item payloads, including modData and tailored boot stats.</summary>
internal sealed class CostumeBackup
{
    [JsonProperty(Required = Required.Always)] public int Version { get; set; } = 1;
    [JsonProperty(Required = Required.AllowNull)] public string? HatXml { get; set; }
    [JsonProperty(Required = Required.AllowNull)] public string? BootsXml { get; set; }
    public string? HatType { get; set; }
    public string? BootsType { get; set; }
    [JsonProperty(Required = Required.AllowNull)] public string? Shirt { get; set; }
    [JsonProperty(Required = Required.AllowNull)] public string? Pants { get; set; }
    [JsonProperty(Required = Required.AllowNull)] public string? Shoes { get; set; }
    [JsonProperty(Required = Required.Always)] public int Hair { get; set; }
    [JsonProperty(Required = Required.Always)] public int Accessory { get; set; }
    [JsonProperty(Required = Required.Always)] public int Skin { get; set; }
    [JsonProperty(Required = Required.Always)] public uint HairColor { get; set; }
    [JsonProperty(Required = Required.Always)] public uint EyeColor { get; set; }

    public static string Serialize(CostumeSnapshot state) => JsonConvert.SerializeObject(new CostumeBackup
    {
        HatXml = SerializeItem(state.Hat), BootsXml = SerializeItem(state.Boots),
        HatType = state.Hat?.GetType().AssemblyQualifiedName, BootsType = state.Boots?.GetType().AssemblyQualifiedName,
        Shirt = state.Shirt, Pants = state.Pants, Shoes = state.Shoes,
        Hair = state.Hair, Accessory = state.Accessory, Skin = state.Skin,
        HairColor = state.HairColor.PackedValue, EyeColor = state.EyeColor.PackedValue
    });

    public static CostumeSnapshot Deserialize(string json)
    {
        var backup = JsonConvert.DeserializeObject<CostumeBackup>(json)
            ?? throw new InvalidDataException("Empty costume backup.");
        if (backup.Version != 1) throw new InvalidDataException($"Unsupported costume backup version {backup.Version}.");
        // Deserialize both items before changing any farmer field.
        return new()
        {
            Hat = DeserializeItem<Hat>(backup.HatXml, backup.HatType), Boots = DeserializeItem<Boots>(backup.BootsXml, backup.BootsType),
            Shirt = backup.Shirt, Pants = backup.Pants, Shoes = backup.Shoes,
            Hair = backup.Hair, Accessory = backup.Accessory, Skin = backup.Skin,
            HairColor = new Color(backup.HairColor), EyeColor = new Color(backup.EyeColor)
        };
    }

    private static string? SerializeItem<T>(T? item) where T : class
    {
        if (item == null) return null;
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        // Runtime type includes XML-serializable state added by custom item subclasses.
        SaveSerializer.GetSerializer(item.GetType()).Serialize(writer, item);
        return writer.ToString();
    }

    private static T? DeserializeItem<T>(string? xml, string? typeName) where T : class
    {
        if (xml == null) return null;
        Type type = typeName == null ? typeof(T) : Type.GetType(typeName, throwOnError: true)!;
        if (!typeof(T).IsAssignableFrom(type)) throw new InvalidDataException($"Invalid {typeof(T).Name} backup type.");
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        return SaveSerializer.GetSerializer(type).Deserialize(reader) as T
            ?? throw new InvalidDataException($"Invalid {typeof(T).Name} costume backup.");
    }
}
