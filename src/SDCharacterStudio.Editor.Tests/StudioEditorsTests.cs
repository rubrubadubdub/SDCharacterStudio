using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Palette;
using SDCharacterStudio.Editor.Studio;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>M8 masks with the fit check (doc 07 §6.3), rigs, expressions and portraits (§3.3).</summary>
public class StudioEditorsTests
{
    private static (EditorHarness Editor, CharacterStudioViewModel Studio) WithRobinRig()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        studio.MakeRig();   // opens the rig, making it (and its head masks) first
        return (editor, studio);
    }

    [Fact]
    public void CancellingExpressionStrokeRestoresPixelsAndHistory()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        studio.NewExpression("cancel-expression");
        var expression = Assert.IsType<ExpressionDocumentViewModel>(studio.ActiveDocument);
        expression.Tool = StudioTool.Pencil;
        studio.Palette.Primary = 0xFFFF0000;
        string? path = expression.Model.Frames[0].Image;
        int history = editor.Shell.Document!.History.UndoCount;
        expression.PointerDown(expression.Box[0], expression.Box[1], PointerButton.Primary, false);
        Assert.NotNull(path);
        Assert.Contains(path!, studio.Context.Overrides.Keys);
        expression.CancelStroke();
        Assert.DoesNotContain(path!, studio.Context.Overrides.Keys);
        Assert.Equal(history, editor.Shell.Document.History.UndoCount);
        Assert.Equal(path, expression.Model.Frames[0].Image);
    }

    [Fact]
    public void CancellingMaskStrokeRestoresPixelsAndHistory()
    {
        var (editor, studio) = WithRobinRig();
        using var _ = editor;
        studio.OpenItem(LibraryKind.Mask, "robin-head-left");
        var mask = Assert.IsType<MaskDocumentViewModel>(studio.ActiveDocument);
        string path = mask.CurrentPath!;
        var before = studio.Context.PackImage(path)!.Pixels.ToArray();
        int history = editor.Shell.Document!.History.UndoCount;
        mask.Tool = MaskTool.Brush;
        mask.PointerDown(2, 20, PointerButton.Primary, false);
        Assert.Contains(path, studio.Context.Overrides.Keys);
        mask.PointerMove(6, 20);
        mask.CancelStroke();
        Assert.DoesNotContain(path, studio.Context.Overrides.Keys);
        Assert.Equal(history, editor.Shell.Document.History.UndoCount);
        Assert.Equal(before, studio.Context.PackImage(path)!.Pixels);
    }

    [Fact]
    public void TheMaskEditorPaintsShapesAndChecksEveryVariant()
    {
        var (editor, studio) = WithRobinRig();
        using var _ = editor;
        studio.OpenItem(LibraryKind.Mask, "robin-head-left");
        var mask = Assert.IsType<MaskDocumentViewModel>(studio.ActiveDocument);
        Assert.Equal("robin", mask.Rig!.Id);
        Assert.Equal(mask.Rig.Frames["left"].Stand, mask.SourceFrame);
        Assert.NotNull(mask.FrameImage);
        // Robin and her summer outfit are both installed: the fit check shows both.
        Assert.Equal(2, mask.Fit.Count);
        Assert.Contains(mask.Fit, v => v.Label == "Robin_Summer");

        int Kept() => studio.Context.PackImage(mask.Model.Image)!.Pixels.Count(p => p >> 24 != 0);
        int before = Kept();
        mask.ClearMaskCommand.Execute(null);
        Assert.Equal(0, Kept());
        mask.Tool = MaskTool.Brush;
        mask.PointerDown(2, 20, PointerButton.Primary, false); mask.PointerMove(6, 20); mask.PointerUp();
        Assert.Equal(5, Kept());
        Assert.Equal("Paint mask", editor.Shell.Document!.History.UndoLabel);
        mask.GrowCommand.Execute(null);
        Assert.True(Kept() > 5);
        mask.AboveNeckCommand.Execute(null);
        Assert.Equal(before, Kept());
        mask.InvertCommand.Execute(null);
        Assert.Equal(16 * 32 - before, Kept());

        // A walk frame gets its own mask; the main one stays.
        mask.SourceFrame = mask.Rig.Frames["left"].Walk.First(w => w != mask.SourceFrame);
        mask.MakeOverrideCommand.Execute(null);
        Assert.True(mask.HasOverride);
        Assert.Single(studio.Library!.Mask("robin-head-left")!.PerFrame);
        Assert.Equal(0, editor.Shell.ValidateNow().Errors);
    }

    [Fact]
    public void RenamingAMaskFollowsRigsAndPoses()
    {
        var (editor, studio) = WithRobinRig();
        using var _ = editor;
        studio.OpenItem(LibraryKind.Mask, "robin-head-left");
        var mask = (MaskDocumentViewModel)studio.ActiveDocument!;
        mask.Fields.OfType<TextField>().Single(f => f.Label == "ID").Value = "robin-face-left";
        Assert.Equal("robin-face-left", studio.Library!.Rig("robin")!.HeadMask["left"]);
        Assert.Null(studio.Library.Mask("robin-head-left"));
        Assert.Equal(0, editor.Shell.ValidateNow().Errors);
    }

    [Fact]
    public void TheRigEditorPlacesTheNeckAndFaceBox()
    {
        var (editor, studio) = WithRobinRig();
        using var _ = editor;
        var rig = Assert.IsType<RigDocumentViewModel>(studio.ActiveDocument);
        rig.Facing = "right";
        rig.Tool = RigTool.Neck;
        rig.PointerDown(9, 14);
        Assert.Equal(new[] { 9, 14 }, studio.Library!.Rig("robin")!.NeckPoint["right"]);
        Assert.Contains(rig.Anchors, a => a.Name == "Neck" && a.X == 9 && a.Y == 14);
        rig.Tool = RigTool.FaceBox;
        rig.PointerDown(5, 9); rig.PointerMove(10, 13); rig.PointerUp();
        Assert.Equal(new[] { 5, 9, 6, 5 }, studio.Library.Rig("robin")!.FaceBox["right"]);
        Assert.Contains(rig.Fields.OfType<ListField>(), f => f.Label == "Colour samples" && f.Rows.Count > 0);
        rig.Portrait = true;
        rig.SetNeck(30, 46);
        Assert.Equal(new[] { 30, 46 }, studio.Library.Rig("robin")!.Portrait!.NeckPoint);
    }

    [Fact]
    public void ExpressionsPaintOverTheFaceAndRenamesFollow()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        Assert.NotNull(studio.NewExpression("eyes closed"));
        var expression = Assert.IsType<ExpressionDocumentViewModel>(studio.ActiveDocument);
        var box = expression.Box;
        var faceBefore = expression.Composite;
        studio.Palette.Primary = ColorSlots.Erase;
        expression.Tool = StudioTool.Pencil;
        expression.PointerDown(box[0] + 1, box[1] + 1, PointerButton.Primary, false);
        expression.PointerMove(box[0] + 3, box[1] + 1);
        expression.PointerUp();
        var frame = studio.Library!.Expressions().Expressions.Single().Frames.Single();
        var pixels = studio.Library.ReadImage(frame.Image)!;
        Assert.Equal(3, pixels.Pixels.Count(p => p == ColorSlots.Erase));
        Assert.Equal("Paint expression", editor.Shell.Document!.History.UndoLabel);
        expression.AddFrameCommand.Execute(null);
        Assert.Equal(2, studio.Library.Expressions().Expressions.Single().Frames.Count);

        var doc = editor.Shell.Document!;
        doc.Edit("Say it", () => doc.Cutscenes[0].Scene.Beats.Add(new SayBeat { Actor = "Robin", Text = "Hm.", Expression = expression.ItemId }));
        expression.Fields.OfType<TextField>().Single(f => f.Label == "ID").Value = "robin-blink";
        Assert.Equal("robin-blink", doc.Cutscenes[0].Scene.Beats.OfType<SayBeat>().Single().Expression);
    }

    [Fact]
    public void PortraitsAreEditedEmotionByEmotion()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        Assert.NotNull(studio.NewPortrait("raincoat"));
        var portrait = Assert.IsType<PortraitDocumentViewModel>(studio.ActiveDocument);
        Assert.True(portrait.IsPortrait);
        Assert.Equal(6, portrait.Model.Frames.Count);
        Assert.Equal("neutral", portrait.EmotionOf(0));
        Assert.Equal("happy", portrait.EmotionOf(1));

        portrait.FrameIndex = 1;
        portrait.IsolateHead();
        var happy = studio.Library!.Portrait(portrait.ItemId)!.Emotions["happy"];
        Assert.Equal(3, happy.Layers.Count);
        var head = Assert.IsType<LiveLayer>(happy.Layers[2]);
        Assert.Equal("Portrait", head.Source!.Kind);
        Assert.Equal(1, head.SourceFrame!.Index);   // the live portrait's happy frame
        Assert.True(happy.Anchors.ContainsKey("Neck"));
        Assert.NotNull(studio.Library.Rig("robin")!.Portrait!.HeadMask);   // made for it

        // The neutral face can't go; a new frame becomes the next free emotion.
        Assert.Equal("6:neutral,happy,sad,unique,love,angry", studio.Library.Portrait(portrait.ItemId)!.Emotions.Count + ":" + string.Join(",", studio.Library.Portrait(portrait.ItemId)!.Emotions.Keys));
        portrait.FrameIndex = 0;
        Assert.Equal("neutral", portrait.EmotionOf(0));
        portrait.DeleteFrameCommand.Execute(null);
        Assert.Equal(6, studio.Library.Portrait(portrait.ItemId)!.Emotions.Count);
        portrait.AddFrameCommand.Execute(null);
        Assert.Contains("6", studio.Library.Portrait(portrait.ItemId)!.Emotions.Keys);
        Assert.DoesNotContain(editor.Shell.ValidateNow().Problems, p => p.IsError && p.File.Contains("portraits"));
    }
}
