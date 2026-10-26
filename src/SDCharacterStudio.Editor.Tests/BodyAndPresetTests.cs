using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Editor.Studio;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>D-49 phase 4: her body map (automatic, fixed with a brush, saved for her) and ready-made poses made from it.</summary>
public sealed class BodyAndPresetTests
{
    private static RgbaImage Composite(CharacterStudioViewModel studio, PoseDocumentViewModel pose, int frame) => studio.Context.RenderFrame(pose.Model, frame).Image;
    private static (int Top, int Bottom) Rows(RgbaImage image)
    {
        int top = -1, bottom = -1;
        for (int i = 0; i < image.Pixels.Length; i++) if (image.Pixels[i] >> 24 != 0) { int row = i / image.Width; if (top < 0) top = row; bottom = row; }
        return (top, bottom);
    }

    [Fact]
    public void SelectingAPartSelectsItsPixels()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio);
        var body = pose.BodyMapFor("left", out bool saved);
        Assert.NotNull(body);
        Assert.False(saved);   // a guess until fixed
        pose.SelectPartCommand.Execute("head");
        var head = pose.SelectionShape!;
        Assert.True(head.Count > 20);
        pose.SelectPartCommand.Execute("legs");
        var legs = pose.SelectionShape!;
        Assert.True(legs.Bounds.Y > head.Bounds.Y + head.Bounds.Height - 1);   // the legs are below the head
        Assert.Equal(StudioTool.Select, pose.Tool);
    }

    [Fact]
    public void FixingHerPartsIsSavedForHerAndPresetsFollow()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio);
        pose.SelectPartCommand.Execute("body");
        var (bx, by) = pose.SelectionShape!.Cells().First();   // a body pixel at the top of her body
        pose.Selection = null;

        pose.ShowParts = true;
        Assert.NotNull(pose.CanvasOverlay);
        pose.PartBrush = "head";
        pose.BrushSize = 1;
        pose.PointerDown(bx, by, PointerButton.Primary); pose.PointerUp(bx, by);
        Assert.Equal("Fix the body parts", editor.Shell.Document!.History.UndoLabel);
        var rig = studio.Library!.RigFor("Robin")!;
        Assert.True(rig.BodyMap!.ContainsKey("left"));
        Assert.Equal("head", BodyMaps.PartAt(pose.BodyMapFor("left", out bool saved)!, bx, by));
        Assert.True(saved);
        Assert.Equal(0, editor.Shell.ValidateNow().Errors);

        // Bowing moves that pixel with her head now.
        var standing = Composite(studio, pose, 0);
        pose.ShowParts = false;
        pose.ApplyPresetCommand.Execute("bow");
        var bowed = Composite(studio, pose, 0);
        Assert.Equal(PixelTools.Get(standing, bx, by), PixelTools.Get(bowed, bx - 1, by + 2));   // forward (left) and down

        pose.ResetBodyPartsCommand.Execute(null);
        Assert.Null(studio.Library!.RigFor("Robin")!.BodyMap);
        Assert.False(BodyMaps.PartAt(pose.BodyMapFor("left", out saved)!, bx, by) == "head" && saved);
    }

    [Fact]
    public void PresetsAreDrawnForHerAndStayHerPixels()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio);
        Assert.Equal(PosePresets.All.Count, pose.Presets.Count);
        Assert.All(pose.Presets, p => Assert.NotNull(p.Thumbnail));
        var standing = Composite(studio, pose, 0);
        var colours = standing.Pixels.Where(p => p >> 24 != 0).ToHashSet();
        var (top, bottom) = Rows(standing);

        pose.ApplyPresetCommand.Execute("kneel");
        var knelt = Composite(studio, pose, 0);
        var (kTop, kBottom) = Rows(knelt);
        Assert.Equal(bottom, kBottom);
        Assert.True(kTop > top);
        Assert.True(pose.IsFlat);
        Assert.Equal("Ready-made pose: Kneel", editor.Shell.Document!.History.UndoLabel);

        pose.ApplyPresetCommand.Execute("tilt");   // RotSprite: her colours only
        var tilted = Composite(studio, pose, 0);
        Assert.NotEqual(standing.Pixels, tilted.Pixels);
        Assert.All(tilted.Pixels.Where(p => p >> 24 != 0), p => Assert.Contains(p, colours));
        Assert.True(StudioImages.IsEmpty(studio.Context.PackImage(pose.Flat!.Paint.Image)!));

        pose.ApplyPresetCommand.Execute("nod");   // a little animation: frames, then it holds the last
        Assert.Equal(5, pose.Model.Frames.Count);
        Assert.True(pose.HoldsLast);
        Assert.Equal(standing.Pixels, Composite(studio, pose, 4).Pixels);
        Assert.NotEqual(standing.Pixels, Composite(studio, pose, 2).Pixels);
        Assert.Equal(0, editor.Shell.ValidateNow().Errors);
        editor.Shell.Document.Undo();
        Assert.Single(pose.Model.Frames);
    }
}

