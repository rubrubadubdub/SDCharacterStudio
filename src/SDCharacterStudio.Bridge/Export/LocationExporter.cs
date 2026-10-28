using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core;
using SDCharacterStudio.Core.Compositing;
using StardewValley;
using StardewValley.GameData.Characters;
using StardewValley.Locations;
using xTile;
using xTile.Tiles;

namespace SDCharacterStudio.Bridge.Export;

/// <summary>Composes live map layers in native16px tile space without changing game draw state.</summary>
public static class LocationExporter
{
    /// <summary>The upgraded farmhouse layouts exported for the stage (D-52): level → the married house's map.</summary>
    private static readonly (int Level, string Asset)[] HouseLayouts = { (1, "Maps/FarmHouse1_marriage"), (2, "Maps/FarmHouse2_marriage") };

    public static void Schedule(ExportSession session)
    {
        var locations = Game1.locations.Where(x => x.Map != null).Distinct().ToArray();
        var entries = new JArray(); session.Catalog["Locations"] = entries;
        var layouts = new JArray(); session.Catalog["HouseLayouts"] = layouts;
        void Next(int index)
        {
            if (index < locations.Length) { session.Queue("location " + locations[index].NameOrUniqueName, () => Live(locations[index], () => Next(index + 1))); return; }
            // Then the upgraded farmhouse as mods make it, for scenes staged in a house the player may not have yet.
            NextLayout(0);
        }
        void NextLayout(int index)
        {
            if (index >= HouseLayouts.Length) return;
            var (level, asset) = HouseLayouts[index];
            session.Queue("house layout " + level, () =>
            {
                try { Layout(level, asset, () => NextLayout(index + 1)); }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // A mod that removes a layout shouldn't fail the whole export: the stage then says it's missing.
                    session.Monitor.Log($"Couldn't export the farmhouse layout {asset}: {ex.Message}", StardewModdingAPI.LogLevel.Warn);
                    NextLayout(index + 1);
                }
            });
        }
        void Live(GameLocation location, Action next)
        {
            var map = location.Map;
            string asset = location.mapPath.Value ?? "Maps/" + location.Name;
            var descriptor = Descriptor(location.NameOrUniqueName, asset, map);
            descriptor["Warps"] = new JArray(location.warps.Select(x => new JObject { ["Tile"] = new JArray(x.X, x.Y), ["To"] = x.TargetName, ["ToTile"] = new JArray(x.TargetX, x.TargetY) }));
            if (location is FarmHouse house)
            {
                // The house as this save has it: its level, and its spouse's spot when it has a spouse's room (D-52).
                descriptor["Level"] = house.upgradeLevel;
                if (house.upgradeLevel >= 1 && house.HasNpcSpouseOrRoommate())
                {
                    var spot = house.GetSpouseRoomSpot();
                    descriptor["Places"] = new JObject { [Places.SpouseRoom] = new JArray(spot.X, spot.Y) };
                }
            }
            Render(session, location.NameOrUniqueName, map, (layer, x, y) => map.GetLayer(layer)?.Tiles[x, y], (x, y) => location.isTilePassable(new Vector2(x, y)), descriptor, entries, next);
        }
        void Layout(int level, string asset, Action next)
        {
            var map = session.Helper.GameContent.Load<Map>(asset);
            string name = "FarmHouse" + level;
            var room = SpouseRoom(session, map, level);
            var descriptor = Descriptor(name, asset, map);
            descriptor["Of"] = "FarmHouse"; descriptor["Level"] = level;
            Tile? TileAt(string layer, int x, int y) => room?.TileAt(layer, x, y, out var tile) == true ? tile : map.GetLayer(layer)?.Tiles[x, y];
            if (room?.Spot(TileAt) is { } spot) descriptor["Places"] = new JObject { [Places.SpouseRoom] = new JArray(spot.X, spot.Y) };
            if (room != null) descriptor["SpouseRoom"] = room.Who;
            Render(session, name, map, TileAt, (x, y) => Passable(TileAt, x, y), descriptor, layouts, next);
        }
        Next(0);
    }

    private static JObject Descriptor(string name, string asset, Map map)
    {
        string stem = "locations/" + Uri.EscapeDataString(name);
        return new JObject
        {
            ["Name"] = name, ["MapAsset"] = asset, ["Size"] = new JArray(map.Layers[0].LayerWidth, map.Layers[0].LayerHeight),
            ["Back"] = stem + ".back.png", ["Front"] = stem + ".front.png", ["Preview"] = stem + ".preview.png", ["Passable"] = stem + ".passable.png",
            ["Layers"] = new JArray(map.Layers.Select(x => new JObject { ["Id"] = x.Id, ["Size"] = new JArray(x.LayerWidth, x.LayerHeight) })),
            ["Warps"] = new JArray(),
        };
    }

    /// <summary>The game's GameLocation.isTilePassable, for a map that isn't a live location.</summary>
    private static bool Passable(Func<string, int, int, Tile?> tileAt, int x, int y)
    {
        if (tileAt("Back", x, y) is { } back && back.TileIndexProperties.ContainsKey("Passable")) return false;
        return tileAt("Buildings", x, y) is not { } building || building.TileIndexProperties.ContainsKey("Shadow") || building.TileIndexProperties.ContainsKey("Passable");
    }

    private static void Render(ExportSession session, string name, Map map, Func<string, int, int, Tile?> tileAt, Func<int, int, bool> passable, JObject descriptor, JArray entries, Action next)
    {
        int width = map.Layers[0].LayerWidth, height = map.Layers[0].LayerHeight;
        string stem = "locations/" + Uri.EscapeDataString(name);
        descriptor["Origin"] = session.Origin((string)descriptor["MapAsset"]!);
        var back = new RgbaImage(checked(width * 16), checked(height * 16), new uint[checked(width * height * 256)]);
        var front = new RgbaImage(back.Width, back.Height, new uint[back.Pixels.Length]);
        var walkable = new RgbaImage(width, height, new uint[checked(width * height)]);
        var sheets = new Dictionary<string, RgbaImage>(StringComparer.OrdinalIgnoreCase);
        var cells = new Dictionary<(string Sheet, int X, int Y, int Width, int Height), RgbaImage>();
        foreach (string layerName in new[] { "Back", "Back2", "Back3", "Buildings", "Front", "Front2", "AlwaysFront" })
        {
            var layer = map.GetLayer(layerName); if (layer == null) continue;
            var output = layerName.StartsWith("Front") || layerName == "AlwaysFront" ? front : back;
            for (int y = 0; y < Math.Min(height, layer.LayerHeight); y++)
            {
                int row = y;
                session.Queue($"map {name}/{layerName}/{row}", () =>
                {
                    for (int x = 0; x < Math.Min(width, layer.LayerWidth); x++)
                    {
                        var tile = tileAt(layerName, x, row);
                        if (tile is AnimatedTile animated) tile = animated.TileFrames[0];
                        if (tile == null || tile.TileIndex < 0) continue;
                        string source = tile.TileSheet.ImageSource;
                        if (!sheets.TryGetValue(source, out var image)) sheets[source] = image = ExportSession.Read(session.Helper.GameContent.Load<Texture2D>(source));
                        // The installed xTile renderer uses declared sheet columns and
                        // native16px bounds, even when a TMX sheet reports another TileSize.
                        var bounds = tile.TileSheet.GetTileImageBounds(tile.TileIndex);
                        int cellWidth = bounds.Width, cellHeight = bounds.Height;
                        var key = (source, bounds.X, bounds.Y, cellWidth, cellHeight);
                        if (!cells.TryGetValue(key, out var cell))
                        {
                            int sx = bounds.X, sy = bounds.Y;
                            if (cellWidth <= 0 || cellHeight <= 0 || sx < 0 || sy < 0 || (long)sx + cellWidth > image.Width || (long)sy + cellHeight > image.Height)
                                throw new InvalidDataException($"Tile {tile.TileIndex} bounds {sx},{sy},{cellWidth},{cellHeight} lie outside tilesheet {source} ({image.Width}x{image.Height}).");
                            var pixels = new uint[checked(cellWidth * cellHeight)];
                            for (int cy = 0; cy < cellHeight; cy++) Array.Copy(image.Pixels, (sy + cy) * image.Width + sx, pixels, cy * cellWidth, cellWidth);
                            cells[key] = cell = new(cellWidth, cellHeight, pixels);
                        }
                        Compositor.Blit(output, cell, x * 16, row * 16);
                    }
                });
            }
        }
        session.Queue("passability " + name, () =>
        {
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                walkable.Pixels[y * width + x] = passable(x, y) ? 0xFFFFFFFF : 0xFF000000;
        });
        session.Queue("map back " + name, () => session.Image(stem + ".back.png", back));
        session.Queue("map front " + name, () => session.Image(stem + ".front.png", front));
        session.Queue("map preview " + name, () => { Compositor.Blit(back, front, 0, 0); session.Image(stem + ".preview.png", back); });
        session.Queue("map metadata " + name, () => { session.Image(stem + ".passable.png", walkable); session.Json(stem + ".json", descriptor); entries.Add(descriptor); next(); });
    }

    /// <summary>
    /// The spouse's room in a layout, as FarmHouse.loadSpouseRoom puts it in: this player's spouse's room if they have one,
    /// else the game's default room. The layout's own map is only read, never changed.
    /// </summary>
    private static SpouseRoomOverlay? SpouseRoom(ExportSession session, Map house, int level)
    {
        var corner = Places.SpouseRoomCorner(level);
        if (house.Properties.TryGetValue("SpouseRoomPosition", out var property)
            && ((string?)property)?.Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: 2 } parts
            && int.TryParse(parts[0], out int px) && int.TryParse(parts[1], out int py))
            corner = (px, py);
        string? spouse = Game1.player is { } player && player.spouse != null && player.isMarriedOrRoommates() && NPC.TryGetData(player.spouse, out _) ? player.spouse : null;
        CharacterSpouseRoomData? data = spouse != null && NPC.TryGetData(spouse, out var character) ? character?.SpouseRoom : null;
        var source = data?.MapSourceRect ?? CharacterSpouseRoomData.DefaultMapSourceRect;
        try
        {
            var room = session.Helper.GameContent.Load<Map>("Maps/" + (data?.MapAsset ?? "spouseRooms"));
            return new SpouseRoomOverlay(room, source, new Rectangle(corner.X, corner.Y, source.Width, source.Height), spouse ?? "");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            session.Monitor.Log($"Couldn't read the spouse's room for the farmhouse layout: {ex.Message}", StardewModdingAPI.LogLevel.Trace);
            return null;
        }
    }

    private sealed class SpouseRoomOverlay
    {
        private readonly Map room;
        private readonly Rectangle source, target;
        public string Who { get; }
        public SpouseRoomOverlay(Map room, Rectangle source, Rectangle target, string who) { this.room = room; this.source = source; this.target = target; Who = who; }

        /// <summary>The tile at a house tile inside the room (true), as GameLocation.ApplyMapOverride copies it in.</summary>
        public bool TileAt(string layer, int x, int y, out Tile? tile)
        {
            tile = null;
            if (!target.Contains(x, y)) return false;
            int sx = source.X + x - target.X, sy = source.Y + y - target.Y;
            // Layers in the room's order: once one has a tile here, every later layer replaces the house's (an empty one clears it).
            int first = -1, own = -1;
            for (int i = 0; i < room.Layers.Count; i++)
            {
                var candidate = room.Layers[i];
                bool inside = sx < candidate.LayerWidth && sy < candidate.LayerHeight;
                if (first < 0 && inside && candidate.Tiles[sx, sy] != null) first = i;
                if (string.Equals(candidate.Id, layer, StringComparison.Ordinal)) own = i;
            }
            if (own < 0 || first < 0 || own < first) return false;
            // The game keeps the house's own front tiles along the room's bottom row.
            if (layer == "Front" && y == target.Bottom - 1) return false;
            var from = room.Layers[own];
            tile = sx < from.LayerWidth && sy < from.LayerHeight ? from.Tiles[sx, sy] : null;
            return true;
        }

        /// <summary>The spouse's spot: the room's marked tile (Paths index 7), else the corner plus (3, 4).</summary>
        public (int X, int Y) Spot(Func<string, int, int, Tile?> tileAt)
        {
            for (int x = target.Left; x < target.Right; x++)
                for (int y = target.Top; y < target.Bottom; y++)
                    if (tileAt("Paths", x, y)?.TileIndex == 7) return (x, y);
            return Places.SpouseRoomSpot((target.X, target.Y));
        }
    }
}
