using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StardewModdingAPI;
using StardewValley;

namespace SdcsBench.Driver;

/// <summary>
/// What the bench mods offer other mods through SMAPI (the same members as the exported mods' API). The driver uses it to
/// play their scenes; it has no SDCharacterStudio reference at all, like a player's game.
/// </summary>
public interface IBenchApi
{
    bool PlayCutscene(string cutscene, bool checkConditions = false);
    bool IsPlaying { get; }
    void StopCutscene();
    IReadOnlyList<string> GetCutscenes();
    string EngineVersion { get; }
    string CommandPrefix { get; }
}

/// <summary>
/// Test driver for the SDCS benches. On launch it makes a disposable in-memory farmer in ScienceHouse at 17:00 (no save is
/// loaded or written), then answers requests: write <c>assertions/&lt;id&gt;.json</c> ({"Check": …}) and read
/// <c>assertion-results/&lt;id&gt;.json</c>. It also writes <c>state.json</c> every few ticks.
/// </summary>
public sealed class ModEntry : Mod
{
    private int ticks;
    private bool initialized, ready, failed;
    private string At(string name) => Path.Combine(Helper.DirectoryPath, name);

    public override void Entry(IModHelper helper)
    {
        Directory.CreateDirectory(At("assertions"));
        Directory.CreateDirectory(At("assertion-results"));
        foreach (string marker in new[] { "ready.json", "finished.json", "failed.json", "state.json" })
            if (File.Exists(At(marker))) File.Delete(At(marker));
        helper.Events.GameLoop.UpdateTicked += (_, _) => Tick();
        helper.Events.GameLoop.Saving += (_, _) =>
        {
            Failure(new Exception("Saving was attempted: the bench driver never saves."));
            throw new InvalidOperationException("The bench driver must never save.");
        };
    }

    private void Tick()
    {
        if (failed) return;
        try
        {
            ticks++;
            if (ticks == 120)
            {
                Game1.exitActiveMenu();
                Game1.game1.loadForNewGame();
                Game1.player.Name = "SDCSBench";
                Game1.player.farmName.Value = "Bench";
                Game1.dayOfMonth = 1; Game1.timeOfDay = 1700; Game1.gameMode = 3;
                Game1.currentLocation = Game1.getLocationFromName("ScienceHouse");
                Game1.player.currentLocation = Game1.currentLocation;
                foreach (string name in new[] { "ScienceHouse", "Town", "Farm", "FarmHouse" })
                    if (Game1.getLocationFromName(name)?.TryGetLocationEvents(out _, out var events) == true)
                        foreach (string key in events.Keys) Game1.player.eventsSeen.Add(key.Split('/')[0]);
                Game1.player.Position = new Vector2(16 * 64, 9 * 64);
                Game1.player.CanMove = true;
                Game1.options.pauseWhenOutOfFocus = false;
                initialized = true;
            }
            if (!initialized) return;
            Game1.timeOfDay = 1700;
            if (!ready && ticks > 240 && Context.IsWorldReady && Context.IsPlayerFree && !Game1.globalFade && !Game1.fadeToBlack && !Game1.isWarping)
            {
                ready = true;
                Write("ready.json", new { Ready = true, Tick = ticks });
                Monitor.Log("READY disposable world", LogLevel.Info);
            }
            if (!ready && ticks > 7200) throw new TimeoutException("World did not become ready in 7200 ticks.");
            // During a scene, often enough to see the camera move frame by frame (a jump shows as one big step).
            if (ready && ticks % (Game1.eventUp || Game1.globalFade || Game1.fadeToBlack ? 2 : 10) == 0) { Write("state.json", State()); ProcessAssertions(); }
        }
        catch (Exception ex) { Failure(ex); }
    }

