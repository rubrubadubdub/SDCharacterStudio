using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Studio;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>D-48 stage 2: split a character into live puppet parts, then drag a part.</summary>
public sealed class PuppetPartsTests
{
    private static RgbaImage Composite(CharacterStudioViewModel studio, PoseDocumentViewModel pose) => studio.Context.RenderFrame(pose.Model, pose.FrameIndex).Image;

    [Fact]
    public void SplittingKeepsThePictureAndEveryPartStaysLive()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio, layered: true);
        var before = Composite(studio, pose);

        pose.SplitIntoPartsCommand.Execute(null);

        Assert.Equal(before.Pixels, Composite(studio, pose).Pixels);   // nothing moved yet
        var layers = pose.Frame!.Layers;
        Assert.False(layers[0].Visible);                                // the original, kept underneath
        var parts = layers.Skip(1).ToList();
        Assert.True(parts.Count >= 3);
        Assert.All(parts, p => Assert.IsType<LiveLayer>(p));            // still the real sprite
        var names = parts.Select(p => studio.Context.FindMask(p.Mask)!.DisplayName).ToList();
        Assert.Contains("Robin head", names);
        Assert.Contains("Robin legs", names);
        Assert.Equal("Split into parts", editor.Shell.Document!.History.UndoLabel);
        Assert.Equal(0, editor.Shell.ValidateNow().Errors);
    }

    [Fact]
    public void DraggingAPartMovesOnlyThatPart()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio, layered: true);
        pose.SplitIntoPartsCommand.Execute(null);
        var before = Composite(studio, pose);
        var head = pose.Frame!.Layers.Single(l => studio.Context.FindMask(l.Mask)?.DisplayName == "Robin head");
        var headPixels = studio.Context.RenderLayer(pose.Model, 0, head).Image;
        var (hx, hy) = Enumerable.Range(0, 32).SelectMany(y => Enumerable.Range(0, 16).Select(x => (x, y))).First(p => headPixels.Pixels[p.y * 16 + p.x] >> 24 != 0);
        Assert.Equal("Robin head", pose.PartAt(hx, hy));

        pose.Tool = StudioTool.Select;
        pose.PointerDown(hx, hy, PointerButton.Primary); pose.PointerMove(hx, hy + 2); pose.PointerUp(hx, hy + 2);

        head = pose.Frame.Layers.Single(l => studio.Context.FindMask(l.Mask)?.DisplayName == "Robin head");
        Assert.Equal(new[] { 0, 2 }, head.Offset);
        var others = pose.Frame.Layers.Where(l => l.Visible && !ReferenceEquals(l, head)).ToList();
        Assert.All(others, l => Assert.Equal(new[] { 0, 0 }, l.Offset));
        var after = Composite(studio, pose);
        Assert.Equal(PixelTools.Get(before, hx, hy), PixelTools.Get(after, hx, hy + 2));
        Assert.Equal("Move pixels", editor.Shell.Document!.History.UndoLabel);
    }

    [Fact]
    public void TheSplitterFindsHeadArmsBodyAndLegs()
    {
        // A front-facing figure: head rows 0-13; body columns 4-11 rows 14-31, legs apart (gap at column 7-8) from row 26.
        var image = new RgbaImage(16, 32, new uint[16 * 32]);
        for (int y = 2; y < 14; y++) for (int x = 4; x < 12; x++) image.Pixels[y * 16 + x] = 0xFFFFCC99;
        for (int y = 14; y < 32; y++) for (int x = 4; x < 12; x++) if (y < 26 || x < 7 || x > 8) image.Pixels[y * 16 + x] = 0xFFAA2222;
        var parts = PartSplitter.Split(image, (_, y) => y < 14, "down").ToDictionary(p => p.Name, p => p.Pixels);
        Assert.Equal(new[] { "legs", "body", "right arm", "left arm", "head" }, parts.Keys);
        bool In(string part, int x, int y) => parts[part][y * 16 + x];
        Assert.True(In("head", 6, 5));
        Assert.True(In("right arm", 4, 18)); Assert.True(In("left arm", 11, 18));   // the outer columns, viewer's left = her right
        Assert.True(In("body", 7, 18));
        Assert.True(In("legs", 5, 30)); Assert.True(In("legs", 10, 30));
        // Every shown pixel is in exactly one part.
        for (int i = 0; i < image.Pixels.Length; i++)
            Assert.Equal(image.Pixels[i] >> 24 != 0 ? 1 : 0, parts.Values.Count(p => p[i]));
    }
}
