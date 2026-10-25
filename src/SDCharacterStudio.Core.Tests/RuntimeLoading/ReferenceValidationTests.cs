using SDCharacterStudio.Core.Compiler;
using SDCharacterStudio.Core.Loading;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Validation;
using SDCharacterStudio.Runtime;
using SDCharacterStudio.Runtime.Loading;
using Xunit;

namespace SDCharacterStudio.Core.Tests.RuntimeLoading;

public sealed class ReferenceValidationTests
{
    [Fact]
    public void LoadedSaveReferenceChecksRejectOnlyScenesWithUnknownNpcsOrLocations()
    {
        var catalog = new Catalog(); catalog.Npcs.Add("Robin"); catalog.Locations.Add("ScienceHouse");
        var good = Scene("good", "ScienceHouse", "Robin");
        var badNpc = Scene("bad-npc", "ScienceHouse", "AbsentNpc");
        var badLocation = Scene("bad-location", "AbsentLocation", "Robin");
        var result = PackReferenceValidation.Validate(new[] { good, badNpc, badLocation }, catalog);
        Assert.Equal(new[] { good }, result.Valid);
        Assert.Equal(new[] { "bad-npc", "bad-location" }, result.Skipped.Select(x => x.CutsceneId));
        Assert.Contains(result.Issues, x => x.File == "cutscenes/bad-npc.json" && x.JsonPath == "Actors[0].Id" && x.Rule == "V7");
        Assert.Contains(result.Issues, x => x.File == "cutscenes/bad-location.json" && x.JsonPath == "Location" && x.Rule == "V15");
    }
    private static RegisteredCutscene Scene(string id, string location, string npc)
    {
        var source = new Cutscene { Format = "1.0", Id = id, Location = location, Actors = new() { new() { Id = npc, Npc = npc, Kind = "Npc", Start = new() { Tile = new[] { 0, 0 } } } } };
        var pack = new LoadedPack(); pack.Cutscenes.Add(new("cutscenes/" + id + ".json", source));
        return new("Player.Test", "Test", source, new("Player.Test." + id, location, "Player.Test." + id, "", new Dictionary<string, string>(), new Dictionary<string, BeatInfo>()), pack, _ => null);
    }
}
