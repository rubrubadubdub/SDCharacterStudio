using System.Windows.Media.Imaging;
using SDCharacterStudio.Core.Palette;
using SDCharacterStudio.Editor.Studio;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>
/// The owner (2026-10-25): "Editing the portrait doesn't seem to do anything at all. I only have transparent pixels to
/// choose from"; "I can't mass cull frames by selecting a bunch then deleting"; "I can't preview the ready-made poses";
/// "I don't see where I can select from the ready-made poses, only the ones I make".
/// </summary>
public sealed class StudioBasicsTests
{
    private static uint Pixel(BitmapSource? image, int x, int y)
    {
        var pixel = new byte[4];
        image!.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return (uint)(pixel[3] << 24 | pixel[2] << 16 | pixel[1] << 8 | pixel[0]);
    }

    [Fact]
    public void AnExpressionPaintsAnywhereOnTheFaceWithTheColoursYouSee()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        Assert.NotNull(studio.NewExpression("blush"));
        var expression = Assert.IsType<ExpressionDocumentViewModel>(studio.ActiveDocument);
        var box = expression.Box;
        expression.Tool = StudioTool.Pencil;

        // Painting with Transparent says so instead of silently doing nothing.
        int toasts = ((FakeUi)editor.Shell.Ui).Toasts.Count;
        studio.Palette.Primary = 0;
        expression.PointerDown(box[0], box[1], PointerButton.Primary, false);
        expression.PointerUp();
        Assert.Equal("Pick a colour first", ((FakeUi)editor.Shell.Ui).Toasts.Skip(toasts).Single().Title);

        // Alt+click takes the colour you see on her face (the overlay alone is empty).
        int faceX = -1, faceY = -1;
        for (int y = 0; y < expression.Height && faceX < 0; y++)
            for (int x = 0; x < expression.Width; x++)
                if (Pixel(expression.Composite, x, y) >> 24 != 0) { faceX = x; faceY = y; break; }
        expression.PointerDown(faceX, faceY, PointerButton.Primary, alt: true);
        Assert.Equal(Pixel(expression.Composite, faceX, faceY), studio.Palette.Primary);

        // Outside the face box: the paint lands there (the box is only the anchor), as her real colour for a slot swatch.
        studio.Palette.Primary = ColorSlots.HairMid;   // this expression maps only Skin and Outline
        int px = box[0] + box[2] + 2, py = box[1] + box[3] + 3;
        expression.PointerDown(px, py, PointerButton.Primary, false);
        expression.PointerUp();
        var frame = studio.Library!.Expressions().Expressions.Single().Frames.Single();
        var image = studio.Library.ReadImage(frame.Image)!;
        int ix = px - box[0] - frame.Offset[0], iy = py - box[1] - frame.Offset[1];
        Assert.Equal(studio.Palette.Resolved(ColorSlots.HairMid), image.Pixels[iy * image.Width + ix]);
        Assert.NotEqual(ColorSlots.HairMid, image.Pixels[iy * image.Width + ix]);
        Assert.Equal((1, 1), (image.Width, image.Height));   // trimmed to what was painted
        Assert.Equal(studio.Palette.Resolved(ColorSlots.HairMid) | 0xFF000000, Pixel(expression.Composite, px, py));

        // Removing her own pixels has a button now that the simple palette hides Erase.
        expression.RemoveHerPixelsCommand.Execute(null);
        Assert.Equal(ColorSlots.Erase, studio.Palette.Primary);
        Assert.Equal("Erase (removes their own pixels)", studio.Palette.PrimaryName);

        // Several frames go at once; picking them all keeps frame 1.
        expression.AddFrameCommand.Execute(null);
        expression.AddFrameCommand.Execute(null);
        Assert.Equal(3, studio.Library.Expressions().Expressions.Single().Frames.Count);
        expression.PickedFrames = new[] { 0, 1, 2 };
        expression.DeleteFrameCommand.Execute(null);
        Assert.Single(studio.Library.Expressions().Expressions.Single().Frames);
    }

    [Fact]
    public void PickedPoseFramesAreDeletedTogether()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio);
        pose.ApplyPresetCommand.Execute("nod");   // five frames
        Assert.Equal(5, pose.Model.Frames.Count);
        pose.PickFrames(new[] { 1, 2, 3 }, 2);
        pose.DeleteFrameCommand.Execute(null);
        Assert.Equal(2, pose.Model.Frames.Count);
        Assert.Equal("Delete 3 frames", editor.Shell.Document!.History.UndoLabel);
        editor.Shell.Document.Undo();
        Assert.Equal(5, studio.Library!.Pose(pose.ItemId)!.Frames.Count);

        pose.Reload();
        pose.PickFrames(Enumerable.Range(0, 5), 0);
        pose.DeleteFrameCommand.Execute(null);
        Assert.Single(pose.Model.Frames);   // all picked: frame 1 stays
    }

    [Fact]
    public void ReadyMadePosesPlayInThePreviewAndAnimationsOfferThem()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio);

        // Pointing at a ready-made pose plays it in the preview; the pose itself doesn't change.
        int frames = pose.Model.Frames.Count;
        pose.PreviewPreset("nod");
        Assert.StartsWith("Ready-made: Nod", pose.PreviewCaption);
        var first = pose.PreviewImage;
        pose.TickPreview(PosePresets.Find("nod")!.Frames[0].Ms + 1);
        Assert.NotSame(first, pose.PreviewImage);
        Assert.Equal(frames, pose.Model.Frames.Count);
        pose.PreviewPreset(null);
        Assert.Null(pose.PreviewCaption);

        // An animation's "Show a pose" offers them; picking one makes her own pose and shows it.
        Assert.NotNull(studio.NewAnimation("nodding"));
        var animation = Assert.IsType<AnimationDocumentViewModel>(studio.ActiveDocument);
        Assert.True(animation.IsFlipbook);
        Assert.Contains(animation.PoseChoices, c => c.Value == AnimationDocumentViewModel.ReadyMadePrefix + "nod" && c.Label == "Nod (ready-made)");
        animation.Steps[0].Do = "Pose";
        animation.Steps[0].Pose = AnimationDocumentViewModel.ReadyMadePrefix + "nod";
        string made = ReadyMadePoses.PoseId("Robin", "nod", "down");
        Assert.Equal(made, animation.Steps[0].Pose);
        var madePose = studio.Library!.Pose(made)!;
        Assert.Equal(PosePresets.Find("nod")!.Frames.Count, madePose.Frames.Count);
        Assert.Contains(animation.PoseChoices, c => c.Value == made);   // now one of her poses
        Assert.Equal(0, editor.Shell.ValidateNow().Errors);
    }
}
