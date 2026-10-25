using System.IO;
using SDCharacterStudio.Core.Compiler;
using SDCharacterStudio.Core.Loading;
using SDCharacterStudio.Core.Migration;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Palette;
using SDCharacterStudio.Editor.Studio;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>M8 animations (doc 07 §7.2, §15), My library (doc 03 §17.3) and copies for other characters (§17.2).</summary>
public class AnimationStudioTests
{
    private static AnimationDocumentViewModel NewAnimation(CharacterStudioViewModel studio, string name = "step and bow")
    {
        studio.Settings.AdvancedPoseTools = true;   // these test the timeline, which is under Advanced (D-48)
        Assert.NotNull(studio.NewAnimation(name));
        return Assert.IsType<AnimationDocumentViewModel>(studio.ActiveDocument);
    }

    private static TimelineRow Row(AnimationDocumentViewModel clip, TrackKind track, string? owner = null) =>
        clip.Rows.First(r => r.Track == track && (owner == null || r.Owner.Id == owner));

    /// <summary>Drag a role's feet onto a tile of the preview stage.</summary>
    private static void DragTo(AnimationDocumentViewModel clip, string owner, int tileX, int tileY)
    {
        clip.BeginDrag(owner);
        clip.EndDrag((tileX - clip.StageLeft) * 16 + 8, (tileY - clip.StageTop) * 16 + 15, pixels: false);
    }

    [Fact]
    public void AnAnimationGetsKeysByRecordingAndOnTheTimeline()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        CharacterStudioTests.NewPose(studio, "bow");
        var clip = NewAnimation(studio);
        Assert.Equal("step-and-bow", clip.ItemId);
        var role = Assert.Single(clip.Owners);
        Assert.Equal("robin", role.Id);
        Assert.Contains(clip.Rows, r => r.Track == TrackKind.Expression);
        Assert.DoesNotContain(clip.Rows, r => r.Track == TrackKind.Camera);   // library clips don't move the camera

        // Record: drag one tile left at 600 ms.
        clip.IsRecording = true;
        clip.Playhead = 600;
        DragTo(clip, "robin", 3, 3);
        var position = clip.LibraryModel!.Tracks.Single().Position!;
        Assert.Equal(new[] { 0, 600 }, position.Select(k => k.At));
        Assert.Equal(new[] { -1, 0 }, position[1].Tile);
        Assert.Equal("Record position", editor.Shell.Document!.History.UndoLabel);

        // A pose key starts with the character's pose; move it; change the length.
        clip.AddKey(Row(clip, TrackKind.Pose), 600);
        var pose = clip.LibraryModel.Tracks.Single().Pose!.Single();
        Assert.Equal("robin-bow", pose.Pose);
        Assert.Same(pose, clip.SelectedKey!.Key);
        Assert.Contains(clip.Fields.OfType<ChoiceField>(), f => f.Label == "Pose");
        clip.MoveKey(clip.SelectedKey, 800);
        Assert.Equal(800, studio.Library!.Clip("step-and-bow")!.Tracks.Single().Pose!.Single().At);
        clip.SetDuration(3200);
        Assert.Equal(3200, studio.Library.Clip("step-and-bow")!.Duration);

        // The stage shows the pose after its key, and the role a tile left.
        clip.Playhead = 1000;
        var actor = Assert.Single(clip.StageActors);
        Assert.NotNull(actor.Image);
        clip.Playhead = 0;
        double startX = clip.StageActors.Single().FeetX;
        clip.Playhead = 1000;
        Assert.Equal(startX - 16, clip.StageActors.Single().FeetX);
        // Mirror preview walks the other way.
        clip.PreviewMirror = true;
        Assert.Equal(startX + 16, clip.StageActors.Single().FeetX);
        clip.PreviewMirror = false;

