using SDCharacterStudio.Core.Compiler;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Palette;
using SDCharacterStudio.Editor.Schema;
using SDCharacterStudio.Editor.Studio;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>
/// Roadmap M8 AC: the doc 07 §14 workflows through the editor's own actions only (clicks, fields, tools), never JSON.
/// §14.3 (the Robin rebuild) is <see cref="RobinRebuildTests"/>.
/// </summary>
public class StudioWorkflowTests
{
    /// <summary>§14.1 Robin in a raincoat, keeping her real hair.</summary>
    [Fact]
    public void RobinInARaincoatKeepingHerRealHair()
    {
        using var editor = new EditorHarness();
        var doc = editor.NewProject("Raincoat", "Player.Raincoat", "ScienceHouse");
        var shell = editor.Shell;
        // 1. Stage: Robin at (17, 6), facing left.
        shell.Stage.DropCharacter("Robin", 17, 6);
        var scene = shell.Scene!;
        var robin = scene.Actors.Single(a => a.Id == "Robin");
        shell.Select(new ActorSelection(scene.Id!, scene.Actors.IndexOf(robin)));
        editor.Choose("Facing", "left");
        Assert.Equal("left", robin.Facing);

        // 2. New pose for Robin, facing left, 16×32.
        shell.IsCharacterStudio = true;
        var studio = shell.CharacterStudio;
        studio.Character = studio.Characters.First(c => c.Key == "Robin");
        studio.Settings.AdvancedPoseTools = true;   // layers (D-48)
        editor.Ui.Prompts.Enqueue("raincoat");
        studio.NewCommand.Execute(LibraryKind.Pose);
        var pose = Assert.IsType<PoseDocumentViewModel>(studio.ActiveDocument);
        Assert.Equal(new[] { 16, 32 }, pose.Model.FrameSize);
        Assert.Equal("left", pose.Model.Facing);

        // 3. Isolate head: hidden full Robin, live head on the neck, empty Body below it.
        pose.IsolateHeadCommand.Execute(null);
        Assert.Collection(pose.Frame!.Layers,
            l => Assert.False(Assert.IsType<LiveLayer>(l).Visible),
            l => Assert.IsType<PaintLayer>(l),
            l => Assert.Equal("Head", Assert.IsType<LiveLayer>(l).Part));

        // 4. Draw the body with slot colours and a raincoat colour.
        studio.Palette.PickPrimaryCommand.Execute(studio.Palette.Slots.First(s => s.Name == "Skin mid"));
        pose.Tool = StudioTool.Rectangle; pose.FilledShapes = true;
        pose.PointerDown(5, 16, PointerButton.Primary); pose.PointerMove(10, 30); pose.PointerUp(10, 30);
        studio.Palette.Primary = 0xFF2E6FD8;   // raincoat blue
        pose.PointerDown(5, 20, PointerButton.Primary); pose.PointerMove(10, 24); pose.PointerUp(10, 24);

        // 5. Neck anchor on the frame, then a second frame for an idle bob.
        pose.Tool = StudioTool.Anchor; pose.AnchorName = "Neck";
        pose.PointerDown(8, 15, PointerButton.Primary);
        pose.AddFrameCommand.Execute(null);
        pose.Tool = StudioTool.Anchor;
        pose.PointerDown(8, 16, PointerButton.Primary);
        pose.FrameIndex = 0; pose.AddToLoopCommand.Execute(null);
        pose.FrameIndex = 1; pose.AddToLoopCommand.Execute(null);
        Assert.Equal(new[] { 8, 16 }, studio.Library!.Pose(pose.ItemId)!.Frames[1].Anchors["Neck"]);
        Assert.Null(pose.Problems);

        // 6. Fit check the head mask on every installed Robin.
        studio.OpenItem(LibraryKind.Mask, "robin-head-left");
        var mask = Assert.IsType<MaskDocumentViewModel>(studio.ActiveDocument);
        Assert.True(mask.Fit.Count >= 2);
        Assert.All(mask.Fit, v => Assert.NotNull(v.OverPose));   // drawn over the open raincoat pose

        // 7. A portrait: Isolate head on the live portrait's neutral face.
        editor.Ui.Prompts.Enqueue("raincoat");
        studio.NewCommand.Execute(LibraryKind.Portrait);
        var portrait = Assert.IsType<PortraitDocumentViewModel>(studio.ActiveDocument);
        portrait.IsolateHeadCommand.Execute(null);

        // 8. Storyboard: Robin plays the pose (from the asset browser), then speaks with the composed portrait.
        shell.IsCharacterStudio = false;
        shell.Assets.SelectedCategory = shell.Assets.Categories.Single(c => c.Id == "poses");
        shell.Select(new SceneSelection(scene.Id!));
        shell.Assets.Items.Single(i => i.Key == pose.ItemId).UseCommand.Execute(null);
        var poseBeat = scene.Beats.OfType<PoseBeat>().Single();
        editor.Choose("Who", "Robin");
        Assert.Equal("Robin", poseBeat.Actor);
        editor.Choose("Play", "loop");
        shell.Storyboard.Insert(LaneNames.Main, scene.Beats.Count, BeatCatalog.Kind("Say"));
        var say = scene.Beats.OfType<SayBeat>().Single();
        editor.Choose("Speaker", "Robin");
        editor.Field<TranslatableTextField>("Line").Text = "Do you like it?";
        editor.Choose("Composed portrait", portrait.ItemId);
        Assert.Equal(portrait.ItemId, say.Portrait);

        // 9. Ready to preview: nothing blocks it.
        var result = shell.ValidateNow();
        Assert.True(result.Errors == 0, string.Join("\n", result.Problems.Select(p => p.Message)));
        var compiled = new EventCompiler().Compile(doc.PackId, scene, doc.Library);
        Assert.DoesNotContain(compiled.Issues, i => i.Severity == Core.Validation.ValidationSeverity.Error);
    }

