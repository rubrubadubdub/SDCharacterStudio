using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Palette;
using SDCharacterStudio.Editor.Studio;
using SDCharacterStudio.Editor.ViewModels;
using SDCharacterStudio.Runtime.Animation;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>
/// Frame tools after Pixelorama (the owner, 2026-10-26): linked frames, reversing picked frames, back-and-forth play,
/// a mirrored view, and an outfit's frames in groups.
/// </summary>
public sealed class FrameToolStudioTests
{
    private const uint Green = 0xFF30A050, Red = 0xFFC03030;

    private static uint Pixel(RgbaImage image, int x, int y) => image.Pixels[y * image.Width + x];
    private static RgbaImage Shown(CharacterStudioViewModel studio, string poseId, int frame) => studio.Context.RenderFrame(studio.Library!.Pose(poseId)!, frame).Image;
    private static string? Picture(CharacterStudioViewModel studio, string poseId, int frame) => PoseDocumentViewModel.FlatOf(studio.Library!.Pose(poseId)!.Frames[frame])!.Paint.Image;

    private static void Dab(PoseDocumentViewModel pose, StudioTool tool, int x, int y)
    {
        pose.Tool = tool;
        pose.PointerDown(x, y, PointerButton.Primary); pose.PointerUp(x, y);
    }

