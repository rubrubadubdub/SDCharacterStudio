using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Serialization;
using SDCharacterStudio.Core.Validation;
using Xunit;

namespace SDCharacterStudio.Core.Tests.Data;

public class ValidationTests
{
    private const string Good = """
        {"Format":"1.0","Id":"talk","Location":"ScienceHouse",
         "Actors":[{"Id":"Robin","Kind":"Npc","Start":{"Tile":[1,2]}}],
         "Beats":[{"Type":"Say","Actor":"Robin","Text":"Hello"}]}
        """;
    public static Cutscene Scene() => PackJson.Deserialize<Cutscene>(Good)!;

    [Fact] public void NativeNpcNameDoesNotViolateContentIdRule()
    {
        Assert.Empty(PackValidator.ValidateCutscene(Scene(),"scene.json",new Catalog { Npcs={"Robin"}, Locations={"ScienceHouse"} }));
    }
    [Theory]
    [InlineData("Format",null,"V1")]
    [InlineData("Format","2.0","V1")]
    [InlineData("Id","Bad Id","V2")]
    [InlineData("Location","NotReal","V15")]
    public void ReportsLocatedFieldErrors(string property,string? value,string rule)
    {
        var json=JObject.Parse(Good); json[property]=value;
        var issues=PackValidator.ValidateCutscene(PackJson.Deserialize<Cutscene>(json.ToString())!,"scene.json",new Catalog { Npcs={"Robin"},Locations={"ScienceHouse"} });
        Assert.Contains(issues,i=>i.Rule==rule && i.File=="scene.json" && i.JsonPath==property);
    }
    [Fact] public void TextAutoFixChangesActualModelAndChoiceHasExtraRestrictions()
    {
        var scene=Scene(); scene.Beats=new() {new SayBeat {Actor="Robin",Text="a/b \"quoted\""}, new ChoiceBeat {Options=new() {new ChoiceOption {Text="bad#\\(break)",Then="next"}}}};
        scene.Branches["next"]=new();
        var issues=PackValidator.ValidateCutscene(scene,"scene.json");
        var fix=Assert.Single(issues,i=>i.Rule=="V5" && i.JsonPath=="Beats[0].Text");
        Assert.Contains(issues,i=>i.Rule=="V6" && i.JsonPath=="Beats[1].Options[0].Text");
        fix.AutoFix!();
        Assert.Equal("a⁄b “quoted”",((SayBeat)scene.Beats[0]).Text!.Literal);
        Assert.DoesNotContain(PackValidator.ValidateCutscene(scene,"scene.json"),i=>i.Rule=="V5");
    }
    [Fact] public void BranchTargetsAndChoiceFreeCycleAreLocated()
    {
        var scene=Scene(); scene.Beats=new(){new GotoBeat{Branch="absent"}};
        scene.Branches["a"]=new(){new GotoBeat{Branch="b"}};scene.Branches["b"]=new(){new GotoBeat{Branch="a"}};
        var issues=PackValidator.ValidateCutscene(scene,"scene.json");
        Assert.Contains(issues,i=>i.Rule=="V8" && i.JsonPath=="Beats[0].Branch");
        Assert.Contains(issues,i=>i.Rule=="V9" && i.Severity==ValidationSeverity.Warning);
        scene.Branches["b"].Insert(0,new ChoiceBeat{Options=new(){new ChoiceOption{Text="again",Then="a"}}});
        Assert.DoesNotContain(PackValidator.ValidateCutscene(scene,"scene.json"),i=>i.Rule=="V9");
    }
    [Fact] public void SortingFixReordersActualTrackWithoutDeletingOutOfRangeKeys()
    {
        var scene=Scene(); var track=new ClipTrack {Facing=new(){new FacingKey{At=500,Facing="left"},new FacingKey{At=0,Facing="right"},new FacingKey{At=999,Facing="up"}}};
        scene.Clips["walk"]=new(){Duration=600,Tracks=new(){track}};
        var issues=PackValidator.ValidateCutscene(scene,"scene.json");
        var sort=Assert.Single(issues,i=>i.Rule=="V10" && i.AutoFix!=null);sort.AutoFix!();
        Assert.Equal(new[]{0,500,999},track.Facing!.Select(k=>k.At));
        Assert.Contains(PackValidator.ValidateCutscene(scene,"scene.json"),i=>i.Rule=="V10" && i.JsonPath.EndsWith("[2].At"));
    }
    [Fact] public void UnknownTypesAndExplicitNullStructuresNeverCrashValidation()
    {
        var scene=PackJson.Deserialize<Cutscene>("""{"Format":"1.0","Id":"talk","Location":"Town","Actors":null,"Branches":{"null":null},"Clips":null,"Beats":[null,{"Type":"New"}]}""")!;
        var issues=PackValidator.ValidateCutscene(scene,"broken.json");
        Assert.Contains(issues,i=>i.JsonPath=="Beats[1].Type");
        Assert.Contains(issues,i=>i.JsonPath=="Beats[0]");
        Assert.Contains(issues,i=>i.JsonPath=="Actors");
    }
    [Fact] public void CatalogChecksNpcActorExactlyButLocationsIgnoreCase()
    {
        // Game1.getLocationFromName ignores case; Event.getActorByName and getCharacterFromName don't.
        var scene=Scene(); var catalog=new Catalog{Npcs={"Robin"},Locations={"sciencehouse"}};
        Assert.Empty(PackValidator.ValidateCutscene(scene,"scene",catalog));
        scene.Actors[0].Id="robin";scene.Actors[0].Npc=null;((SayBeat)scene.Beats[0]).Actor="robin";
        var wrongCase=Assert.Single(PackValidator.ValidateCutscene(scene,"scene",catalog),i=>i.Rule=="V7");
        Assert.Contains("use 'Robin'",wrongCase.Message);
        scene.Actors[0].Id="Robin";scene.Actors[0].Npc="Leah";
        Assert.Contains(PackValidator.ValidateCutscene(scene,"scene",catalog),i=>i.Rule=="V7");
    }
    [Fact] public void ScriptArgumentsAndEventKeysRejectSegmentInjection()
    {
        var scene=Scene();scene.Trigger=new(){Preconditions=new(){"t 600/evil"}};scene.Beats.Add(new RawBeat{Command="pause 1/end"});
        var issues=PackValidator.ValidateCutscene(scene,"scene");
        Assert.Contains(issues,i=>i.Rule=="V5" && i.JsonPath=="Trigger.Preconditions[0]");
        Assert.Contains(issues,i=>i.Rule=="V5" && i.JsonPath=="Beats[1].Command");
        Assert.Contains(PackValidator.ValidateEventKey("Bad/Pack",scene,"scene"),i=>i.Rule=="V17");
        Assert.Empty(PackValidator.ValidateEventKey("Author.Pack",scene,"scene"));
    }
    [Fact] public void EndCommandAndContentReferencesAreValidatedWithoutRejectingQualifiedItems()
    {
        var scene=Scene();scene.OnEnd.End="warpOut/end";scene.Beats.Add(new PoseBeat{Actor="Robin",Pose="Bad Pose"});scene.Beats.Add(new ItemBeat{Item="(O)Mod.Object",Count=1});
        var issues=PackValidator.ValidateCutscene(scene,"scene");
        Assert.Contains(issues,i=>i.Rule=="V5"&&i.JsonPath=="OnEnd.End");
        Assert.Contains(issues,i=>i.Rule=="V2"&&i.JsonPath=="Beats[1].Pose");
        Assert.DoesNotContain(issues,i=>i.JsonPath.StartsWith("Beats[2]",StringComparison.Ordinal));
    }
}
