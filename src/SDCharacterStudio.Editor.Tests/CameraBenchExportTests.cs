using System.IO;
using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Export;
using SDCharacterStudio.Editor.Project;

namespace SDCharacterStudio.Editor.Tests;

public sealed class CameraBenchExportTests
{
    [Fact]
    public void ExportCameraBench()
    {
        if (Environment.GetEnvironmentVariable("SDCS_CAMERA_BENCH_EXPORT") != "1") return;
        string output = Environment.GetEnvironmentVariable("SDCS_CAMERA_BENCH_OUTPUT")!;
        var doc = PackDocument.Create(new NewProjectOptions { Name = "Camera Bench", Author = "SDCharacterStudio", UniqueId = "Bench.Camera", Location = "Town", ParentFolder = output });
        doc.Cutscenes.Clear();
        foreach (string end in new[] { "stay", "default" })
        foreach (string path in new[] { "main", "branch" })
        foreach (string form in new[] { "fixed", "beat" })
        {
            string id = $"camera-{end}-{path}-{form}";
            var scene = new Cutscene { Format = "1.0", Id = id, Location = "Town", Trigger = new Trigger { Conditions = "FALSE", Once = false }, OnEnd = new EndSettings { End = end } };
            scene.Actors.Add(new Actor { Id = "farmer", Kind = "Farmer", Start = new Position { Tile = new[] { 44, 60 } }, Facing = "right" });
            if (form == "fixed") scene.Camera = new JObject { ["Tile"] = new JArray(25, 60) };
            else scene.Beats.Add(new CameraBeat { Center = new Position { Tile = new[] { 25, 60 } }, Duration = 1 });
            scene.Beats.Add(new PropBeat { Item = "(F)1130", At = new Position { Tile = new[] { 47, 59 } } });
            scene.Beats.Add(new MoveBeat { Actor = "farmer", To = new Position { Tile = new[] { 52, 60 } } });
            scene.Beats.Add(new MessageBeat { Text = "The scene is ending." });
            scene.Beats.Add(new WaitBeat { Duration = 600 });
            if (path == "branch")
            {
                scene.Beats.Add(new GotoBeat { Branch = "ending" });
                scene.Branches["ending"] = new List<Beat> { new EndBeat() };
            }
            else scene.Beats.Add(new EndBeat());
            doc.Cutscenes.Add(new CutsceneDocument($"cutscenes/{id}.json", scene));
        }
        doc.Save();
        using var editor = new EditorHarness();
        editor.Shell.Attach(doc);
        var validation = editor.Shell.ValidateNow();
        Assert.True(validation.Errors == 0, string.Join(" | ", validation.Problems.Select(p => p.Message)));
        ModExporter.WriteScenes(doc, Path.Combine(output, "embedded-source"));
        string framework = Path.Combine(output, "framework");
        PackExporter.CopyDirectory(doc.PackFolder, framework);
        string manifestPath = Path.Combine(framework, "manifest.json");
        var manifest = JObject.Parse(File.ReadAllText(manifestPath));
        manifest["UniqueID"] = "Bench.CameraFramework";
        File.WriteAllText(manifestPath, manifest.ToString());
        Assert.Equal(8, Directory.GetFiles(Path.Combine(framework, "cutscenes"), "*.json").Length);
    }
}
