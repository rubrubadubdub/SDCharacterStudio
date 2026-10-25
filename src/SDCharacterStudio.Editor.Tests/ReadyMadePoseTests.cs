using System.IO;
using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Studio;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>D-48 stage 4: ready-made poses are recipes over live puppet parts.</summary>
public sealed class ReadyMadePoseTests
{
    private static RgbaImage Composite(CharacterStudioViewModel studio, PoseDocumentViewModel pose) => studio.Context.RenderFrame(pose.Model, pose.FrameIndex).Image;
    private static (int Top, int Bottom) Rows(RgbaImage image)
    {
        int top = -1, bottom = -1;
        for (int i = 0; i < image.Pixels.Length; i++) if (image.Pixels[i] >> 24 != 0) { int row = i / image.Width; if (top < 0) top = row; bottom = row; }
        return (top, bottom);
    }

    [Fact]
    public void KneelingDropsOntoTheKneesAndStaysLive()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio, layered: true);
        var (top, bottom) = Rows(Composite(studio, pose));

        pose.ApplyRecipe("kneel");

        var (kneelTop, kneelBottom) = Rows(Composite(studio, pose));
        Assert.Equal(bottom, kneelBottom);   // the knees are on the ground
        Assert.True(kneelTop > top, "Kneeling should bring the head down.");
        Assert.False(pose.Frame!.Layers[0].Visible);
        Assert.All(pose.Frame.Layers, l => Assert.IsType<LiveLayer>(l));
        Assert.Equal("Ready-made pose: Kneel", editor.Shell.Document!.History.UndoLabel);
        Assert.Equal(0, editor.Shell.ValidateNow().Errors);
    }

    [Fact]
    public void BowingMovesTheHeadDownAndForwardAndRecipesStartFresh()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio, layered: true);   // Robin facing left
        pose.ApplyRecipe("bow");
        var head = pose.Frame!.Layers.Single(l => studio.Context.FindMask(l.Mask)?.DisplayName == "Robin head");
        Assert.Equal(new[] { -1, 2 }, head.Offset);

        int layers = pose.Frame.Layers.Count;
        pose.ApplyRecipe("kneel");                           // replaces the bow, doesn't stack on it
        Assert.Equal(layers, pose.Frame!.Layers.Count);
        head = pose.Frame.Layers.Single(l => studio.Context.FindMask(l.Mask)?.DisplayName == "Robin head");
        Assert.Equal(0, head.Offset[0]);
    }

    [Fact]
    public void BegInASideViewKneelsAndBowsWithoutSeparateArms()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio, layered: true);   // a side view: head, body, legs
        pose.ApplyRecipe("beg");
        var names = pose.Frame!.Layers.Skip(1).Select(l => studio.Context.FindMask(l.Mask)!.DisplayName).ToList();
        Assert.DoesNotContain(names, n => n!.EndsWith("arm"));
        Assert.Equal("Ready-made pose: Beg", editor.Shell.Document!.History.UndoLabel);
    }

    [Fact]
    public void InTheSimpleEditorAReadyMadePoseIsStillOnePicture()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        var pose = CharacterStudioTests.NewPose(studio);
        var standing = Composite(studio, pose);
        var (top, bottom) = Rows(standing);

        pose.ApplyRecipe("kneel");

        Assert.True(pose.IsFlat);
        Assert.Equal(2, pose.Frame!.Layers.Count);
        var (kneelTop, kneelBottom) = Rows(Composite(studio, pose));
        Assert.Equal(bottom, kneelBottom);
        Assert.True(kneelTop > top, "Kneeling should bring the head down.");
        Assert.DoesNotContain(studio.Library!.Masks().Masks, m => m.Tags.Contains(PoseDocumentViewModel.EditMaskTag));
        Assert.Equal("Ready-made pose: Kneel", editor.Shell.Document!.History.UndoLabel);
        Assert.Equal(0, editor.Shell.ValidateNow().Errors);

        editor.Shell.Document.Undo();   // one step back to standing
        Assert.True(pose.IsFlat);
        Assert.Equal(standing.Pixels, Composite(studio, pose).Pixels);
    }

    /// <summary>
    /// A contact sheet of every recipe on real art, for review (only with SDCS_RECIPE_RENDER set to a PNG path and
    /// SDCS_UI_EXPORT to a Bridge export profile). Rows: character and facing; columns: standing, then each recipe.
    /// </summary>
    [Fact]
    public void RenderRecipesOnRealArt()
    {
        if (Environment.GetEnvironmentVariable("SDCS_RECIPE_RENDER") is not { Length: > 0 } output
            || Environment.GetEnvironmentVariable("SDCS_UI_EXPORT") is not { Length: > 0 } export) return;
        const int scale = 6, cell = 20;
        var rows = new[] { ("Robin", "left"), ("Robin", "down"), ("Abigail", "left"), ("Abigail", "down"), ("Abigail", "up") };
        var sheet = new RgbaImage((PoseRecipes.All.Count + 1) * cell * scale, rows.Length * 36 * scale, new uint[(PoseRecipes.All.Count + 1) * cell * scale * rows.Length * 36 * scale]);
        for (int i = 0; i < sheet.Pixels.Length; i++) sheet.Pixels[i] = 0xFF2A2D33;
        for (int r = 0; r < rows.Length; r++)
        {
            var (who, facing) = rows[r];
            using var editor = new EditorHarness(export);
            editor.NewProject();
            editor.Shell.IsCharacterStudio = true;
            var studio = editor.Shell.CharacterStudio;
            studio.Character = studio.Characters.First(c => c.Key == who);
            for (int c = 0; c <= PoseRecipes.All.Count; c++)
            {
                Assert.NotNull(studio.NewPose("character", $"{who} {facing} {c}", facing));
                var pose = (PoseDocumentViewModel)studio.ActiveDocument!;
                if (c > 0) pose.ApplyRecipe(PoseRecipes.All[c - 1].Id);
                var image = Composite(studio, pose);
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
        File.WriteAllBytes(output, StudioImages.Encode(sheet));
    }
}