    private JObject State() => new()
    {
        ["Tick"] = ticks, ["WorldReady"] = Context.IsWorldReady, ["PlayerFree"] = Context.IsPlayerFree, ["EventUp"] = Game1.eventUp,
        ["Event"] = Game1.CurrentEvent?.id, ["Location"] = Game1.currentLocation?.NameOrUniqueName, ["DialogueUp"] = Game1.dialogueUp,
        // What the player wears (costume checks: a scene's clothes and whether they come off at the end).
        ["Hat"] = Game1.player?.hat.Value?.QualifiedItemId, ["Shirt"] = Game1.player?.shirtItem.Value?.QualifiedItemId,
        // The camera and the fade (camera checks: nothing jumps while the scene is on screen).
        ["ViewportX"] = Game1.viewport.X, ["ViewportY"] = Game1.viewport.Y, ["Fade"] = Game1.fadeToBlackAlpha, ["GlobalFade"] = Game1.globalFade,
        ["PlayerTile"] = Game1.player == null ? null : new JArray(Game1.player.TilePoint.X, Game1.player.TilePoint.Y),
        // Story flags and numbers scenes set (logic checks: which branch a Check or Chance took).
        ["Mail"] = Game1.player == null ? null : new JArray(Game1.player.mailReceived.Where(m => m.StartsWith("SDCS.", StringComparison.Ordinal)).OrderBy(m => m, StringComparer.Ordinal)),
        ["Stats"] = Game1.player == null ? null : new JObject(Game1.player.stats.Values.Where(p => p.Key.StartsWith("SDCS.", StringComparison.Ordinal)).Select(p => new JProperty(p.Key, p.Value))),
        // Furniture pictures among the location's sprites (a scene's Prop beats: on during the scene, gone after it).
        ["FurnitureSprites"] = Game1.currentLocation?.temporarySprites.Count(s => s.texture?.Name?.Contains("furniture", StringComparison.OrdinalIgnoreCase) == true) ?? 0,
        // A scene can only start once warps and fades have settled.
        ["Settled"] = Context.IsPlayerFree && !Game1.eventUp && !Game1.isWarping && !Game1.globalFade && !Game1.fadeToBlack && Game1.locationRequest == null,
    };

    private void ProcessAssertions()
    {
        foreach (string path in Directory.EnumerateFiles(At("assertions"), "*.json").OrderBy(p => p, StringComparer.Ordinal).Take(8))
        {
            string id = Path.GetFileNameWithoutExtension(path);
            try
            {
                var request = JObject.Parse(File.ReadAllText(path));
                string check = (string?)request["Check"] ?? throw new ArgumentException("Missing Check");
                Write("assertion-results/" + id + ".json", new { Id = id, Check = check, Ok = true, Data = Execute(check, request) });
            }
            catch (JsonReaderException) { continue; }
            catch (IOException) { continue; }   // the test is still putting the request in place: read it next tick
            catch (Exception ex) { Write("assertion-results/" + id + ".json", new { Id = id, Ok = false, Error = ex.ToString() }); }
            File.Move(path, Path.ChangeExtension(path, ".done"), true);
        }
    }

    private IBenchApi Bench(JObject request)
    {
        string mod = (string?)request["Mod"] ?? throw new ArgumentException("Missing Mod");
        return Helper.ModRegistry.GetApi<IBenchApi>(mod) ?? throw new InvalidOperationException($"Mod '{mod}' isn't loaded or has no API.");
    }

