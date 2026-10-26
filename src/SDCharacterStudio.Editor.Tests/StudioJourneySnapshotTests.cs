using System.IO;
using System.Windows;
using System.Windows.Media;
using SDCharacterStudio.Editor.Infrastructure;
using SDCharacterStudio.Editor.Studio;
using SDCharacterStudio.Editor.ViewModels;
using Wpf.Ui.Appearance;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>
/// A first-time modder's walk through the Character Studio on real art, screen by screen, at a desktop and a laptop
/// size: for reviewing the UX as a whole. Off by default: set SDCS_STUDIO_SNAPSHOTS to an output folder, and
/// SDCS_UI_EXPORT to a Bridge export profile (read only).
/// </summary>
public class StudioJourneySnapshotTests
{
    [Fact]
    public void RenderStudioJourney()
    {
        string? output = Environment.GetEnvironmentVariable("SDCS_STUDIO_SNAPSHOTS");
        if (string.IsNullOrEmpty(output)) return;
        EditorUiThread.Run(() => { Run(output, 1600, 1000, "a"); Run(output, 1366, 768, "b"); });
    }

    private static void Run(string output, int width, int height, string prefix)
    {
        Directory.CreateDirectory(output);
        using var temp = new TempFolder();
        ApplicationThemeManager.Apply(ApplicationTheme.Dark, Wpf.Ui.Controls.WindowBackdropType.None, updateAccent: false);
        ApplicationAccentColorManager.Apply(Color.FromRgb(0x4C, 0xB0, 0x7A), ApplicationTheme.Dark);
        string profile = UiSnapshotTests.ExportProfile();
        var settings = new EditorSettings
        {
            ExportFolder = Path.GetDirectoryName(profile)!, ExportProfile = profile, ProjectsFolder = temp.Path, GameModsFolder = null,
            PipeName = "SDCS.Journey." + Guid.NewGuid().ToString("N"), WindowMaximized = false, WindowWidth = width, WindowHeight = height,
        };
        var window = new MainWindow { WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.None, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
        var ui = new WpfUserInterface(window);
        var shell = new MainViewModel(ui, settings);
        window.Attach(shell, ui);
        window.Left = SystemParameters.VirtualScreenLeft - 4000; window.Top = 0;
        window.Width = width; window.Height = height;
        window.Show();
        shell.LoadCatalog();
        var dialog = new NewProjectViewModel(shell) { Name = "My First Mod", Author = "Player", UniqueId = "Player.MyFirstMod", FirstScene = "Hello", Location = "Town", ParentFolder = temp.Path };
        dialog.CreateCommand.Execute(null);
        shell.Attach(dialog.Created!);
        void Shot(string name, int wait = 700) { UiSnapshotTests.Pump(wait); UiSnapshotTests.Save(window, Path.Combine(output, $"{prefix}-{name}.png")); }

        // 1. Into the Character Studio for the first time.
        shell.IsCharacterStudio = true;
        var studio = shell.CharacterStudio;
        studio.Settings.AdvancedPoseTools = false;
        studio.Character = studio.Characters.First(c => c.Key == "Abigail");
        Shot("01-studio-start");

        // 2. A new pose: what you see first.
        studio.NewPose("character", "wave", "down");
        var pose = (PoseDocumentViewModel)studio.ActiveDocument!;
        Shot("02-new-pose");
        // ... after picking a ready-made pose.
        pose.ApplyPresetCommand.Execute("nod");
        Shot("03-ready-made");
        // ... drawing.
        pose.Tool = StudioTool.Pencil;
        studio.Palette.Primary = 0xFFE8C020;
        pose.PointerDown(4, 18, PointerButton.Primary); pose.PointerMove(11, 18); pose.PointerUp(11, 18);
        Shot("04-drawing");

        // 3. An animation (flipbook).
        studio.NewAnimation("greet");
        Shot("05-animation");

        // 4. An expression and a portrait.
        studio.NewExpression("blush");
        Shot("06-expression");
        studio.NewPortrait("winter");
        Shot("07-portrait");

        // 5. Advanced, on the pose.
        studio.OpenItem(LibraryKind.Pose, pose.ItemId);
        pose.Advanced = true;
        Shot("08-advanced");
        pose.Advanced = false;

        // 6. My library.
        studio.Personal = new PersonalLibrary(Path.Combine(temp.Path, "Library"));
        studio.ShowMyLibrary = true;
        Shot("09-my-library");
        studio.ShowMyLibrary = false;

        // 7. Back in the scene: using the pose.
        shell.IsCharacterStudio = false;
        shell.Stage.DropCharacter("Abigail", 30, 60);
        shell.Assets.SelectedCategory = shell.Assets.Categories.First(c => c.Id == "poses");
        Shot("10-scene-my-poses", 1000);

        shell.Document!.Save();
        window.Close();
    }
}
