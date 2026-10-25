using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Loading;
using SDCharacterStudio.Core.Tests.TestSupport;
using SDCharacterStudio.Core.Validation;
using Xunit;

namespace SDCharacterStudio.Core.Tests.Data;

/// <summary>Outfits (sheet poses) that poses and other outfits wear with a live source of Kind "Outfit".</summary>
public sealed class OutfitTests
{
    private static string Sheet(string id, string source) =>
        $$$"""{"Format":"1.0","Id":"{{{id}}}","Kind":"sheet","FrameSize":[16,32],"SheetSize":[64,128],"Frames":[{"Duration":100,"Layers":[{"Type":"Live","Source":{{{source}}},"SourceFrame":{"Index":0}}]}]}""";

    private static string Wearing(string id, string outfit) =>
        $$$"""{"Format":"1.0","Id":"{{{id}}}","FrameSize":[16,32],"Frames":[{"Duration":100,"Layers":[{"Type":"Live","Source":{"Kind":"Outfit","Npc":"Abigail","Outfit":"{{{outfit}}}"},"SourceFrame":{"Index":0}}]}]}""";

    private static PackReadResult Read(params (string Path, string Json)[] poses)
    {
        var files = MemoryPackFileSystem.Minimal();
        foreach (var (path, json) in poses) files.Files["library/poses/" + path] = json;
        files.Files["cutscenes/scene.json"] = """{"Format":"1.0","Id":"scene","Location":"Town","Actors":[{"Id":"Abigail","Kind":"Npc","Start":{"Tile":[8,5]}}],"Beats":[{"Type":"Pose","Actor":"Abigail","Pose":"wave"}]}""";
        return new PackReader(files).Read(exportValidation: false);
    }

    [Fact]
    public void APoseWearsAnOutfitByName()
    {
        var read = Read(("raincoat.json", Sheet("raincoat", """{"Kind":"Npc","Npc":"Abigail"}""")), ("wave.json", Wearing("wave", "raincoat")));
        Assert.DoesNotContain(read.Issues, i => i.Severity == ValidationSeverity.Error);
        var live = Assert.IsType<Models.LiveLayer>(read.Pack.Poses.Single(p => p.Value.Id == "wave").Value.Frames[0].Layers[0]);
        Assert.Equal("raincoat", live.Source!.Outfit);
        Assert.Equal(new[] { "raincoat" }, Outfits.Worn(read.Pack.Poses.Single(p => p.Value.Id == "wave").Value));
    }

    [Fact]
    public void OnlyAnOutfitCanBeWornAndItMustExist()
    {
        var notOutfit = Read(("wave.json", Wearing("wave", "bow")), ("bow.json", """{"Format":"1.0","Id":"bow","FrameSize":[16,32],"Frames":[{"Duration":100,"Layers":[]}]}"""));
        Assert.Contains(notOutfit.Issues, i => i.Severity == ValidationSeverity.Error && i.Message.Contains("'bow' is a pose, not an outfit"));
        var missing = Read(("wave.json", Wearing("wave", "gone")));
        Assert.Contains(missing.Issues, i => i.Severity == ValidationSeverity.Error && i.Message == "Unknown outfit 'gone'.");
        var unnamed = Read(("wave.json", Wearing("wave", "x").Replace(",\"Outfit\":\"x\"", "")));
        Assert.Contains(unnamed.Issues, i => i.Severity == ValidationSeverity.Error && i.JsonPath.EndsWith("Source.Outfit"));
    }

