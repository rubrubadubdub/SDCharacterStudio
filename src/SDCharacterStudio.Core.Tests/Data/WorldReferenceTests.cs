using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core.Compiler;
using SDCharacterStudio.Core.Loading;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Validation;
using SDCharacterStudio.Runtime;
using SDCharacterStudio.Runtime.Loading;
using Xunit;

namespace SDCharacterStudio.Core.Tests.Data;

/// <summary>V19: scenes built for SVE, Ridgeside Village or East Scarp content are skipped cleanly on installs without it.</summary>
public sealed class WorldReferenceTests
{
    private static Catalog Game() => new()
    {
        Npcs = { "Robin", "Claire", "JadeMalic" }, Locations = { "ScienceHouse", "Custom_Ridgeside_RidgesideVillage" },
        KnownAssets = new(StringComparer.OrdinalIgnoreCase) { "Characters/Claire_Beach", "Characters/Robin" },
        KnownItems = new(StringComparer.OrdinalIgnoreCase) { "(O)709", "(H)0", "(S)1010", "(B)504" },
        KnownHairstyles = new() { 0, 1, 2 }
    };

    private static LoadedPack Pack(params Layer[] layers)
    {
        var pack = new LoadedPack();
        pack.Poses.Add(new("library/poses/p.json", new Pose { Format = "1.0", Id = "p", FrameSize = new[] { 16, 32 }, Frames = new() { new PoseFrame { Duration = 100, Layers = layers.ToList() } } }));
        pack.Costumes = new("library/costumes.json", new CostumeLibrary { Format = "1.0", Costumes = new() { new Costume { Id = "beach", Set = new(StringComparer.OrdinalIgnoreCase) { ["Hat"] = "(H)Mod.SunHat" } } } });
        return pack;
    }

    private static Cutscene Scene(params Beat[] beats) => new()
    {
        Format = "1.0", Id = "s", Location = "ScienceHouse",
        Actors = new() { new() { Id = "Robin", Kind = "Npc", Start = new() { Tile = new[] { 1, 1 } } }, new() { Id = "farmer", Kind = "Farmer", Start = new() { Tile = new[] { 2, 1 } } } },
        Beats = beats.ToList()
    };

    [Fact]
    public void InstalledContentPasses()
    {
        var pack = Pack(new LiveLayer { Source = new() { Kind = "Npc", Npc = "Claire" } }, new LiveLayer { Source = new() { Kind = "NpcSheet", Asset = "Characters/Claire_Beach" } });
        var scene = Scene(new PoseBeat { Actor = "Robin", Pose = "p" }, new ItemBeat { Item = "(O)709" }, new FriendshipBeat { Npc = "JadeMalic", Points = 250 },
            new CostumeBeat { Actor = "farmer", Set = new() { ["Hat"] = "none", ["Shirt"] = "1010", ["Shoes"] = "original", ["Hair"] = 2 } });
        Assert.Empty(WorldReferenceValidator.Validate(pack, scene, "cutscenes/s.json", Game()));
    }

    [Fact]
    public void MissingModContentIsReportedWhereItIsUsed()
    {
        var pack = Pack(new LiveLayer { Source = new() { Kind = "Npc", Npc = "Lance" } }, new LiveLayer { Source = new() { Kind = "Asset", Asset = "Mods/Absent/Props", Rect = new[] { 0, 0, 1, 1 } } });
        var scene = Scene(new PoseBeat { Actor = "Robin", Pose = "p" }, new ItemBeat { Item = "(O)FlashShifter.StardewValleyExpandedCP_Aegis" },
            new FriendshipBeat { Npc = "Lance", Points = 10 }, new CostumeBeat { Actor = "Robin", Sprite = new() { Asset = "Characters/Robin_SVE" } },
            new CostumeBeat { Actor = "farmer", Preset = "beach", Set = new() { ["Pants"] = "Mod.Shorts", ["Hair"] = 99 } });
        var issues = WorldReferenceValidator.Validate(pack, scene, "cutscenes/s.json", Game());
        Assert.All(issues, x => Assert.Equal("V19", x.Rule));
        Assert.Contains(issues, x => x.File == "library/poses/p.json" && x.JsonPath == "Frames[0].Layers[0].Source.Npc" && x.Message.Contains("'Lance'"));
        Assert.Contains(issues, x => x.File == "library/poses/p.json" && x.JsonPath == "Frames[0].Layers[1].Source.Asset");
        Assert.Contains(issues, x => x.JsonPath == "Beats[1].Item");
        Assert.Contains(issues, x => x.JsonPath == "Beats[3].Sprite.Asset");
        Assert.Contains(issues, x => x.JsonPath == "Beats[4].Set.Pants" && x.Message.Contains("(P)Mod.Shorts"));
        Assert.Contains(issues, x => x.JsonPath == "Beats[4].Set.Hair");
        Assert.Contains(issues, x => x.File == "library/costumes.json" && x.JsonPath == "Costumes[0].Set.Hat");
        Assert.Single(issues, x => x.Message.Contains("'Lance'")); // reported once, not per use
    }

