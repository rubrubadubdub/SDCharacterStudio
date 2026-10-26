using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Palette;
using SDCharacterStudio.Editor.Studio;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>
/// Painting helpers after Pixelorama (the owner, 2026-10-26: "Implement in your suggested order"): paint inside her, the
/// shading brush, outline what you painted, named colours and live recolours.
/// </summary>
public sealed class PaintHelperStudioTests
{
    private const uint Green = 0xFF30A050, Blue = 0xFF3060C0, Yellow = 0xFFE8C020;

    private static uint Pixel(RgbaImage image, int x, int y) => image.Pixels[y * image.Width + x];
    private static RgbaImage Shown(CharacterStudioViewModel studio, string poseId, int frame = 0) => studio.Context.RenderFrame(studio.Library!.Pose(poseId)!, frame).Image;
    private static RgbaImage Painted(CharacterStudioViewModel studio, string poseId, int frame = 0) =>
        studio.Context.PackImage(PoseDocumentViewModel.FlatOf(studio.Library!.Pose(poseId)!.Frames[frame])!.Paint.Image)!;

    private static (EditorHarness, CharacterStudioViewModel, PoseDocumentViewModel) Pose(string name = "touchup")
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        var pose = CharacterStudioTests.NewPose(studio, name);
        Assert.True(pose.IsFlat);
        return (editor, studio, pose);
    }

    private static void Stroke(PoseDocumentViewModel pose, StudioTool tool, (int X, int Y) from, (int X, int Y) to, PointerButton button = PointerButton.Primary)
    {
        pose.Tool = tool;
        pose.PointerDown(from.X, from.Y, button); pose.PointerMove(to.X, to.Y); pose.PointerUp(to.X, to.Y);
    }

    [Fact]
    public void PaintingInsideHerKeepsOffTheEmptyPixels()
    {
        var (editor, studio, pose) = Pose();
        using var _ = editor;
        try
        {
            var before = Shown(studio, pose.ItemId);
            // A row that crosses her: some of it her, some of it empty.
            int y = Enumerable.Range(0, pose.Height).First(r => Enumerable.Range(0, pose.Width).Any(x => Pixel(before, x, r) >> 24 != 0) && Enumerable.Range(0, pose.Width).Any(x => Pixel(before, x, r) >> 24 == 0));
            pose.PaintInside = true;
            studio.Palette.Primary = Green;
            Stroke(pose, StudioTool.Pencil, (0, y), (pose.Width - 1, y));
            var after = Shown(studio, pose.ItemId);
            for (int x = 0; x < pose.Width; x++)
                Assert.Equal(Pixel(before, x, y) >> 24 == 0 ? 0u : Green, Pixel(after, x, y) >> 24 == 0 ? 0u : Pixel(after, x, y));

            editor.Shell.Document!.Undo();
            pose.PaintInside = false;
            Stroke(pose, StudioTool.Pencil, (0, y), (pose.Width - 1, y));
            after = Shown(studio, pose.ItemId);
            Assert.All(Enumerable.Range(0, pose.Width), x => Assert.Equal(Green, Pixel(after, x, y)));
        }
        finally { pose.PaintInside = false; }
    }

    [Fact]
    public void TheShadingBrushStepsAlongHerOwnColours()
    {
        var (editor, studio, pose) = Pose();
        using var _ = editor;
        uint skinMid = studio.Palette.Resolved(ColorSlots.SkinMid), skinDark = studio.Palette.Resolved(ColorSlots.SkinDark);
        var shown = Shown(studio, pose.ItemId);
        int at = Array.IndexOf(shown.Pixels, skinMid);
        Assert.True(at >= 0, "Her standing frame shows her skin.");
        int x = at % pose.Width, y = at / pose.Width;

        Stroke(pose, StudioTool.Shade, (x, y), (x, y));                                  // left click: darker
        Assert.Equal("Shade", editor.Shell.Document!.History.UndoLabel);
        Assert.Equal(ColorSlots.SkinDark, Pixel(Painted(studio, pose.ItemId), x, y));   // her slot, so it stays live
        Assert.Equal(skinDark, Pixel(Shown(studio, pose.ItemId), x, y));
        Stroke(pose, StudioTool.Shade, (x, y), (x, y), PointerButton.Secondary);         // right click: lighter, back to mid
        Assert.Equal(ColorSlots.SkinMid, Pixel(Painted(studio, pose.ItemId), x, y));

        // Your own colour shades by eye; an empty pixel stays empty.
        int empty = Array.FindIndex(shown.Pixels, p => p >> 24 == 0);
        studio.Palette.Primary = Green;
        Stroke(pose, StudioTool.Pencil, (x, y), (x, y));
        Stroke(pose, StudioTool.Shade, (x, y), (x, y));
        Assert.True(Shading.Hsv(Pixel(Shown(studio, pose.ItemId), x, y)).Value < Shading.Hsv(Green).Value);
        Stroke(pose, StudioTool.Shade, (empty % pose.Width, empty / pose.Width), (empty % pose.Width, empty / pose.Width));
        Assert.Equal(0u, Pixel(Shown(studio, pose.ItemId), empty % pose.Width, empty / pose.Width) >> 24);
    }

    [Fact]
    public void ShadingSeveralPickedFramesWorksOutEachFromItsOwnColours()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        string id = studio.NewOutfit("apron")!;
        var outfit = (PoseDocumentViewModel)studio.ActiveDocument!;
        var shown = Enumerable.Range(0, 5).Select(f => Shown(studio, id, f)).ToList();
        // A spot she shows on all four steps of the walk down.
        int at = Enumerable.Range(0, outfit.Width * outfit.Height).First(i => Enumerable.Range(0, 4).All(f => shown[f].Pixels[i] >> 24 != 0));
        int x = at % outfit.Width, y = at / outfit.Width;
        outfit.PickFrames(new[] { 0, 1, 2, 3 }, 0);
        Stroke(outfit, StudioTool.Shade, (x, y), (x, y));
        Assert.Equal("Shade on 4 frames", editor.Shell.Document!.History.UndoLabel);
        for (int f = 0; f < 4; f++)
            Assert.True(Shading.Hsv(Pixel(Shown(studio, id, f), x, y)).Value < Shading.Hsv(Pixel(shown[f], x, y)).Value, $"frame {f} got darker");
        Assert.Equal(Pixel(shown[4], x, y), Pixel(Shown(studio, id, 4), x, y));        // frame 5 wasn't picked
    }

    [Fact]
    public void OutlineGoesAroundWhatYouPaintedWhereItMeetsEmptySpace()
    {
        var (editor, studio, pose) = Pose();
        using var _ = editor;
        var shown = Shown(studio, pose.ItemId);
        bool Empty(int x, int y) => x >= 0 && y >= 0 && x < pose.Width && y < pose.Height && Pixel(shown, x, y) >> 24 == 0;
        // An empty patch away from her, five wide and three high.
        var spot = Enumerable.Range(0, pose.Width * pose.Height).Select(i => (X: i % pose.Width, Y: i / pose.Width))
            .First(p => Enumerable.Range(-1, 5).All(dx => Enumerable.Range(-1, 3).All(dy => Empty(p.X + dx, p.Y + dy))));
        studio.Palette.Primary = Green;
        Stroke(pose, StudioTool.Pencil, spot, (spot.X + 2, spot.Y));
        pose.OutlinePaint();
        Assert.Equal("Outline what you painted", editor.Shell.Document!.History.UndoLabel);
        var after = Shown(studio, pose.ItemId);
        uint outline = Pixel(after, spot.X - 1, spot.Y);
        Assert.NotEqual(0u, outline >> 24);
        Assert.NotEqual(Green, outline);
        foreach (var (ox, oy) in new[] { (spot.X + 3, spot.Y), (spot.X, spot.Y - 1), (spot.X + 2, spot.Y + 1) })
            Assert.Equal(outline, Pixel(after, ox, oy));
        Assert.Equal(0u, Pixel(after, spot.X - 1, spot.Y - 1) >> 24);                    // corners stay empty (a clean outline)
        Assert.Equal(Green, Pixel(after, spot.X + 1, spot.Y));                           // the paint itself is untouched
    }

    [Fact]
    public void ANamedColourChangesEverywhereItIsPainted()
    {
        var (editor, studio, pose) = Pose("buttons");
        using var _ = editor;
        var coat = studio.AddNamedColor("Coat", Yellow)!;
        Assert.Equal(Yellow, coat.Mid);
        Assert.Contains(studio.Palette.Named, r => r.Name == "Coat" && r.Shades.Select(s => s.Color).SequenceEqual(new[] { coat.Dark, coat.Mid, coat.Light }));
        studio.Palette.Primary = coat.Mid;
        Stroke(pose, StudioTool.Pencil, (1, 1), (1, 1));
        studio.Palette.Primary = coat.Dark;
        Stroke(pose, StudioTool.Pencil, (2, 1), (2, 1));
        // Shading a named colour steps along its ramp.
        Stroke(pose, StudioTool.Shade, (1, 1), (1, 1), PointerButton.Secondary);
        Assert.Equal(coat.Light, Pixel(Shown(studio, pose.ItemId), 1, 1));
        Stroke(pose, StudioTool.Shade, (1, 1), (1, 1));
        Assert.Equal(coat.Mid, Pixel(Shown(studio, pose.ItemId), 1, 1));
        // An outfit painted with it too, and a recolour to it.
        string outfit = studio.NewOutfit("coat")!;
        var outfitDoc = (PoseDocumentViewModel)studio.ActiveDocument!;
        outfitDoc.FrameIndex = 2;
        studio.Palette.Primary = coat.Mid;
        Stroke(outfitDoc, StudioTool.Pencil, (3, 3), (3, 3));
        studio.Palette.Primary = coat.Mid;
        outfitDoc.AddRecolor();

        Assert.Equal(2, studio.ChangeNamedColor("Coat", Blue));                         // the pose's picture and the outfit's frame
        var fresh = studio.NamedColors().Single();
        Assert.Equal(Blue, fresh.Mid);
        Assert.Equal(Blue, Pixel(Shown(studio, pose.ItemId), 1, 1));
        Assert.Equal(fresh.Dark, Pixel(Shown(studio, pose.ItemId), 2, 1));
        Assert.Equal(Blue, Pixel(Shown(studio, outfit, 2), 3, 3));
        Assert.Equal("#3060C0", ((LiveLayer)studio.Library!.Pose(outfit)!.Frames[0].Layers[0]).Recolor!.Single().To);
        Assert.Equal("Colour changed", ((FakeUi)editor.Shell.Ui).Toasts[^1].Title);

        editor.Shell.Document!.Undo();                                                  // one step back, everywhere
        studio.Context.InvalidatePack();
        Assert.Equal(Yellow, Pixel(Shown(studio, pose.ItemId), 1, 1));
        Assert.Equal(Yellow, Pixel(Shown(studio, outfit, 2), 3, 3));
        Assert.Equal(Yellow, studio.NamedColors().Single().Mid);
    }

    [Fact]
    public void ARecolourChangesHerShirtStillLive()
    {
        var (editor, studio, pose) = Pose("red-shirt");
        using var _ = editor;
        var before = Shown(studio, pose.ItemId);
        uint skinMid = studio.Palette.Resolved(ColorSlots.SkinMid);
        Assert.True(pose.CanRecolor);
        studio.Palette.Primary = Blue;
        pose.AddRecolor();
        var row = Assert.Single(pose.Recolors);
        Assert.Equal("robin.Shirt", row.From);                                          // clothes first
        Assert.Equal(Blue, row.To);
        var rule = Assert.Single(((LiveLayer)studio.Library!.Pose(pose.ItemId)!.Frames[0].Layers[0]).Recolor!);
        Assert.Equal(("robin.Shirt", "#3060C0", 25), (rule.From, rule.To, rule.Range));
        Assert.Equal(0, editor.Shell.ValidateNow().Errors);

        var after = Shown(studio, pose.ItemId);
        var changed = Enumerable.Range(0, before.Pixels.Length).Where(i => before.Pixels[i] != after.Pixels[i]).ToList();
        Assert.NotEmpty(changed);
        Assert.All(changed, i => Assert.InRange(Shading.Hsv(after.Pixels[i]).Hue, 195, 255));   // blue now
        Assert.All(Enumerable.Range(0, before.Pixels.Length).Where(i => before.Pixels[i] == skinMid), i => Assert.Equal(skinMid, after.Pixels[i]));

        // The row changes it: wider, then the left colour.
        pose.Recolors.Single().RangeChoice = "40";
        Assert.Equal(40, ((LiveLayer)studio.Library.Pose(pose.ItemId)!.Frames[0].Layers[0]).Recolor!.Single().Range);
        studio.Palette.Primary = Green;
        pose.Recolors.Single().UseLeftColourCommand.Execute(null);
        Assert.Equal("#30A050", ((LiveLayer)studio.Library.Pose(pose.ItemId)!.Frames[0].Layers[0]).Recolor!.Single().To);
        pose.Recolors.Single().RemoveCommand.Execute(null);
        Assert.Empty(pose.Recolors);
        Assert.Equal(before.Pixels, Shown(studio, pose.ItemId).Pixels);
    }

    [Fact]
    public void ACopyForSomeoneElseRecoloursTheirShirt()
    {
        var (editor, studio, pose) = Pose("red-shirt");
        using var _ = editor;
        studio.Palette.Primary = Blue;
        pose.AddRecolor();
        string? copy = studio.CopyPoseFor(pose.ItemId, studio.Characters.First(c => c.Key == "Sophia"));
        Assert.NotNull(copy);
        var rule = Assert.Single(((LiveLayer)studio.Library!.Pose(copy)!.Frames[0].Layers[0]).Recolor!);
        Assert.Equal(("sophia.Shirt", "#3060C0"), (rule.From, rule.To));
        Assert.Equal(0, editor.Shell.ValidateNow().Errors);
    }
}
