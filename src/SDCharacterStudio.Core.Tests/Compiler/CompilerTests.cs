using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core;
using SDCharacterStudio.Core.Compiler;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Serialization;
using SDCharacterStudio.Core.Validation;
using Xunit;

namespace SDCharacterStudio.Core.Tests.Compiler;

public sealed class CompilerTests
{
    private const string Pack = "Author.Test";
    private const string Key = Pack + ".scene";

    [Fact]
    public void HeaderAndSimpleBeatsUseNativeUnitsAndCommands()
    {
        var result = Compile("""[{"Type":"Say","Actor":"Robin","Text":"Hello","Emotion":"happy"},{"Type":"Wait","Duration":500},{"Type":"Face","Actor":"farmer","Facing":"up"}]""");
        Assert.True(result.Success, Errors(result));
        Assert.Equal("none/18 6/farmer 16 6 1 Robin 21 6 3/SDCS_Begin Author.Test.scene/speak Robin \"Hello$h\"/pause 500/faceDirection farmer 0/SDCS_End Author.Test.scene/end", result.Cutscene!.MainScript);
        Assert.Empty(result.Cutscene.SubScripts);
    }

    [Fact]
    public void StayKeepsThePlayerWhereTheSceneLeftThem()
    {
        var result = new EventCompiler().Compile(Pack, PackJson.Deserialize<Cutscene>("""{"Format":"1.0","Id":"scene","Location":"Town","OnEnd":{"End":"stay"},"Actors":[{"Id":"farmer","Kind":"Farmer","Start":{"Tile":[1,2]}}],"Beats":[]}""")!);
        Assert.True(result.Success, Errors(result));
        Assert.EndsWith("/SDCS_End Author.Test.scene stay/end", result.Cutscene!.MainScript);
    }

    [Theory]
    [InlineData("{\"Type\":\"Message\",\"Text\":\"Hello\"}", "message \"Hello\"")]
    [InlineData("{\"Type\":\"Clip\",\"Clip\":\"walk\",\"Hold\":true}", "SDCS_Clip Author.Test.scene walk hold")]
    [InlineData("{\"Type\":\"Move\",\"Actor\":\"Robin\",\"To\":{\"Tile\":[2,3]}}", "SDCS_Move Author.Test.scene main.0")]
    [InlineData("{\"Type\":\"Emote\",\"Actor\":\"Robin\",\"Emote\":\"blush\",\"Wait\":false}", "emote Robin 60 true")]
    [InlineData("{\"Type\":\"Pose\",\"Actor\":\"Robin\",\"Release\":true}", "SDCS_Pose Author.Test.scene main.0")]
    [InlineData("{\"Type\":\"Costume\",\"Actor\":\"farmer\",\"Restore\":true}", "SDCS_Costume Author.Test.scene main.0")]
    [InlineData("{\"Type\":\"Sound\",\"Cue\":\"coin\"}", "playSound coin")]
    [InlineData("{\"Type\":\"Sound\",\"Cue\":\"coin\",\"Pitch\":900}", "SDCS_Sound Author.Test.scene main.0")]
    [InlineData("{\"Type\":\"Music\",\"Cue\":\"none\"}", "playMusic none")]
    [InlineData("{\"Type\":\"Mail\",\"Remove\":\"Flag\"}", "addMailReceived Flag false")]
    [InlineData("{\"Type\":\"Friendship\",\"Npc\":\"Robin\",\"Points\":-750}", "friendship Robin -750")]
    [InlineData("{\"Type\":\"Money\",\"Amount\":5000}", "money 5000")]
    [InlineData("{\"Type\":\"Money\",\"Amount\":5000,\"HoldUp\":true}", "SDCS_Give Author.Test.scene main.0")]
    [InlineData("{\"Type\":\"Item\",\"Item\":\"(O)709\",\"Count\":100}", "addItem (O)709 100")]
    [InlineData("{\"Type\":\"Quest\",\"Add\":\"quest\"}", "addQuest quest")]
    [InlineData("{\"Type\":\"Effect\",\"Effect\":\"dust\",\"Actor\":\"Robin\"}", "SDCS_Effect Author.Test.scene main.0")]
    // D-50: an item on the map (the runtime draws it until the scene is over).
    [InlineData("{\"Type\":\"Prop\",\"Item\":\"(F)1130\",\"At\":{\"Tile\":[38,58]}}", "SDCS_Prop Author.Test.scene main.0")]
    [InlineData("{\"Type\":\"Prop\",\"Remove\":true,\"At\":{\"Tile\":[38,58]}}", "SDCS_Prop Author.Test.scene main.0")]
    [InlineData("{\"Type\":\"Shake\",\"Actor\":\"Robin\",\"Duration\":600}", "shake Robin 600")]
    // Vanilla shake takes NPCs only ("no NPC found with name 'farmer'"); the player shakes through the runtime.
    [InlineData("{\"Type\":\"Shake\",\"Actor\":\"farmer\",\"Duration\":600}", "SDCS_Shake Author.Test.scene main.0")]
    [InlineData("{\"Type\":\"Jump\",\"Actor\":\"Robin\",\"Strength\":3,\"Silent\":true}", "jump Robin 3 true")]
    [InlineData("{\"Type\":\"Camera\",\"Center\":{\"Tile\":[2,3]}}", "viewport 2 3")]
    [InlineData("{\"Type\":\"Camera\",\"Center\":{\"Tile\":[2,3]},\"Duration\":100}", "SDCS_Camera Author.Test.scene main.0")]
    [InlineData("{\"Type\":\"Say\",\"Actor\":\"Robin\",\"Text\":\"Hi\",\"Portrait\":\"custom\"}", "SDCS_Say Author.Test.scene main.0")]
    [InlineData("{\"Type\":\"Raw\",\"Command\":\"addConversationTopic robinSecret 7\"}", "addConversationTopic robinSecret 7")]
    public void BeatMappingsMatchEngineCommands(string beat, string command)
    {
        var result = Compile("[" + beat + "]");
        Assert.True(result.Success, Errors(result));
        Assert.Contains(command, result.Cutscene!.MainScript.Split('/'));
        Assert.True(result.Cutscene.Beats.ContainsKey("main.0"));
    }