    [Fact]
    public void NpcNamesMustMatchExactlyAndSuggestTheRealName()
    {
        var pack = Pack(new LiveLayer { Source = new() { Kind = "Portrait", Npc = "claire" } });
        var issue = Assert.Single(WorldReferenceValidator.Validate(pack, Scene(new PoseBeat { Actor = "Robin", Pose = "p" }), "cutscenes/s.json", Game()));
        Assert.Contains("use 'Claire'", issue.Message);
    }

    [Fact]
    public void TemporaryActorsAndUntrackedCatalogKindsAreNotFlagged()
    {
        var pack = Pack(new LiveLayer { Source = new() { Kind = "Npc", Npc = "Gull" } }, new LiveLayer { Source = new() { Kind = "NpcSheet", Asset = "Characters/Anything" } });
        var scene = Scene(new PoseBeat { Actor = "Robin", Pose = "p" }, new ItemBeat { Item = "(O)Mod.Anything" });
        scene.Actors.Add(new() { Id = "Gull", Kind = "Temporary", Sprite = new() { Asset = "Characters/Seagull" }, Start = new() { Tile = new[] { 3, 3 } } });
        var untracked = new Catalog { Npcs = { "Robin" }, Locations = { "ScienceHouse" } };
        Assert.Empty(WorldReferenceValidator.Validate(pack, scene, "cutscenes/s.json", untracked));
    }

    [Fact]
    public void ColourSlotsSampledFromAModNpcRequireThatNpc()
    {
        var pack = Pack(new PaintLayer { Image = "art/body.png", ColorSlots = new() { ["Skin"] = "eloise.Skin", ["Outline"] = "Farmer.Skin", ["Hair"] = "#112233" } });
        pack.Characters.Add(new("library/characters/eloise.json", new CharacterRig { Format = "1.0", Id = "eloise", Npc = "Eloise" }));
        var issue = Assert.Single(WorldReferenceValidator.Validate(pack, Scene(new PoseBeat { Actor = "Robin", Pose = "p" }), "cutscenes/s.json", Game()));
        Assert.Equal("Frames[0].Layers[0].ColorSlots.Skin", issue.JsonPath);
    }

    [Fact]
    public void RuntimeRevalidationSkipsOnlyScenesUsingMissingContent()
    {
        var pack = Pack(new LiveLayer { Source = new() { Kind = "Npc", Npc = "Lance" } });
        var plain = Registered(pack, Scene(new WaitBeat { Duration = 1 }), "plain");
        var usesLance = Registered(pack, Scene(new PoseBeat { Actor = "Robin", Pose = "p" }), "uses-lance");
        var result = PackReferenceValidation.Validate(new[] { plain, usesLance }, Game());
        Assert.Equal(new[] { plain }, result.Valid);
        var skipped = Assert.Single(result.Skipped);
        Assert.Equal("uses-lance", skipped.CutsceneId);
        Assert.Contains("Lance", skipped.Reason);
    }

    private static RegisteredCutscene Registered(LoadedPack pack, Cutscene source, string id)
    {
        source.Id = id;
        pack.Cutscenes.Add(new("cutscenes/" + id + ".json", source));
        return new("Player.Test", "Test", source, new("Player.Test." + id, source.Location!, "Player.Test." + id, "", new Dictionary<string, string>(), new Dictionary<string, BeatInfo>()), pack, _ => null);
    }
}
