using System.IO;
using SDCharacterStudio.Core.Compiler;
using SDCharacterStudio.Core.Loading;
using SDCharacterStudio.Core.Migration;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Preview;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>Preview in game from the Character Studio (doc 07 §15): a temporary scene that compiles like any other.</summary>
public class StudioPreviewTests
{
    private static void AssertCompiles(EditorHarness editor, Cutscene scene)
    {
        var doc = editor.Shell.Document!;
        var compiled = new EventCompiler().Compile(doc.PackId, scene, doc.Library);
        Assert.DoesNotContain(compiled.Issues, i => i.Severity == Core.Validation.ValidationSeverity.Error);
    }

    [Fact]
    public void PosesAndAnimationsPreviewInATemporaryScene()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio, "bow");
        var scene = studio.PreviewScene(pose, out var why);
        Assert.Null(why);
        Assert.Equal(CharacterStudioViewModel.PreviewSceneId, scene!.Id);
        Assert.Equal(editor.Shell.Scene!.Location, scene.Location);
        Assert.Contains(scene.Actors, a => a.Npc == "Robin");
        Assert.Equal("robin-bow", scene.Beats.OfType<PoseBeat>().First().Pose);
        AssertCompiles(editor, scene);

        Assert.NotNull(studio.NewAnimation("bow"));
        var clip = (AnimationDocumentViewModel)studio.ActiveDocument!;
        clip.PreviewMirror = true;
        scene = studio.PreviewScene(clip, out why)!;
        var beat = scene.Beats.OfType<ClipBeat>().Single();
        Assert.Equal("Robin", beat.Cast!["robin"]);
        Assert.True(beat.Mirror);
        AssertCompiles(editor, scene);

        // Masks and rigs preview through the poses that use them.
        studio.MakeRig();
        Assert.Null(studio.PreviewScene(studio.ActiveDocument, out why));
        Assert.NotNull(why);
    }

    [Fact]
    public void ThePreviewPackCarriesTheTemporaryScene()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio, "bow");
        var scene = studio.PreviewScene(pose, out string? _)!;
        using var temp = new TempFolder();
        using var service = new PreviewService(new BridgeClient("SDCS.StudioPreviewTests." + Guid.NewGuid().ToString("N")), temp.Path);
        var (folder, packId) = service.PrepareTemporaryPreview(editor.Shell.Document!, scene);
        Assert.EndsWith(PreviewService.PreviewSuffix, packId);
        Assert.True(File.Exists(Path.Combine(folder, "cutscenes", CharacterStudioViewModel.PreviewSceneId + ".json")));
        Assert.True(File.Exists(Path.Combine(folder, "library", "poses", "robin-bow.json")));   // unsaved edits included
        var read = new PackReader(new DiskPackFileSystem(folder), new PackMigrator()).Read(exportValidation: true);
        Assert.Contains(read.Pack.Cutscenes, c => c.Value.Id == CharacterStudioViewModel.PreviewSceneId);
        Assert.DoesNotContain(read.Issues, i => i.Severity == Core.Validation.ValidationSeverity.Error);
    }
}
