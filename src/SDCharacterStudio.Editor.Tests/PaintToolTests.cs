using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Palette;
using SDCharacterStudio.Editor.Studio;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>D-49 phase 1: painting like a pixel editor (Aseprite's tools, keys and options).</summary>
public sealed class PaintToolTests
{
    private const uint Blue = 0xFF2040D0;
    private static RgbaImage Composite(CharacterStudioViewModel studio, PoseDocumentViewModel pose) => studio.Context.RenderFrame(pose.Model, pose.FrameIndex).Image;
    private static RgbaImage Paint(CharacterStudioViewModel studio, PoseDocumentViewModel pose) => studio.Context.PackImage(pose.Flat!.Paint.Image)!;
    private static List<(int X, int Y)> Painted(RgbaImage image) =>
        Enumerable.Range(0, image.Height).SelectMany(y => Enumerable.Range(0, image.Width).Select(x => (x, y))).Where(p => PixelTools.Get(image, p.x, p.y) >> 24 != 0).ToList();

    private static (CharacterStudioViewModel Studio, PoseDocumentViewModel Pose, EditorHarness Editor) Open()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        var pose = CharacterStudioTests.NewPose(studio);
        pose.BrushSize = 1; pose.Symmetry = false; pose.PixelPerfect = true; pose.FilledShapes = false;
        studio.Palette.Primary = Blue;
        return (studio, pose, editor);
    }

    [Fact]
    public void ABiggerBrushPaintsADiscAndSymmetryMirrorsIt()
    {
        var (studio, pose, editor) = Open();
        using var _ = editor;
        pose.Tool = StudioTool.Pencil;
        Assert.True(pose.HandleKey("OemCloseBrackets", false, false));
        Assert.True(pose.HandleKey("Oem6", false, false));
        Assert.Equal(3, pose.BrushSize);
        pose.PointerDown(3, 10, PointerButton.Primary); pose.PointerUp(3, 10);
        var painted = Painted(Paint(studio, pose));
        Assert.Equal(9, painted.Count);   // a 3-px disc is the 3×3 block
        Assert.Contains((2, 9), painted); Assert.Contains((4, 11), painted);

        editor.Shell.Document!.Undo();
        pose.Symmetry = true;
        pose.BrushSize = 1;
        pose.PointerDown(2, 20, PointerButton.Primary); pose.PointerUp(2, 20);
        painted = Painted(Paint(studio, pose));
        Assert.Equal(new[] { (2, 20), (pose.Width - 3, 20) }.OrderBy(p => p.Item1), painted.OrderBy(p => p.X));
        Assert.True(pose.HandleKey("Oem4", false, false));
        Assert.Equal(1, pose.BrushSize);   // never below 1
    }

    [Fact]
    public void PixelPerfectStrokesHaveNoDoubledCorners()
    {
        var (studio, pose, editor) = Open();
        using var _ = editor;
        pose.Tool = StudioTool.Pencil;
        // A staircase drawn the way a hand does it: right, down, right, down...
        pose.PointerDown(2, 2, PointerButton.Primary);
        pose.PointerMove(3, 2); pose.PointerMove(3, 3); pose.PointerMove(4, 3); pose.PointerMove(4, 4);
        pose.PointerUp(4, 4);
        Assert.Equal(new[] { (2, 2), (3, 3), (4, 4) }, Painted(Paint(studio, pose)).OrderBy(p => p.X).ToArray());

        editor.Shell.Document!.Undo();
        pose.PixelPerfect = false;
        pose.PointerDown(2, 2, PointerButton.Primary);
        pose.PointerMove(3, 2); pose.PointerMove(3, 3); pose.PointerMove(4, 3); pose.PointerMove(4, 4);
        pose.PointerUp(4, 4);
        Assert.Equal(5, Painted(Paint(studio, pose)).Count);
    }

    [Fact]
    public void TheEllipseToolDrawsInsideItsBox()
    {
        var (studio, pose, editor) = Open();
        using var _ = editor;
        Assert.True(pose.HandleKey("U", false, true));   // Shift+U
        Assert.Equal(StudioTool.Ellipse, pose.Tool);
        pose.PointerDown(2, 4, PointerButton.Primary); pose.PointerMove(10, 12); pose.PointerUp(10, 12);
        var painted = Painted(Paint(studio, pose));
        Assert.All(painted, p => Assert.True(p.X >= 2 && p.X <= 10 && p.Y >= 4 && p.Y <= 12));
        Assert.Contains((6, 4), painted); Assert.Contains((6, 12), painted); Assert.Contains((2, 8), painted); Assert.Contains((10, 8), painted);
        Assert.DoesNotContain((6, 8), painted);   // an outline
        Assert.Equal("Draw ellipse", editor.Shell.Document!.History.UndoLabel);

        editor.Shell.Document.Undo();
        pose.FilledShapes = true;
        pose.PointerDown(2, 4, PointerButton.Primary); pose.PointerMove(10, 12); pose.PointerUp(10, 12);
        Assert.Contains((6, 8), Painted(Paint(studio, pose)));
    }

    [Fact]
    public void AltPicksWithAnyToolAndTheEyedropperHandsBack()
    {
        var (studio, pose, editor) = Open();
        using var _ = editor;
        var before = Composite(studio, pose);
        var (x, y) = Painted(before).First();
        pose.Tool = StudioTool.Eraser;
        pose.PointerDown(x, y, PointerButton.Primary, alt: true); pose.PointerUp(x, y);
        Assert.Equal(before.Pixels, Composite(studio, pose).Pixels);   // nothing erased
        Assert.Equal(StudioTool.Eraser, pose.Tool);
        Assert.NotEqual(Blue, studio.Palette.Primary);

        pose.Tool = StudioTool.Pencil;
        Assert.True(pose.HandleKey("I", false, false));
        Assert.Equal(StudioTool.Picker, pose.Tool);
        pose.PointerDown(x, y, PointerButton.Primary); pose.PointerUp(x, y);
        Assert.Equal(StudioTool.Pencil, pose.Tool);   // straight back to painting
    }

    [Fact]
    public void PickingHerColourPicksHerSlotAndPaintingRemembersIt()
    {
        var (studio, pose, editor) = Open();
        using var _ = editor;
        var skin = studio.Palette.VisibleSlots.Single(s => s.Name == "Skin mid");
        uint shownSkin = StudioImages.FromColor(((System.Windows.Media.SolidColorBrush)skin.Display).Color);
        var picture = Composite(studio, pose);
        var at = Painted(picture).FirstOrDefault(p => PixelTools.Get(picture, p.X, p.Y) == shownSkin);
        Assert.NotEqual(default, at);   // her skin is in the standing frame

        pose.Tool = StudioTool.Picker;
        pose.PointerDown(at.X, at.Y, PointerButton.Primary); pose.PointerUp(at.X, at.Y);
        Assert.Equal(ColorSlots.SkinMid, studio.Palette.Primary);   // her slot: it follows outfits

        // The picture's colours: her skin shows as her skin and paints her slot.
        Assert.Contains(studio.Palette.InPicture, s => s.Color == ColorSlots.SkinMid);

        pose.PointerDown(8, 16, PointerButton.Primary); pose.PointerUp(8, 16);   // back on the pencil
        Assert.Equal(ColorSlots.SkinMid, studio.Palette.Recent.First().Color);
    }

    [Fact]
    public void OnionSkinShowsSeveralFramesTinted()
    {
        var (studio, pose, editor) = Open();
        using var _ = editor;
        pose.AddFrameCommand.Execute(null);
        pose.AddFrameCommand.Execute(null);
        pose.FrameIndex = 2;
        pose.OnionBefore = 2; pose.OnionAfter = 1; pose.OnionTint = true;
        pose.ShowOnion = true;
        Assert.NotNull(pose.OnionPrevious);
        Assert.Null(pose.OnionNext);   // the last frame, no wrap
        pose.OnionWrap = true;
        Assert.NotNull(pose.OnionNext);   // wraps to the first frame
        pose.OnionBefore = 0;
        Assert.Null(pose.OnionPrevious);
    }

    [Fact]
    public void FlipsAndFrameKeys()
    {
        var (studio, pose, editor) = Open();
        using var _ = editor;
        var before = Composite(studio, pose);
        Assert.True(pose.HandleKey("V", false, true));   // Shift+V: flip the whole frame vertically
        var flipped = Composite(studio, pose);
        for (int y = 0; y < pose.Height; y++)
            for (int x = 0; x < pose.Width; x++)
                Assert.Equal(PixelTools.Get(before, x, pose.Height - 1 - y), PixelTools.Get(flipped, x, y));
        Assert.Equal("Flip pixels", editor.Shell.Document!.History.UndoLabel);

        pose.AddFrameCommand.Execute(null);
        pose.AddFrameCommand.Execute(null);
        Assert.True(pose.HandleKey("Home", false, false));
        Assert.Equal(0, pose.FrameIndex);
        Assert.True(pose.HandleKey("End", false, false));
        Assert.Equal(2, pose.FrameIndex);
        Assert.True(pose.HandleKey("F1", false, false));
        Assert.True(pose.IsShortcutsOpen);
        Assert.NotEmpty(pose.Shortcuts);
    }

    [Fact]
    public void ToolOptionsAreRemembered()
    {
        var (studio, pose, editor) = Open();
        using var _ = editor;
        pose.BrushSize = 4; pose.Symmetry = true; pose.OnionAfter = 3;
        Assert.Equal(4, studio.Settings.BrushSize);
        Assert.True(studio.Settings.Symmetry);
        Assert.Equal(3, studio.Settings.OnionAfter);
        pose.BrushSize = 99;
        Assert.Equal(PoseDocumentViewModel.MaxBrush, pose.BrushSize);
    }
}
