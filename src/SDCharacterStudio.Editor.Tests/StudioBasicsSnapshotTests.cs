using System.IO;
using System.Windows;
using System.Windows.Media;
using SDCharacterStudio.Editor.Infrastructure;
using SDCharacterStudio.Editor.Studio;
using SDCharacterStudio.Editor.ViewModels;
using Wpf.Ui.Appearance;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>
/// The owner's Character Studio report (2026-10-25) rendered on real art: closing Abigail's eyes on her portrait, and a
/// ready-made pose playing in the preview. Off by default: set SDCS_STUDIO_SNAPSHOTS to an output folder, and SDCS_UI_EXPORT
/// to a Bridge export profile (read only).
/// </summary>
public class StudioBasicsSnapshotTests
{
    [Fact]
    public void RenderStudioBasics()
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
            PipeName = "SDCS.StudioBasics." + Guid.NewGuid().ToString("N"), WindowMaximized = false, WindowWidth = 1600, WindowHeight = 1000,
        };
        var window = new MainWindow { WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.None, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
        var ui = new WpfUserInterface(window);
        var shell = new MainViewModel(ui, settings);
        window.Attach(shell, ui);
        window.Left = SystemParameters.VirtualScreenLeft - 4000; window.Top = 0;
        window.Show();
        shell.LoadCatalog();
        var dialog = new NewProjectViewModel(shell) { Name = "Studio Basics", Author = "Player", UniqueId = "Player.StudioBasics", FirstScene = "First scene", Location = "Town", ParentFolder = temp.Path };
        dialog.CreateCommand.Execute(null);
        shell.Attach(dialog.Created!);
        shell.IsCharacterStudio = true;
        var studio = shell.CharacterStudio;
        studio.Settings.AdvancedPoseTools = false;
        studio.Character = studio.Characters.First(c => c.Key == "Abigail");

        // Her portrait's eyes closed: skin taken from her cheek (Alt+click), painted over both eyes, then a dark lid line.
        studio.NewExpression("eyes closed");
        var expression = (ExpressionDocumentViewModel)studio.ActiveDocument!;
        var target = expression.Fields.OfType<ChoiceField>().First(f => f.Label == "On");
        target.Selected = target.Options.First(o => o.Value == "Portrait");
        expression.Zoom = 12;
        UiSnapshotTests.Pump(500);
        expression.Tool = StudioTool.Pencil;
        expression.PointerDown(26, 33, PointerButton.Primary, alt: true);   // her cheek
        foreach (int y in new[] { 24, 25, 26, 27, 28 })
        {
            expression.PointerDown(16, y, PointerButton.Primary, false); expression.PointerMove(22, y); expression.PointerUp();
            expression.PointerDown(27, y, PointerButton.Primary, false); expression.PointerMove(34, y); expression.PointerUp();
        }
        studio.Palette.Primary = 0xFF4A1C3C;   // a dark lid line
        expression.PointerDown(17, 27, PointerButton.Primary, false); expression.PointerMove(21, 27); expression.PointerUp();
        expression.PointerDown(28, 27, PointerButton.Primary, false); expression.PointerMove(33, 27); expression.PointerUp();
        UiSnapshotTests.Pump(600);
        UiSnapshotTests.Save(window, Path.Combine(output, "60-expression-portrait.png"));

        // A ready-made pose playing in the preview while it's pointed at.
        studio.NewPose("character", "wave", "down");
        var pose = (PoseDocumentViewModel)studio.ActiveDocument!;
        pose.Zoom = 12;
        pose.PreviewPreset("nod");
        pose.TickPreview(400);
        UiSnapshotTests.Pump(500);
        UiSnapshotTests.Save(window, Path.Combine(output, "61-preset-preview.png"));
        pose.PreviewPreset(null);

        shell.Document!.Save();
        window.Close();
    }
}