    [Fact]
    public void ChoiceFallThroughReturnsToItsOwnContinuation()
    {
        var result = Compile("""[{"Type":"Choice","Options":[{"Text":"First","Then":"shared"}]},{"Type":"Message","Text":"One"},{"Type":"Choice","Options":[{"Text":"Second","Then":"shared"}]},{"Type":"Message","Text":"Two"}]""", """{"shared":[{"Type":"Message","Text":"Branch"}]}""");
        Assert.True(result.Success, Errors(result));
        var scripts = result.Cutscene!.SubScripts;
        Assert.EndsWith("switchEvent " + Key + ".c1", scripts[Key + ".shared.c1"]);
        Assert.EndsWith("switchEvent " + Key + ".c2", scripts[Key + ".shared.c2"]);
        Assert.Contains("message \"One\"", scripts[Key + ".c1"]);
        Assert.Contains("message \"Two\"", scripts[Key + ".c2"]);
        Assert.Contains("SDCS_End " + Key + "/end", result.Cutscene.MainScript);
        Assert.All(scripts.Values, script => Assert.StartsWith("pause 1/", script));
    }

    [Fact]
    public void CheckSwitchesToEachCaseThenOtherwiseAndReturnsLikeAChoice()
    {
        // D-51: two cases and a branch for otherwise, then a beat after the Check that every way comes back to.
        var result = Compile("""[{"Type":"Check","Cases":[{"When":"PLAYER_HEARTS Current Robin 6","Then":"close"},{"When":"SEASON Fall, WEATHER Here Rain","Then":"rainy"}],"Otherwise":"else"},{"Type":"Message","Text":"After"}]""",
            """{"close":[{"Type":"Message","Text":"Close"}],"rainy":[{"Type":"Message","Text":"Rainy"}],"else":[{"Type":"Message","Text":"Else"}]}""");
        Assert.True(result.Success, Errors(result));
        var scripts = result.Cutscene!.SubScripts;
        Assert.Contains($"SDCS_Check {Key} main.0 {Key}.close.c1 {Key}.rainy.c1 {Key}.else.c1", result.Cutscene.MainScript.Split('/'));
        foreach (string branch in new[] { "close", "rainy", "else" }) Assert.EndsWith("switchEvent " + Key + ".c1", scripts[$"{Key}.{branch}.c1"]);
        Assert.Contains("message \"After\"", scripts[Key + ".c1"]);
        Assert.Equal("PLAYER_HEARTS Current Robin 6", ((CheckBeat)result.Cutscene.Beats["main.0"].Beat).Cases[0].When);
    }

