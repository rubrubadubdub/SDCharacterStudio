using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Editor.Studio;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>D-49 phase 3: the play order in plain words, the live preview, several frames at once, the idle loop.</summary>
public sealed class AnimateTests
{
    private static RgbaImage Composite(CharacterStudioViewModel studio, PoseDocumentViewModel pose, int frame) => studio.Context.RenderFrame(pose.Model, frame).Image;

    private static (CharacterStudioViewModel Studio, PoseDocumentViewModel Pose, EditorHarness Editor) Open()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        return (studio, CharacterStudioTests.NewPose(studio), editor);
    }

    [Fact]
    public void ThePlayOrderReadsPlainlyAndSurvivesFrameChanges()
    {
        var (studio, pose, editor) = Open();
        using var _ = editor;
        Assert.True(pose.LoopsAll);
        Assert.Equal("One frame: it holds", pose.PlayOrderText);
        pose.AddFrameCommand.Execute(null);
        pose.AddFrameCommand.Execute(null);
        Assert.Equal("Loops all 3 frames", pose.PlayOrderText);
        Assert.All(pose.Frames, f => Assert.Equal("loop", f.Role));

        pose.LoopFromNumber = 2;   // frame 1 once, then 2–3 loop
        Assert.True(pose.PlaysOnceThenLoops);
        Assert.Equal(new[] { 0 }, pose.Model.Intro);
        Assert.Equal(new[] { 1, 2 }, pose.Model.Loop);
        Assert.Equal(new[] { "once", "loop", "loop" }, pose.Frames.Select(f => f.Role));
        Assert.Equal("Play order", editor.Shell.Document!.History.UndoLabel);

        pose.FrameIndex = 0;
        pose.AddFrameCommand.Execute(null);   // a copy of the "once" frame plays once too
        Assert.Equal(new[] { 0, 1 }, pose.Model.Intro);
        Assert.Equal(new[] { 2, 3 }, pose.Model.Loop);

        pose.MoveFrameTo(3, 0, copy: false);   // order goes by position
        Assert.Equal(new[] { 0, 1 }, pose.Model.Intro);
        Assert.Equal(new[] { 2, 3 }, pose.Model.Loop);

        pose.HoldsLast = true;
        Assert.Equal(new[] { 0, 1, 2 }, pose.Model.Intro);
        Assert.Equal(new[] { 3 }, pose.Model.Loop);
        Assert.Equal("Plays frames 1–3, then holds frame 4", pose.PlayOrderText);
        pose.LoopsAll = true;
        Assert.Empty(pose.Model.Intro);
        Assert.Empty(pose.Model.Loop);
        Assert.Equal(0, editor.Shell.ValidateNow().Errors);
    }

    [Fact]
    public void FramesDragToAnotherPlaceOrCopy()
    {
        var (studio, pose, editor) = Open();
        using var _ = editor;
        pose.AddFrameCommand.Execute(null);
        pose.AddFrameCommand.Execute(null);
        for (int i = 0; i < 3; i++) pose.SetFrameDuration(i, 100 + i);
        pose.MoveFrameTo(0, 3, copy: false);
        Assert.Equal(new[] { 101, 102, 100 }, pose.Model.Frames.Select(f => f.Duration));
        Assert.Equal(2, pose.FrameIndex);
        pose.MoveFrameTo(1, 0, copy: true);
        Assert.Equal(new[] { 102, 101, 102, 100 }, pose.Model.Frames.Select(f => f.Duration));
        Assert.Equal("Copy frame", editor.Shell.Document!.History.UndoLabel);
        var flats = pose.Model.Frames.Select(PoseDocumentViewModel.FlatOf).ToList();
        Assert.All(flats, f => Assert.NotNull(f));
        Assert.Equal(4, flats.Select(f => f!.Paint.Image).Distinct().Count());   // the copy owns its pixels
    }

    [Fact]
    public void ThePreviewPlaysAndShowsEdits()
    {
        var (studio, pose, editor) = Open();
        using var _ = editor;
        pose.AddFrameCommand.Execute(null);
        pose.Selection = new PixelRect(0, 0, 16, 12);
        pose.NudgeSelection(0, 2);   // frame 2: the head lower
        pose.Selection = null;
        pose.FrameIndex = 0;
        pose.TickPreview(0);
        var first = pose.PreviewImage;
        Assert.NotNull(first);
        pose.TickPreview(pose.Model.Frames[0].Duration + 5);
        Assert.NotSame(first, pose.PreviewImage);   // moved on to frame 2

        pose.PreviewPlaying = false;   // paused: it shows the frame you're on, and your edits
        var paused = pose.PreviewImage;
        pose.Tool = StudioTool.Eraser;
        pose.PointerDown(8, 20, PointerButton.Primary); pose.PointerUp(8, 20);
        pose.TickPreview(16);
        Assert.NotSame(paused, pose.PreviewImage);
    }

    [Fact]
    public void AMoveAndATransformApplyToEveryPickedFrame()
    {
        var (studio, pose, editor) = Open();
        using var _ = editor;
        pose.AddFrameCommand.Execute(null);
        pose.AddFrameCommand.Execute(null);
        var before = Enumerable.Range(0, 3).Select(i => Composite(studio, pose, i)).ToList();
        pose.FrameIndex = 0;
        pose.PickFrames(new[] { 0, 1, 2 });
        Assert.True(pose.HasSeveralPicked);

        pose.Tool = StudioTool.Select;
        pose.PointerDown(0, 0, PointerButton.Primary); pose.PointerMove(15, 11); pose.PointerUp(15, 11);
        pose.PointerDown(5, 5, PointerButton.Primary); pose.PointerMove(5, 7); pose.PointerUp(5, 7);
        for (int frame = 0; frame < 3; frame++)
        {
            var after = Composite(studio, pose, frame);
            for (int y = 0; y < 12; y++)
                for (int x = 0; x < 16; x++)
                    if (PixelTools.Get(before[frame], x, y) >> 24 != 0) Assert.Equal(PixelTools.Get(before[frame], x, y), PixelTools.Get(after, x, y + 2));
        }
        Assert.Equal("Move pixels", editor.Shell.Document!.History.UndoLabel);

        // A quarter turn of the same selection turns it in every picked frame, as one step.
        var moved = Enumerable.Range(0, 3).Select(i => Composite(studio, pose, i).Pixels).ToList();
        Assert.True(pose.HandleKey("R", false, true));
        Assert.All(Enumerable.Range(1, 2), i => Assert.NotEqual(moved[i], Composite(studio, pose, i).Pixels));
        Assert.Equal(Composite(studio, pose, 0).Pixels, Composite(studio, pose, 1).Pixels);   // the copies stay alike
        editor.Shell.Document.Undo();
        Assert.All(Enumerable.Range(0, 3), i => Assert.Equal(moved[i], Composite(studio, pose, i).Pixels));
    }

    [Fact]
    public void ABreathingLoopFromHerOwnSprite()
    {
        var (studio, pose, editor) = Open();
        using var _ = editor;
        var standing = Composite(studio, pose, 0);
        pose.MakeIdleLoopCommand.Execute(null);
        Assert.Equal(2, pose.Model.Frames.Count);
        Assert.True(pose.LoopsAll);
        Assert.All(pose.Model.Frames, f => Assert.True(f.Duration >= 600));
        var breath = Composite(studio, pose, 1);
        int top = Enumerable.Range(0, pose.Height).First(y => Enumerable.Range(0, pose.Width).Any(x => PixelTools.Get(standing, x, y) >> 24 != 0));
        // Her head is a pixel lower; her feet haven't moved.
        for (int x = 0; x < pose.Width; x++)
        {
            Assert.Equal(PixelTools.Get(standing, x, top), PixelTools.Get(breath, x, top + 1));
            Assert.Equal(PixelTools.Get(standing, x, pose.Height - 1), PixelTools.Get(breath, x, pose.Height - 1));
        }
        Assert.True(StudioImages.IsEmpty(studio.Context.PackImage(pose.Flat!.Paint.Image)!));   // still her live pixels
        Assert.Equal("Idle loop", editor.Shell.Document!.History.UndoLabel);
        Assert.Equal(0, editor.Shell.ValidateNow().Errors);
    }
}
