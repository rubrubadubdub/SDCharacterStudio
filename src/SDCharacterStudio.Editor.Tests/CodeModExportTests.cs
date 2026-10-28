using System.IO;
using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core;
using SDCharacterStudio.Core.Loading;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Export;
using SDCharacterStudio.Editor.ViewModels;
using SDCharacterStudio.Runtime.Loading;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>
/// Export › My C# mod: conditions written with the project's ID still match when the mod runs the scenes under its own
/// ID (D-53), and the read-me says which other mods the scenes need.
/// </summary>
public sealed class CodeModExportTests
{
    /// <summary>The C# mod's scenes folder, read as the embedded engine reads it: no manifest of its own, the host mod's ID.</summary>
    private sealed class HostedScenes(string folder, string hostId) : IPackFileSystem
    {
        private readonly DiskPackFileSystem disk = new(folder);
        private readonly string manifest = "{\"UniqueID\":\"" + hostId + "\",\"Name\":\"Host\",\"ContentPackFor\":{\"UniqueID\":\"Player.SDCharacterStudio\"}}";
        public bool Exists(string path) => path == "manifest.json" || disk.Exists(path);
        public string ReadText(string path) => path == "manifest.json" ? manifest : disk.ReadText(path);
        public IEnumerable<string> ListFiles(string glob) => disk.ListFiles(glob);
    }

    [Fact]
    public void ConditionsWrittenInTheEditorStillMatchInsideAnotherModsId()
    {
        using var editor = new EditorHarness();
        var doc = editor.NewProject("Story", "Player.Story", "Town", "first");
        editor.Ui.Prompts.Enqueue("second");
        editor.Shell.AddCutsceneCommand.Execute(null);
        var first = doc.Cutscenes[0].Scene;
        var second = doc.Cutscenes[1].Scene;
        // As the condition rows write them: "has seen a scene" picks the project's scene, "story number" its number.
        string seen = ConditionContext.ProjectScenes(doc).Single(s => s.Detail == first.Id).Value!;
        Assert.Equal("Player.Story.first", seen);
        var visits = Schema.Conditions.NewRow(Schema.Conditions.Kind("number"), Ids.NumberStat(doc.PackId, "visits"));
        var group = new Schema.ConditionGroup();
        group.Rows.Add(Schema.Conditions.NewRow(Schema.Conditions.Kind("seen"), seen));
        group.Rows.Add(visits);
        doc.Edit("story", () =>
        {
            first.Beats.Insert(0, new NumberBeat { Name = "visits" });
            second.Trigger!.Conditions = Schema.Conditions.Print(group);
            second.Branches["again"] = new List<Beat> { new MessageBeat { Text = "Back again!" } };
            second.Beats.Insert(0, new CheckBeat { Cases = { new CheckCase { When = Schema.Conditions.Print(visits), Then = "again" } } });
        });

        var export = new ExportViewModel(editor.Shell) { Target = ExportTarget.CodeModScenes };
        export.ExportCommand.Execute(null);
        Assert.True(export.Done, export.Error);
        string scenes = Path.Combine(export.OutputFolder, ModExporter.ScenesFolder);
        Assert.Equal("Player.Story", JObject.Parse(File.ReadAllText(Path.Combine(scenes, "studio.json")))["Pack"]?["MadeAs"]?.ToString());
        Assert.Contains("A scene's event ID is your mod's UniqueID, a dot and the scene ID", File.ReadAllText(Path.Combine(export.OutputFolder, "README.txt")));

        // The host mod has its own ID: the engine names the scene and the number after it, and the conditions follow.
        var loaded = PackCompilation.Compile(new HostedScenes(scenes, "Someone.HostMod"), "Someone.HostMod", "Host", _ => null, _ => true);
        Assert.Empty(loaded.Skipped);
        var hostedFirst = loaded.Cutscenes.Single(c => c.Source.Id == "first");
        var hostedSecond = loaded.Cutscenes.Single(c => c.Source.Id == "second");
        Assert.Equal("Someone.HostMod.first", hostedFirst.Compiled.Key);
        string stat = Ids.NumberStat("Someone.HostMod", "visits");
        Assert.Contains($"G PLAYER_HAS_SEEN_EVENT Current {hostedFirst.Compiled.Key}, PLAYER_STAT Current {stat} 1", hostedSecond.Compiled.MainKey);
        Assert.Equal($"PLAYER_STAT Current {stat} 1", hostedSecond.Compiled.Beats.Values.Select(b => b.Beat).OfType<CheckBeat>().Single().Cases[0].When);
        Assert.DoesNotContain("Player.Story", hostedSecond.Compiled.MainKey);
    }

    [Fact]
    public void TheCodeModExportSaysWhatTheScenesNeedFromOtherMods()
    {
        using var editor = new EditorHarness();
        editor.NewProject("Lab", "Player.Lab", "ScienceHouse");
        var export = new ExportViewModel(editor.Shell) { Target = ExportTarget.CodeModScenes };
        string Export()
        {
            export.ExportCommand.Execute(null);
            Assert.True(export.Done, export.Error);
            return File.ReadAllText(Path.Combine(export.OutputFolder, "README.txt"));
        }

        // The export has ScienceHouse as Stardew Valley Expanded changes it: the scene was staged on that map.
        string readme = Export();
        Assert.Contains("Players install only your mod, and the mods below.", readme);
        Assert.Contains("Positions were placed on maps as other mods change them. Without the mod, they may land elsewhere:", readme);
        Assert.Contains("  - ScienceHouse, as Stardew Valley Expanded (FlashShifter.StardewValleyExpandedCP) changes it", readme);
        Assert.DoesNotContain("Dependencies", readme);
        Assert.Contains("ScienceHouse, as Stardew Valley Expanded", export.Result);

        // Sophia comes from that mod: now it's something the host mod must depend on (and the map is then always theirs).
        editor.Shell.Stage.DropCharacter("Sophia", 12, 7);
        readme = Export();
        Assert.Contains("These mods add things the scenes use. Add them to \"Dependencies\" in your mod's manifest.json:", readme);
        Assert.Contains("  - Stardew Valley Expanded (FlashShifter.StardewValleyExpandedCP), required: NPC Sophia", readme);
        Assert.DoesNotContain("Positions were placed", readme);
        Assert.Contains("\"Dependencies\"", export.Result);
    }
}
