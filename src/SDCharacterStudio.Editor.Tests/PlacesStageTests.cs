using System.IO;
using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core;
using SDCharacterStudio.Core.Bridge;
using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Export;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>
/// D-52: positions that count from the spouse's room, staged on the upgraded farmhouse the game export has (or saying
/// plainly that it hasn't). D-54: positions off the map, or starts and walks on tiles nobody can walk on, are pointed out.
/// </summary>
public sealed class PlacesStageTests
{
    /// <summary>
    /// The mini export plus a farmhouse as a new save has it (12×12, never upgraded) and, unless <paramref name="layouts"/>
    /// is false (an export made before they were exported), the two upgraded houses with their spouse's spots. The
    /// ScienceHouse gets a walkable map with one wall tile at 5,5.
    /// </summary>
    private static string Export(TempFolder temp, bool layouts = true)
    {
        string folder = Path.Combine(temp.Path, "Export", "places");
        PackExporter.CopyDirectory(TestEnvironment.MiniExport, folder);
        var catalog = JObject.Parse(File.ReadAllText(Path.Combine(folder, "catalog.json")));
        var locations = (JArray)catalog["Locations"]!;
        locations.Add(new JObject { ["Name"] = "FarmHouse", ["MapAsset"] = "Maps\\FarmHouse", ["Size"] = new JArray(12, 12), ["Level"] = 0 });
        var science = locations.OfType<JObject>().Single(l => (string?)l["Name"] == "ScienceHouse");
        science["Passable"] = "locations/ScienceHouse.passable.png";
        Directory.CreateDirectory(Path.Combine(folder, "locations"));
        var walkable = new RgbaImage(32, 25, Enumerable.Repeat(0xFFFFFFFFu, 32 * 25).ToArray());
        walkable.Pixels[5 * 32 + 5] = 0xFF000000;
        File.WriteAllBytes(Path.Combine(folder, "locations", "ScienceHouse.passable.png"), PngEncoder.Encode(walkable));
        if (layouts)
            catalog["HouseLayouts"] = new JArray(
                new JObject { ["Name"] = "FarmHouse1", ["MapAsset"] = "Maps/FarmHouse1_marriage", ["Of"] = "FarmHouse", ["Level"] = 1, ["Size"] = new JArray(40, 16), ["SpouseRoom"] = "", ["Places"] = new JObject { ["SpouseRoom"] = new JArray(32, 5) } },
                new JObject { ["Name"] = "FarmHouse2", ["MapAsset"] = "Maps/FarmHouse2_marriage", ["Of"] = "FarmHouse", ["Level"] = 2, ["Size"] = new JArray(64, 33), ["SpouseRoom"] = "Robin", ["Places"] = new JObject { ["SpouseRoom"] = new JArray(53, 24) } });
        File.WriteAllText(Path.Combine(folder, "catalog.json"), catalog.ToString());
        return folder;
    }

    private static void SelectActor(EditorHarness editor, int index) => editor.Shell.Select(new ActorSelection(editor.Shell.Scene!.Id!, index));

    [Fact]
    public void ASpouseRoomSceneIsStagedInTheUpgradedHouseAndEditedFromTheRoom()
    {
        using var temp = new TempFolder();
        using var editor = new EditorHarness(Export(temp));
        editor.NewProject("Home", "Player.Home", "FarmHouse", "Greeting");
        var scene = editor.Shell.Scene!;
        var stage = editor.Shell.Stage;

        // The player's start: "Relative to" is offered in the farmhouse. The export's own house (never upgraded) has no
        // spouse's room, so the player goes right on its spot, and the stage moves to the first upgraded house.
        SelectActor(editor, 0);
        editor.Choose("Relative to", Places.SpouseRoom);
        Assert.Equal(Places.SpouseRoom, scene.Actors[0].Start!.From);
        Assert.Equal(new[] { 0, 0 }, scene.Actors[0].Start!.Tile);
        stage.Rebuild();
        Assert.Equal("FarmHouse1", stage.Staged.Layout?.Name);
        Assert.Equal("FarmHouse (upgraded once)", stage.MapName);
        Assert.Equal((32, 5), (stage.Actors[0].TileX, stage.Actors[0].TileY));
        Assert.Null(stage.PlaceNotice);
        Assert.True(stage.ShowHousePicker);
        Assert.Equal(new[] { "FarmHouse", "FarmHouse1", "FarmHouse2" }, stage.HouseOptions.Select(o => o.Value));
        Assert.Equal("with Robin's room", stage.HouseOptions[2].Detail);
        Assert.Contains("Tiles from the spouse's room", editor.Field<TileField>("Starts at").Help);

        // Someone dropped on the stage counts from the room too: one tile right and down of the spot.
        stage.DropCharacter("Robin", 33, 6);
        var robin = scene.Actors[1].Start!;
        Assert.Equal((Places.SpouseRoom, 1, 1), (robin.From, robin.Tile![0], robin.Tile[1]));

        // Another house: the room is elsewhere, and so is everyone in it. Their positions don't change.
        stage.SelectedHouse = stage.HouseOptions.Single(o => o.Value == "FarmHouse2");
        Assert.Equal("FarmHouse2", editor.Shell.Document!.Project.StageLayout(scene.Id));
        Assert.Equal((54, 25), (stage.Actors[1].TileX, stage.Actors[1].TileY));
        Assert.Equal(new[] { 1, 1 }, scene.Actors[1].Start!.Tile);

        // Dragging and "walks here" write tiles from the room.
        stage.MoveActor(1, 55, 24, 0, 0);
        Assert.Equal(new[] { 2, 0 }, scene.Actors[1].Start!.Tile);
        Assert.Equal(Places.SpouseRoom, scene.Actors[1].Start!.From);
        stage.WalkHere(1, 53, 26);
        var walk = scene.Beats.OfType<MoveBeat>().Single();
        Assert.Equal((Places.SpouseRoom, 0, 2), (walk.To!.From, walk.To.Tile![0], walk.To.Tile[1]));
        Assert.Contains("(0, 2) from the spouse's room", Schema.BeatCatalog.Summary(walk));
        stage.PlaceItem("(F)1130", 52, 24);
        var prop = scene.Beats.OfType<PropBeat>().Single();
        Assert.Equal((Places.SpouseRoom, -1, 0), (prop.At!.From, prop.At.Tile![0], prop.At.Tile[1]));

        // Back to the map: the spot stays where the stage shows it.
        SelectActor(editor, 1);
        editor.Choose("Relative to", null);
        Assert.Null(scene.Actors[1].Start!.From);
        Assert.Equal(new[] { 55, 24 }, scene.Actors[1].Start!.Tile);
    }

