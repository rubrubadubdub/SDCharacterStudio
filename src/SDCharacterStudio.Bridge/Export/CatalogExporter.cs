using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core.Compositing;
using StardewModdingAPI;
using StardewValley;
using StardewValley.GameData;
using StardewValley.GameData.Characters;
using StardewValley.TokenizableStrings;

namespace SDCharacterStudio.Bridge.Export;

/// <summary>Reads final, patched game content; all calls and queued jobs run on the game thread.</summary>
public static partial class CatalogExporter
{
    public static void ScheduleStatic(ExportSession session)
    {
        // Rebuild discovery on a full static pass; disabled mods' old PNGs can
        // remain on disk for incremental storage without staying in the catalog.
        session.Catalog["Textures"] = new JArray();
        session.Queue("character metadata", () => ScheduleCharacters(session, false));
        session.Queue("farmer wardrobe metadata", () => ScheduleWardrobe(session));
        session.Queue("item metadata", () => ScheduleItems(session));
        session.Queue("sounds, emotes and event commands", () => ScheduleMiscellaneous(session));
    }

    public static void ScheduleSave(ExportSession session)
    {
        session.Queue("current NPC appearances", () => ScheduleCharacters(session, true));
        session.Queue("player appearance", () => FarmerExporter.Schedule(session, Game1.player));
    }

    private static void ScheduleCharacters(ExportSession session, bool current)
    {
        var characters = new JArray();
        session.Catalog["Characters"] = characters;
        var data = session.Helper.GameContent.Load<Dictionary<string, CharacterData>>("Data/Characters");
        foreach (var pair in data.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var name = pair.Key;
            var source = pair.Value;
            var textureName = NPC.getTextureNameForCharacter(name);
            var sprite = "Characters/" + textureName;
            var portrait = "Portraits/" + textureName;
            var record = new JObject
            {
                ["Name"] = name, ["DisplayName"] = TokenParser.ParseText(source.DisplayName ?? name),
                ["TextureName"] = textureName, ["Size"] = new JArray(source.Size.X, source.Size.Y),
                ["Origin"] = session.Origin("Data/Characters", name)
            };
            characters.Add(record);
            var defaultSprite = CharacterTexture(session, record, "Sprite", sprite);
            var defaultPortrait = CharacterTexture(session, record, "Portrait", portrait);
            var appearances = new JArray();
            record["Appearances"] = appearances;
            foreach (var appearance in source.Appearance ?? new List<CharacterAppearanceData>())
            {
                var entry = new JObject
                {
                    ["Id"] = appearance.Id, ["Condition"] = appearance.Condition,
                    ["Season"] = appearance.Season?.ToString(), ["Indoors"] = appearance.Indoors,
                    ["Outdoors"] = appearance.Outdoors, ["IsIslandAttire"] = appearance.IsIslandAttire,
                    ["Precedence"] = appearance.Precedence, ["Weight"] = appearance.Weight
                };
                appearances.Add(entry);
                CharacterTexture(session, entry, "Sprite", appearance.Sprite ?? sprite, defaultSprite);
                CharacterTexture(session, entry, "Portrait", appearance.Portrait ?? portrait, defaultPortrait);
            }
            // Loading, pixel reads and encoding stay in individual queued texture jobs.
            session.Queue("frame count " + name, () =>
            {
                var texture = defaultSprite == null ? null : session.Helper.GameContent.Load<Texture2D>(defaultSprite);
                record["FrameCount"] = texture == null ? 0 : texture.Width / Math.Max(1, source.Size.X) * (texture.Height / Math.Max(1, source.Size.Y));
            });
            if (!current) continue;
            session.Queue("current appearance " + name, () =>
            {
                var npc = Game1.getCharacterFromName(name);
                if (npc == null) return;
                // Portrait's getter initializes the appearance only when the NPC needs it.
                var livePortrait = npc.Portrait;
                var liveSprite = npc.Sprite?.Texture;
                var spriteAsset = NormalizeAsset(npc.Sprite?.loadedTexture ?? npc.Sprite?.overrideTextureName ?? npc.Sprite?.textureName.Value ?? liveSprite?.Name ?? sprite);
                var portraitAsset = NormalizeAsset(string.IsNullOrWhiteSpace(livePortrait?.Name) ? portrait : livePortrait.Name);
                record["Current"] = new JObject { ["Sprite"] = liveSprite == null ? null : spriteAsset, ["Portrait"] = livePortrait == null ? null : portraitAsset, ["AppearanceId"] = npc.LastAppearanceId };
                // Re-register after defaults even when the names match: an event or framework
                // can replace the live texture without changing its advertised content name.
                if (liveSprite != null) session.Queue("live NPC sprite " + name, () => session.Texture(spriteAsset, liveSprite));
                if (livePortrait != null) session.Queue("live NPC portrait " + name, () => session.Texture(portraitAsset, livePortrait));
            });
        }
    }

