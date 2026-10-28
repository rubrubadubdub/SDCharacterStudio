using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Serialization;
using SDCharacterStudio.Core.Tests.TestSupport;
using SDCharacterStudio.Runtime.Loading;
using Xunit;

namespace SDCharacterStudio.Core.Tests;

/// <summary>
/// D-53: the editor writes a pack's own IDs (its scenes' event IDs, its story numbers and flags) with the project's
/// UniqueID. A C# mod runs the scenes under its own ID, so the engine reads those IDs as the mod's.
/// </summary>
public sealed class StoryIdsTests
{
    private static MemoryPackFileSystem StoryPack(string? madeAs)
    {
        var files = MemoryPackFileSystem.Minimal();
        // A C# mod's scenes folder has no manifest of its own: the engine runs it as the host mod.
        files.Files["manifest.json"] = """{"UniqueID":"Host.Mod","ContentPackFor":{"UniqueID":"Player.SDCharacterStudio"}}""";
        files.Files["studio.json"] = madeAs == null ? """{"Format":"1.0"}""" : "{\"Format\":\"1.0\",\"Pack\":{\"MadeAs\":\"" + madeAs + "\"}}";
        files.Files["cutscenes/first.json"] = """{"Format":"1.0","Id":"first","Location":"Town","Trigger":{},"Beats":[{"Type":"Number","Name":"visits"},{"Type":"Mail","Add":"SDCS.Editor.Pack.first.met"}]}""";
        files.Files["cutscenes/second.json"] = """
        {"Format":"1.0","Id":"second","Location":"Town",
         "Trigger":{"Conditions":"PLAYER_HAS_SEEN_EVENT Current Editor.Pack.first, PLAYER_STAT Current SDCS.Editor.Pack.visits 1","Preconditions":["e Editor.Pack.first"]},
         "Beats":[
           {"Type":"Check","Cases":[{"When":"PLAYER_HAS_MAIL Current SDCS.Editor.Pack.first.met","Then":"yes"}]},
           {"Type":"Choice","Options":[{"Text":"Again","Then":"yes","When":"PLAYER_STAT Current SDCS.Editor.Pack.visits 2"}]},
           {"Type":"Item","OneOf":[{"Item":"(O)709","When":"!PLAYER_HAS_MAIL Current SDCS.Editor.Pack.second.got-O709"}],"Remember":true}
         ],
         "Branches":{"yes":[{"Type":"Wait","Duration":1}]}}
        """;
        return files;
    }

    [Fact]
    public void ConditionsMadeUnderTheEditorsIdMatchWhatTheHostModWrites()
    {
        var result = PackCompilation.Compile(StoryPack("Editor.Pack"), "Host.Mod", "Host", _ => null, _ => true);
        Assert.Equal(2, result.Cutscenes.Count);
        var second = result.Cutscenes.Single(c => c.Source.Id == "second");
        // The engine names the scene Host.Mod.first and the number SDCS.Host.Mod.visits: the conditions now say so too.
        Assert.Equal("Host.Mod.first", result.Cutscenes.Single(c => c.Source.Id == "first").Compiled.Key);
        Assert.Contains("G PLAYER_HAS_SEEN_EVENT Current Host.Mod.first, PLAYER_STAT Current " + Ids.NumberStat("Host.Mod", "visits") + " 1", second.Compiled.MainKey);
        Assert.Contains("/e Host.Mod.first/", second.Compiled.MainKey);
        var beats = second.Compiled.Beats.Values.Select(b => b.Beat).ToList();
        Assert.Equal("PLAYER_HAS_MAIL Current SDCS.Host.Mod.first.met", beats.OfType<CheckBeat>().Single().Cases[0].When);
        Assert.Equal("PLAYER_STAT Current SDCS.Host.Mod.visits 2", beats.OfType<ChoiceBeat>().Single().Options[0].When);
        Assert.Equal("!PLAYER_HAS_MAIL Current " + Ids.PickedItemFlag("Host.Mod", "second", "(O)709"), beats.OfType<ItemBeat>().Single().OneOf![0].When);
        // The flag the first scene sets is renamed the same way, so set and checked stay one name.
        Assert.Equal("SDCS.Host.Mod.first.met", result.Cutscenes.Single(c => c.Source.Id == "first").Compiled.Beats.Values.Select(b => b.Beat).OfType<MailBeat>().Single().Add);
    }

    [Fact]
    public void APackRunningUnderItsOwnIdIsLeftAlone()
    {
        // No MadeAs (a hand-made pack), the same ID (a complete mod), or the editor's preview copy: nothing changes.
        foreach (var (madeAs, id) in new[] { ((string?)null, "Host.Mod"), ("Host.Mod", "Host.Mod"), ("Host.Mod", "Host.Mod" + Ids.PreviewSuffix) })
        {
            var scene = PackJson.Deserialize<Cutscene>("""{"Format":"1.0","Id":"s","Location":"Town","Trigger":{"Conditions":"PLAYER_HAS_SEEN_EVENT Current Host.Mod.s"},"Beats":[]}""")!;
            Assert.Equal(0, StoryIds.Rehome(scene, madeAs, id, new[] { "s" }));
            Assert.Equal("PLAYER_HAS_SEEN_EVENT Current Host.Mod.s", scene.Trigger!.Conditions);
        }
        // Only whole IDs of the pack's own scenes change; another mod's scene and look-alike names stay.
        var other = PackJson.Deserialize<Cutscene>("""{"Format":"1.0","Id":"s","Location":"Town","Trigger":{"Conditions":"PLAYER_HAS_SEEN_EVENT Current Editor.Pack.s Editor.Pack.other Editor.Pack.s2 Other.Editor.Pack.s, PLAYER_HAS_MAIL Current XSDCS.Editor.Pack.flag"},"Beats":[]}""")!;
        Assert.Equal(1, StoryIds.Rehome(other, "Editor.Pack", "Host.Mod", new[] { "s" }));
        Assert.Equal("PLAYER_HAS_SEEN_EVENT Current Host.Mod.s Editor.Pack.other Editor.Pack.s2 Other.Editor.Pack.s, PLAYER_HAS_MAIL Current XSDCS.Editor.Pack.flag", other.Trigger!.Conditions);
    }
}