/// <summary>
/// A contact sheet of every preset on real art, for review (only with SDCS_PRESET_RENDER set to a PNG path and
/// SDCS_UI_EXPORT to a Bridge export profile). Rows: character and facing; columns: standing, then each preset (the
/// middle frame of an animated one).
/// </summary>
public sealed class PresetContactSheet
{
    [Fact]
    public void RenderPresetsOnRealArt()
    {
        if (Environment.GetEnvironmentVariable("SDCS_PRESET_RENDER") is not { Length: > 0 } output
            || Environment.GetEnvironmentVariable("SDCS_UI_EXPORT") is not { Length: > 0 } export) return;
        const int scale = 5, cell = 20;
        var rows = new[] { ("Robin", "left"), ("Robin", "down"), ("Abigail", "left"), ("Abigail", "down"), ("Abigail", "right"), ("Abigail", "up") };
        int columns = PosePresets.All.Count + 1;
        var sheet = new RgbaImage(columns * cell * scale, rows.Length * 36 * scale, new uint[columns * cell * scale * rows.Length * 36 * scale]);
        for (int i = 0; i < sheet.Pixels.Length; i++) sheet.Pixels[i] = 0xFF2A2D33;
        for (int r = 0; r < rows.Length; r++)
        {
            var (who, facing) = rows[r];
            using var editor = new EditorHarness(export);
            editor.NewProject();
            editor.Shell.IsCharacterStudio = true;
            var studio = editor.Shell.CharacterStudio;
            studio.Settings.AdvancedPoseTools = false;
            studio.Character = studio.Characters.First(c => c.Key == who);
            Assert.NotNull(studio.NewPose("character", $"{who} {facing}", facing));
            var pose = (PoseDocumentViewModel)studio.ActiveDocument!;
            for (int c = 0; c < columns; c++)
            {
                RgbaImage image;
                if (c == 0) image = studio.Context.RenderFrame(pose.Model, 0).Image;
                else
                {
                    var preset = PosePresets.All[c - 1];
                    var standing = studio.StandingFrame(studio.Character!, facing)!;
                    var body = pose.BodyMapFor(facing, out _)!;
                    image = Compositor.Remap(standing, PosePresets.Map(standing, body, preset.Frames[preset.Frames.Count / 2], facing));
                }
                for (int y = 0; y < image.Height; y++)
                    for (int x = 0; x < image.Width; x++)
                    {
                        uint pixel = image.Pixels[y * image.Width + x];
                        if (pixel >> 24 == 0) continue;
                        for (int dy = 0; dy < scale; dy++)
                            for (int dx = 0; dx < scale; dx++)
                                sheet.Pixels[((r * 36 + 2 + y) * scale + dy) * sheet.Width + (c * cell + 2 + x) * scale + dx] = pixel;
                    }
            }
        }
        System.IO.File.WriteAllBytes(output, StudioImages.Encode(sheet));
    }
}
