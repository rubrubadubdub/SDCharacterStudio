using System.IO;
using System.Windows;
using System.Windows.Media;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Infrastructure;
using SDCharacterStudio.Editor.Schema;
using SDCharacterStudio.Editor.Studio;
using SDCharacterStudio.Editor.ViewModels;
using Wpf.Ui.Appearance;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>
/// Your own outfits on real art: a raincoat painted on Abigail's walk, listed with her outfits, a pose started from it,
/// and a Costume beat putting it on. Off by default: set SDCS_STUDIO_SNAPSHOTS to an output folder, and SDCS_UI_EXPORT
/// to a Bridge export profile (read only).
/// </summary>
public class OutfitSnapshotTests
{
    [Fact]
    public void RenderOutfits()
    {
        string? output = Environment.GetEnvironmentVariable("SDCS_STUDIO_SNAPSHOTS");
        if (string.IsNullOrEmpty(output)) return;
        EditorUiThread.Run(() => Run(output));
    }

    private static void Run(string output)
    {
        Directory.CreateDirectory(output);
        using var temp = new TempFolder();
        ApplicationThemeManager.Apply(ApplicationTheme.Dark, Wpf.Ui.Controls.WindowBackdropType.None, updateAccent: false);
        ApplicationAccentColorManager.Apply(Color.FromRgb(0x4C, 0xB0, 0x7A), ApplicationTheme.Dark);
        string profile = UiSnapshotTests.ExportProfile();
        var settings = new EditorSettings
        {
            ExportFolder = Path.GetDirectoryName(profile)!, ExportProfile = profile, ProjectsFolder = temp.Path, GameModsFolder = null,
            PipeName = "SDCS.Outfits." + Guid.NewGuid().ToString("N"), WindowMaximized = false, WindowWidth = 1600, WindowHeight = 1000,
        };
        var window = new MainWindow { WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.None, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
        var ui = new WpfUserInterface(window);
        var shell = new MainViewModel(ui, settings);
        window.Attach(shell, ui);
        window.Left = SystemParameters.VirtualScreenLeft - 4000; window.Top = 0;
        window.Show();
        shell.LoadCatalog();
        var dialog = new NewProjectViewModel(shell) { Name = "Outfits", Author = "Player", UniqueId = "Player.Outfits", FirstScene = "Rainy day", Location = "Town", ParentFolder = temp.Path };
        dialog.CreateCommand.Execute(null);
        shell.Attach(dialog.Created!);
        shell.IsCharacterStudio = true;
        var studio = shell.CharacterStudio;
        studio.Settings.AdvancedPoseTools = false;
        studio.Character = studio.Characters.First(c => c.Key == "Abigail");

        // A yellow raincoat over her body on the four steps walking down, painted once with all four picked.
        studio.NewOutfit("raincoat");
        var outfit = (PoseDocumentViewModel)studio.ActiveDocument!;
        outfit.Zoom = 12;
        outfit.PickFrames(new[] { 0, 1, 2, 3 }, 0);
        outfit.Tool = StudioTool.Rectangle;
        outfit.FilledShapes = true;
        studio.Palette.Primary = 0xFFE8C020;
        outfit.PointerDown(3, 16, PointerButton.Primary); outfit.PointerMove(12, 26); outfit.PointerUp(12, 26);
        studio.Palette.Primary = 0xFF8A6A10;
        outfit.Tool = StudioTool.Line;
        outfit.PointerDown(3, 26, PointerButton.Primary); outfit.PointerMove(12, 26); outfit.PointerUp(12, 26);
        outfit.PickFrames(new[] { 1 }, 1);
        UiSnapshotTests.Pump(800);
        UiSnapshotTests.Save(window, Path.Combine(output, "70-outfit-editor.png"));

        // A new pose: her frames come in the raincoat; starting from one, the pose wears it.
        var wave = studio.NewPose("character", "wave", "down") is { } waveId ? (PoseDocumentViewModel)studio.ActiveDocument! : null;
        wave!.Zoom = 12;
        wave.ShowSheetFrames = true;
        UiSnapshotTests.Pump(800);
        UiSnapshotTests.Save(window, Path.Combine(output, "71-pose-her-frames.png"));
        wave.StartFromSheetFrame(0);
        wave.OutfitOpen = true;
        UiSnapshotTests.Pump(800);
        UiSnapshotTests.Save(window, Path.Combine(output, "72-pose-wears-outfit.png"));

        // A scene: a Costume beat puts it on, and the stage shows her in it.
        shell.IsCharacterStudio = false;
        shell.Stage.DropCharacter("Abigail", 30, 60);
        var scene = shell.Scene!;
        shell.Document!.Edit("costume", () => scene.Beats.Add(new CostumeBeat { Actor = "Abigail", Sprite = new SpriteReference { Asset = "Characters/Abigail" } }));
        shell.Select(new BeatSelection(scene.Id!, LaneNames.Main, scene.Beats.Count - 1));
        var change = shell.Inspector.Fields.OfType<ChoiceField>().First(f => f.Label == "Change");
        change.Selected = change.Options.First(o => o.Value == "sheet");
        UiSnapshotTests.Pump(1200);
        UiSnapshotTests.Save(window, Path.Combine(output, "73-costume-beat.png"));

        shell.Document!.Save();
        window.Close();
    }
}
