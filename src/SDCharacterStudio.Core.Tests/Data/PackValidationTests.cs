using SDCharacterStudio.Core.Loading;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Tests.TestSupport;
using SDCharacterStudio.Core.Validation;
using Xunit;

namespace SDCharacterStudio.Core.Tests.Data;
public class PackValidationTests
{
    private static LoadedPack Pack() => new() { Manifest=new(){UniqueID="Author.Pack",ContentPackFor=new(){UniqueID="Player.SDCharacterStudio"}},Index=new(){Format="1.0"} };
    [Fact] public void DuplicateIdsAreDetectedAcrossFilesIgnoringCase()
    {
        var pack=Pack();pack.Cutscenes.Add(new("one.json",ValidationTests.Scene()));pack.Cutscenes.Add(new("two.json",ValidationTests.Scene()));
        Assert.Contains(PackValidator.ValidatePack(pack),i=>i.Rule=="V3"&&i.File=="two.json"&&i.JsonPath=="Id");
        pack.Cutscenes[1].Value.Id="other";Assert.DoesNotContain(PackValidator.ValidatePack(pack),i=>i.Rule=="V3");
    }
    [Fact] public void ImageFramesAndMasksCheckDimensionsIncludingPerFrameOverrides()
    {
        var fs=MemoryPackFileSystem.Minimal();fs.Images["art/body.png"]=new(32,32);fs.Images["masks/head.png"]=new(16,32);fs.Images["masks/bad.png"]=new(15,32);
        var pack=Pack();pack.Poses.Add(new("pose.json",new(){Format="1.0",Id="pose",FrameSize=new[]{16,32},Frames=new(){new(){Duration=100,Layers=new(){new ImageLayer {Image="art/body.png",FrameSize=new[]{16,32},Frame=1}}}}}));
        pack.Masks=new("masks.json",new(){Format="1.0",Masks=new(){new(){Id="head",Image="masks/head.png",FrameSize=new[]{16,32}}}});
        Assert.Empty(PackValidator.ValidatePack(pack,fs));
        ((ImageLayer)pack.Poses[0].Value.Frames[0].Layers[0]).Frame=2;pack.Masks.Value.Masks[0].PerFrame["13"]="masks/bad.png";
        var issues=PackValidator.ValidatePack(pack,fs);
        Assert.Contains(issues,i=>i.Rule=="V11"&&i.JsonPath=="Frames[0].Layers[0].Image");
        Assert.Contains(issues,i=>i.Rule=="V12"&&i.JsonPath=="Masks[0].PerFrame.13");
    }
    [Fact] public void ReservedColorSourcesAcceptKnownRigAndFarmerAndFixedColors()
    {
        var pack=Pack();pack.Characters.Add(new("rig.json",new(){Format="1.0",Id="robin",Npc="Robin",ColorSamples=new(){{"Skin",new(){Mode="auto-skin"}}}}));
        var layer=new LiveLayer{Source=new(){Kind="Npc",Npc="Robin"},ColorSlots=new(){{"Skin","Robin.Skin"},{"Hair","Farmer.Hair"},{"Outline","#102030"},{"Custom1","#112233"}}};
        pack.Poses.Add(new("pose.json",new(){Format="1.0",Id="pose",FrameSize=new[]{16,32},Frames=new(){new(){Duration=100,Layers=new(){layer}}}}));
        Assert.DoesNotContain(PackValidator.ValidatePack(pack),i=>i.Rule=="V13");
        layer.ColorSlots["Unknown"]="#ffffff";layer.ColorSlots["Skin"]="Missing.Unknown";
        var issues=PackValidator.ValidatePack(pack);Assert.Equal(2,issues.Count(i=>i.Rule=="V13"));
    }
    [Fact] public void NonVanillaAssetRequiresBothDependencyDeclarationsWithExportSeverity()
    {
        var pack=Pack();var scene=ValidationTests.Scene();scene.Actors[0].Sprite=new(){Asset="Characters/Robin_Beach"};pack.Cutscenes.Add(new("scene.json",scene));
        var catalog=new Catalog{Npcs={"Robin"},Locations={"ScienceHouse"},Assets={{"Characters/Robin_Beach",new(false,"Author.SCC")}}};
        Assert.Contains(PackValidator.ValidatePack(pack,catalog:catalog),i=>i.Rule=="V14"&&i.Severity==ValidationSeverity.Error);
        Assert.Contains(PackValidator.ValidatePack(pack,catalog:catalog,exportValidation:false),i=>i.Rule=="V14"&&i.Severity==ValidationSeverity.Warning);
        pack.Index!.Requires.Add(new(){Mod="author.scc"});pack.Manifest!.Dependencies.Add(new(){UniqueID="AUTHOR.SCC"});
        Assert.DoesNotContain(PackValidator.ValidatePack(pack,catalog:catalog),i=>i.Rule=="V14");
    }
    [Fact] public void OptionalProviderDependencyNeedsNoRequiresButRequiredOneDoes()
    {
        // "Only some scenes need it" (Q-15): an optional manifest dependency is enough; Requires mirrors required ones.
        var pack=Pack();var scene=ValidationTests.Scene();scene.Actors[0].Sprite=new(){Asset="Characters/Robin_Beach"};pack.Cutscenes.Add(new("scene.json",scene));
        var catalog=new Catalog{Npcs={"Robin"},Locations={"ScienceHouse"},Assets={{"Characters/Robin_Beach",new(false,"Author.SCC")}}};
        pack.Manifest!.Dependencies.Add(new(){UniqueID="Author.SCC",IsRequired=false});
        Assert.DoesNotContain(PackValidator.ValidatePack(pack,catalog:catalog),i=>i.Rule=="V14");
        pack.Manifest.Dependencies[0].IsRequired=true;
        Assert.Contains(PackValidator.ValidatePack(pack,catalog:catalog),i=>i.Rule=="V14"&&i.Message.Contains("Requires"));
    }
    [Fact] public void VideosRequireFramesWithMatchingDimensions()
    {
        var pack=Pack();var scene=ValidationTests.Scene();scene.Beats.Add(new VideoBeat{Video="intro"});pack.Cutscenes.Add(new("scene.json",scene));var fs=MemoryPackFileSystem.Minimal();
        Assert.Contains(PackValidator.ValidatePack(pack,fs),i=>i.Rule=="V16");
        fs.Images["videos/intro/frame-001.png"]=new(24,32);fs.Images["videos/intro/frame-002.png"]=new(24,32);
        Assert.DoesNotContain(PackValidator.ValidatePack(pack,fs),i=>i.Rule=="V16");
        fs.Images["videos/intro/frame-002.png"]=new(25,32);Assert.Contains(PackValidator.ValidatePack(pack,fs),i=>i.Rule=="V16");
    }
    [Fact] public void LengthUsesEncodedByteLimit()
    {
        Assert.Empty(PackValidator.ValidateScriptLength(new string('a',65535),"event"));
        Assert.Contains(PackValidator.ValidateScriptLength(new string('é',32768),"event"),i=>i.Rule=="V18"&&i.Severity==ValidationSeverity.Warning);
    }
}
