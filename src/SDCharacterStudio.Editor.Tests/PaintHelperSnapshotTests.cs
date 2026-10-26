using System.IO;
using System.Windows;
using System.Windows.Media;
using SDCharacterStudio.Editor.Infrastructure;
using SDCharacterStudio.Editor.Studio;
using SDCharacterStudio.Editor.ViewModels;
using Wpf.Ui.Appearance;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>
/// The painting helpers on real art: Abigail's raincoat with a named colour, shaded and outlined, inside her only, and
/// her shirt recoloured. Off by default: set SDCS_STUDIO_SNAPSHOTS to an output folder, and SDCS_UI_EXPORT to a Bridge
/// export profile (read only).
/// </summary>
public class PaintHelperSnapshotTests
{
    [Fact]
    public void RenderPaintHelpers()
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
            PipeName = "SDCS.PaintHelpers." + Guid.NewGuid().ToString("N"), WindowMaximized = false, WindowWidth = 1600, WindowHeight = 1000,
        };
        var window = new MainWindow { WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.None, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
        var ui = new WpfUserInterface(window);
        var shell = new MainViewModel(ui, settings);
        window.Attach(shell, ui);
        window.Left = SystemParameters.VirtualScreenLeft - 4000; window.Top = 0;
        window.Show();
        shell.LoadCatalog();
        var dialog = new NewProjectViewModel(shell) { Name = "Paint Helpers", Author = "Player", UniqueId = "Player.PaintHelpers", FirstScene = "Rainy day", Location = "Town", ParentFolder = temp.Path };
        dialog.CreateCommand.Execute(null);
        shell.Attach(dialog.Created!);
        shell.IsCharacterStudio = true;
        var studio = shell.CharacterStudio;
        studio.Settings.AdvancedPoseTools = false;
        studio.Character = studio.Characters.First(c => c.Key == "Abigail");

        // A raincoat in a named colour, painted inside her only on the four steps walking down, then shaded and outlined.
        studio.NewOutfit("raincoat");
        var coat = (PoseDocumentViewModel)studio.ActiveDocument!;
        coat.Zoom = 12;
        var yellow = studio.AddNamedColor("Coat", 0xFFE8C020)!;
        studio.AddNamedColor("Trim", 0xFF8A3A20);
        coat.PickFrames(new[] { 0, 1, 2, 3 }, 0);
        coat.PaintInside = true;
        coat.Tool = StudioTool.Rectangle; coat.FilledShapes = true;
        studio.Palette.Primary = yellow.Mid;
        coat.PointerDown(0, 16, PointerButton.Primary); coat.PointerMove(15, 26); coat.PointerUp(15, 26);
        coat.Tool = StudioTool.Shade; coat.BrushSize = 1;
        foreach (int y in new[] { 17, 19, 21, 23, 25 }) { coat.PointerDown(5, y, PointerButton.Primary); coat.PointerUp(5, y); coat.PointerDown(10, y, PointerButton.Primary); coat.PointerUp(10, y); }
        coat.PaintInside = false;
        coat.Tool = StudioTool.Pencil;
        coat.PointerDown(2, 27, PointerButton.Primary); coat.PointerMove(13, 27); coat.PointerUp(13, 27);   // a hem below her
        coat.OutlinePaint();
        coat.PickFrames(new[] { 0 }, 0);
        coat.Tool = StudioTool.Shade;
        UiSnapshotTests.Pump(800);
        UiSnapshotTests.Save(window, Path.Combine(output, "80-shade-inside-named.png"));

        // Her shirt recoloured, still live (on a pose).
        studio.Outfit = studio.Outfits[0];
        studio.NewPose("character", "red top", "down");
        var pose = (PoseDocumentViewModel)studio.ActiveDocument!;
        pose.Zoom = 12;
        studio.Palette.Primary = 0xFFC03030;
        pose.AddRecolor();
        pose.PresetsOpen = false; pose.BodyOpen = false; pose.RecolorOpen = true;
        UiSnapshotTests.Pump(800);
        UiSnapshotTests.Save(window, Path.Combine(output, "81-recolour.png"));
        pose.PresetsOpen = true; pose.BodyOpen = true;

        // The coat colour changed everywhere.
        studio.ChangeNamedColor("Coat", 0xFF3060C0);
        studio.OpenItem(LibraryKind.Pose, "abigail-raincoat");
        UiSnapshotTests.Pump(800);
        UiSnapshotTests.Save(window, Path.Combine(output, "82-named-changed.png"));

        shell.Document!.Save();
        window.Close();
    }
}