    /// <summary>§14.2 The player loses their hat, then changes shirts.</summary>
    [Fact]
    public void ThePlayerLosesTheirHatThenChangesShirts()
    {
        using var editor = new EditorHarness();
        var doc = editor.NewProject("Hat", "Player.HatLoss", "Town");
        var shell = editor.Shell;
        var scene = shell.Scene!;
        Assert.Contains(scene.Actors, a => a.Id == "farmer");   // 1. the player is on stage

        // 2. A clip with Parts keys: Hat original at 0, Hat none at 1200, Shirt (S)1010 at 2400.
        var studio = shell.CharacterStudio;
        Assert.NotNull(studio.NewSceneClip("hat-loss"));
        var clip = Assert.IsType<AnimationDocumentViewModel>(studio.ActiveDocument);
        clip.SetDuration(3000);
        var parts = clip.Rows.Single(r => r.Owner.Id == "farmer" && r.Track == TrackKind.Parts);
        void SetPart(int at, string part, string value)
        {
            clip.AddKey(parts, at);
            var list = clip.Fields.OfType<ListField>().Single(f => f.Label == "Change clothes or look");
            var row = list.Rows.Single();
            var which = (ChoiceField)row.Cells[0];
            which.Selected = which.Options.Single(o => o.Value == part);
            row = clip.Fields.OfType<ListField>().Single(f => f.Label == "Change clothes or look").Rows.Single();
            var choice = (ChoiceField)row.Cells[1];
            if (choice.Options.FirstOrDefault(o => o.Value == value) is { } option) choice.Selected = option; else choice.Text = value;
        }
        SetPart(0, "Hat", "original");
        SetPart(1200, "Hat", "none");
        SetPart(2400, "Shirt", "(S)1010");
        var keys = scene.Clips["hat-loss"].Tracks.Single(t => t.Actor == "farmer").Parts!;
        Assert.Equal(new[] { 0, 1200, 2400 }, keys.Select(k => k.At));
        Assert.Equal("original", keys[0].Set!["Hat"].ToString());
        Assert.Equal("none", keys[1].Set!["Hat"].ToString());
        Assert.Equal("(S)1010", keys[2].Set!["Shirt"].ToString());

        // The clip plays from a Clip beat; 3. a Costume beat afterwards keeps the shirt for the rest of the scene.
        shell.IsCharacterStudio = false;
        shell.Storyboard.Insert(LaneNames.Main, 0, BeatCatalog.Kind("Clip"));
        editor.Choose("Clip", "hat-loss");
        Assert.Equal("hat-loss", scene.Beats.OfType<ClipBeat>().Single().Clip);
        shell.Storyboard.Insert(LaneNames.Main, 1, BeatCatalog.Kind("Costume"));
        editor.Choose("Who", "farmer");
        var costume = scene.Beats.OfType<CostumeBeat>().Single();
        Assert.NotNull(costume.Set);

        // 4. Ready to preview; the runtime restores the player's look afterwards (verified in M4).
        var result = shell.ValidateNow();
        Assert.True(result.Errors == 0, string.Join("\n", result.Problems.Select(p => p.Message)));
        Assert.DoesNotContain(new EventCompiler().Compile(doc.PackId, scene, doc.Library).Issues, i => i.Severity == Core.Validation.ValidationSeverity.Error);
    }
}
