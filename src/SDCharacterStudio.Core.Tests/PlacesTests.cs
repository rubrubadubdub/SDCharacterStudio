using SDCharacterStudio.Core.Compiler;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Serialization;
using SDCharacterStudio.Core.Validation;
using SDCharacterStudio.Runtime.Animation;
using SDCharacterStudio.Runtime.Playback;
using Xunit;

namespace SDCharacterStudio.Core.Tests;

/// <summary>
/// D-52: a position can count from a place the game finds when the scene starts (the spouse's room moves with the house
/// upgrade, map mods and the spouse). The compiled event starts everyone at the place's fallback spot; the engine moves
/// them, the camera, walks, items and effects to the real spot.
/// </summary>
public sealed class PlacesTests
{
    private const string Pack = "Author.Test";

    /// <summary>A spouse greets the player in their room: everything counts from the spouse's spot.</summary>
    internal static Cutscene SpouseScene(string location = "FarmHouse", bool trigger = true) => PackJson.Deserialize<Cutscene>($$"""
    {
      "Format": "1.0", "Id": "greeting", "Location": "{{location}}",
      {{(trigger ? "\"Trigger\": { \"Conditions\": \"SEASON spring\" }," : "")}}
      "Actors": [
        { "Id": "farmer", "Kind": "Farmer", "Start": { "From": "SpouseRoom", "Tile": [0, 2] }, "Facing": "up" },
        { "Id": "Abigail", "Kind": "Npc", "Npc": "Abigail", "Start": { "From": "SpouseRoom", "Tile": [0, 0] }, "Facing": "down" }
      ],
      "Beats": [
        { "Type": "Move", "Actor": "Abigail", "To": { "From": "SpouseRoom", "Tile": [1, 1] } },
        { "Type": "Camera", "Center": { "From": "SpouseRoom", "Tile": [0, 1] } },
        { "Type": "Prop", "Item": "(F)1130", "At": { "From": "SpouseRoom", "Tile": [-1, 0] } },
        { "Type": "Say", "Actor": "Abigail", "Text": "Welcome home." }
      ]
    }
    """)!;

    [Fact]
    public void ThePositionsJsonNamesThePlaceAndLeavesItOutOtherwise()
    {
        var scene = SpouseScene();
        Assert.Equal(Places.SpouseRoom, scene.Actors[1].Start!.From);
        Assert.Equal(new[] { 0, 0 }, scene.Actors[1].Start!.Tile);
        // A map position is written as before: no "From": null in every scene file.
        Assert.DoesNotContain("From", PackJson.Serialize(new Position { Tile = new[] { 3, 4 } }));
        Assert.Contains("\"From\": \"SpouseRoom\"", PackJson.Serialize(scene.Actors[1].Start!));
    }

    [Fact]
    public void TheEventStartsAtTheFallbackSpotAndWaitsForTheRoom()
    {
        var result = new EventCompiler().Compile(Pack, SpouseScene());
        Assert.True(result.Success, string.Join("\n", result.Issues.Select(i => i.JsonPath + ": " + i.Message)));
        var compiled = result.Cutscene!;
        var (x, y) = Places.Fallback(Places.SpouseRoom);
        Assert.Equal((32, 5), (x, y));
        // Header: the camera on the first actor (the player), everyone at the fallback spot plus their offset.
        Assert.StartsWith($"none/32 7/farmer 32 7 0 Abigail 32 5 2/SDCS_Begin {Pack}.greeting/", compiled.MainScript);
        // A cut to a spot in the room goes through the engine (vanilla 'viewport' only knows map tiles).
        Assert.Contains($"SDCS_Camera {Pack}.greeting main.1", compiled.MainScript);
        // The trigger waits until the player has a spouse's room: the same check the engine places the scene with.
        Assert.Equal($"{Pack}.greeting/G SEASON spring/G SDCS_HAS_PLACE SpouseRoom", compiled.MainKey);
    }

