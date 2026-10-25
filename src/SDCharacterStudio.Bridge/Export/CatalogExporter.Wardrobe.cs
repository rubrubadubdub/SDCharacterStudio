using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json.Linq;
using StardewValley;
using StardewValley.GameData.Pants;
using StardewValley.GameData.Shirts;
using StardewValley.Objects;

namespace SDCharacterStudio.Bridge.Export;

public static partial class CatalogExporter
{
    private static void ScheduleWardrobe(ExportSession session)
    {
        const string farmer = "Characters/Farmer/";
        var bases = new JObject { ["Male"] = farmer + "farmer_base", ["Female"] = farmer + "farmer_girl_base",
            ["MaleBald"] = farmer + "farmer_base_bald", ["FemaleBald"] = farmer + "farmer_girl_base_bald" };
        var parts = new JObject
        {
            ["BaseTextures"] = bases, ["FrameCount"] = FarmerRenderer.featureYOffsetPerFrame.Length,
            ["FeatureXOffsetPerFrame"] = new JArray(FarmerRenderer.featureXOffsetPerFrame),
            ["FeatureYOffsetPerFrame"] = new JArray(FarmerRenderer.featureYOffsetPerFrame),
            ["HairstyleHatOffset"] = new JArray(FarmerRenderer.hairstyleHatOffset),
            ["SkinColors"] = farmer + "skinColors", ["ShoeColors"] = farmer + "shoeColors",
            ["AnimationFrames"] = FarmerExporter.AnimationMetadata()
        };
        session.Catalog["FarmerParts"] = parts;
        foreach (var asset in bases.Properties().Select(p => (string)p.Value!)) session.ScheduleTexture(asset);
        foreach (var asset in new[] { "skinColors", "shoeColors", "hairstyles", "accessories", "hats", "shirts", "pants" })
            session.ScheduleTexture(farmer + asset);

        var hairstyles = new JArray();
        parts["Hairstyles"] = hairstyles;
        var hairData = session.Helper.GameContent.Load<Dictionary<int, string>>("Data/HairData");
        var hairTexture = session.Helper.GameContent.Load<Texture2D>(farmer + "hairstyles");
        var columns = hairTexture.Width / 16;
        var hairIndices = Enumerable.Range(0, columns * (hairTexture.Height / 96)).Concat(hairData.Keys.Where(i => i >= 0)).Distinct().OrderBy(i => i);
        foreach (var index in hairIndices)
        {
            string texture = farmer + "hairstyles";
            int tileX = index % columns, tileY = index / columns, covered = -1;
            bool left = false, bald = index is >= 49 and <= 55;
            if (hairData.TryGetValue(index, out var raw))
            {
                var fields = raw.Split('/');
                if (fields.Length < 3) throw new FormatException("Data/HairData has fewer than three fields for " + index);
                texture = farmer + fields[0];
                tileX = int.Parse(fields[1], CultureInfo.InvariantCulture);
                tileY = int.Parse(fields[2], CultureInfo.InvariantCulture);
                left = fields.Length > 3 && fields[3].Equals("true", StringComparison.OrdinalIgnoreCase);
                covered = fields.Length > 4 ? int.Parse(fields[4], CultureInfo.InvariantCulture) : -1;
                bald = fields.Length > 5 && fields[5].Equals("true", StringComparison.OrdinalIgnoreCase);
            }
            hairstyles.Add(new JObject
            {
                ["Index"] = index, ["Texture"] = NormalizeAsset(texture), ["TileX"] = tileX, ["TileY"] = tileY,
                ["UsesUniqueLeftSprite"] = left, ["CoveredIndex"] = covered, ["IsBaldStyle"] = bald,
                ["Origin"] = hairData.ContainsKey(index) ? session.Origin("Data/HairData", index.ToString(CultureInfo.InvariantCulture)) : session.Origin(texture)
            });
            session.ScheduleTexture(texture);
        }

        var hats = new JArray();
        parts["Hats"] = hats;
        foreach (var id in session.Helper.GameContent.Load<Dictionary<string, string>>("Data/Hats").Keys.OrderBy(id => id, StringComparer.Ordinal))
        {
            var hat = new Hat(id);
            var parsed = ItemRegistry.GetDataOrErrorItem(hat.QualifiedItemId);
            hats.Add(new JObject { ["Id"] = hat.QualifiedItemId, ["Name"] = parsed.DisplayName,
                ["Texture"] = NormalizeAsset(parsed.TextureName), ["SpriteIndex"] = parsed.SpriteIndex,
                ["HairDrawType"] = hat.hairDrawType.Value, ["IgnoreHairstyleOffset"] = hat.ignoreHairstyleOffset.Value,
                ["IsMask"] = hat.isMask, ["IsPrismatic"] = hat.isPrismatic.Value, ["Origin"] = session.Origin("Data/Hats", id) });
            session.ScheduleTexture(parsed.TextureName);
        }

        var shirts = new JArray();
        parts["Shirts"] = shirts;
        foreach (var pair in session.Helper.GameContent.Load<Dictionary<string, ShirtData>>("Data/Shirts").OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var data = pair.Value;
            var texture = data.Texture ?? farmer + "shirts";
            shirts.Add(new JObject { ["Id"] = "(S)" + pair.Key, ["Texture"] = NormalizeAsset(texture), ["SpriteIndex"] = data.SpriteIndex,
                ["DefaultColor"] = data.DefaultColor, ["CanBeDyed"] = data.CanBeDyed, ["IsPrismatic"] = data.IsPrismatic,
                ["HasSleeves"] = data.HasSleeves, ["Origin"] = session.Origin("Data/Shirts", pair.Key) });
            session.ScheduleTexture(texture);
        }
        var pants = new JArray();
        parts["Pants"] = pants;
        foreach (var pair in session.Helper.GameContent.Load<Dictionary<string, PantsData>>("Data/Pants").OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var data = pair.Value;
            var texture = data.Texture ?? farmer + "pants";
            pants.Add(new JObject { ["Id"] = "(P)" + pair.Key, ["Texture"] = NormalizeAsset(texture), ["SpriteIndex"] = data.SpriteIndex,
                ["DefaultColor"] = data.DefaultColor, ["CanBeDyed"] = data.CanBeDyed, ["IsPrismatic"] = data.IsPrismatic,
                ["Origin"] = session.Origin("Data/Pants", pair.Key) });
            session.ScheduleTexture(texture);
        }
        var boots = new JArray();
        parts["Boots"] = boots;
        foreach (var id in session.Helper.GameContent.Load<Dictionary<string, string>>("Data/Boots").Keys.OrderBy(id => id, StringComparer.Ordinal))
        {
            var boot = new Boots(id);
            boots.Add(new JObject { ["Id"] = boot.QualifiedItemId, ["Name"] = boot.DisplayName,
                ["ColorIndex"] = boot.indexInColorSheet.Value, ["Origin"] = session.Origin("Data/Boots", id) });
        }

        var accessoryTexture = session.Helper.GameContent.Load<Texture2D>(farmer + "accessories");
        var accessories = new JArray();
        parts["Accessories"] = accessories;
        // Native draw code uses a 32px stride: down and side, with up hidden and left mirrored.
        var renderer = new FarmerRenderer();
        try
        {
            for (var index = 0; index < accessoryTexture.Width / 16 * (accessoryTexture.Height / 32); index++)
                accessories.Add(new JObject { ["Index"] = index, ["Texture"] = farmer + "accessories",
                    ["IsFacialHair"] = renderer.isAccessoryFacialHair(index), ["DrawBelowHair"] = renderer.drawAccessoryBelowHair(index) });
        }
        finally { renderer.unload(); }
    }
}
