using SDCharacterStudio.Core.Compiler;
using SDCharacterStudio.Core.Loading;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Serialization;
using SDCharacterStudio.Core.Tests.TestSupport;
using SDCharacterStudio.Core.Validation;
using Xunit;

namespace SDCharacterStudio.Core.Tests.Data;

public sealed class SemanticBoundaryTests
{
    [Theory]
    [InlineData("\"Offset\":null", "Frames[0].Layers[0].Offset")]
    [InlineData("\"Offset\":[0]", "Frames[0].Layers[0].Offset")]
    [InlineData("\"ColorSlots\":{\"Skin\":\"#112233ff\"}", "Frames[0].Layers[0].ColorSlots.Skin")]
    public void ReaderRejectsLayerValuesThatCannotBeComposited(string property,string expectedPath)
    {
        var fs=MemoryPackFileSystem.Minimal();
        fs.Files["library/poses/p.json"]="{\"Format\":\"1.0\",\"Id\":\"p\",\"FrameSize\":[1,1],\"Frames\":[{\"Duration\":1,\"Layers\":[{\"Type\":\"Live\",\"Source\":{\"Kind\":\"Farmer\"},"+property+"}]}]}";
        Assert.Contains(new PackReader(fs).Read().Issues,i=>i.File=="library/poses/p.json"&&i.JsonPath==expectedPath&&i.Severity==ValidationSeverity.Error);
    }

    [Fact]
    public void ReaderChecksAnchorsAndSucceedsOnceAnchorExists()
    {
        var fs=MemoryPackFileSystem.Minimal();
        var prefix="{\"Format\":\"1.0\",\"Id\":\"p\",\"FrameSize\":[1,1],\"Frames\":[{\"Duration\":1,\"Anchors\":";
        var suffix=",\"Layers\":[{\"Type\":\"Live\",\"Source\":{\"Kind\":\"Farmer\"},\"AnchorTo\":{\"Anchor\":\"Neck\",\"From\":[0,0]}}]}]}";
        fs.Files["library/poses/p.json"]=prefix+"null"+suffix;
        var issues=new PackReader(fs).Read().Issues;
        Assert.Contains(issues,i=>i.JsonPath=="Frames[0].Anchors");
        Assert.Contains(issues,i=>i.JsonPath=="Frames[0].Layers[0].AnchorTo.Anchor");
        fs.Files["library/poses/p.json"]=prefix+"{\"Neck\":[0,0]}"+suffix;
        Assert.Empty(new PackReader(fs).Read().Issues);
    }

    [Theory]
    [InlineData("library/characters/r.json","{\"Format\":\"1.0\",\"Id\":\"r\",\"Npc\":\"Robin\",\"HeadMask\":{\"left\":\"not-there\"},\"Portrait\":{\"HeadMask\":\"also-not-there\"}}","HeadMask.left")]
    [InlineData("library/characters/r.json","{\"Format\":\"1.0\",\"Id\":\"r\",\"Npc\":\"Robin\",\"Portrait\":{\"HeadMask\":\"also-not-there\"}}","Portrait.HeadMask")]
    [InlineData("cutscenes/s.json","{\"Format\":\"1.0\",\"Id\":\"s\",\"Location\":\"X\",\"Actors\":[{\"Id\":\"farmer\",\"Kind\":\"Farmer\",\"Start\":{\"Tile\":[0,0]},\"Costume\":\"not-there\"}],\"Beats\":[]}","Actors[0].Costume")]
    [InlineData("library/poses/p.json","{\"Format\":\"1.0\",\"Id\":\"p\",\"For\":\"not-there\",\"FrameSize\":[1,1],\"Frames\":[{\"Duration\":1,\"Layers\":[]}]}","For")]
    [InlineData("library/expressions.json","{\"Format\":\"1.0\",\"Expressions\":[{\"Id\":\"x\",\"For\":\"not-there\",\"Frames\":[]}]}","Expressions[0].For")]
    [InlineData("library/portraits/p.json","{\"Format\":\"1.0\",\"Id\":\"p\",\"For\":\"not-there\",\"Emotions\":{}}","For")]
    public void NestedContentReferencesAreResolvedWithTheirActualPaths(string file,string json,string expectedPath)
    {
        var fs=MemoryPackFileSystem.Minimal();fs.Files[file]=json;
        Assert.Contains(new PackReader(fs).Read().Issues,i=>i.File==file&&i.JsonPath==expectedPath&&i.Rule=="Reference");
    }

    [Theory]
    [InlineData("{\"Type\":\"Move\",\"To\":{\"Tile\":[1,1]}}")]
    [InlineData("{\"Type\":\"Pose\",\"Pose\":\"bow\"}")]
    [InlineData("{\"Type\":\"Costume\",\"Restore\":true}")]
    [InlineData("{\"Type\":\"Say\",\"Text\":\"Hi\"}")]
    [InlineData("{\"Type\":\"Face\",\"Facing\":\"left\"}")]
    [InlineData("{\"Type\":\"Emote\",\"Emote\":\"heart\"}")]
    [InlineData("{\"Type\":\"Shake\",\"Duration\":600}")]
    [InlineData("{\"Type\":\"Jump\"}")]
    public void ActorDependentBeatsRequireAnActorBeforeCompilerCanSucceed(string beat)
    {
        var scene=PackJson.Deserialize<Cutscene>("{\"Format\":\"1.0\",\"Id\":\"s\",\"Location\":\"X\",\"Beats\":["+beat+"]}")!;
        Assert.Contains(PackValidator.ValidateCutscene(scene,"s.json"),i=>i.JsonPath=="Beats[0].Actor"&&i.Severity==ValidationSeverity.Error);
        Assert.False(new EventCompiler().Compile("Author.Pack",scene).Success);
    }

