using System.IO;
using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Palette;
using SDCharacterStudio.Editor.Project;
using SDCharacterStudio.Editor.Studio;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>M8 foundations: the asset store, pack library edits and composing poses like the runtime.</summary>
public class StudioFoundationTests
{
    private static Pose SimplePose(string id) => new()
    {
        Format = "1.0", Id = id, FrameSize = new[] { 16, 32 }, Facing = "left",
        Frames = { new PoseFrame { Duration = 100, Layers = { new PaintLayer { Image = $"art/poses/{id}/paint-0.png" } } } }
    };

    [Fact]
    public void LibraryEditsAreUndoableSavedAndReopened()
    {
        using var editor = new EditorHarness();
        var doc = editor.NewProject();
        var library = new PackLibrary(doc);
        var paint = StudioImages.Blank(16, 32);
        paint.Pixels[5 * 16 + 3] = ColorSlots.SkinMid;
        doc.Edit("New pose", () => { library.SavePose(SimplePose("wave")); library.WriteImage("art/poses/wave/paint-0.png", paint); });
        Assert.Equal("wave", Assert.Single(doc.Library.Poses).Value.Id);
        Assert.True(doc.Undo());
        Assert.Empty(doc.Library.Poses);
        Assert.True(doc.Redo());
        Assert.Single(doc.Library.Poses);

        Assert.Equal(0, editor.Shell.ValidateNow().Errors);
        doc.Save();
        Assert.True(File.Exists(Path.Combine(doc.PackFolder, "library", "poses", "wave.json")));
        var saved = StudioImages.Decode(File.ReadAllBytes(Path.Combine(doc.PackFolder, "art", "poses", "wave", "paint-0.png")));
        Assert.Equal(paint.Pixels, saved.Pixels);   // exact, slot colour included

        var reopened = PackDocument.Open(doc.ProjectPath);
        Assert.Equal("wave", Assert.Single(reopened.Library.Poses).Value.Id);
        Assert.Empty(reopened.LoadIssues);
    }

    [Fact]
    public void UnsavedImagesReachValidation()
    {
        using var editor = new EditorHarness();
        var doc = editor.NewProject();
        var library = new PackLibrary(doc);
        doc.Edit("New pose", () => { library.SavePose(SimplePose("wave")); library.WriteImage("art/poses/wave/paint-0.png", StudioImages.Blank(8, 8)); });
        var problem = Assert.Single(editor.Shell.ValidateNow().Problems, p => p.IsError);
        Assert.Equal("V11", problem.Rule);   // the paint image's size doesn't match the pose
        doc.Edit("Fix size", () => library.WriteImage("art/poses/wave/paint-0.png", StudioImages.Blank(16, 32)));
        Assert.Equal(0, editor.Shell.ValidateNow().Errors);
    }

    [Fact]
    public void ExplicitStudioIndexGetsNewFilesListed()
    {
        using var editor = new EditorHarness();
        var doc = editor.NewProject();
        doc.Edit("Explicit index", () => doc.Studio.Library = new LibraryIndex { Poses = new() { "library/poses/existing.json" } });
        doc.Edit("New pose", () => new PackLibrary(doc).SavePose(SimplePose("wave")));
        Assert.Contains("library/poses/wave.json", doc.Studio.Library!.Poses!);
        Assert.Single(doc.Library.Poses);
    }

    [Fact]
    public void MissingArtShowsAPlaceholderAndSaysWhy()
    {
        using var editor = new EditorHarness();
        editor.NewProject();
        var context = new StudioContext(() => editor.Shell.Document, () => editor.Shell.Catalog);
        var pose = new Pose
        {
            Format = "1.0", Id = "x", FrameSize = new[] { 16, 32 }, For = "nobody",
            Frames = { new PoseFrame { Duration = 100, Layers = { new LiveLayer { Source = new LiveSource { Kind = "Npc", Npc = "Robin" }, Part = "Head", AnchorTo = new LayerAnchor { Anchor = "Neck", From = "rig" } } } } }
        };
        var result = context.RenderFrame(pose, 0);
        Assert.Contains(result.Problems, p => p.Contains("No rig for 'Robin'"));
        Assert.Contains(result.Problems, p => p.Contains("no 'Neck' anchor"));
        Assert.False(StudioImages.IsEmpty(result.Image));
    }

    [Fact]
    public void LibraryClipsValidateAndCompileInTheEditor()
    {
        using var editor = new EditorHarness();
        var doc = editor.NewProject();
        editor.Shell.Stage.DropCharacter("Robin", 12, 10);
        doc.Edit("Library clip", () => new PackLibrary(doc).SaveClip(new LibraryClip
        {
            Id = "hop", Duration = 500, Roles = { new ClipRole { Id = "hopper", Kind = "Npc" } },
            Tracks = { new ClipTrack { Role = "hopper", Position = new() { new PositionKey { At = 0, Tile = new[] { 0, 0 } }, new PositionKey { At = 500, Tile = new[] { 1, 0 } } } } }
        }));
        doc.Edit("Use it", () => doc.Cutscenes[0].Scene.Beats.Add(new ClipBeat { Clip = "hop", Cast = new() { ["hopper"] = "Robin" } }));
        var result = editor.Shell.ValidateNow();
        Assert.Equal(0, result.Errors);
        doc.Edit("Wrong cast", () => ((ClipBeat)doc.Cutscenes[0].Scene.Beats[0]).Cast = new() { ["hopper"] = "farmer" });
        Assert.Contains(editor.Shell.ValidateNow().Problems, p => p.IsError && p.Message.Contains("needs a Npc actor"));
    }
}
