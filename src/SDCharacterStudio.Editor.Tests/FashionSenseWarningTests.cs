using System.IO;
using Newtonsoft.Json.Linq;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Tests;

public sealed class FashionSenseWarningTests
{
    [Fact]
    public void WarningTracksFarmerSelectionAndCatalogReload()
    {
        using var export = new TempFolder();
        CopyDirectory(TestEnvironment.MiniExport, export.Path);
        var mods = Path.Combine(export.Path, "mods.json");
        var modList = JArray.Parse(File.ReadAllText(mods));
        modList.Add(new JObject { ["UniqueID"] = "PeacefulEnd.FashionSense", ["Name"] = "Fashion Sense", ["Version"] = "7.5.0" });
        File.WriteAllText(mods, modList.ToString());

        using var editor = new EditorHarness(export.Path);
        editor.NewProject();
        editor.Shell.IsCharacterStudio = true;
        var studio = editor.Shell.CharacterStudio;
        studio.Character = studio.Characters.First(c => c.IsFarmer);
        Assert.True(studio.ShowsFashionSenseWarning);

        studio.Character = studio.Characters.First(c => !c.IsFarmer);
        Assert.False(studio.ShowsFashionSenseWarning);

        modList.RemoveAt(modList.Count - 1);
        File.WriteAllText(mods, modList.ToString());
        editor.Shell.LoadCatalog();
        studio.Character = studio.Characters.First(c => c.IsFarmer);
        Assert.False(studio.ShowsFashionSenseWarning);
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), true);
    }
}
