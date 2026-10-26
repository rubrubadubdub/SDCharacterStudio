using System.IO;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Palette;
using SDCharacterStudio.Editor.Studio;
using SDCharacterStudio.Editor.ViewModels;
using SDCharacterStudio.Runtime.Rendering;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>M8 Character Studio: the pose editor's frames, layers, pixel tools and helpers (doc 07 §3.2–§6.5, §11).</summary>
public class CharacterStudioTests
{
    internal static (EditorHarness Editor, CharacterStudioViewModel Studio) OpenStudio(string character = "Robin")
    {
        var editor = new EditorHarness();
        editor.NewProject();
        editor.Shell.IsCharacterStudio = true;
        var studio = editor.Shell.CharacterStudio;
        // The simple editor and default tool options, whatever another test running alongside saved to the shared studio settings.
        var defaults = new StudioSettings();
        studio.Settings.AdvancedPoseTools = false;
        studio.Settings.BrushSize = defaults.BrushSize; studio.Settings.PixelPerfect = defaults.PixelPerfect; studio.Settings.Symmetry = defaults.Symmetry;
        studio.Settings.FilledShapes = defaults.FilledShapes; studio.Settings.OnionBefore = defaults.OnionBefore; studio.Settings.OnionAfter = defaults.OnionAfter;
        studio.Settings.OnionTint = defaults.OnionTint; studio.Settings.OnionWrap = defaults.OnionWrap; studio.Settings.PaintInside = defaults.PaintInside;
        studio.Character = studio.Characters.First(c => c.Key == character);
        return (editor, studio);
    }

    /// <param name="layered">Make it with Advanced tools on: layers instead of one flat picture (D-48).</param>
    internal static PoseDocumentViewModel NewPose(CharacterStudioViewModel studio, string name = "bow", string kind = "character", bool layered = false)
    {
        if (layered) studio.Settings.AdvancedPoseTools = true;
        Assert.NotNull(studio.NewPose(kind, name));
        return Assert.IsType<PoseDocumentViewModel>(studio.ActiveDocument);
    }

    [Fact]
    public void ANewPoseStartsFromTheLiveStandingFrameWithARig()
    {
        var (editor, studio) = OpenStudio();
        using var _ = editor;
        var pose = NewPose(studio);
        Assert.Equal("robin-bow", pose.Model.Id);
        Assert.Equal("left", pose.Model.Facing);
        Assert.True(pose.IsFlat);   // one flat picture that still follows her sprite (D-48)
        var live = Assert.IsType<LiveLayer>(pose.Frame!.Layers[0]);
        Assert.Equal("Robin", live.Source!.Npc);
        Assert.NotNull(live.Map);
        Assert.IsType<PaintLayer>(pose.Frame.Layers[1]);
        Assert.True(pose.Frame.Anchors.ContainsKey("Neck"));
        var rig = studio.Library!.Rig(pose.Model.For);
        Assert.Equal("Robin", rig!.Npc);
        Assert.Equal(4, rig.HeadMask.Count);
        Assert.NotNull(pose.Composite);
        Assert.Null(pose.Problems);
        Assert.Contains(studio.Sections.First(s => s.Kind == LibraryKind.Pose).Items, i => i.Id == "robin-bow");
        Assert.Equal(0, editor.Shell.ValidateNow().Errors);
    }

