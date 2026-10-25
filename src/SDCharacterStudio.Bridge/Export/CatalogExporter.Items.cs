using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core.Bridge;
using StardewModdingAPI;
using StardewValley;

namespace SDCharacterStudio.Bridge.Export;

public static partial class CatalogExporter
{
    private static void ScheduleItems(ExportSession session)
    {
        var items = new JObject();
        session.Catalog["Items"] = items;
        var types = new[] { ("(O)", "Data/Objects"), ("(H)", "Data/Hats"), ("(S)", "Data/Shirts"), ("(P)", "Data/Pants"),
            ("(B)", "Data/Boots"), ("(F)", "Data/Furniture"), ("(BC)", "Data/BigCraftables"), ("(W)", "Data/Weapons"), ("(T)", "Data/Tools") };
        foreach (var (type, asset) in types)
        {
            var entries = new JArray();
            items[type] = entries;
            var definition = ItemRegistry.GetTypeDefinition(type);
            if (definition == null) continue;
            foreach (var id in definition.GetAllIds().Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal))
            {
                var qualifiedId = type + id;
                session.Queue("item " + qualifiedId, () =>
                {
                    var data = ItemRegistry.GetDataOrErrorItem(qualifiedId);
                    // A digest avoids illegal filename characters and case-folding collisions in mod IDs.
                    var icon = "icons/" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(qualifiedId))).ToLowerInvariant() + ".png";
                    var texture = data.GetTexture();
                    var source = data.GetSourceRect();
                    bool clamped = source.X < 0 || source.Y < 0 || (long)source.X + source.Width > texture.Width || (long)source.Y + source.Height > texture.Height;
                    session.Image(icon, clamped
                        ? TextureCrop.PointClamp(ReadPixels(texture), source.X, source.Y, source.Width, source.Height)
                        : ReadPixels(texture, source));
                    var entry = new JObject
                    {
                        ["Id"] = qualifiedId, ["QualifiedId"] = qualifiedId, ["Name"] = data.InternalName,
                        ["DisplayName"] = data.DisplayName, ["Icon"] = icon, ["Texture"] = NormalizeAsset(data.GetTextureName()),
                        ["SpriteIndex"] = data.SpriteIndex, ["Size"] = new JArray(source.Width, source.Height),
                        ["SourceRect"] = new JArray(source.X, source.Y, source.Width, source.Height),
                        ["TextureSize"] = new JArray(texture.Width, texture.Height), ["IconSampling"] = "PointClamp",
                        ["IsErrorItem"] = data.IsErrorItem, ["Origin"] = session.Origin(asset, id)
                    };
                    if (clamped)
                    {
                        string warning = $"Source rectangle ({source.X},{source.Y},{source.Width},{source.Height}) exceeds its {texture.Width}x{texture.Height} texture; icon uses native PointClamp edge sampling.";
                        entry["IconWarning"] = warning;
                        session.Monitor.Log(qualifiedId + ": " + warning, LogLevel.Warn);
                    }
                    entries.Add(entry);
                });
            }
        }
    }
}
