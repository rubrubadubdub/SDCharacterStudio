using System.IO;
using Newtonsoft.Json.Linq;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>Roadmap M7 AC 2 and 3: dependencies from mod content, map-edit variants and mixed-mod packs (Q-15).</summary>
public class DependencyTests
{
    private const string Scc = "Poltergeister.SeasonalCuteCharacters";
    private const string Sve = "FlashShifter.StardewValleyExpandedCP";
    private const string Rsv = "Rafseazz.RidgesideVillage";

    private static (JObject Manifest, JObject Studio) Export(EditorHarness editor)
    {
        var export = new ExportViewModel(editor.Shell);
        Assert.False(export.HasErrors, string.Join("\n", export.Validation.Problems.Select(p => p.Message)));
        export.ExportCommand.Execute(null);
        Assert.Null(export.Error);
        Assert.True(export.Done);
        string folder = editor.Shell.Document!.PackFolder;
        return (JObject.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json"))), JObject.Parse(File.ReadAllText(Path.Combine(folder, "studio.json"))));
    }

    private static void AddRobinInSccOutfit(EditorHarness editor)
    {
        editor.Shell.Stage.DropCharacter("Robin", 12, 10);
        var assets = editor.Shell.Assets;
        assets.SelectedCategory = assets.Categories.First(c => c.Id == "sprites");
        var summer = assets.Items.Single(i => i.Key == "Characters/Robin_Summer");
        Assert.Equal(BadgeKind.Provided, summer.Badge);
        Assert.Equal("SCC", summer.BadgeText);
        assets.Use(summer);   // Robin is selected after the drop: her starting outfit changes
        Assert.Equal("Characters/Robin_Summer", editor.Shell.Scene!.Actors.Single(a => a.Id == "Robin").Sprite!.Asset);
    }

    [Fact]
    public void SccOutfitBecomesARequiredDependencyWithAReason()
    {
        using var editor = new EditorHarness();
        editor.NewProject();
        AddRobinInSccOutfit(editor);

        var (manifest, studio) = Export(editor);
        var dependency = manifest["Dependencies"]!.Single(d => (string?)d["UniqueID"] == Scc);
        Assert.True((bool?)dependency["IsRequired"] ?? true);
        var requirement = studio["Requires"]!.Single(r => (string?)r["Mod"] == Scc);
        Assert.Contains("Characters/Robin_Summer", (string?)requirement["Reason"]);
        Assert.Contains("first-scene", (string?)requirement["Reason"]);
    }

    [Fact]
    public void OnlySomeScenesMakesTheModOptional()
    {
        using var editor = new EditorHarness();
        editor.NewProject();
        AddRobinInSccOutfit(editor);

        var dialog = new DependenciesViewModel(editor.Shell);
        var row = dialog.Providers.Single(p => p.ModId == Scc);
        Assert.False(row.OnlySomeScenes);
        row.OnlySomeScenes = true;
        dialog.ApplyCommand.Execute(null);

        var (manifest, studio) = Export(editor);
        Assert.False((bool)manifest["Dependencies"]!.Single(d => (string?)d["UniqueID"] == Scc)["IsRequired"]!);
        Assert.DoesNotContain(studio["Requires"] ?? new JArray(), r => (string?)r["Mod"] == Scc);

        // The choice is stored in the project, so the next export keeps it.
        Assert.Equal(Project.DependencyMode.Optional, editor.Shell.Document!.Project.ModeFor(Scc));
    }

    [Fact]
    public void EditedVanillaContentIsNoDependencyUnlessOptedIn()
    {
        using var editor = new EditorHarness();
        editor.NewProject(location: "ScienceHouse");   // SVE redraws ScienceHouse
        var (manifest, studio) = Export(editor);
        Assert.Empty(manifest["Dependencies"] ?? new JArray());
        Assert.Empty(studio["Requires"] ?? new JArray());

        var dialog = new DependenciesViewModel(editor.Shell);
        var sve = dialog.Editors.Single(e => e.ModId == Sve);
        Assert.False(sve.AddOptional);   // Q-7 default: off
        sve.AddOptional = true;
        dialog.ApplyCommand.Execute(null);
        (manifest, studio) = Export(editor);
        Assert.False((bool)manifest["Dependencies"]!.Single(d => (string?)d["UniqueID"] == Sve)["IsRequired"]!);
        Assert.Empty(studio["Requires"] ?? new JArray());
    }

    [Fact]
    public void UnknownProviderIsAskedOnceAndRemembered()
    {
        using var editor = new EditorHarness();
        editor.NewProject();
        editor.Shell.Stage.DropCharacter("Robin", 12, 10);
        var scene = editor.Shell.Scene!;
        editor.Shell.Edit("Mystery outfit", () => scene.Actors.Single(a => a.Id == "Robin").Sprite = new() { Asset = "Characters/Mystery_Outfit" });

        var validation = editor.Shell.ValidateNow();
        var problem = validation.Problems.Single(p => p.Rule == "V14");
        Assert.False(problem.IsError);
        Assert.Equal("Dependencies…", problem.FixLabel);

        bool opened = false;
        editor.Ui.Dialogs = vm =>
        {
            var dialog = Assert.IsType<DependenciesViewModel>(vm);
            dialog.Unknown.Single().Provider = Scc;
            dialog.ApplyCommand.Execute(null);
            opened = true;
            return true;
        };
        editor.Shell.Fix(problem);
        Assert.True(opened);
        Assert.DoesNotContain(editor.Shell.ValidateNow().Problems, p => p.Rule == "V14");
        var (_, studio) = Export(editor);
        Assert.Contains("Characters/Mystery_Outfit", (string?)studio["Requires"]!.Single(r => (string?)r["Mod"] == Scc)["Reason"]);
    }

    [Fact]
    public void MapEditedBySveWarnsAndMakesAHasModVariant()
    {
        using var editor = new EditorHarness();
        editor.NewProject(location: "ScienceHouse", firstScene: "Robin hello");
        var stage = editor.Shell.Stage;
        Assert.True(stage.HasMapWarning);
        Assert.Equal(Sve, stage.MapWarningMod);
        Assert.Contains("Stardew Valley Expanded", stage.MapWarning);

        stage.MakeVariantCommand.Execute(null);
        var doc = editor.Shell.Document!;
        Assert.Equal(2, doc.Cutscenes.Count);
        var original = doc.FindCutscene("robin-hello")!.Scene;
        var variant = doc.Cutscenes.Single(c => c.Scene.Id != "robin-hello").Scene;
        Assert.Equal("Player.SDCharacterStudio_HAS_MOD " + Sve, original.Trigger!.Conditions);
        Assert.Equal("!Player.SDCharacterStudio_HAS_MOD " + Sve, variant.Trigger!.Conditions);
        Assert.Equal(variant.Id, editor.Shell.CurrentCutsceneItem!.Id);   // the editor switches to the copy to restage it
        Assert.Equal(0, editor.Shell.ValidateNow().Errors);

        // The copy says what it is and never makes a variant of itself (the owner could click the button forever).
        Assert.True(stage.IsMapInfo);
        Assert.Contains("isn't installed", stage.MapWarning);
        Assert.Equal("Open the original", stage.MapActionText);
        stage.MakeVariantCommand.Execute(null);
        Assert.Equal(2, doc.Cutscenes.Count);
        Assert.Equal("robin-hello", editor.Shell.CurrentCutsceneItem!.Id);   // it went to the original instead
        Assert.True(stage.IsMapInfo);
        Assert.Equal("Open the variant", stage.MapActionText);
        Assert.Contains("only with", stage.MapWarning);
        stage.MapActionCommand.Execute(null);
        Assert.Equal(variant.Id, editor.Shell.CurrentCutsceneItem!.Id);
        Assert.Equal(2, doc.Cutscenes.Count);
        Assert.Equal("!Player.SDCharacterStudio_HAS_MOD " + Sve, variant.Trigger!.Conditions);   // untouched

        stage.DismissMapNoticeCommand.Execute(null);   // the banner closes for this scene
        Assert.False(stage.HasMapWarning);
        stage.Rebuild();
        Assert.False(stage.HasMapWarning);
        editor.Shell.OpenCutscene("robin-hello");
        Assert.True(stage.HasMapWarning);   // other scenes still say theirs

        while (doc.Undo()) { }
        Assert.Single(editor.Shell.Document!.Cutscenes);
    }

    [Fact]
    public void MixedSveAndRsvPackFollowsQ15()
    {
        using var editor = new EditorHarness();
        editor.NewProject(firstScene: "Sophia visits");
        editor.Shell.Stage.DropCharacter("Sophia", 12, 10);
        editor.Ui.Prompts.Enqueue("Ridgeside walk");
        editor.Shell.AddCutsceneCommand.Execute(null);
        editor.Shell.Stage.SetLocation("Custom_Ridgeside_RidgesideVillage");
        Assert.Contains(editor.Ui.Toasts, t => t.Message.Contains("Ridgeside Village"));

        var dialog = new DependenciesViewModel(editor.Shell);
        Assert.Equal(new[] { "Ridgeside Village", "Stardew Valley Expanded" }, dialog.Providers.Select(p => p.Name).ToArray());
        Assert.Equal(new[] { "ridgeside-walk" }, dialog.Providers.Single(p => p.ModId == Rsv).Dependency.Cutscenes.ToArray());
        // Default (D-7): both required. The designer marks each as only needed by some scenes.
        foreach (var row in dialog.Providers) row.OnlySomeScenes = true;
        dialog.ApplyCommand.Execute(null);

        var (manifest, studio) = Export(editor);
        Assert.All(manifest["Dependencies"]!, d => Assert.False((bool)d["IsRequired"]!));
        Assert.Equal(new[] { Sve, Rsv }, manifest["Dependencies"]!.Select(d => (string)d["UniqueID"]!).OrderBy(x => x, StringComparer.Ordinal).ToArray());
        Assert.Empty(studio["Requires"] ?? new JArray());
    }
}
