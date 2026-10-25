using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StardewValley;
using StardewValley.Objects;

namespace SDCharacterStudio.Bridge.Export;

/// <summary>Exports native farmer canvases from an owned appearance snapshot.</summary>
public static class FarmerExporter
{
    private sealed record Pose(string Name, int Facing, int[] Frames, int Ms = 200, int ArmOffset = 6, int[]? Durations = null);
    private static readonly Pose[] Poses =
    {
        new("standing", 2, new[] { 0 }, 0), new("standing", 1, new[] { 6 }, 0),
        new("standing", 0, new[] { 12 }, 0), new("standing", 3, new[] { 6 }, 0),
        new("walking", 2, new[] { 1, 0, 2, 0 }), new("walking", 1, new[] { 7, 6, 8, 6 }),
        new("walking", 0, new[] { 13, 12, 14, 12 }), new("walking", 3, new[] { 7, 6, 8, 6 }),
        new("sitting", 2, new[] { 107 }, 0, 12), new("sitting", 1, new[] { 117 }, 0),
        new("sitting", 0, new[] { 113 }, 0), new("sitting", 3, new[] { 117 }, 0),
        new("holding-up-item", 2, new[] { 57 }, 0),
        new("eating", 2, new[] { 0, 84, 85, 86, 86, 87, 88, 87, 88, 87, 0 }, 250, 6,
            new[] { 0, 250, 400, 1, 400, 250, 250, 250, 250, 250, 250 }),
        new("drinking", 2, new[] { 0, 90, 91, 92, 93, 92, 93, 92, 93, 91, 90 }, 250, 6,
            new[] { 1, 250, 150, 250, 200, 250, 200, 250, 200, 250, 50 }),
        new("tired", 2, new[] { 16 }, 0), new("passed-out", 2, new[] { 5 }, 0),
        new("sick", 2, new[] { 104, 105 }, 350)
    };

    internal static string FacingName(int facing) => facing switch { 0 => "up", 1 => "right", 2 => "down", 3 => "left", _ => throw new ArgumentOutOfRangeException(nameof(facing)) };

    public static JArray AnimationMetadata() => new(Poses.Select(pose => new JObject
    {
        ["Name"] = pose.Name, ["Facing"] = FacingName(pose.Facing),
        ["Frames"] = new JArray(pose.Frames.Select((frame, index) => new JObject { ["Frame"] = frame, ["ArmOffset"] = pose.ArmOffset, ["Ms"] = pose.Durations?[index] ?? pose.Ms }))
    }));