    [Fact]
    public void AnExportWithoutTheUpgradedHouseSaysSoPlainly()
    {
        using var temp = new TempFolder();
        using var editor = new EditorHarness(Export(temp, layouts: false));
        editor.NewProject("Home", "Player.Home", "FarmHouse", "Greeting");
        var scene = editor.Shell.Scene!;
        editor.Shell.Edit("room", () => scene.Actors[0].Start = new Position { From = Places.SpouseRoom, Tile = new[] { 0, 1 } });
        var stage = editor.Shell.Stage;
        stage.Rebuild();
        Assert.False(stage.ShowHousePicker);
        Assert.Null(stage.Staged.Layout);
        Assert.Contains("Ask the game to export again", stage.PlaceNotice);
        // Meanwhile the room stands at its usual spot, as the compiled event starts it.
        Assert.Equal((32, 6), (stage.Actors[0].TileX, stage.Actors[0].TileY));
    }

    [Fact]
    public void RelativeToIsOfferedInTheFarmhouseOnly()
    {
        using var editor = new EditorHarness();
        editor.NewProject("Town Pack", "Player.TownPack", "Town");
        SelectActor(editor, 0);
        Assert.DoesNotContain(editor.Shell.Inspector.Fields, f => f.Label == "Relative to");
    }

    [Fact]
    public void PositionsOffTheMapOrOnAWallAreWarnings()
    {
        using var temp = new TempFolder();
        string export = Export(temp);
        using var editor = new EditorHarness(export);
        var doc = editor.NewProject("Lab", "Player.Lab", "ScienceHouse");
        var scene = doc.Cutscenes[0].Scene;
        doc.Edit("stage", () =>
        {
            scene.Actors[0].Start = new Position { Tile = new[] { 53, 24 } };
            scene.Beats.Insert(0, new MoveBeat { Actor = "farmer", To = new Position { Tile = new[] { 5, 5 } } });
        });
        var problems = ValidationService.Validate(ValidationInput.From(doc, editor.Shell.Catalog)).Problems;
        var off = Assert.Single(problems, p => p.Rule == "V21" && p.JsonPath == "Actors[0].Start.Tile");
        Assert.False(off.IsError);
        Assert.Equal("The player starts at 53,24, which is off the ScienceHouse map (32×25).", off.Message);
        Assert.Contains(problems, p => p.Rule == "V22" && p.JsonPath == "Beats[0].To.Tile" && p.Message.StartsWith("The player walks to 5,5, a tile nobody can walk on"));

        // The farmhouse: its export is one save's house, so only a chosen upgraded house is checked.
        var house = editor.NewProject("Home", "Player.Home", "FarmHouse");
        var home = house.Cutscenes[0].Scene;
        house.Edit("stage", () => home.Actors[0].Start = new Position { Tile = new[] { 53, 24 } });
        Assert.DoesNotContain(ValidationService.Validate(ValidationInput.From(house, editor.Shell.Catalog)).Problems, p => p.Rule == "V21");
        house.Project.SetStageLayout(home.Id!, "FarmHouse1");
        Assert.Contains(ValidationService.Validate(ValidationInput.From(house, editor.Shell.Catalog)).Problems,
            p => p.Rule == "V21" && p.Message == "The player starts at 53,24, which is off the FarmHouse (upgraded once) map (40×16).");
        house.Project.SetStageLayout(home.Id!, "FarmHouse2");
        Assert.DoesNotContain(ValidationService.Validate(ValidationInput.From(house, editor.Shell.Catalog)).Problems, p => p.Rule == "V21");
        // A position from the spouse's room is found in the game: never checked against a map.
        house.Project.SetStageLayout(home.Id!, "FarmHouse1");
        house.Edit("room", () => home.Actors[0].Start = new Position { From = Places.SpouseRoom, Tile = new[] { 40, 40 } });
        Assert.DoesNotContain(ValidationService.Validate(ValidationInput.From(house, editor.Shell.Catalog)).Problems, p => p.Rule is "V21" or "V22");
    }
}