    [Fact]
    public void CheckKeepsGoingOrEndsTheSceneWhenNoCaseIsTrue()
    {
        // Otherwise unset: straight on to the beats after the Check.
        var on = Compile("""[{"Type":"Check","Cases":[{"When":"PLAYER_HAS_MAIL Current Flag","Then":"yes"}]},{"Type":"Message","Text":"After"}]""", """{"yes":[{"Type":"Message","Text":"Yes"}]}""");
        Assert.True(on.Success, Errors(on));
        Assert.Contains($"SDCS_Check {Key} main.0 {Key}.yes.c1 {Key}.c1", on.Cutscene!.MainScript.Split('/'));
        // Nothing after it: the scene ends (a script that only ends it).
        var last = Compile("""[{"Type":"Check","Cases":[{"When":"PLAYER_HAS_MAIL Current Flag","Then":"yes"}]}]""", """{"yes":[{"Type":"Message","Text":"Yes"}]}""");
        Assert.True(last.Success, Errors(last));
        Assert.Contains($"SDCS_Check {Key} main.0 {Key}.yes {Key}.finish", last.Cutscene!.MainScript.Split('/'));
        Assert.Equal($"pause 1/SDCS_End {Key}/end", last.Cutscene.SubScripts[Key + ".finish"]);
        Assert.EndsWith("/end", last.Cutscene.SubScripts[Key + ".yes"]);
    }

    [Fact]
    public void ChanceSwitchesToAnOutcomeAndRandomItemsAndLinesGoThroughTheRuntime()
    {
        // D-51 step 2: a Chance with three outcomes that come back to the beat after it.
        var chance = Compile("""[{"Type":"Chance","Outcomes":[{"Weight":5,"Then":"laughs"},{"Weight":3,"Then":"groans"},{"Weight":2,"Then":"snorts"}]},{"Type":"Message","Text":"After"}]""",
            """{"laughs":[{"Type":"Message","Text":"Ha"}],"groans":[{"Type":"Message","Text":"Ugh"}],"snorts":[{"Type":"Message","Text":"Pfft"}]}""");
        Assert.True(chance.Success, Errors(chance));
        Assert.Contains($"SDCS_Chance {Key} main.0 {Key}.laughs.c1 {Key}.groans.c1 {Key}.snorts.c1", chance.Cutscene!.MainScript.Split('/'));
        Assert.EndsWith("switchEvent " + Key + ".c1", chance.Cutscene.SubScripts[Key + ".snorts.c1"]);

        // A bucket of items, and a line out of several, are the runtime's to pick.
        var picks = Compile("""[{"Type":"Item","OneOf":[{"Item":"(H)15","Weight":4},{"Item":"(H)0","Count":2}],"Remember":true},{"Type":"Say","Actor":"Robin","Text":"Bye.","Or":["Later.",{"i18n":"line.three"}]},{"Type":"Message","Text":"One","Or":["Two"]}]""");
        Assert.False(picks.Success);   // the translation is missing: every line is checked like the first
        Assert.Contains(picks.Issues, x => x.JsonPath == "Beats[1].Or[1]");
        picks = Compile("""[{"Type":"Item","OneOf":[{"Item":"(H)15","Weight":4},{"Item":"(H)0","Count":2}],"Remember":true},{"Type":"Say","Actor":"Robin","Text":"Bye.","Or":["Later."]},{"Type":"Message","Text":"One","Or":["Two"]}]""");
        Assert.True(picks.Success, Errors(picks));
        var script = picks.Cutscene!.MainScript.Split('/');
        Assert.Contains($"SDCS_Give {Key} main.0", script);
        Assert.Contains($"SDCS_Say {Key} main.1", script);
        Assert.Contains($"SDCS_Message {Key} main.2", script);
        Assert.Equal("Later.", ((SayBeat)picks.Cutscene.Beats["main.1"].Beat).Or![0].Literal);
        Assert.Equal("SDCS.Player.Pack.scene.got-H15", Ids.PickedItemFlag("Player.Pack", "scene", "(H)15"));
        Assert.Equal("SDCS.Player.Pack.scene.got-H15", Ids.PickedItemFlag("Player.Pack.DevPreview", "scene", "(H)15"));   // a preview keeps the real names
    }