    [Fact]
    public void AManualSceneHasNoTriggerToGuard()
    {
        var result = new EventCompiler().Compile(Pack, SpouseScene(trigger: false));
        Assert.True(result.Success);
        Assert.Equal($"{Pack}.greeting", result.Cutscene!.MainKey);
    }

    [Fact]
    public void AnUnknownPlaceIsAnErrorAndTheRoomOutsideTheHouseIsAWarning()
    {
        var scene = SpouseScene();
        scene.Actors[1].Start!.From = "Attic";
        var issues = PackValidator.ValidateCutscene(scene, "cutscenes/greeting.json");
        var unknown = Assert.Single(issues, i => i.Rule == "V20");
        Assert.Equal(ValidationSeverity.Error, unknown.Severity);
        Assert.Equal("Actors[1].Start.From", unknown.JsonPath);
        Assert.Contains("SpouseRoom", unknown.Message);
        Assert.False(new EventCompiler().Compile(Pack, scene).Success);

        var town = PackValidator.ValidateCutscene(SpouseScene("Town"), "cutscenes/greeting.json");
        var warning = Assert.Single(town, i => i.Rule == "V20");
        Assert.Equal(ValidationSeverity.Warning, warning.Severity);
        Assert.Equal("The spouse's room is in the FarmHouse, but this scene plays in Town: positions there won't line up.", warning.Message);
        Assert.DoesNotContain(PackValidator.ValidateCutscene(SpouseScene(), "cutscenes/greeting.json"), i => i.Rule == "V20");
    }

    [Fact]
    public void APlaceTurnsOffsetsIntoMapTilesAndBack()
    {
        var anchored = new Position { From = Places.SpouseRoom, Tile = new[] { 1, -1 }, Px = new[] { 2, 0 } };
        var absolute = Places.Absolute(anchored, (53, 24));
        Assert.Equal(new[] { 54, 23 }, absolute.Tile);
        Assert.Equal(new[] { 2, 0 }, absolute.Px);
        Assert.Null(absolute.From);
        Assert.Equal(new[] { 1, -1 }, Places.Offset(54, 23, (53, 24)));
        var map = new Position { Tile = new[] { 5, 6 } };
        Assert.Same(map, Places.Absolute(map, (53, 24)));
        Assert.Equal(new[] { 33, 4 }, Places.FallbackTile(anchored));
        Assert.Equal(new[] { Places.SpouseRoom }, Places.Used(SpouseScene()));
        Assert.Empty(Places.Used(CompilerTestsScene()));
        // The game's own corners: once upgraded (29, 1), twice (50, 20); the spot is 3 right and 4 down.
        Assert.Equal((32, 5), Places.SpouseRoomSpot(Places.SpouseRoomCorner(1)));
        Assert.Equal((53, 24), Places.SpouseRoomSpot(Places.SpouseRoomCorner(2)));
    }

    [Fact]
    public void AWalkToTheRoomGoesWhereTheRoomIs()
    {
        var walk = SpouseScene().Beats.OfType<MoveBeat>().Single();
        // The engine resolves the target (the room at 53,24 in a twice-upgraded house) and walks there from 50,20.
        var to = Places.Absolute(walk.To!, (53, 24)).Tile;
        Assert.Equal(new[] { "speed Abigail 2", "move Abigail 4 0 1", "move Abigail 0 5 2" }, MovePlanner.Plan(walk, 50, 20, to));
        // Without a resolved target it's a map tile as always.
        Assert.Equal(new[] { "speed Abigail 2", "move Abigail -49 0 3", "move Abigail 0 -19 0" }, MovePlanner.Plan(walk, 50, 20));
        // A camera centre or effect at a place lands on the resolved tile.
        var centre = AnimationMath.Position(Places.Absolute(new Position { From = Places.SpouseRoom, Tile = new[] { 0, 1 } }, (53, 24)));
        Assert.Equal(new WorldPoint(53 * 64, 25 * 64), centre);
    }

    private static Cutscene CompilerTestsScene() => Compiler.CompilerTests.Scene("[]");
}