    [Fact]
    public void AStrokeIsOneUndoStepAndKeepsSlotColoursExact()
    {
        var (editor, studio) = OpenStudio();
        using var _ = editor;
        var pose = NewPose(studio);
        pose.AddPaintLayerCommand.Execute(null);
        var paint = Assert.IsType<PaintLayer>(pose.SelectedLayer);
        studio.Palette.Primary = ColorSlots.SkinMid;
        pose.Tool = StudioTool.Pencil;
        pose.PointerDown(3, 5, PointerButton.Primary);
        pose.PointerMove(6, 5);
        pose.PointerUp(6, 5);
        Assert.Equal("Draw", editor.Shell.Document!.History.UndoLabel);
        var image = studio.Library!.ReadImage(paint.Image)!;
        for (int x = 3; x <= 6; x++) Assert.Equal(ColorSlots.SkinMid, image.Pixels[5 * image.Width + x]);
        Assert.Empty(studio.Context.Overrides);

        editor.Shell.Document.Undo();
        image = studio.Library.ReadImage(paint.Image)!;
        Assert.True(StudioImages.IsEmpty(image));
        Assert.IsType<PaintLayer>(pose.SelectedLayer);   // adding the layer is its own step

        // Fill, line and the eraser.
        pose.Tool = StudioTool.Rectangle; pose.FilledShapes = true;
        pose.PointerDown(2, 2, PointerButton.Primary); pose.PointerMove(4, 4); pose.PointerUp(4, 4);
        pose.Tool = StudioTool.Eraser;
        pose.PointerDown(3, 3, PointerButton.Primary); pose.PointerUp(3, 3);
        image = studio.Library.ReadImage(paint.Image)!;
        Assert.Equal(8, image.Pixels.Count(p => p == ColorSlots.SkinMid));
        Assert.Equal(0u, image.Pixels[3 * image.Width + 3]);
    }

    [Fact]
    public void DrawingOnALiveLayerAddsAPaintLayerAboveIt()
    {
        var (editor, studio) = OpenStudio();
        using var _ = editor;
        var pose = NewPose(studio);
        pose.LayerIndex = 0;
        pose.Tool = StudioTool.Pencil;   // new poses open with Select (D-48)
        pose.PointerDown(1, 1, PointerButton.Primary);
        pose.PointerUp(1, 1);
        Assert.Equal(2, pose.Frame!.Layers.Count);
        Assert.IsType<PaintLayer>(pose.Frame.Layers[1]);
        Assert.Equal(1, pose.LayerIndex);
    }

    [Fact]
    public void IsolateHeadPutsABodyPaintLayerUnderTheLiveHead()
    {
        var (editor, studio) = OpenStudio();
        using var _ = editor;
        var pose = NewPose(studio, layered: true);
        pose.IsolateHead();
        var layers = pose.Frame!.Layers;
        Assert.Equal(3, layers.Count);
        Assert.False(Assert.IsType<LiveLayer>(layers[0]).Visible);
        var body = Assert.IsType<PaintLayer>(layers[1]);
        Assert.Equal("Robin.Skin", body.ColorSlots!["Skin"].Replace(pose.Model.For!, "Robin"));
        var head = Assert.IsType<LiveLayer>(layers[2]);
        Assert.Equal("Head", head.Part);
        Assert.Equal("Neck", head.AnchorTo!.Anchor);
        Assert.True(pose.ShowGhost);
        Assert.Equal(1, pose.LayerIndex);
        Assert.Null(pose.Problems);
        Assert.Equal(0, editor.Shell.ValidateNow().Errors);
    }

    [Fact]
    public void LayerOperationsAreUndoable()
    {
        var (editor, studio) = OpenStudio();
        using var _ = editor;
        var pose = NewPose(studio, layered: true);
        pose.AddPaintLayerCommand.Execute(null);
        pose.AddEffectLayerCommand.Execute(null);
        Assert.Equal(new[] { "Live", "Paint", "Effect" }, pose.Frame!.Layers.Select(l => l.Type));
        pose.LayerIndex = 1;
        pose.MoveLayerUpCommand.Execute(null);
        Assert.Equal(new[] { "Live", "Effect", "Paint" }, pose.Frame.Layers.Select(l => l.Type));
        Assert.Equal(2, pose.LayerIndex);
        pose.Layers.First(r => r.Index == 0).IsVisible = false;
        Assert.False(pose.Frame.Layers[0].Visible);
        pose.DuplicateLayerCommand.Execute(null);
        var paints = pose.Frame.Layers.OfType<PaintLayer>().ToArray();
        Assert.Equal(2, paints.Length);
        Assert.NotEqual(paints[0].Image, paints[1].Image);   // the copy owns its pixels
        pose.DeleteLayerCommand.Execute(null);
        Assert.Single(pose.Frame.Layers.OfType<PaintLayer>());
        editor.Shell.Document!.Undo();
        Assert.Equal(2, pose.Frame.Layers.OfType<PaintLayer>().Count());
        Assert.Equal(4, pose.Layers.Count);
    }

