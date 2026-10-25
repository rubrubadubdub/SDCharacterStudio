using System.IO;
using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core.Compiler;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Serialization;
using SDCharacterStudio.Editor.Project;

namespace SDCharacterStudio.Editor.Tests;

public class WriterTests
{
    [Fact]
    public void OmitsDefaultsSoPacksLookHandWritten()
    {
        var beat = new MoveBeat { Actor = "Robin", To = new Position { Tile = new[] { 17, 6 } }, Speed = "slow" };
        var json = JObject.Parse(PackWriter.Serialize(beat));
        Assert.Equal("Move", (string?)json["Type"]);
        Assert.Equal("slow", (string?)json["Speed"]);
        Assert.Null(json["Wait"]);                         // default true
        Assert.Null(json["Path"]);                         // default x-first
        Assert.Null(json["To"]!["Px"]);                    // default [0,0]
        Assert.Equal(new[] { "Type", "Actor", "To", "Speed" }, json.Properties().Select(p => p.Name).ToArray());
        Assert.DoesNotContain("\r", PackWriter.Serialize(beat));
    }

    [Fact]
    public void WriteIfChangedLeavesIdenticalFilesAlone()
    {
        using var temp = new TempFolder();
        string file = Path.Combine(temp.Path, "a", "x.json");
        Assert.True(PackWriter.WriteIfChanged(file, "{}\n"));
        File.SetLastWriteTimeUtc(file, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.False(PackWriter.WriteIfChanged(file, "{}\n"));
        Assert.Equal(2020, File.GetLastWriteTimeUtc(file).Year);
        Assert.True(PackWriter.WriteIfChanged(file, "{ }\n"));
        Assert.Equal(new byte[] { (byte)'{', (byte)' ' }, File.ReadAllBytes(file)[..2]);   // UTF-8, no BOM
    }
}

public class HistoryTests
{
    [Fact]
    public void UndoRedoAndCapacity()
    {
        var history = new History("s0");
        history.Commit("one", "s1");
        history.Commit("same", "s1");       // unchanged: not a step
        history.Commit("two", "s2");
        Assert.Equal("two", history.UndoLabel);
        Assert.Equal("s1", history.Undo());
        Assert.Equal("s0", history.Undo());
        Assert.Null(history.Undo());
        Assert.Equal("s1", history.Redo());
        history.Commit("branch", "s3");     // a new edit drops the redo
        Assert.False(history.CanRedo);
        for (int i = 0; i < History.Capacity + 20; i++) history.Commit("n", "x" + i);
        int undos = 0;
        while (history.Undo() != null) undos++;
        Assert.Equal(History.Capacity, undos);
    }
}

public class DocumentTests
{
    [Fact]
    public void CreateEditUndoSaveAndReopen()
    {
        using var editor = new EditorHarness();
        var doc = editor.NewProject("Robin Things", "Player.RobinThings", "ScienceHouse", "Hello Robin");
        Assert.True(File.Exists(doc.ProjectPath));
        Assert.True(File.Exists(Path.Combine(doc.PackFolder, "manifest.json")));
        var scene = doc.Cutscenes.Single().Scene;
        Assert.Equal("hello-robin", scene.Id);
        Assert.Equal("Hello Robin", scene.Title);
        Assert.False(doc.IsDirty);

        doc.Edit("Rename", () => scene.Title = "Changed");
        Assert.True(doc.IsDirty);
        Assert.True(doc.Undo());
        Assert.Equal("Hello Robin", doc.Cutscenes.Single().Scene.Title);
        Assert.True(doc.Redo());
        Assert.Equal("Changed", doc.Cutscenes.Single().Scene.Title);
        doc.Save();
        Assert.False(doc.IsDirty);

        var reopened = PackDocument.Open(doc.ProjectPath);
        Assert.Equal("Changed", reopened.Cutscenes.Single().Scene.Title);
        Assert.Equal("Player.RobinThings", reopened.PackId);
        Assert.Empty(reopened.LoadIssues);
    }

    [Fact]
    public void DeletedCutsceneFileIsRemovedOnSave()
    {
        using var editor = new EditorHarness();
        var doc = editor.NewProject();
        editor.Ui.Prompts.Enqueue("Second scene");
        editor.Shell.AddCutsceneCommand.Execute(null);
        doc.Save();
        string second = Path.Combine(doc.PackFolder, "cutscenes", "second-scene.json");
        Assert.True(File.Exists(second));
        editor.Shell.CurrentCutsceneItem = editor.Shell.Cutscenes.First(c => c.Id == "second-scene");
        editor.Shell.DeleteCutsceneCommand.Execute(null);
        doc.Save();
        Assert.False(File.Exists(second));
        var studio = JObject.Parse(File.ReadAllText(Path.Combine(doc.PackFolder, "studio.json")));
        Assert.DoesNotContain("cutscenes/second-scene.json", studio["Cutscenes"]?.Values<string>() ?? Array.Empty<string>());
    }

    [Fact]
    public void AutosaveOffersRecoveryOnReopen()
    {
        using var editor = new EditorHarness();
        var doc = editor.NewProject();
        doc.Edit("Unsaved", () => doc.Cutscenes[0].Scene.Title = "Not saved yet");
        editor.Shell.AutosaveTick();
        var recovery = Autosave.FindRecovery(doc.ProjectPath);
        Assert.NotNull(recovery);

        using var second = new EditorHarness();
        second.Ui.ConfirmAnswer = true;    // "Restore"
        second.Shell.OpenProjectFile(doc.ProjectPath);
        Assert.Equal("Not saved yet", second.Shell.Scene!.Title);
        Assert.True(second.Shell.Document!.IsDirty);
    }
}