    /// <summary>A pose of three frames, each its own picture.</summary>
    private static (EditorHarness, CharacterStudioViewModel, PoseDocumentViewModel) ThreeFrames(string name = "blink")
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        var pose = CharacterStudioTests.NewPose(studio, name);
        pose.AddFrameCommand.Execute(null);
        pose.AddFrameCommand.Execute(null);
        Assert.Equal(3, pose.Model.Frames.Count);
        Assert.Equal(3, Enumerable.Range(0, 3).Select(i => Picture(studio, pose.ItemId, i)).Distinct().Count());
        return (editor, studio, pose);
    }

    [Fact]
    public void LinkedFramesShareOnePicture()
    {
        var (editor, studio, pose) = ThreeFrames();
        using var _ = editor;
        string? old2 = Picture(studio, pose.ItemId, 2);
        pose.FrameIndex = 0;
        pose.PickFrames(new[] { 0, 1, 2 }, 0);
        Assert.True(pose.CanLink);
        pose.LinkPickedCommand.Execute(null);
        Assert.Equal("Link 2 frames", editor.Shell.Document!.History.UndoLabel);
        Assert.Equal(1, Enumerable.Range(0, 3).Select(i => Picture(studio, pose.ItemId, i)).Distinct().Count());
        Assert.Null(studio.Library!.ReadImage(old2));                                    // the picture it had is gone
        Assert.All(pose.Frames, f => Assert.Equal(pose.Frames[0].LinkColor, f.LinkColor));
        Assert.NotNull(pose.Frames[0].LinkColor);
        Assert.Equal("Linked with frames 2, 3: drawing on one draws on all", pose.LinkedText);

        // Draw on one: all three show it.
        pose.PickFrames(new[] { 0 }, 0);
        studio.Palette.Primary = Green;
        Dab(pose, StudioTool.Pencil, 1, 1);
        Assert.All(Enumerable.Range(0, 3), i => Assert.Equal(Green, Pixel(Shown(studio, pose.ItemId, i), 1, 1)));

        // Shading with linked frames picked shades once, not once per frame.
        pose.PickFrames(new[] { 0, 1, 2 }, 0);
        Dab(pose, StudioTool.Shade, 1, 1);
        Assert.Equal(Shading.HueShift(Green, lighter: false), Pixel(Shown(studio, pose.ItemId, 2), 1, 1));

        // Unlink the last: it keeps the picture, and changes on its own from now on.
        pose.PickFrames(new[] { 2 }, 2);
        pose.UnlinkFrameCommand.Execute(null);
        Assert.NotEqual(Picture(studio, pose.ItemId, 0), Picture(studio, pose.ItemId, 2));
        Assert.Null(pose.Frames[2].LinkColor);
        Assert.NotNull(pose.Frames[1].LinkColor);                                         // 1 and 2 still share frame 1's
        studio.Palette.Primary = Red;
        Dab(pose, StudioTool.Pencil, 2, 2);
        Assert.Equal(Red, Pixel(Shown(studio, pose.ItemId, 2), 2, 2));
        Assert.NotEqual(Red, Pixel(Shown(studio, pose.ItemId, 0), 2, 2));
        Assert.Equal(0, editor.Shell.ValidateNow().Errors);

        // A duplicate of the pose keeps its frames 1 and 2 linked.
        studio.Edit("Duplicate", lib => LibraryCopies.Duplicate(lib, LibraryKind.Pose, pose.ItemId, "robin-blink-2"));
        Assert.Equal(Picture(studio, "robin-blink-2", 0), Picture(studio, "robin-blink-2", 1));
        Assert.NotEqual(Picture(studio, "robin-blink-2", 0), Picture(studio, pose.ItemId, 0));
    }

    [Fact]
    public void PickedFramesReverseInTheirPlaces()
    {
        var (editor, studio, pose) = ThreeFrames("sway");
        using var _ = editor;
        var pictures = Enumerable.Range(0, 3).Select(i => Picture(studio, pose.ItemId, i)).ToList();
        pose.PickFrames(new[] { 0, 1, 2 }, 0);
        pose.ReversePickedCommand.Execute(null);
        Assert.Equal("Reverse 3 frames", editor.Shell.Document!.History.UndoLabel);
        Assert.Equal(pictures.AsEnumerable().Reverse(), Enumerable.Range(0, 3).Select(i => Picture(studio, pose.ItemId, i)));

        // An outfit's frames stay where the game wants them.
        studio.NewOutfit("shawl");
        var outfit = (PoseDocumentViewModel)studio.ActiveDocument!;
        outfit.PickFrames(new[] { 0, 1 }, 0);
        outfit.ReversePickedCommand.Execute(null);
        Assert.Equal("Frames stay in place", ((FakeUi)editor.Shell.Ui).Toasts[^1].Title);
    }

    [Fact]
    public void BackAndForthPlaysForwardsThenBackwards()
    {
        var (editor, studio, pose) = ThreeFrames("wave");
        using var _ = editor;
        pose.AddFrameCommand.Execute(null);                                               // four frames
        pose.PlaysBackAndForth = true;
        Assert.Equal(new[] { 0, 1, 2, 3, 2, 1 }, studio.Library!.Pose(pose.ItemId)!.Loop);
        Assert.True(pose.PlaysBackAndForth);
        Assert.False(pose.LoopsAll); Assert.False(pose.HasCustomOrder);
        Assert.Equal("Plays 1–4 and back, again and again", pose.PlayOrderText);
        Assert.Equal(new[] { "loop", "loop", "loop", "loop" }, pose.Frames.Select(f => f.Role));

        // The game plays it that way (the runtime's own timeline).
        var timeline = new PoseTimeline(studio.Library.Pose(pose.ItemId)!, "loop");
        var shown = new List<int>();
        for (int i = 0; i < 8; i++) { shown.Add(timeline.Frame); timeline.Advance(studio.Library.Pose(pose.ItemId)!.Frames[timeline.Frame].Duration); }
        Assert.Equal(new[] { 0, 1, 2, 3, 2, 1, 0, 1 }, shown);

        // Frames come and go; it stays back and forth.
        pose.FrameIndex = 3;
        pose.DeleteFrameCommand.Execute(null);
        Assert.Equal(new[] { 0, 1, 2, 1 }, studio.Library.Pose(pose.ItemId)!.Loop);
        pose.AddFrameCommand.Execute(null);
        Assert.Equal(new[] { 0, 1, 2, 3, 2, 1 }, studio.Library.Pose(pose.ItemId)!.Loop);
        pose.LoopsAll = true;
        Assert.False(pose.PlaysBackAndForth);
    }

    [Fact]
    public void MirrorViewFlipsOnlyTheView()
    {
        var (editor, studio, pose) = ThreeFrames("look");
        using var _ = editor;
        var before = Shown(studio, pose.ItemId, 0).Pixels;
        Assert.Equal(1, pose.ViewScaleX);
        Assert.True(pose.HandleKey("M", ctrl: false, shift: true));
        Assert.True(pose.MirrorView);
        Assert.Equal(-1, pose.ViewScaleX);
        Assert.Equal(before, Shown(studio, pose.ItemId, 0).Pixels);                       // the pose itself is untouched
        Assert.Equal(StudioTool.Select, pose.Tool);                                        // Shift+M isn't the Select key
    }

    [Fact]
    public void AnOutfitsFramesShowInGroups()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        studio.NewOutfit("cloak");
        var outfit = (PoseDocumentViewModel)studio.ActiveDocument!;
        Assert.Equal("Walking down", outfit.Frames[0].Group);
        Assert.Equal("", outfit.Frames[1].Group);
        Assert.Equal("Walking right", outfit.Frames[4].Group);
        Assert.Equal("Walking left", outfit.Frames[12].Group);
        var pose = CharacterStudioTests.NewPose(studio, "bow");
        Assert.Null(pose.Frames[0].Group);                                                 // a pose has no groups
    }
}