    [Fact]
    public void StoryNumbersGoThroughTheRuntimeAndCountOnSkip()
    {
        // D-51 step 3: a story number changes in the scene, and a skipped scene still changes it.
        var scene = Scene("""[{"Type":"Number","Name":"jokes-told","Change":"add","Amount":1,"UpTo":3}]""");
        scene.Skippable = true;
        scene.OnSkip = new() { new NumberBeat { Name = "jokes-told", Change = "add", Amount = 1 } };
        var result = new EventCompiler().Compile(Pack, scene);
        Assert.True(result.Success, Errors(result));
        Assert.Contains($"SDCS_Number {Key} main.0", result.Cutscene!.MainScript.Split('/'));
        Assert.Contains($"SDCS_SkipNumber {Key} skip.0", result.Cutscene.MainScript);
        Assert.Equal("SDCS.Player.Pack.jokes-told", Ids.NumberStat("Player.Pack.DevPreview", "jokes-told"));
    }

    [Fact]
    public void AnswersOfferedOnlyIfGoThroughTheRuntime()
    {
        // D-51 step 4: one answer has a condition, so the runtime builds the question; with none true it goes on after.
        var result = Compile("""[{"Type":"Choice","Prompt":"Well?","Options":[{"Text":"Tell the truth","Then":"truth"},{"Text":"Ask about the necklace","Then":"necklace","When":"PLAYER_HAS_MAIL Current LostIt"}]},{"Type":"Message","Text":"After"}]""",
            """{"truth":[{"Type":"Message","Text":"T"}],"necklace":[{"Type":"Message","Text":"N"}]}""");
        Assert.True(result.Success, Errors(result));
        Assert.Contains($"SDCS_Choice {Key} main.0 {Key}.truth.c1 {Key}.necklace.c1 {Key}.c1", result.Cutscene!.MainScript.Split('/'));
        Assert.DoesNotContain("quickQuestion", result.Cutscene.MainScript);
        // Without conditions it stays the game's own question.
        var plain = Compile("""[{"Type":"Choice","Options":[{"Text":"A","Then":"truth"}]}]""", """{"truth":[]}""");
        Assert.StartsWith("quickQuestion", plain.Cutscene!.MainScript.Split('/').First(s => s.StartsWith("quickQuestion")));
        var empty = Compile("""[{"Type":"Choice","Options":[{"Text":"A","Then":"truth","When":" "}]}]""", """{"truth":[]}""");
        Assert.Contains(empty.Issues, x => x.JsonPath == "Beats[0].Options[0].When");
    }

    [Theory]
    [InlineData("""[{"Type":"Number","Name":"Jokes Told"}]""", "Beats[0].Name")]
    [InlineData("""[{"Type":"Number","Name":"jokes","Change":"multiply"}]""", "Beats[0].Change")]
    [InlineData("""[{"Type":"Number","Name":"jokes","Amount":-1}]""", "Beats[0].Amount")]
    [InlineData("""[{"Type":"Number","Name":"jokes","Amount":3,"UpTo":2}]""", "Beats[0].UpTo")]
    public void NumberProblemsPointAtTheField(string beats, string path)
    {
        var result = Compile(beats);
        Assert.False(result.Success);
        Assert.Contains(result.Issues, x => x.Severity == ValidationSeverity.Error && x.JsonPath == path);
    }

    [Theory]
    [InlineData("""[{"Type":"Chance","Outcomes":[]}]""", "Beats[0].Outcomes")]
    [InlineData("""[{"Type":"Chance","Outcomes":[{"Weight":0,"Then":"yes"}]}]""", "Beats[0].Outcomes")]
    [InlineData("""[{"Type":"Chance","Outcomes":[{"Weight":-1,"Then":"yes"},{"Weight":1,"Then":"yes"}]}]""", "Beats[0].Outcomes[0].Weight")]
    [InlineData("""[{"Type":"Chance","Outcomes":[{"Then":"nowhere"}]}]""", "Beats[0].Outcomes[0].Then")]
    [InlineData("""[{"Type":"Item","OneOf":[{"Item":"","Weight":1}]}]""", "Beats[0].OneOf[0].Item")]
    [InlineData("""[{"Type":"Item","OneOf":[{"Item":"(H)0","Weight":0}]}]""", "Beats[0].OneOf")]
    public void ChanceAndBucketProblemsPointAtTheField(string beats, string path)
    {
        var result = Compile(beats, """{"yes":[]}""");
        Assert.False(result.Success);
        Assert.Contains(result.Issues, x => x.Severity == ValidationSeverity.Error && x.JsonPath == path);
    }

