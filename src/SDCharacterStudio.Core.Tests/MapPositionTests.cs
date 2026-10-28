using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Serialization;
using SDCharacterStudio.Core.Validation;
using Xunit;

namespace SDCharacterStudio.Core.Tests;

/// <summary>D-54: positions off the scene's map are warnings; a start or walk on a tile nobody can walk on is a hint.</summary>
public sealed class MapPositionTests
{
    [Fact]
    public void APositionOffTheMapIsAWarningInPlainWords()
    {
        var scene = PackJson.Deserialize<Cutscene>("""
        {"Format":"1.0","Id":"s","Location":"ScienceHouse","Camera":{"Tile":[53,24]},
         "Actors":[{"Id":"Abigail","Kind":"Npc","Start":{"Tile":[53,24]}},{"Id":"farmer","Kind":"Farmer","Start":{"Tile":[5,5]}},
                   {"Id":"Robin","Kind":"Npc","Start":{"From":"SpouseRoom","Tile":[40,40]}}],
         "Beats":[{"Type":"Move","Actor":"Abigail","To":{"Tile":[53,25]}},{"Type":"Move","Actor":"farmer","To":{"Tile":[3,4]}},
                  {"Type":"Prop","Item":"(F)1130","At":{"Tile":[-1,2]}},{"Type":"Effect","Effect":"hearts","At":{"Tile":[12,0]}}]}
        """)!;
        bool? Walkable(int x, int y) => !(x == 3 && y == 4);
        var issues = MapPositionValidator.Validate(scene, "cutscenes/s.json", new MapGrid("ScienceHouse", 12, 12, Walkable));
        Assert.All(issues, i => Assert.Equal(ValidationSeverity.Warning, i.Severity));
        Assert.Equal(new[]
        {
            "Actors[0].Start.Tile|V21|Abigail starts at 53,24, which is off the ScienceHouse map (12×12).",
            "Camera.Tile|V21|The camera centres on 53,24, which is off the ScienceHouse map (12×12).",
            "Beats[0].To.Tile|V21|Abigail walks to 53,25, which is off the ScienceHouse map (12×12).",
            "Beats[1].To.Tile|V22|The player walks to 3,4, a tile nobody can walk on in the ScienceHouse map (a wall or furniture?). Fine if you meant it.",
            "Beats[2].At.Tile|V21|The item stands on -1,2, which is off the ScienceHouse map (12×12).",
            "Beats[3].At.Tile|V21|The effect is at 12,0, which is off the ScienceHouse map (12×12).",
        }, issues.Select(i => $"{i.JsonPath}|{i.Rule}|{i.Message}"));
        // The farmhouse and cabins change with every upgrade: their export is one save's house.
        Assert.True(MapPositionValidator.VariesByUpgrade("FarmHouse"));
        Assert.True(MapPositionValidator.VariesByUpgrade("Cabin2b6e1"));
        Assert.False(MapPositionValidator.VariesByUpgrade("ScienceHouse"));
    }
}