        clip.SelectedKey = clip.Rows.SelectMany(r => r.Keys).First(k => k.Key is PoseKey);
        clip.DeleteKey();
        Assert.Null(studio.Library.Clip("step-and-bow")!.Tracks.Single().Pose);
        Assert.Equal(0, editor.Shell.ValidateNow().Errors);
    }

    [Fact]
    public void PartsKeysChangeThePlayersLook()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio("farmer");
        using var _ = editor;
        var clip = NewAnimation(studio, "lose the hat");
        Assert.Equal("Farmer", clip.Owners.Single().Kind);
        clip.AddKey(Row(clip, TrackKind.Parts), 1200);
        var key = Assert.IsType<PartsKey>(clip.SelectedKey!.Key);
        Assert.Equal("none", key.Set!["Hat"].ToString());
        var look = clip.Fields.OfType<ListField>().Single(f => f.Label == "Change clothes or look");
        look.AddCommand.Execute(null);
        Assert.Equal(2, studio.Library!.Clip(clip.ItemId)!.Tracks.Single().Parts!.Single().Set!.Count);
        var arms = clip.Fields.OfType<ChoiceField>().Single(f => f.Label == "Arms");
        arms.Selected = arms.Options.Single(o => o.Value == "hide");
        Assert.False(studio.Library.Clip(clip.ItemId)!.Tracks.Single().Parts!.Single().Visible!["Arms"]);
    }

    [Fact]
    public void ASceneClipIsEditedHereAndSavedToTheLibrary()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        editor.Shell.Stage.DropCharacter("Robin", 12, 10);
        var scene = editor.Shell.Scene!;
        string clipId = studio.NewSceneClip("hop")!;
        var clip = Assert.IsType<AnimationDocumentViewModel>(studio.ActiveDocument);
        Assert.False(clip.IsLibrary);
        Assert.Contains(clip.Rows, r => r.Track == TrackKind.Camera);
        clip.IsRecording = true;
        clip.Playhead = 500;
        DragTo(clip, "Robin", 14, 10);
        var keys = scene.Clips[clipId].Tracks.Single(t => t.Actor == "Robin").Position!;
        Assert.Equal(new[] { 14, 10 }, keys.Single().Tile);   // scenes use absolute tiles
        editor.Ui.Prompts.Enqueue("Hop over");
        clip.SelectedKey = null;   // the clip's own properties
        clip.Fields.OfType<ActionField>().Single(f => f.ButtonText.StartsWith("Save to library")).Action.Execute(null);
        var saved = studio.Library!.Clip("hop-over")!;
        Assert.Equal("Robin", saved.Roles.Single().Id, ignoreCase: true);
        Assert.Equal(new[] { 2, 0 }, saved.Tracks.Single().Position!.Last().Tile);   // relative to where Robin stood
        Assert.IsType<AnimationDocumentViewModel>(studio.ActiveDocument);
        Assert.True(((AnimationDocumentViewModel)studio.ActiveDocument!).IsLibrary);
    }

    [Fact]
    public void UsingAnAnimationInASceneCastsItOnTheSelectedActor()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        editor.Shell.Stage.DropCharacter("Robin", 12, 10);
        editor.Shell.Stage.DropCharacter("Sophia", 16, 10);
        var clip = NewAnimation(studio, "wave");
        var scene = editor.Shell.Scene!;
        editor.Shell.Select(new ActorSelection(scene.Id!, scene.Actors.FindIndex(a => a.Id == "Sophia")));
        Assert.True(studio.UseClipInScene(clip.ItemId, mirror: true));
        var beat = scene.Beats.OfType<ClipBeat>().Single();
        Assert.Equal("wave", beat.Clip);
        Assert.Equal("Sophia", beat.Cast!["robin"]);
        Assert.True(beat.Mirror);
        Assert.False(editor.Shell.IsCharacterStudio);
        Assert.Equal(0, editor.Shell.ValidateNow().Errors);
    }

    [Fact]
    public void TheAssetBrowserAndTheClipBeatUseAnimations()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        CharacterStudioTests.NewPose(studio, "bow");
        var clip = NewAnimation(studio, "bow");
        clip.AddKey(Row(clip, TrackKind.Pose), 0);
        editor.Shell.IsCharacterStudio = false;
        editor.Shell.Stage.DropCharacter("Robin", 12, 10);
        editor.Shell.Stage.DropCharacter("Sophia", 16, 10);
        var scene = editor.Shell.Scene!;

        // My animations in the asset browser: Use casts onto the selected actor.
        var assets = editor.Shell.Assets;
        assets.SelectedCategory = assets.Categories.Single(c => c.Id == "animations");
        var item = Assert.Single(assets.Items);
        Assert.Equal(AssetKind.Animation, item.Kind);
        editor.Shell.Select(new ActorSelection(scene.Id!, scene.Actors.FindIndex(a => a.Id == "Robin")));
        item.UseCommand.Execute(null);
        var beat = scene.Beats.OfType<ClipBeat>().Single();
        Assert.Equal("Robin", beat.Cast!["robin"]);

        // The beat's properties: recast, mirror, swap the pose.
        editor.Shell.Select(new BeatSelection(scene.Id!, LaneNames.Main, scene.Beats.IndexOf(beat)));
        editor.Choose("robin", "Sophia");
        Assert.Equal("Sophia", beat.Cast["robin"]);
        editor.Field<BoolField>("Mirror").Value = true;
        Assert.True(beat.Mirror);
        studio.CopyPoseFor("robin-bow", studio.Characters.First(c => c.Key == "Sophia"));
        editor.Shell.IsCharacterStudio = false;
        editor.Shell.Select(new BeatSelection(scene.Id!, LaneNames.Main, scene.Beats.IndexOf(beat)));
        editor.Choose("robin-bow", "sophia-bow");
        Assert.Equal("sophia-bow", beat.PoseMap!["robin-bow"]);
        Assert.Equal(0, editor.Shell.ValidateNow().Errors);
        Assert.Contains(editor.Shell.Inspector.Fields.OfType<ActionField>(), f => f.ButtonText.Contains("Character Studio"));
    }

    [Fact]
    public void ACopyForAnotherCharacterSwapsSourcesAndSaysWhatIsMissing()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio, "bow", layered: true);
        pose.IsolateHead();
        studio.Palette.Primary = ColorSlots.SkinMid;
        pose.PointerDown(5, 20, PointerButton.Primary); pose.PointerUp(5, 20);
        // Give the body layer a mask that only Robin has.
        var masks = studio.Library!.Masks();
        string? body = null;
        studio.Edit("Mask", lib =>
        {
            lib.WriteImage("masks/robin-body-upper-left.png", PixelTools.AboveRow(16, 32, 24));
            lib.SaveMask(new Mask { Id = "robin-body-upper-left", Image = "masks/robin-body-upper-left.png", FrameSize = new[] { 16, 32 } });
            var p = lib.Pose("robin-bow")!;
            p.Frames[0].Layers.OfType<LiveLayer>().First(l => l.Part == "Full").Mask = "robin-body-upper-left";
            body = p.Frames[0].Layers.OfType<PaintLayer>().Single().Image;
            lib.SavePose(p);
        });
        var sophia = studio.Characters.First(c => c.Key == "Sophia");
        var plan = PoseCopies.Plan(studio.Library, editor.Shell.Catalog, "robin-bow", "Sophia");
        Assert.Equal("sophia-bow", plan.NewId);
        Assert.Equal(("robin-body-upper-left", "sophia-body-upper-left"), Assert.Single(plan.MissingMasks));

        editor.Ui.AskAnswer = false;   // "Don't make the copy"
        Assert.Null(studio.CopyPoseFor("robin-bow", sophia));
        Assert.Null(studio.Library.Pose("sophia-bow"));

        editor.Ui.AskAnswer = true;    // copy the missing mask as a start
        Assert.Equal("sophia-bow", studio.CopyPoseFor("robin-bow", sophia));
        var copy = studio.Library.Pose("sophia-bow")!;
        Assert.Equal("Sophia", studio.Library.Rig(copy.For)!.Npc);
        var layers = copy.Frames[0].Layers;
        Assert.All(layers.OfType<LiveLayer>(), l => Assert.Equal("Sophia", l.Source!.Npc));
        Assert.Equal("sophia-body-upper-left", layers.OfType<LiveLayer>().First(l => l.Part == "Full").Mask);
        Assert.NotNull(studio.Library.Mask("sophia-body-upper-left"));
        var paint = layers.OfType<PaintLayer>().Single();
        Assert.NotEqual(body, paint.Image);   // the copy owns its pixels
        Assert.StartsWith(copy.For + ".", paint.ColorSlots!["Skin"]);
        Assert.Equal(0, editor.Shell.ValidateNow().Errors);
    }

    /// <summary>
    /// D-39 acceptance: an animation using a custom pose goes to My library from one project, is used in a different project,
    /// cast on another character at another spot and mirrored, and both packs work without each other or the library.
    /// </summary>
    [Fact]
    public void AnAnimationTravelsToAnotherProjectThroughMyLibrary()
    {
        using var shelf = new TempFolder();
        var library = new PersonalLibrary(Path.Combine(shelf.Path, "Library"));

        // Project A: Robin steps and bows.
        var (editorA, studioA) = CharacterStudioTests.OpenStudio();
        studioA.Personal = library;
        var pose = CharacterStudioTests.NewPose(studioA, "bow");
        pose.IsolateHead();
        studioA.Palette.Primary = ColorSlots.SkinMid;
        pose.PointerDown(4, 18, PointerButton.Primary); pose.PointerMove(11, 18); pose.PointerUp(11, 18);
        var clip = NewAnimation(studioA);
        clip.IsRecording = true; clip.Playhead = 600;
        DragTo(clip, "robin", 3, 3);
        clip.AddKey(Row(clip, TrackKind.Pose), 600);
        var entry = studioA.SaveToMyLibrary(LibraryKind.Clip, "step-and-bow")!;
        Assert.True(File.Exists(Path.Combine(entry.Folder, "library", "clips", "step-and-bow.json")));
        Assert.True(File.Exists(Path.Combine(entry.Folder, "library", "poses", "robin-bow.json")));
        Assert.True(File.Exists(Path.Combine(entry.Folder, "thumbnail.png")));
        string packA = editorA.Shell.Document!.PackFolder;
        editorA.Shell.Document.Save();
        editorA.Dispose();

        // Project B: Sophia plays it, somewhere else, mirrored, with her own copy of the pose.
        var (editorB, studioB) = CharacterStudioTests.OpenStudio("Sophia");
        using var __ = editorB;
        studioB.Personal = library;
        studioB.ShowMyLibrary = true;
        var item = Assert.Single(studioB.MyAnimations);
        studioB.UseFromMyLibrary(item);
        var docB = editorB.Shell.Document!;
        Assert.NotNull(studioB.Library!.Clip("step-and-bow"));
        Assert.NotNull(studioB.Library.Pose("robin-bow"));
        Assert.Single(docB.Project.LibraryLinks);
        Assert.True(studioB.MyAnimations.Single().InThisPack);

        string? sophiaPose = studioB.CopyPoseFor("robin-bow", studioB.Characters.First(c => c.Key == "Sophia"));
        Assert.Equal("sophia-bow", sophiaPose);
        editorB.Shell.Stage.DropCharacter("Sophia", 20, 7);
        var scene = editorB.Shell.Scene!;
        editorB.Shell.Select(new ActorSelection(scene.Id!, scene.Actors.FindIndex(a => a.Id == "Sophia")));
        Assert.True(studioB.UseClipInScene("step-and-bow", mirror: true, poseMap: new Dictionary<string, string> { ["robin-bow"] = "sophia-bow" }));
        Assert.Equal(0, editorB.Shell.ValidateNow().Errors);
        docB.Save();

        // Neither the library nor project A is needed any more.
        Directory.Delete(library.Root, recursive: true);
        Assert.False(Directory.Exists(packA));   // the first project's folder went with its harness
        var read = new PackReader(new DiskPackFileSystem(docB.PackFolder), new PackMigrator()).Read(exportValidation: true);
        Assert.DoesNotContain(read.Issues, i => i.Severity == Core.Validation.ValidationSeverity.Error);
        var played = read.Pack.Cutscenes.Single().Value;
        var expanded = LibraryClips.Expand(played, read.Pack);
        var generated = Assert.Single(expanded.Clips);
        var track = generated.Value.Tracks.Single();
        Assert.Equal("Sophia", track.Actor);
        Assert.Equal(new[] { 1, 0 }, track.Position!.Last().Tile);   // mirrored: a tile right instead of left
        Assert.True(track.Position!.Last().Relative);
        Assert.Equal("sophia-bow", track.Pose!.Single().Pose);
        var compiled = new EventCompiler().Compile(docB.PackId, played, read.Pack);
        Assert.DoesNotContain(compiled.Issues, i => i.Severity == Core.Validation.ValidationSeverity.Error);
    }

    [Fact]
    public void UsingAgainReusesUpdatesOrRenames()
    {
        using var shelf = new TempFolder();
        var library = new PersonalLibrary(Path.Combine(shelf.Path, "Library"));
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        studio.Personal = library;
        CharacterStudioTests.NewPose(studio, "bow");
        var clip = NewAnimation(studio, "bow");
        clip.AddKey(Row(clip, TrackKind.Pose), 0);
        studio.SaveToMyLibrary(LibraryKind.Clip, "bow");
        studio.ShowMyLibrary = true;

        // Same pack, same content: Add a copy finds identical items and reuses them.
        editor.Ui.AskAnswer = false;
        studio.UseFromMyLibrary(studio.MyAnimations.Single());
        Assert.Single(studio.Library!.Items(LibraryKind.Clip));
        Assert.Single(studio.Library.Items(LibraryKind.Pose));

        // The pack's copy changes; another copy gets a new name.
        studio.Edit("Longer", lib => { var c = lib.Clip("bow")!; c.Duration = 5000; lib.SaveClip(c); });
        studio.ShowMyLibrary = true;
        studio.UseFromMyLibrary(studio.MyAnimations.Single());
        Assert.NotNull(studio.Library.Clip("bow-2"));
        Assert.Contains(editor.Ui.Toasts, t => t.Title == "Renamed in this pack");

        // Update: the linked copy is overwritten in place.
        editor.Ui.AskAnswer = true;
        studio.ShowMyLibrary = true;
        studio.UseFromMyLibrary(studio.MyAnimations.Single());
        Assert.Equal(2000, studio.Library.Clip("bow-2")!.Duration);
        Assert.Equal(5000, studio.Library.Clip("bow")!.Duration);
        Assert.Single(editor.Shell.Document!.Project.LibraryLinks);
    }
}
