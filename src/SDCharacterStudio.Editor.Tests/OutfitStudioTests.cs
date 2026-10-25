using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Schema;
using SDCharacterStudio.Editor.Studio;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>
/// Your own outfits (the owner, 2026-10-25: "what if I built my own clothes for Abigail?"): a whole sprite sheet to paint
/// clothes on, listed with the game's outfits, worn by the poses made from it, and put on in scenes.
/// </summary>
public sealed class OutfitStudioTests
{
    private const uint Yellow = 0xFFE8C020, Green = 0xFF30A050;

    /// <summary>A raincoat for Robin with a yellow pixel painted on her frame 0 (and the outfit open).</summary>
    private static (EditorHarness Editor, CharacterStudioViewModel Studio, string Id, PoseDocumentViewModel Outfit) Raincoat()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        string id = studio.NewOutfit("raincoat")!;
        var outfit = Assert.IsType<PoseDocumentViewModel>(studio.ActiveDocument);
        outfit.FrameIndex = 0;
        Paint(outfit, studio, 8, 20, Yellow);
        return (editor, studio, id, outfit);
    }

    private static void Paint(PoseDocumentViewModel doc, CharacterStudioViewModel studio, int x, int y, uint color)
    {
        doc.Tool = StudioTool.Pencil;
        studio.Palette.Primary = color;
        doc.PointerDown(x, y, PointerButton.Primary);
        doc.PointerUp(x, y);
    }

    private static uint Pixel(RgbaImage image, int x, int y) => image.Pixels[y * image.Width + x];

    [Fact]
    public void ANewOutfitIsHerWholeSheetToPaintOnAndTheStudioShowsHerInIt()
    {
        var (editor, studio, id, outfit) = Raincoat();
        using var _ = editor;
        Assert.Equal("robin-raincoat", id);
        var pose = studio.Library!.Pose(id)!;
        Assert.Equal("sheet", pose.Kind);
        Assert.Equal(new[] { 64, 128 }, pose.SheetSize);                        // her sheet's size: every frame, in its place
        Assert.Equal(16, pose.Frames.Count);
        Assert.All(pose.Frames, f => Assert.NotNull(PoseDocumentViewModel.FlatOf(f)));   // each frame one picture to paint on
        Assert.Equal(5, ((LiveLayer)pose.Frames[5].Layers[0]).SourceFrame!.Index);
        Assert.Equal("Npc", ((LiveLayer)pose.Frames[0].Layers[0]).Source!.Kind);   // her own look: follows seasons and sprite mods
        Assert.Equal(0, editor.Shell.ValidateNow().Errors);

        // The outfit editor: no play order, no ready-made poses, frames stay in place.
        Assert.True(outfit.IsSheet);
        Assert.False(outfit.ShowsPlayOrder); Assert.False(outfit.ShowsReadyMade); Assert.False(outfit.CanChangeFrames);
        Assert.Equal("Frame 6 of 16 · walking right, step 2", (outfit.FrameIndex = 5) == 5 ? outfit.FrameCaption : null);
        outfit.AddFrameCommand.Execute(null);
        Assert.Equal(16, studio.Library.Pose(id)!.Frames.Count);
        Assert.Equal("Frames stay in place", ((FakeUi)editor.Shell.Ui).Toasts[^1].Title);

        // Listed first among her outfits, picked, and the studio draws her in it everywhere.
        var choice = studio.Outfits[1];
        Assert.True(choice.IsYours);
        Assert.Equal(("Raincoat", "your outfit"), (choice.Label, choice.Detail));
        Assert.Same(choice, studio.Outfit);
        var sheet = studio.Context.NpcTexture("Robin", false)!;
        Assert.Equal(Yellow, Pixel(sheet, 8, 20));                                   // frame 0's paint on the sheet
        Assert.Equal(sheet.Pixels, studio.Context.OutfitSheet(id)!.Pixels);

        // Its own shelf.
        Assert.Contains(studio.Sections.Single(s => s.Title == "Outfits").Items, i => i.Item.Id == id);
        Assert.DoesNotContain(studio.Sections.Single(s => s.Title == "Poses").Items, i => i.Item.Id == id);
    }

    [Fact]
    public void AnOutfitNeverDrawsOnTopOfItselfOrWearsItself()
    {
        var (editor, studio, id, outfit) = Raincoat();
        using var _ = editor;
        // The studio shows the raincoat, but the raincoat itself is drawn from her own look: its paint once, not twice.
        Assert.Equal(StudioContext.OutfitKey(id), studio.Outfit!.Asset);
        var own = studio.Context.NpcTexture("Robin", false, ownOutfits: false)!;
        var drawn = studio.Context.RenderFrame(outfit.Model, 1).Image;
        Assert.Equal(StudioImages.Frame(own, 16, 32, 1)!.Pixels, drawn.Pixels);     // an unpainted frame is just her
        Assert.False(outfit.CanPinOutfit);                                            // the only outfit shown is itself
        outfit.WearOutfit(StudioContext.OutfitKey(id));
        Assert.Equal("Not itself", ((FakeUi)editor.Shell.Ui).Toasts[^1].Title);
        Assert.Equal("Npc", ((LiveLayer)studio.Library!.Pose(id)!.Frames[0].Layers[0]).Source!.Kind);
    }

    [Fact]
    public void APoseStartedFromYourOutfitWearsItAndStaysLinked()
    {
        var (editor, studio, id, _) = Raincoat();
        using var __ = editor;
        // Made while the raincoat is shown: it wears it from the start.
        var bow = CharacterStudioTests.NewPose(studio, "bow");
        Assert.Equal(id, ((LiveLayer)bow.Model.Frames[0].Layers[0]).Source!.Outfit);
        Assert.Equal("Wears your outfit", ((FakeUi)editor.Shell.Ui).Toasts[^1].Title);

        // Made in her own look, then started from the raincoat's frames: it wears it from then on.
        var raincoat = studio.Outfit;
        studio.Outfit = studio.Outfits[0];
        var wave = CharacterStudioTests.NewPose(studio, "wave");
        Assert.Equal("Npc", ((LiveLayer)wave.Model.Frames[0].Layers[0]).Source!.Kind);
        studio.Outfit = raincoat;
        wave.ShowSheetFrames = true;
        Assert.Equal(16, wave.SheetFrames.Count);                                    // her frames, in your outfit
        wave.StartFromSheetFrame(0);

        var live = (LiveLayer)wave.Model.Frames[0].Layers[0];
        Assert.Equal((Outfits.SourceKind, id), (live.Source!.Kind, live.Source.Outfit));
        Assert.Equal("Wears your outfit", ((FakeUi)editor.Shell.Ui).Toasts[^1].Title);
        Assert.Equal("This pose always wears: Raincoat.", wave.OutfitText);
        Assert.Equal(Yellow, Pixel(studio.Context.RenderFrame(wave.Model, 0).Image, 8, 20));

        // Shown in her own look again, the pose still wears the raincoat.
        studio.Outfit = studio.Outfits[0];
        Assert.Equal(Yellow, Pixel(studio.Context.RenderFrame(studio.Library!.Pose(wave.ItemId)!, 0).Image, 8, 20));

        // Paint more on the outfit: the pose shows it (one outfit, worn by every pose made from it).
        studio.OpenItem(LibraryKind.Pose, id);
        var outfit = (PoseDocumentViewModel)studio.ActiveDocument!;
        outfit.FrameIndex = 0;
        Paint(outfit, studio, 9, 21, Green);
        Assert.Equal(Green, Pixel(studio.Context.RenderFrame(studio.Library.Pose(wave.ItemId)!, 0).Image, 9, 21));
        Assert.Equal(0, editor.Shell.ValidateNow().Errors);

        // Renaming the outfit follows the poses that wear it; "used by" says who.
        var idField = outfit.Fields.OfType<TextField>().First(f => f.Label == "ID");
        idField.Value = "robin-yellow-coat";
        Assert.Equal("robin-yellow-coat", ((LiveLayer)studio.Library.Pose(wave.ItemId)!.Frames[0].Layers[0]).Source!.Outfit);
        Assert.Contains("pose " + wave.ItemId, References.UsesOf(studio.Library, editor.Shell.Document!, LibraryKind.Pose, "robin-yellow-coat"));
        Assert.Equal(0, editor.Shell.ValidateNow().Errors);
    }

    [Fact]
    public void ANewOutfitStartsFromTheOutfitShown()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        studio.Outfit = studio.Outfits.First(o => o.Asset == "Characters/Robin_Summer");
        string summer = studio.NewOutfit("summer coat")!;
        var source = ((LiveLayer)studio.Library!.Pose(summer)!.Frames[0].Layers[0]).Source!;
        Assert.Equal(("NpcSheet", "Characters/Robin_Summer"), (source.Kind, source.Asset));
        Assert.Equal("Made from: Summer.", ((PoseDocumentViewModel)studio.ActiveDocument!).OutfitText);

        // Made from one of yours (it's shown now): an outfit on an outfit.
        string hooded = studio.NewOutfit("hooded")!;
        source = ((LiveLayer)studio.Library.Pose(hooded)!.Frames[0].Layers[0]).Source!;
        Assert.Equal((Outfits.SourceKind, summer), (source.Kind, source.Outfit));
        Assert.Equal(0, editor.Shell.ValidateNow().Errors);
    }

    [Fact]
    public void DrawingWithSeveralFramesPickedDrawsOnAllOfThem()
    {
        var (editor, studio, id, outfit) = Raincoat();
        using var _ = editor;
        outfit.PickFrames(new[] { 0, 1, 2, 3 }, 0);                                  // the four steps walking down
        Paint(outfit, studio, 3, 12, Green);
        Assert.Equal("Draw on 4 frames", editor.Shell.Document!.History.UndoLabel);
        var sheet = studio.Context.OutfitSheet(id)!;
        for (int frame = 0; frame < 4; frame++) Assert.Equal(Green, Pixel(sheet, frame * 16 + 3, 12));
        Assert.NotEqual(Green, Pixel(sheet, 3, 32 + 12));                            // frame 4 wasn't picked

        // Erasing too, and one undo takes it back from all of them.
        outfit.Tool = StudioTool.Eraser;
        outfit.PointerDown(3, 12, PointerButton.Primary); outfit.PointerUp(3, 12);
        Assert.Equal("Erase on 4 frames", editor.Shell.Document.History.UndoLabel);
        sheet = studio.Context.OutfitSheet(id)!;
        for (int frame = 0; frame < 4; frame++) Assert.Equal(0u, Pixel(sheet, frame * 16 + 3, 12) >> 24);
        editor.Shell.Document.Undo();
        studio.Context.InvalidatePack();
        sheet = studio.Context.OutfitSheet(id)!;
        for (int frame = 0; frame < 4; frame++) Assert.Equal(Green, Pixel(sheet, frame * 16 + 3, 12));
    }

    [Fact]
    public void APoseTakesTheOutfitItWearsToMyLibrary()
    {
        using var shelf = new TempFolder();
        var personal = new PersonalLibrary(System.IO.Path.Combine(shelf.Path, "Library"));
        var (editorA, studioA, id, _) = Raincoat();
        studioA.Personal = personal;
        string bow = studioA.NewPose("character", "bow", "down")!;   // facing down (the painted frame), wearing the raincoat shown
        var entry = studioA.SaveToMyLibrary(LibraryKind.Pose, bow)!;
        Assert.True(System.IO.File.Exists(System.IO.Path.Combine(entry.Folder, "library", "poses", id + ".json")));
        editorA.Dispose();

        var (editorB, studioB) = CharacterStudioTests.OpenStudio();
        using var __ = editorB;
        studioB.Personal = personal;
        studioB.ShowMyLibrary = true;
        studioB.UseFromMyLibrary(Assert.Single(studioB.MyPoses));
        Assert.Equal("sheet", studioB.Library!.Pose(id)!.Kind);
        Assert.Equal(id, ((LiveLayer)studioB.Library.Pose("robin-bow")!.Frames[0].Layers[0]).Source!.Outfit);
        Assert.Equal(Yellow, Pixel(studioB.Context.RenderFrame(studioB.Library.Pose("robin-bow")!, 0).Image, 8, 20));
        Assert.Equal(0, editorB.Shell.ValidateNow().Errors);
    }

    [Fact]
    public void ScenesPutYourOutfitOnAndTheStageShowsIt()
    {
        var (editor, studio, id, _) = Raincoat();
        using var __ = editor;
        var shell = editor.Shell;
        shell.IsCharacterStudio = false;
        shell.Stage.DropCharacter("Robin", 6, 5);
        var scene = shell.Scene!;
        var doc = shell.Document!;
        doc.Edit("costume", () => scene.Beats.Add(new CostumeBeat { Actor = "Robin", Sprite = new SpriteReference { Asset = "Characters/Robin" } }));
        shell.Select(new BeatSelection(scene.Id!, LaneNames.Main, scene.Beats.Count - 1));
        editor.Choose("Change", "sheet");
        Assert.Equal(id, ((CostumeBeat)scene.Beats[^1]).Sprite!.Pose);              // her outfit, picked for her
        var outfitField = editor.Field<ChoiceField>("Outfit");
        Assert.Contains(outfitField.Options, o => o.Value == id && o.Label == "Raincoat" && o.Image != null);
        Assert.Equal(0, shell.ValidateNow().Errors);

        // On the stage at that beat she wears it.
        var robin = scene.Actors.First(a => a.Npc == "Robin" || a.Id == "Robin");
        Assert.Equal(StudioContext.OutfitKey(id), StageLooks.For(scene, robin, doc, shell.Selection).SpriteAsset);
        var onStage = shell.Stage.Actors.First(a => a.Id == robin.Id);
        Assert.Equal("Wearing Raincoat", onStage.Outfit);
        Assert.True(onStage.HasSprite);

        // Or from the start.
        shell.Select(new ActorSelection(scene.Id!, scene.Actors.IndexOf(robin)));
        editor.Choose("Outfit at the start", StudioContext.OutfitKey(id));
        Assert.Equal(id, robin.Sprite!.Pose);
        Assert.Null(robin.Sprite.Asset);
        Assert.Equal(0, shell.ValidateNow().Errors);
    }
}