    [Theory]
    [InlineData("""[{"Type":"Check","Cases":[]}]""", "Beats[0].Cases")]
    [InlineData("""[{"Type":"Check","Cases":[{"When":"","Then":"yes"}]}]""", "Beats[0].Cases[0].When")]
    [InlineData("""[{"Type":"Check","Cases":[{"When":"SEASON Fall","Then":"nowhere"}]}]""", "Beats[0].Cases[0].Then")]
    [InlineData("""[{"Type":"Check","Cases":[{"When":"SEASON Fall","Then":"yes"}],"Otherwise":"nowhere"}]""", "Beats[0].Otherwise")]
    public void CheckProblemsPointAtTheField(string beats, string path)
    {
        var result = Compile(beats, """{"yes":[]}""");
        Assert.False(result.Success);
        Assert.Contains(result.Issues, x => x.Severity == ValidationSeverity.Error && x.JsonPath == path);
    }

    [Fact]
    public void GotoChainRetainsFallThroughContinuation()
    {
        var result = Compile("""[{"Type":"Choice","Options":[{"Text":"Go","Then":"alias"}]},{"Type":"Message","Text":"Back"}]""", """{"alias":[{"Type":"Goto","Branch":"shared"}],"shared":[{"Type":"Wait","Duration":3}]}""");
        Assert.True(result.Success, Errors(result));
        Assert.Contains(result.Cutscene!.SubScripts.Values, x => x.Contains("pause 3/switchEvent " + Key + ".c1"));
        Assert.All(result.Cutscene.SubScripts.Values, x => Assert.StartsWith("pause 1/", x));
    }

    [Fact]
    public void TerminalChoiceWithoutRestHasNoExtraTail()
    {
        var result = Compile("""[{"Type":"Choice","Options":[{"Text":"Go","Then":"ask"}]}]""", """{"ask":[{"Type":"Choice","Options":[{"Text":"End","Then":"done"}]}],"done":[{"Type":"End"}]}""");
        Assert.True(result.Success, Errors(result));
        Assert.StartsWith("pause 1/quickQuestion #End", result.Cutscene!.SubScripts[Key + ".ask"]);
        Assert.DoesNotContain("/SDCS_End", result.Cutscene.SubScripts[Key + ".ask"]);
        Assert.Equal("pause 1/SDCS_End " + Key + "/end", result.Cutscene.SubScripts[Key + ".done"]);
    }

    [Fact]
    public void GotoCycleWarnsWithoutCompilerRecursion()
    {
        var result = Compile("""[{"Type":"Goto","Branch":"a"}]""", """{"a":[{"Type":"Goto","Branch":"b"}],"b":[{"Type":"Goto","Branch":"a"}]}""");
        Assert.True(result.Success, Errors(result));
        Assert.Contains(result.Issues, x => x.Severity == ValidationSeverity.Warning && x.Rule == "V9");
        Assert.Equal(2, result.Cutscene!.SubScripts.Count);
    }

    [Theory]
    [InlineData("{\"Type\":\"Message\",\"Text\":\"unsafe/text\"}")]
    [InlineData("{\"Type\":\"Sound\",\"Cue\":\"coin/end\"}")]
    [InlineData("{\"Type\":\"Raw\",\"Command\":\"pause 1/end\"}")]
    [InlineData("{\"Type\":\"Choice\",\"Prompt\":\"Unsafe#prompt\",\"Options\":[{\"Text\":\"OK\",\"Then\":\"done\"}]}")]
    [InlineData("{\"Type\":\"Prop\",\"At\":{\"Tile\":[1,2]}}")]            // placing nothing
    [InlineData("{\"Type\":\"Prop\",\"Item\":\"(F)1130\"}")]                 // nowhere
    public void ScriptInjectionIsLocatedAndProducesNoOutput(string beat)
    {
        var result = Compile("[" + beat + "]", """{"done":[{"Type":"End"}]}""");
        Assert.False(result.Success);
        Assert.Null(result.Cutscene);
        Assert.Contains(result.Issues, x => x.Severity == ValidationSeverity.Error && x.JsonPath.Contains("Beats[0]"));
    }