    [Fact]
    public void AnOutfitMayBeMadeFromAnotherButNeverFromItself()
    {
        var chain = Read(("raincoat.json", Sheet("raincoat", """{"Kind":"Npc","Npc":"Abigail"}""")),
            ("hooded.json", Sheet("hooded", """{"Kind":"Outfit","Npc":"Abigail","Outfit":"raincoat"}""")), ("wave.json", Wearing("wave", "hooded")));
        Assert.DoesNotContain(chain.Issues, i => i.Severity == ValidationSeverity.Error);
        Assert.Null(Outfits.Cycle(chain.Pack, "hooded"));

        var loop = Read(("a.json", Sheet("a", """{"Kind":"Outfit","Npc":"Abigail","Outfit":"b"}""")),
            ("b.json", Sheet("b", """{"Kind":"Outfit","Npc":"Abigail","Outfit":"a"}""")), ("wave.json", Wearing("wave", "a")));
        Assert.Equal(new[] { "a", "b", "a" }, Outfits.Cycle(loop.Pack, "a"));
        Assert.Contains(loop.Issues, i => i.Severity == ValidationSeverity.Error && i.Message.StartsWith("Outfit 'a' is made from itself: a → b → a."));
        Assert.Contains(loop.Issues, i => i.Severity == ValidationSeverity.Error && i.Message.StartsWith("Outfit 'b' is made from itself"));
        var self = Read(("a.json", Sheet("a", """{"Kind":"Outfit","Npc":"Abigail","Outfit":"a"}""")), ("wave.json", Wearing("wave", "a")));
        Assert.Contains(self.Issues, i => i.Message.StartsWith("Outfit 'a' is made from itself: a → a."));
    }

    [Fact]
    public void AnOutfitCoversHerWholeSheetWithHerOwnFramesWhereItHasNone()
    {
        var read = Read(("raincoat.json", Sheet("raincoat", """{"Kind":"NpcSheet","Npc":"Abigail","Asset":"Characters/Abigail_Winter"}""")));
        var outfit = read.Pack.Poses.Single().Value;
        Assert.Same(outfit, Outfits.CoverSheet(outfit, 16, 32));                     // already covers a one-frame sheet
        var covered = Outfits.CoverSheet(outfit, 64, 320);                            // her sheet in the game: 40 frames
        Assert.Equal(40, covered.Frames.Count);
        Assert.Equal(new[] { 64, 320 }, covered.SheetSize);
        var added = Assert.IsType<Models.LiveLayer>(Assert.Single(covered.Frames[39].Layers));
        Assert.Equal(("NpcSheet", "Characters/Abigail_Winter", 39), (added.Source!.Kind, added.Source.Asset, added.SourceFrame!.Index));
        Assert.Single(outfit.Frames);                                                 // the outfit itself is untouched
    }

    [Fact]
    public void APortraitCantWearAnOutfit()
    {
        var files = MemoryPackFileSystem.Minimal();
        files.Files["library/poses/raincoat.json"] = Sheet("raincoat", """{"Kind":"Npc","Npc":"Abigail"}""");
        files.Files["library/portraits/smile.json"] = """{"Format":"1.0","Id":"smile","FrameSize":[64,64],"Emotions":{"neutral":{"Layers":[{"Type":"Live","Source":{"Kind":"Outfit","Npc":"Abigail","Outfit":"raincoat"}}]}}}""";
        var read = new PackReader(files).Read(exportValidation: false);
        Assert.Contains(read.Issues, i => i.Severity == ValidationSeverity.Error && i.Message.Contains("a portrait can't wear one"));
    }

    [Fact]
    public void WhatAnOutfitIsMadeFromIsWhatThePackNeeds()
    {
        var read = Read(("raincoat.json", Sheet("raincoat", """{"Kind":"NpcSheet","Npc":"Abigail","Asset":"Characters/Abigail_Mod"}""")), ("wave.json", Wearing("wave", "raincoat")));
        var found = OutsideReferences.Collect(read.Pack, read.Pack.Cutscenes.Single().Value, "cutscenes/scene.json", includeActorsAndLocation: false);
        Assert.Contains(found, r => r.Kind == OutsideReferenceKind.Npc && r.Value == "Abigail" && r.Usage == "outfit in pose wave");
        Assert.Contains(found, r => r.Kind == OutsideReferenceKind.Asset && r.Value == "Characters/Abigail_Mod" && r.Usage == "live source in pose raincoat");
    }
}
