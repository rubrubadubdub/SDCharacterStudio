using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Schema;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>D-49: the stage shows what each actor wears at the selected beat, and costumes say whether they last.</summary>
public sealed class StageCostumeTests
{
    private static Cutscene Scene() => new()
    {
        Id = "earmuffs",
        Actors =
        {
            new Actor { Id = "farmer", Kind = "Farmer", Start = new Position { Tile = new[] { 5, 5 } } },
            new Actor { Id = "Robin", Kind = "Npc", Npc = "Robin", Start = new Position { Tile = new[] { 6, 5 } } },
        },
        Beats =
        {
            new FaceBeat { Actor = "farmer", Facing = "down" },
            new CostumeBeat { Actor = "farmer", Set = new() { ["Hat"] = "(H)11" } },
            new CostumeBeat { Actor = "Robin", Sprite = new SpriteReference { Asset = "Characters/Robin_Summer" } },
            new CostumeBeat { Actor = "farmer", Set = new() { ["Shirt"] = "(S)1001" } },
            new CostumeBeat { Actor = "farmer", Restore = true },
        },
    };

    [Fact]
    public void WhatTheyWearFollowsTheSelectedBeat()
    {
        var scene = Scene();
        StageLook At(int index, int actor = 0) => StageLooks.For(scene, scene.Actors[actor], null, new BeatSelection(scene.Id!, LaneNames.Main, index));
        Assert.Empty(At(0).Outfit);
        Assert.Equal("(H)11", At(1).Outfit["Hat"]);   // the earmuffs from their beat on
        Assert.Null(At(1, actor: 1).SpriteAsset);
        Assert.Equal("Characters/Robin_Summer", At(2, actor: 1).SpriteAsset);
        Assert.Equal(new[] { "Hat", "Shirt" }, At(3).Outfit.Keys.OrderBy(k => k));
        Assert.Empty(At(4).Outfit);   // back to their own look
        Assert.NotEqual(At(1).OutfitKey, At(3).OutfitKey);
        Assert.Empty(StageLooks.For(scene, scene.Actors[0], null, (Selection?)null).Outfit);   // nothing selected: the start

        scene.Actors[0].Costume = new JObject { ["Hat"] = "(H)0" };   // a starting costume
        Assert.Equal("(H)0", StageLooks.For(scene, scene.Actors[0], null, (Selection?)null).Outfit["Hat"]);
        Assert.Equal("(H)11", At(1).Outfit["Hat"]);
    }

    [Fact]
    public void ACostumeBeatSaysWhetherTheClothesStayAfterTheScene()
    {
        using var editor = new EditorHarness();
        var doc = editor.NewProject();
        var shell = editor.Shell;
        shell.Stage.DropCharacter("farmer", 5, 5);
        var scene = shell.Scene!;
        doc.Edit("costume", () => scene.Beats.Add(new CostumeBeat { Actor = "farmer", Set = new() { ["Hat"] = "(H)0" } }));
        shell.Select(new BeatSelection(scene.Id!, LaneNames.Main, scene.Beats.Count - 1));
        var keep = editor.Field<BoolField>("Keep these clothes after the scene ends");
        Assert.False(keep.Value);
        Assert.True(scene.OnEnd.RestoreCostume);
        keep.Value = true;
        Assert.False(scene.OnEnd.RestoreCostume);
        Assert.Contains("Cowboy Hat", BeatCatalog.Summary(scene.Beats[^1], shell.Catalog));

        var renders = CostumeRenders.Load();
        Assert.Null(renders.Find(shell.Catalog!, "Hat=(H)0"));   // nothing drawn by the game yet
    }
}