    [Fact]
    public void TranslationIsValidatedAfterResolutionEvenForCustomSay()
    {
        var scene = Scene("""[{"Type":"Say","Actor":"Robin","Text":{"i18n":"hello"},"Portrait":"custom"}]""");
        var result = new EventCompiler().Compile(Pack, scene, _ => "unsafe/translation");
        Assert.False(result.Success);
        Assert.Contains(result.Issues, x => x.JsonPath.EndsWith("Text"));
    }

    [Fact]
    public void TriggerAndEndVariantsArePreserved()
    {
        var scene = Scene("[]");
        var json = JObject.FromObject(scene, PackJson.CreateSerializer());
        json["Trigger"] = JObject.Parse("""{"Preconditions":["F","t 1700 2100"],"Conditions":"PLAYER_HAS_MAIL Current Flag","OnDayAfterFlag":"Promise","Once":false}""");
        json["OnEnd"] = JObject.Parse("""{"End":"position 4 5"}""");
        var result = new EventCompiler().Compile(Pack, PackJson.Deserialize<Cutscene>(json.ToString()));
        Assert.True(result.Success, Errors(result));
        Assert.Equal(Key + "/F/t 1700 2100/G PLAYER_HAS_MAIL Current Flag/n Promise_Ready", result.Cutscene!.MainKey);
        Assert.EndsWith("SDCS_End " + Key + "/end position 4 5", result.Cutscene.MainScript);
    }

    [Fact]
    public void SkipEffectsAreTriggerActionsNotEventCommands()
    {
        var json = JObject.FromObject(Scene("[]"), PackJson.CreateSerializer());
        json["Skippable"] = true;
        json["OnSkip"] = JArray.Parse("""[{"Type":"Mail","Add":"Flag"},{"Type":"Money","Amount":5},{"Type":"Item","Item":"(O)709","Count":2},{"Type":"Friendship","Npc":"Robin","Points":250},{"Type":"Costume","Actor":"farmer","Restore":true}]""");
        var result = new EventCompiler().Compile(Pack, PackJson.Deserialize<Cutscene>(json.ToString()));
        Assert.True(result.Success, Errors(result));
        Assert.Contains("/skippable/setSkipActions AddMail Current Flag received#AddMoney 5#AddItem (O)709 2#AddFriendshipPoints Robin 250#SDCS_SkipCostume Author.Test.scene skip.4/SDCS_Begin", result.Cutscene!.MainScript);
    }

    [Fact]
    public void OversizedScriptWarnsAndStillCompiles()
    {
        var scene = Scene(new JArray(new JObject { ["Type"] = "Message", ["Text"] = new string('x', 65536) }).ToString());
        var result = new EventCompiler().Compile(Pack, scene);
        Assert.True(result.Success, Errors(result));
        Assert.Contains(result.Issues, x => x.Rule == "V18" && x.Severity == ValidationSeverity.Warning);
    }

    [Fact]
    public void TranslatedContinuationErrorKeepsItsOriginalJsonPath()
    {
        var scene = Scene("""[{"Type":"Choice","Options":[{"Text":"Go","Then":"empty"}]},{"Type":"Message","Text":{"i18n":"unsafe"}}]""", """{"empty":[]}""");
        var result = new EventCompiler().Compile(Pack, scene, _ => "bad/text");
        Assert.False(result.Success);
        Assert.Contains(result.Issues, x => x.JsonPath == "Beats[1].Text");
    }