    [Fact]
    public void ActorReferencesResolveAgainstDeclaredActorsIgnoringCase()
    {
        var scene=ValidationTests.Scene();scene.Beats=new(){new MoveBeat{Actor="robin",To=new(){Tile=new[]{1,1}}}};
        Assert.Empty(PackValidator.ValidateCutscene(scene,"s.json"));
        ((MoveBeat)scene.Beats[0]).Actor="Leah";
        Assert.Contains(PackValidator.ValidateCutscene(scene,"s.json"),i=>i.JsonPath=="Beats[0].Actor"&&i.Rule=="Reference");
    }

    [Fact]
    public void ReaderAcceptsResolvedRigMaskAndActorCostumeReferences()
    {
        var fs=MemoryPackFileSystem.Minimal();
        fs.Images["masks/head.png"]=new(1,1);
        fs.Files["library/masks.json"]="""{"Format":"1.0","Masks":[{"Id":"head","FrameSize":[1,1],"Image":"masks/head.png"}]}""";
        fs.Files["library/characters/r.json"]="""{"Format":"1.0","Id":"r","Npc":"Robin","HeadMask":{"left":"head"},"Portrait":{"HeadMask":"head"}}""";
        fs.Files["library/poses/p.json"]="""{"Format":"1.0","Id":"p","For":"r","FrameSize":[1,1],"Frames":[{"Duration":1,"Layers":[{"Type":"Live","Source":{"Kind":"Farmer"},"ColorSlots":{"Skin":"#112233"}}]}]}""";
        fs.Files["library/costumes.json"]="""{"Format":"1.0","Costumes":[{"Id":"beach-day","Set":{"Hat":"original"}}]}""";
        fs.Files["cutscenes/s.json"]="""{"Format":"1.0","Id":"s","Location":"X","Actors":[{"Id":"farmer","Kind":"Farmer","Start":{"Tile":[0,0]},"Costume":"beach-day"}],"Beats":[{"Type":"Move","Actor":"farmer","To":{"Tile":[1,1]}}]}""";
        Assert.Empty(new PackReader(fs).Read().Issues);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("null")]
    public void MalformedBeatDiscriminatorsProduceLocatedIssuesInReaderAndDirectValidator(string discriminator)
    {
        var json="{\"Format\":\"1.0\",\"Id\":\"s\",\"Location\":\"X\",\"Beats\":[{\"Type\":"+discriminator+"}]}";
        var fs=MemoryPackFileSystem.Minimal();fs.Files["cutscenes/s.json"]=json;
        Assert.Contains(new PackReader(fs).Read().Issues,i=>i.File=="cutscenes/s.json"&&i.JsonPath=="Beats[0].Type");
        var scene=PackJson.Deserialize<Cutscene>(json)!;
        Assert.Contains(PackValidator.ValidateCutscene(scene,"direct.json"),i=>i.File=="direct.json"&&i.JsonPath=="Beats[0].Type");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("null")]
    public void MalformedLayerDiscriminatorsProduceLocatedIssuesInReaderAndDirectValidator(string discriminator)
    {
        var json="{\"Format\":\"1.0\",\"Id\":\"p\",\"FrameSize\":[1,1],\"Frames\":[{\"Duration\":1,\"Layers\":[{\"Type\":"+discriminator+"}]}]}";
        var fs=MemoryPackFileSystem.Minimal();fs.Files["library/poses/p.json"]=json;
        Assert.Contains(new PackReader(fs).Read().Issues,i=>i.File=="library/poses/p.json"&&i.JsonPath=="Frames[0].Layers[0].Type");
        var pack=new LoadedPack();pack.Poses.Add(new("direct.json",PackJson.Deserialize<Pose>(json)!));
        Assert.Contains(PackValidator.ValidatePack(pack),i=>i.File=="direct.json"&&i.JsonPath=="Frames[0].Layers[0].Type");
    }

    [Fact]
    public void ColorSampleFallbackMustNameAnotherKnownRigSample()
    {
        var fs=MemoryPackFileSystem.Minimal();
        fs.Files["library/characters/r.json"]="""{"Format":"1.0","Id":"r","Npc":"Robin","ColorSamples":{"Pants":{"Mode":"auto-common","Fallback":"missing"},"Shirt":{"Mode":"auto-common"}}}""";
        Assert.Contains(new PackReader(fs).Read().Issues,i=>i.JsonPath=="ColorSamples.Pants.Fallback"&&i.Rule=="Reference");
        fs.Files["library/characters/r.json"]=fs.Files["library/characters/r.json"].Replace("\"missing\"","\"Shirt\"",StringComparison.Ordinal);
        Assert.Empty(new PackReader(fs).Read().Issues);
    }

    [Fact]
    public void FadeWaitRejectsStringBooleanBeforeCompilerSeesCoercedValue()
    {
        var fs=MemoryPackFileSystem.Minimal();
        fs.Files["cutscenes/s.json"]="""{"Format":"1.0","Id":"s","Location":"X","Beats":[{"Type":"Fade","To":"black","Duration":500,"Wait":"false"}]}""";
        Assert.Contains(new PackReader(fs).Read().Issues,i=>i.File=="cutscenes/s.json"&&i.JsonPath=="Beats[0].Wait"&&i.Rule=="Schema");
        fs.Files["cutscenes/s.json"]=fs.Files["cutscenes/s.json"].Replace("\"Wait\":\"false\"","\"Wait\":false",StringComparison.Ordinal);
        Assert.Empty(new PackReader(fs).Read().Issues);
    }
}