    [Fact]
    public void FramesCopyTheirPaintAndSequencesFollowDeletes()
    {
        var (editor, studio) = OpenStudio();
        using var _ = editor;
        var pose = NewPose(studio, layered: true);
        pose.AddPaintLayerCommand.Execute(null);
        pose.AddFrameCommand.Execute(null);
        Assert.Equal(1, pose.FrameIndex);
        var first = pose.Model.Frames[0].Layers.OfType<PaintLayer>().Single();
        var second = pose.Model.Frames[1].Layers.OfType<PaintLayer>().Single();
        Assert.NotEqual(first.Image, second.Image);
        pose.AddToLoopCommand.Execute(null);
        pose.FrameIndex = 0;
        pose.AddToIntroCommand.Execute(null);
        pose.AddToLoopCommand.Execute(null);
        Assert.Equal(new[] { 1, 0 }, pose.Model.Loop);
        pose.DeleteFrameCommand.Execute(null);
        Assert.Single(pose.Model.Frames);
        Assert.Equal(new[] { 0 }, pose.Model.Loop);
        Assert.Empty(pose.Model.Intro);
        Assert.Equal(0, editor.Shell.ValidateNow().Errors);
    }

    [Fact]
    public void SelectionsMoveMirrorAndPaste()
    {
        var (editor, studio) = OpenStudio();
        using var _ = editor;
        var pose = NewPose(studio);
        pose.AddPaintLayerCommand.Execute(null);
        var paint = (PaintLayer)pose.SelectedLayer!;
        studio.Palette.Primary = ColorSlots.Outline;
        pose.Tool = StudioTool.Pencil;
        pose.PointerDown(1, 1, PointerButton.Primary); pose.PointerUp(1, 1);
        pose.Tool = StudioTool.Select;
        pose.PointerDown(0, 0, PointerButton.Primary); pose.PointerMove(2, 2); pose.PointerUp(2, 2);
        Assert.Equal(new PixelRect(0, 0, 3, 3), pose.Selection);
        // Drag the selection 4 px right.
        pose.PointerDown(1, 1, PointerButton.Primary); pose.PointerMove(5, 1); pose.PointerUp(5, 1);
        var image = studio.Library!.ReadImage(paint.Image)!;
        Assert.Equal(0u, image.Pixels[1 * image.Width + 1]);
        Assert.Equal(ColorSlots.Outline, image.Pixels[1 * image.Width + 5]);
        Assert.Equal("Move pixels", editor.Shell.Document!.History.UndoLabel);
        pose.CopySelectionCommand.Execute(null);
        pose.Selection = new PixelRect(8, 8, 3, 3);
        pose.PasteSelectionCommand.Execute(null);
        image = studio.Library.ReadImage(paint.Image)!;
        Assert.Equal(ColorSlots.Outline, image.Pixels[9 * image.Width + 9]);
        Assert.True(pose.HandleKey("H", false, false));   // mirror the pasted piece
        image = studio.Library.ReadImage(paint.Image)!;
        Assert.Equal(ColorSlots.Outline, image.Pixels[9 * image.Width + 9]);   // centre column stays
    }

    [Fact]
    public void AnchorsPivotAndPropertiesEditThePose()
    {
        var (editor, studio) = OpenStudio();
        using var _ = editor;
        var pose = NewPose(studio);
        pose.Tool = StudioTool.Anchor;
        pose.AnchorName = "Hand";
        pose.PointerDown(4, 20, PointerButton.Primary);
        Assert.Equal(new[] { 4, 20 }, pose.Frame!.Anchors["Hand"]);
        var duration = pose.Fields.OfType<NumberField>().First(f => f.Label == "Duration");
        duration.Value = 250;
        Assert.Equal(250, pose.Model.Frames[0].Duration);
        Assert.Equal(250, studio.Library!.Pose("robin-bow")!.Frames[0].Duration);
        var pivot = pose.Fields.OfType<NumberField>().First(f => f.Label == "Pivot Y");
        pivot.Value = 30;
        Assert.Equal(30, studio.Library.Pose("robin-bow")!.Pivot![1]);
        Assert.Contains(pose.Fields.OfType<ListField>(), f => f.Label == "Anchors" && f.Rows.Count == 2);
    }