    [Fact]
    public void InternalNamesCannotCollideWithLegalBranchIds()
    {
        var result = Compile("""[{"Type":"Choice","Options":[{"Text":"Go","Then":"main"}]},{"Type":"Wait","Duration":1}]""", """{"main":[{"Type":"Goto","Branch":"branch.main"}],"branch.main":[{"Type":"Goto","Branch":"c1"}],"c1":[]}""");
        Assert.True(result.Success, Errors(result));
        Assert.Contains(result.Cutscene!.SubScripts.Keys, x => x.EndsWith(".c2", StringComparison.Ordinal));
        Assert.Equal(result.Cutscene.SubScripts.Count, result.Cutscene.SubScripts.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void EmptyTriggerIsNaturallyTriggerableAndCameraDefaultsToFirstActor()
    {
        var json = JObject.FromObject(Scene("[]"), PackJson.CreateSerializer());
        json["Trigger"] = new JObject(); json.Remove("Camera");
        var result = new EventCompiler().Compile(Pack, PackJson.Deserialize<Cutscene>(json.ToString()));
        Assert.True(result.Success, Errors(result));
        Assert.Equal(Key + "/G TRUE", result.Cutscene!.MainKey);
        Assert.StartsWith("none/16 6/", result.Cutscene.MainScript);
    }

    [Fact]
    public void FadeAndTemporaryActorUseVerifiedEngineSyntax()
    {
        var json = JObject.FromObject(Scene("""[{"Type":"Fade","To":"black","Duration":500,"Wait":false}]"""), PackJson.CreateSerializer());
        ((JArray)json["Actors"]!).Add(JObject.Parse("""{"Id":"Gull","Kind":"Temporary","Sprite":{"Asset":"Characters/Seagull"},"FrameSize":[16,16],"Start":{"Tile":[5,2]},"Facing":"down"}"""));
        var result = new EventCompiler().Compile(Pack, PackJson.Deserialize<Cutscene>(json.ToString()));
        Assert.True(result.Success, Errors(result));
        Assert.Contains("/addTemporaryActor Seagull 16 16 5 2 2 true character Gull/SDCS_Begin", result.Cutscene!.MainScript);
        Assert.Contains("globalFade 0.03333333 true", result.Cutscene.MainScript.Split('/'));
    }

    [Fact]
    public void RecompilationDoesNotMutateTranslatedSourceModels()
    {
        var scene = Scene("""[{"Type":"Message","Text":{"i18n":"hello"}}]""");
        string before = PackJson.Serialize(scene);
        var first = new EventCompiler().Compile(Pack, scene, _ => "One");
        var second = new EventCompiler().Compile(Pack, scene, _ => "Two");
        Assert.Contains("message \"One\"", first.Cutscene!.MainScript);
        Assert.Contains("message \"Two\"", second.Cutscene!.MainScript);
        Assert.Equal(before, PackJson.Serialize(scene));
    }

    [Fact]
    public void SkipCostumeConsequencesRemainAddressableInTheRegistry()
    {
        var json = JObject.FromObject(Scene("[]"), PackJson.CreateSerializer());
        json["Skippable"] = true;
        json["OnSkip"] = JArray.Parse("""[{"Type":"Costume","Actor":"farmer","Set":{"Hat":"none"}}]""");
        var result = new EventCompiler().Compile(Pack, PackJson.Deserialize<Cutscene>(json.ToString()));
        Assert.True(result.Success, Errors(result));
        Assert.Contains("setSkipActions SDCS_SkipCostume " + Key + " skip.0", result.Cutscene!.MainScript.Split('/'));
        var stored = Assert.IsType<CostumeBeat>(result.Cutscene.Beats["skip.0"].Beat);
        Assert.Equal("none", (string?)stored.Set!["Hat"]);
        Assert.Equal("OnSkip[0]", result.Cutscene.Beats["skip.0"].JsonPath);
    }

    private static CompilationResult Compile(string beats, string branches = "{}") => new EventCompiler().Compile(Pack, Scene(beats, branches));
    internal static Cutscene Scene(string beats, string branches = "{}") => PackJson.Deserialize<Cutscene>("{\"Format\":\"1.0\",\"Id\":\"scene\",\"Location\":\"ScienceHouse\",\"Camera\":{\"Tile\":[18,6]},\"Actors\":[{\"Id\":\"farmer\",\"Kind\":\"Farmer\",\"Start\":{\"Tile\":[16,6]},\"Facing\":\"right\"},{\"Id\":\"Robin\",\"Kind\":\"Npc\",\"Npc\":\"Robin\",\"Start\":{\"Tile\":[21,6]},\"Facing\":\"left\"}],\"Beats\":" + beats + ",\"Branches\":" + branches + "}")!;
    private static string Errors(CompilationResult result) => string.Join("\n", result.Issues.Select(x => x.JsonPath + ": " + x.Message));
}
