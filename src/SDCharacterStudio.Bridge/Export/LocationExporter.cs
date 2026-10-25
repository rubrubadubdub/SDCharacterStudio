using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core.Compositing;
using StardewValley;
using xTile.Tiles;

namespace SDCharacterStudio.Bridge.Export;

/// <summary>Composes live map layers in native16px tile space without changing game draw state.</summary>
public static class LocationExporter
{
    public static void Schedule(ExportSession session)
    {
        var locations = Game1.locations.Where(x => x.Map != null).Distinct().ToArray();
        var entries = new JArray(); session.Catalog["Locations"] = entries;
        void Next(int index)
        {
            if (index >= locations.Length) return;
            session.Queue("location " + locations[index].NameOrUniqueName, () => Start(locations[index], () => Next(index + 1)));
        }
        void Start(GameLocation location, Action next)
        {
            var map = location.Map;
            int width = map.Layers[0].LayerWidth, height = map.Layers[0].LayerHeight;
            string stem = "locations/" + Uri.EscapeDataString(location.NameOrUniqueName);
            var back = new RgbaImage(checked(width * 16), checked(height * 16), new uint[checked(width * height * 256)]);
            var front = new RgbaImage(back.Width, back.Height, new uint[back.Pixels.Length]);
            var passable = new RgbaImage(width, height, new uint[checked(width * height)]);
            var sheets = new Dictionary<string, RgbaImage>(StringComparer.OrdinalIgnoreCase);
            var cells = new Dictionary<(string Sheet, int X, int Y, int Width, int Height), RgbaImage>();
            string asset = location.mapPath.Value ?? "Maps/" + location.Name;
            var descriptor = new JObject
            {
                ["Name"] = location.NameOrUniqueName, ["MapAsset"] = asset, ["Size"] = new JArray(width, height),
                ["Back"] = stem + ".back.png", ["Front"] = stem + ".front.png", ["Preview"] = stem + ".preview.png", ["Passable"] = stem + ".passable.png",
                ["Layers"] = new JArray(map.Layers.Select(x => new JObject { ["Id"] = x.Id, ["Size"] = new JArray(x.LayerWidth, x.LayerHeight) })),
                ["Warps"] = new JArray(location.warps.Select(x => new JObject { ["Tile"] = new JArray(x.X, x.Y), ["To"] = x.TargetName, ["ToTile"] = new JArray(x.TargetX, x.TargetY) })),
                ["Origin"] = session.Origin(asset)
            };
            foreach (string name in new[] { "Back", "Back2", "Back3", "Buildings", "Front", "Front2", "AlwaysFront" })
            {
                var layer = map.GetLayer(name); if (layer == null) continue;
                var output = name.StartsWith("Front") || name == "AlwaysFront" ? front : back;
                for (int y = 0; y < Math.Min(height, layer.LayerHeight); y++)
                {
                    int row = y;
                    session.Queue($"map {location.NameOrUniqueName}/{name}/{row}", () =>
                    {
                        for (int x = 0; x < Math.Min(width, layer.LayerWidth); x++)
                        {
                            var tile = layer.Tiles[x, row];
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
            session.Queue("passability " + location.NameOrUniqueName, () =>
            {
                for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                    passable.Pixels[y * width + x] = location.isTilePassable(new Vector2(x, y)) ? 0xFFFFFFFF : 0xFF000000;
            });
            session.Queue("map back " + location.NameOrUniqueName, () => session.Image(stem + ".back.png", back));
            session.Queue("map front " + location.NameOrUniqueName, () => session.Image(stem + ".front.png", front));
            session.Queue("map preview " + location.NameOrUniqueName, () => { Compositor.Blit(back, front, 0, 0); session.Image(stem + ".preview.png", back); });
            session.Queue("map metadata " + location.NameOrUniqueName, () => { session.Image(stem + ".passable.png", passable); session.Json(stem + ".json", descriptor); entries.Add(descriptor); next(); });
        }
        Next(0);
    }
}