    private static void ScheduleMiscellaneous(ExportSession session)
    {
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in session.Helper.GameContent.Load<Dictionary<string, AudioCueData>>("Data/AudioChanges").Keys)
            candidates.Add(key);
        // XACT cue names are null-terminated ASCII. Exists rejects incidental binary strings.
        var bank = Path.Combine(Path.GetDirectoryName(typeof(Game1).Assembly.Location)!, "Content", "XACT", "Sound Bank.xsb");
        if (File.Exists(bank))
            foreach (Match match in Regex.Matches(Encoding.ASCII.GetString(File.ReadAllBytes(bank)), @"[A-Za-z_][A-Za-z0-9_ .-]{1,127}(?=\x00)"))
                candidates.Add(match.Value);
        foreach (var cue in new[] { "tinyWhip", "Cowboy_Footstep", "dwop", "thudStep", "coin", "smallSelect", "cancel", "doorClose", "doorCreak", "footstep", "breathin", "breathout" })
            candidates.Add(cue);
        session.Json("sounds.json", candidates.Where(name => Game1.soundBank?.Exists(name) == true).OrderBy(name => name, StringComparer.Ordinal).ToArray());
        session.Catalog["Sounds"] = "sounds.json";
        SoundPreviewExporter.Schedule(session);

        session.ScheduleTexture("TileSheets/emotes");
        session.Queue("emote sheet", () => session.Image("emotes.png", ReadPixels(session.Helper.GameContent.Load<Texture2D>("TileSheets/emotes"))));
        session.Json("emotes.json", Farmer.EMOTES.Select(emote => new
        {
            Name = emote.emoteString, DisplayName = emote.displayName, Id = emote.emoteIconIndex,
            Facing = emote.facingDirection, Hidden = emote.hidden,
            Frames = emote.animationFrames?.Select(frame => new { Frame = frame.frame, Ms = frame.milliseconds, ArmOffset = frame.armOffset }).ToArray()
        }).ToArray());
        session.Catalog["Emotes"] = "emotes.json";
        // Reflection here is the documented public command-handler discovery mechanism.
        Event.TryResolveCommandName("emote", out _); // Initializes native handlers and aliases.
        var commands = new HashSet<string>(typeof(Event.DefaultCommands).GetMethods(BindingFlags.Public | BindingFlags.Static).Select(method => method.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var field in new[] { "Commands", "CommandAliases" })
            if (typeof(Event).GetField(field, BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) is System.Collections.IDictionary handlers)
                foreach (string name in handlers.Keys) commands.Add(name);
        session.Json("event-commands.json", commands.OrderBy(name => name, StringComparer.Ordinal).ToArray());
        session.Catalog["EventCommands"] = "event-commands.json";
    }

    internal static string NormalizeAsset(string name) => name.Replace('\\', '/');

    private static string? CharacterTexture(ExportSession session, JObject record, string key, string asset, string? fallback = null)
    {
        asset = NormalizeAsset(asset);
        if (session.Helper.GameContent.DoesAssetExist<Texture2D>(session.Helper.GameContent.ParseAssetName(asset)))
        {
            record[key] = asset;
            session.ScheduleTexture(asset);
            return asset;
        }
        // Data/Characters also contains dialogue-only actors with no sheet or portrait.
        // Preserve the requested name for diagnostics without advertising a nonexistent PNG.
        record[key] = fallback;
        record["Requested" + key] = asset;
        session.Monitor.Log("Character " + key.ToLowerInvariant() + " is unavailable: " + asset, LogLevel.Trace);
        return fallback;
    }

    internal static RgbaImage ReadPixels(Texture2D texture, Rectangle? region = null)
    {
        var rect = region ?? new Rectangle(0, 0, texture.Width, texture.Height);
        if (rect.Width <= 0 || rect.Height <= 0 || rect.X < 0 || rect.Y < 0 || rect.Right > texture.Width || rect.Bottom > texture.Height)
            throw new InvalidDataException("Item source rectangle is outside its live texture.");
        var colors = new Color[checked(rect.Width * rect.Height)];
        texture.GetData(0, rect, colors, 0, colors.Length);
        var pixels = new uint[colors.Length];
        for (var index = 0; index < colors.Length; index++)
        {
            var color = colors[index];
            if (color.A == 0) continue;
            var r = Math.Min(255, (color.R * 255 + color.A / 2) / color.A);
            var g = Math.Min(255, (color.G * 255 + color.A / 2) / color.A);
            var b = Math.Min(255, (color.B * 255 + color.A / 2) / color.A);
            pixels[index] = (uint)(color.A << 24 | r << 16 | g << 8 | b);
        }
        return new RgbaImage(rect.Width, rect.Height, pixels);
    }
}
