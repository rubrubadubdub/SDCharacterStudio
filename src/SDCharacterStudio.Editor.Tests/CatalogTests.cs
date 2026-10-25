using System.IO;
using SDCharacterStudio.Editor.Catalog;

namespace SDCharacterStudio.Editor.Tests;

public class CatalogTests
{
    [Fact]
    public void LoadsCharactersLocationsItemsModsAndOrigins()
    {
        var catalog = TestEnvironment.LoadMini();
        Assert.Equal("mini", catalog.Profile);
        Assert.True(catalog.HasWorld);
        Assert.Contains(catalog.Characters, c => c.Name == "Robin");
        Assert.Equal("Seasonal Cute Characters", catalog.ModLabel("Poltergeister.SeasonalCuteCharacters"));
        Assert.True(catalog.AssetOrigin("Characters/Robin_Summer")!.IsModProvided);
        Assert.True(catalog.AssetOrigin("Characters\\Robin")!.IsEdited);
        Assert.True(catalog.AssetOrigin("Characters/Mystery_Outfit")!.IsUnknown);
        Assert.True(catalog.Location("ScienceHouse")!.Origin.IsEdited);
        Assert.Equal("Hardwood", catalog.Item("(O)709")!.Label);
        Assert.Equal("(O)709", catalog.Item("709")!.Qualified);
        Assert.Equal(new[] { "coin", "dwop", "getNewSpecialItem", "money", "shwip" }, catalog.Sounds);
    }

    [Fact]
    public void AnswersCoreCatalogQuestionsLikeTheRuntime()
    {
        var catalog = TestEnvironment.LoadMini();
        Assert.Equal("Robin", catalog.FindNpc("Robin"));
        Assert.Equal("Robin", catalog.FindNpc("robin"));   // canonical name for V7's exact-case suggestion
        Assert.Null(catalog.FindNpc("Nobody"));
        Assert.True(catalog.LocationExists("ScienceHouse"));
        Assert.False(catalog.LocationExists("Atlantis"));
        Assert.True(catalog.ItemExists("(O)336"));
        Assert.False(catalog.ItemExists("(O)99999"));         // a tracked type: really missing
        Assert.True(catalog.ItemExists("(W)4"));             // weapons aren't exported: can't judge
        Assert.True(catalog.HairstyleExists(0));
        Assert.False(catalog.HairstyleExists(500));
    }

    [Fact]
    public void FindsProfilesNewestFirst()
    {
        using var temp = new TempFolder();
        foreach (var (name, age) in new[] { ("old", -10), ("new", 0) })
        {
            Directory.CreateDirectory(Path.Combine(temp.Path, name));
            string file = Path.Combine(temp.Path, name, "catalog.json");
            File.WriteAllText(file, "{}");
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(age));
        }
        Directory.CreateDirectory(Path.Combine(temp.Path, "not-a-profile"));
        var profiles = ExportCatalog.FindProfiles(temp.Path).Select(Path.GetFileName).ToArray();
        Assert.Equal(new[] { "new", "old" }, profiles);
        Assert.Empty(ExportCatalog.FindProfiles(Path.Combine(temp.Path, "missing")));
    }

    [Fact]
    public void ReadsTheNativeModdedExportWhenPresent()
    {
        // The native M6 export (SVE installed) isn't committed; check it when this machine has it.
        string? root = TestEnvironment.RepoRoot;
        string profile = root == null ? "" : Path.Combine(root, ".superpowers", "sdd", "2026-10-24-m6-bridge-preview", "native", "Export", "modded-final");
        if (!File.Exists(Path.Combine(profile, "catalog.json"))) return;
        var catalog = ExportCatalog.Load(profile);
        Assert.True(catalog.Characters.Count > 50);
        Assert.Contains(catalog.Mods, m => m.UniqueID == "FlashShifter.StardewValleyExpandedCP");
        var scienceHouse = catalog.Location("ScienceHouse")!;
        Assert.Contains("FlashShifter.StardewValleyExpandedCP", scienceHouse.Origin.EditedBy);
        Assert.True(File.Exists(catalog.FullPath(scienceHouse.Back)));
        Assert.NotNull(catalog.TextureFile(catalog.Character("Robin")!.Sprite));
        Assert.NotNull(catalog.Player);
    }
}