    private JObject Execute(string check, JObject request)
    {
        switch (check)
        {
            case "state": return State();
            case "bench":
                var api = Bench(request);
                return new JObject { ["EngineVersion"] = api.EngineVersion, ["CommandPrefix"] = api.CommandPrefix, ["Cutscenes"] = new JArray(api.GetCutscenes()), ["IsPlaying"] = api.IsPlaying };
            case "play":
                return new JObject { ["Started"] = Bench(request).PlayCutscene((string?)request["Cutscene"] ?? throw new ArgumentException("Missing Cutscene")) };
            case "stop":
                Bench(request).StopCutscene();
                return new JObject();
            case "actors":
                var current = Game1.CurrentEvent ?? throw new InvalidOperationException("No event is running.");
                return new JObject
                {
                    ["Event"] = current.id,
                    ["Actors"] = new JArray(current.actors.Select(a => new JObject
                    {
                        ["Name"] = a.Name, ["Tile"] = new JArray(a.TilePoint.X, a.TilePoint.Y), ["Facing"] = a.FacingDirection, ["Invisible"] = a.IsInvisible
                    })),
                };
            case "forget":
                // Un-see a mod's scenes (the world starts with every event here seen), so walking in can trigger them.
                string prefix = (string?)request["Prefix"] ?? throw new ArgumentException("Missing Prefix");
                int forgotten = Game1.player.eventsSeen.RemoveWhere(id => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                return new JObject { ["Forgotten"] = forgotten };
            case "warp":
                // Walk into a location the way a player does: vanilla checks that location's events on arrival.
                Game1.warpFarmer((string?)request["Location"] ?? throw new ArgumentException("Missing Location"),
                    (int?)request["X"] ?? 0, (int?)request["Y"] ?? 0, (int?)request["Facing"] ?? 0);
                return new JObject { ["Warping"] = true };
            case "looks":
                // What villagers wear (outfit checks): each event actor's sheet and one of its pixels, and the same pixel of
                // every stand-in a pose draws (generated textures have no asset name).
                // With W and H: a block of pixels from there (row by row) instead of one.
                int x = (int?)request["X"] ?? 0, y = (int?)request["Y"] ?? 0, w = (int?)request["W"] ?? 1, h = (int?)request["H"] ?? 1;
                JToken Look(Microsoft.Xna.Framework.Graphics.Texture2D texture, int left, int top) => w == 1 && h == 1 ? (JToken?)Pixel(texture, left, top) ?? JValue.CreateNull()
                    : new JArray(Enumerable.Range(0, w * h).Select(i => Pixel(texture, left + i % w, top + i / w)));
                return new JObject
                {
                    ["Actors"] = new JArray((Game1.CurrentEvent?.actors ?? new List<NPC>()).Select(a => new JObject
                    {
                        ["Name"] = a.Name, ["Sheet"] = a.Sprite.loadedTexture, ["Size"] = new JArray(a.Sprite.Texture.Width, a.Sprite.Texture.Height),
                        ["Pixel"] = Look(a.Sprite.Texture, x, y), ["Invisible"] = a.IsInvisible,
                    })),
                    ["StandIns"] = new JArray((Game1.currentLocation?.temporarySprites.ToList() ?? new List<TemporaryAnimatedSprite>())
                        .Where(s => s.texture != null && string.IsNullOrEmpty(s.texture.Name) && s.sourceRect.Width > 0)
                        .Select(s => Look(s.texture, s.sourceRect.X + x, s.sourceRect.Y + y))),
                };
            case "home":
            {
                // The farmhouse for place checks (D-52): optionally married to a villager, at an upgrade level; the player
                // is then sent in through its door, so the house loads as the game shows it.
                var player = Game1.player;
                if ((string?)request["Spouse"] is { Length: > 0 } spouse)
                {
                    player.spouse = spouse;
                    player.friendshipData[spouse] = new Friendship(2500) { Status = FriendshipStatus.Married, WeddingDate = new WorldDate(1, Season.Spring, 1) };
                }
                var house = Utility.getHomeOfFarmer(player);
                if ((int?)request["Level"] is { } level) house.setMapForUpgradeLevel(level);
                var spot = house.GetSpouseRoomSpot();
                var entry = house.getEntryLocation();
                Game1.warpFarmer(house.NameOrUniqueName, entry.X, entry.Y, 0);
                return new JObject
                {
                    ["Level"] = house.upgradeLevel, ["HasSpouse"] = house.HasNpcSpouseOrRoommate(), ["SpouseRoom"] = new JArray(spot.X, spot.Y),
                    ["Entry"] = new JArray(entry.X, entry.Y), ["Size"] = new JArray(house.Map.Layers[0].LayerWidth, house.Map.Layers[0].LayerHeight),
                };
            }
            case "query":
                // A game state query where the player is (a scene's trigger conditions, e.g. SDCS_HAS_PLACE SpouseRoom).
                return new JObject { ["Result"] = GameStateQuery.CheckConditions((string?)request["Query"] ?? throw new ArgumentException("Missing Query"), Game1.currentLocation, Game1.player) };
            case "finish":
                Write("finished.json", new { Ok = true, Tick = ticks });
                return new JObject { ["Finished"] = true };
            default: throw new ArgumentException("Unknown check " + check);
        }
    }

    /// <summary>One texture pixel as #AARRGGBB (null outside it).</summary>
    private static string? Pixel(Microsoft.Xna.Framework.Graphics.Texture2D texture, int x, int y)
    {
        if (x < 0 || y < 0 || x >= texture.Width || y >= texture.Height) return null;
        var data = new Color[1];
        texture.GetData(0, new Rectangle(x, y, 1, 1), data, 0, 1);
        return $"#{data[0].A:X2}{data[0].R:X2}{data[0].G:X2}{data[0].B:X2}";
    }

    private void Write(string name, object value)
    {
        string path = At(name), temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonConvert.SerializeObject(value, Formatting.Indented));
        // A test may be reading the old file this instant: try again shortly rather than fail the run.
        for (int attempt = 0; ; attempt++)
        {
            try { File.Move(temporary, path, true); return; }
            catch (Exception ex) when (attempt < 20 && ex is IOException or UnauthorizedAccessException) { Thread.Sleep(5); }
        }
    }

    private void Failure(Exception ex)
    {
        failed = true;
        Write("failed.json", new { Error = ex.ToString() });
        Monitor.Log(ex.ToString(), LogLevel.Error);
    }
}