    [Fact]
    public void RenamingAPoseFollowsScenesThatUseIt()
    {
        var (editor, studio) = OpenStudio();
        using var _ = editor;
        var pose = NewPose(studio);
        var doc = editor.Shell.Document!;
        doc.Edit("Use pose", () => doc.Cutscenes[0].Scene.Beats.Add(new PoseBeat { Actor = "Robin", Pose = "robin-bow" }));
        var id = pose.Fields.OfType<TextField>().First(f => f.Label == "ID");
        id.Value = "robin-curtsy";
        Assert.Equal("robin-curtsy", pose.Model.Id);
        Assert.Equal("robin-curtsy", doc.Cutscenes[0].Scene.Beats.OfType<PoseBeat>().Single().Pose);
        Assert.Null(studio.Library!.Pose("robin-bow"));
        Assert.NotNull(studio.Library.Pose("robin-curtsy"));
        Assert.StartsWith("scene ", Assert.Single(References.UsesOf(studio.Library, doc, LibraryKind.Pose, "robin-curtsy")));
    }

    [Fact]
    public void ExternalEditsComeBackAsOneUndoStep()
    {
        var (editor, studio) = OpenStudio();
        using var _ = editor;
        var pose = NewPose(studio, layered: true);
        pose.AddPaintLayerCommand.Execute(null);
        pose.AddFrameCommand.Execute(null);
        var layer = pose.SelectedLayer!;
        using var session = pose.EditExternally(layer, launch: false)!;
        Assert.True(File.Exists(session.File));
        Assert.True(File.Exists(Path.ChangeExtension(session.File, ".json")));
        Assert.Contains("GIMP Palette", File.ReadAllText(Path.Combine(Path.GetDirectoryName(session.File)!, "sdcs-slots.gpl")));
        var strip = StudioImages.Decode(File.ReadAllBytes(session.File));
        Assert.Equal(pose.Width * 2, strip.Width);
        strip.Pixels[2 * strip.Width + pose.Width + 1] = ColorSlots.HairMid;   // frame 2, (1,2)
        File.WriteAllBytes(session.File, StudioImages.Encode(strip));
        Assert.True(session.CheckNow());
        var second = (PaintLayer)pose.Model.Frames[1].Layers[1];
        Assert.Equal(ColorSlots.HairMid, studio.Library!.ReadImage(second.Image)!.Pixels[2 * pose.Width + 1]);
        Assert.Equal("Edit outside", editor.Shell.Document!.History.UndoLabel);

        File.WriteAllBytes(session.File, StudioImages.Encode(StudioImages.Blank(5, 5)));
        Assert.False(session.CheckNow() && editor.Ui.Toasts.All(t => t.Title != "Size changed"));
        Assert.Contains(editor.Ui.Toasts, t => t.Title == "Size changed");
    }

    [Fact]
    public void PlayerPartStringsRoundTrip()
    {
        foreach (var text in new[] { "Full", "Head+Eyes+Accessory+Hair+Hat", "Base+Shirt", "Body+Arms" })
            Assert.Equal(FarmerParts.Parse(text), FarmerParts.Parse(PoseDocumentViewModel.PartString(FarmerParts.Parse(text))));
    }

    [Fact]
    public void ThePaletteFileListsEverySlotColour()
    {
        string gpl = ExternalEditor.GplPalette();
        Assert.StartsWith("GIMP Palette", gpl);
        Assert.Contains($"{(ColorSlots.SkinMid >> 16) & 0xFF,3} {(ColorSlots.SkinMid >> 8) & 0xFF,3} {ColorSlots.SkinMid & 0xFF,3}\tSkin mid", gpl);
        Assert.Equal(PaletteViewModel.SlotColors.Length, gpl.Split('\n').Count(l => l.Contains('\t')));
    }
}
