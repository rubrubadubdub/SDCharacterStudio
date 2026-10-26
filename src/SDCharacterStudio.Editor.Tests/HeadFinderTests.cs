using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Editor.Catalog;
using SDCharacterStudio.Editor.Studio;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>Head masks fit each character's own head, not row 15 for everyone (the owner: "she's cut below her nose").</summary>
public sealed class HeadFinderTests
{
    private const uint Hair = 0xFF6A2A90, Skin = 0xFFFFD8C0, Outline = 0xFF20101C, Shirt = 0xFF3050C0, Pants = 0xFF404048, Shoes = 0xFF502810;

    /// <summary>A villager drawn like the game's: hair from row 4, her face down to the chin outline, then shirt, pants, shoes.</summary>
    private static RgbaImage Villager(int chin, uint? shirt = null)
    {
        var image = new RgbaImage(16, 32, new uint[16 * 32]);
        void Row(int y, int from, int to, uint color) { for (int x = from; x <= to; x++) image.Pixels[y * 16 + x] = color; }
        for (int y = 4; y <= 10; y++) Row(y, 3, 12, Hair);
        for (int y = 11; y < chin; y++) { Row(y, 3, 12, Outline); Row(y, 4, 11, Skin); }
        Row(chin, 5, 10, Outline);
        for (int y = chin + 1; y <= 25; y++) Row(y, 2, 13, shirt ?? Shirt);
        for (int y = 26; y <= 29; y++) Row(y, 4, 11, Pants);
        for (int y = 30; y <= 31; y++) Row(y, 4, 11, Shoes);
        return image;
    }

    [Fact]
    public void TheHeadEndsAtHerChin()
    {
        Assert.Equal(16, HeadFinder.Detect(Villager(16), back: false));
        Assert.Equal(19, HeadFinder.Detect(Villager(19), back: false));   // a character drawn lower in her frame
    }

    [Fact]
    public void AHatTheColourOfHerCoatCantBeTold()
    {
        Assert.Null(HeadFinder.Detect(Villager(16, shirt: Hair), back: false));
        Assert.Null(HeadFinder.Rows("Somebody", _ => Villager(16, shirt: Hair)));
    }

    [Fact]
    public void SideViewsFollowTheFrontWithinAPixelAndTheBackMayReachLower()
    {
        // Hair hanging in front of her in a side view hides her shirt: that view would say the chin is lower.
        var rows = HeadFinder.Rows("Somebody", facing => Villager(facing switch { "right" => 19, "up" => 19, _ => 16 }))!;
        Assert.Equal(16, rows["down"]);
        Assert.Equal(17, rows["right"]);
        Assert.Equal(16, rows["left"]);
        Assert.Equal(19, rows["up"]);
    }

    [Fact]
    public void ARigMadeForHerCutsHerHeadAtHerChin()
    {
        var rows = new Dictionary<string, int> { ["down"] = 19, ["right"] = 19, ["up"] = 21, ["left"] = 18 };
        var result = RigFactory.ForNpc("Abigail", "abigail", 16, 32, rows);
        Assert.Equal(19, result.Rig.NeckPoint["down"][1]);
        Assert.Equal(21, result.Rig.NeckPoint["up"][1]);
        Assert.Equal(new[] { 4, 13, 8, 6 }, result.Rig.FaceBox["down"]);   // her face, 4 rows lower than the average one
        var down = result.Masks.Single(m => m.Mask.Id == "abigail-head-down").Image;
        Assert.True(down.Pixels[19 * 16 + 8] >> 24 != 0);
        Assert.True(down.Pixels[20 * 16 + 8] >> 24 == 0);
        // Without her sprite: the average rig, as before.
        Assert.Equal(15, RigFactory.ForNpc("Abigail", "abigail", 16, 32).Rig.NeckPoint["down"][1]);
    }

    [Fact]
    public void AnOlderRigsUntouchedHeadMasksGetHerHeadLineAndEditedOnesStay()
    {
        var (editor, studio) = CharacterStudioTests.OpenStudio();
        using var _ = editor;
        string rigId = "";
        studio.Edit("Old rig", lib => rigId = RigFactory.EnsureNpcRig(lib, editor.Shell.Catalog, "Robin"));
        var rig = studio.Library!.Rig(rigId)!;
        string edited = rig.HeadMask["left"];
        studio.Edit("Edit a mask", lib => lib.WriteImage(lib.Mask(edited)!.Image!, PixelTools.AboveRow(16, 32, 12)));

        var rows = new Dictionary<string, int> { ["down"] = 18, ["right"] = 18, ["up"] = 20, ["left"] = 18 };
        bool changed = false;
        studio.Edit("Fix heads", lib => changed = RigFactory.UpgradeAverageHeads(lib, lib.Rig(rigId)!, rows));

        Assert.True(changed);
        rig = studio.Library!.Rig(rigId)!;
        var library = studio.Library!;
        var down = library.ReadImage(library.Mask(rig.HeadMask["down"])!.Image)!;
        Assert.True(down.Pixels[18 * 16 + 8] >> 24 != 0);
        Assert.Equal(18, rig.NeckPoint["down"][1]);
        Assert.Equal(20, rig.NeckPoint["up"][1]);
        var left = library.ReadImage(library.Mask(edited)!.Image)!;
        Assert.True(left.Pixels[13 * 16 + 8] >> 24 == 0);   // the one someone edited is still theirs
        Assert.Equal(15, rig.NeckPoint["left"][1]);
        Assert.Equal(0, editor.Shell.ValidateNow().Errors);
    }

    /// <summary>On the real game (SDCS_UI_EXPORT or a native export): Abigail's head ends at her chin, not across her nose.</summary>
    [Fact]
    public void AbigailsHeadEndsAtHerChinInTheRealGame()
    {
        string profile = UiSnapshotTests.ExportProfile();
        var catalog = ExportCatalog.Load(profile);
        if (catalog?.Character("Abigail") is not { } abigail || StudioImages.LoadFile(catalog.TextureFile(abigail.Sprite)) is not { } sheet) return;
        var rows = HeadFinder.Rows("Abigail", facing => StudioImages.Frame(sheet, 16, 32, facing switch { "right" => 4, "up" => 8, "left" => 12, _ => 0 }))!;
        Assert.InRange(rows["down"], 18, 20);
        Assert.InRange(rows["right"], 18, 20);
        Assert.InRange(rows["left"], 18, 20);
    }
}