    /// <returns>Relative directory containing the completed appearance's parts and metadata.</returns>
    public static string Schedule(ExportSession session, Farmer farmer, bool currentPlayer = true)
    {
        ArgumentNullException.ThrowIfNull(farmer);
        var snapshot = farmer.CreateFakeEventFarmer();
        // The native fake factory shares equipped item references; own these too for queued work.
        snapshot.hat.Value = farmer.hat.Value?.getOne() as Hat;
        snapshot.boots.Value = farmer.boots.Value?.getOne() as Boots;
        snapshot.shirtItem.Value = farmer.shirtItem.Value?.getOne() as Clothing;
        snapshot.pantsItem.Value = farmer.pantsItem.Value?.getOne() as Clothing;
        snapshot.bathingClothes.Value = farmer.bathingClothes.Value;
        snapshot.prismaticHair.Value = farmer.prismaticHair.Value;
        snapshot.currentEyes = farmer.currentEyes;
        snapshot.FarmerRenderer.textureName.Value = farmer.FarmerRenderer.textureName.Value;
        snapshot.FarmerRenderer.heightOffset.Value = farmer.FarmerRenderer.heightOffset.Value;
        session.RegisterCleanup(() =>
        {
            // Native unload omits the independently created recoloured texture. This field is
            // verified in the installed 1.6.15 engine; only the owned fake renderer is touched.
            try { session.Helper.Reflection.GetField<Texture2D?>(snapshot.FarmerRenderer, "baseTexture").GetValue()?.Dispose(); }
            finally { snapshot.FarmerRenderer.unload(); }
        });
        var appearance = Describe(snapshot);
        // Name, farmer identity, renderer identity and transient animation do not affect this key.
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(appearance.ToString(Formatting.None)))).ToLowerInvariant();
        var folder = "farmer/parts/" + hash;
        var metadata = (JObject)appearance.DeepClone();
        metadata["Name"] = farmer.Name;
        metadata["AppearanceHash"] = hash;
        metadata["PartsFolder"] = folder;
        metadata["Canvas"] = new JArray(32, 48);
        metadata["BaseOrigin"] = new JArray(8, 16);
        metadata["FrameCount"] = FarmerRenderer.featureYOffsetPerFrame.Length;
        metadata["BaseFiles"] = new JObject { ["Base"] = "base_{frame:000}.png", ["Head"] = "head_{frame:000}.png", ["Body"] = "body_{frame:000}.png", ["Facing"] = "down" };
        metadata["AnimationFrames"] = AnimationMetadata();
        var files = new JArray();
        metadata["PartFiles"] = files;

        for (var frame = 0; frame < FarmerRenderer.featureYOffsetPerFrame.Length; frame++)
            foreach (var part in new[] { "Base", "Head", "Body" })
                SchedulePart(session, snapshot, folder, files, part, frame, 2, 6, part.ToLowerInvariant() + "_" + frame.ToString("000") + ".png");

        var curated = Poses.SelectMany(pose => pose.Frames.Select(frame => (pose.Facing, Frame: frame, pose.ArmOffset))).Distinct().ToArray();
        foreach (var (facing, frame, armOffset) in curated)
            foreach (var part in new[] { "Head", "Hair", "Hat", "Shirt", "Pants", "Eyes", "Accessory", "Arms", "Full", "Shoes" })
                SchedulePart(session, snapshot, folder, files, part, frame, facing, armOffset,
                    part.ToLowerInvariant() + "_" + FacingName(facing) + "_" + frame.ToString("000") + ".png");

        session.Queue("farmer appearance metadata", () =>
        {
            session.Json(folder + "/appearance.json", metadata);
            if (currentPlayer)
            {
                session.Json("farmer/player.json", metadata);
                session.Catalog["Player"] = "farmer/player.json";
            }
        });
        return folder;
    }

    private static void SchedulePart(ExportSession session, Farmer snapshot, string folder, JArray files, string part, int frame, int facing, int armOffset, string file)
    {
        files.Add(new JObject { ["Part"] = part, ["Frame"] = frame, ["Facing"] = FacingName(facing), ["ArmOffset"] = armOffset, ["File"] = file });
        session.Queue("farmer " + file, () =>
        {
            // Set a callback-free standalone animation on the owned fake; Runtime reads its
            // arm offsets only when the requested frame is the current frame.
            snapshot.FarmerSprite.setCurrentSingleFrame(frame, 32000, secondaryArm: armOffset == 12, flip: facing == 3);
            session.Image(folder + "/" + file, session.Runtime.RenderFarmerPart(snapshot, frame, facing, part));
        });
    }

    private static JObject Describe(Farmer farmer)
    {
        farmer.GetDisplayShirt(out var shirtTexture, out var shirtIndex);
        farmer.GetDisplayPants(out var pantsTexture, out var pantsIndex);
        return new JObject
        {
            ["IsMale"] = farmer.IsMale, ["Skin"] = farmer.skin.Value, ["Hair"] = farmer.hair.Value,
            ["HairColor"] = Hex(farmer.hairstyleColor.Value), ["EyeColor"] = Hex(farmer.newEyeColor.Value),
            ["Accessory"] = farmer.accessory.Value, ["Hat"] = farmer.hat.Value?.QualifiedItemId,
            ["Shirt"] = "(S)" + farmer.GetShirtId(), ["ShirtColor"] = Hex(farmer.GetShirtColor()),
            ["Pants"] = "(P)" + farmer.GetPantsId(), ["PantsColor"] = Hex(farmer.GetPantsColor()),
            ["Boots"] = farmer.boots.Value?.QualifiedItemId, ["ShoeColorIndex"] = farmer.shoes.Value,
            ["BaseTexture"] = CatalogExporter.NormalizeAsset(farmer.FarmerRenderer.textureName.Value),
            ["HeightOffset"] = farmer.FarmerRenderer.heightOffset.Value,
            ["ShirtTexture"] = shirtTexture.Name, ["ShirtSpriteIndex"] = shirtIndex,
            ["PantsTexture"] = pantsTexture.Name, ["PantsSpriteIndex"] = pantsIndex,
            ["BathingClothes"] = farmer.bathingClothes.Value, ["CurrentEyes"] = farmer.currentEyes,
            ["PrismaticHair"] = farmer.prismaticHair.Value, ["PrismaticHat"] = farmer.hat.Value?.isPrismatic.Value ?? false,
            ["PrismaticShirt"] = farmer.shirtItem.Value?.isPrismatic.Value ?? false, ["PrismaticPants"] = farmer.pantsItem.Value?.isPrismatic.Value ?? false
        };
    }

    private static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
