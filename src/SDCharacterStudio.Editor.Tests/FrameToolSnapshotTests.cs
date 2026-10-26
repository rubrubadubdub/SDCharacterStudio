using System.IO;
using System.Windows;
using System.Windows.Media;
using SDCharacterStudio.Editor.Infrastructure;
using SDCharacterStudio.Editor.Studio;
using SDCharacterStudio.Editor.ViewModels;
using Wpf.Ui.Appearance;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>
/// The frame tools on real art: linked frames and back-and-forth play in the strip, the mirrored view, and an outfit's
/// frames in groups. Off by default: set SDCS_STUDIO_SNAPSHOTS to an output folder, and SDCS_UI_EXPORT to a Bridge
/// export profile (read only).
/// </summary>
public class FrameToolSnapshotTests
{
    [Fact]
    public void RenderFrameTools()
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
            PipeName = "SDCS.FrameTools." + Guid.NewGuid().ToString("N"), WindowMaximized = false, WindowWidth = 1600, WindowHeight = 1000,
        };
        var window = new MainWindow { WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.None, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
        var ui = new WpfUserInterface(window);
        var shell = new MainViewModel(ui, settings);
        window.Attach(shell, ui);
        window.Left = SystemParameters.VirtualScreenLeft - 4000; window.Top = 0;
        window.Show();
        shell.LoadCatalog();
        var dialog = new NewProjectViewModel(shell) { Name = "Frame Tools", Author = "Player", UniqueId = "Player.FrameTools", FirstScene = "Wave", Location = "Town", ParentFolder = temp.Path };
        dialog.CreateCommand.Execute(null);
        shell.Attach(dialog.Created!);
        shell.IsCharacterStudio = true;
        var studio = shell.CharacterStudio;
        studio.Settings.AdvancedPoseTools = false;
        studio.Character = studio.Characters.First(c => c.Key == "Abigail");

        // A wave: four frames, the first two linked (a hold), back and forth; two picked; the view mirrored.
        studio.NewPose("character", "wave", "down");
        var pose = (PoseDocumentViewModel)studio.ActiveDocument!;
        pose.Zoom = 12;
        pose.ApplyPresetCommand.Execute("nod");
        while (pose.Model.Frames.Count > 4) { pose.FrameIndex = pose.Model.Frames.Count - 1; pose.DeleteFrameCommand.Execute(null); }
        pose.FrameIndex = 0;
        pose.PickFrames(new[] { 0, 1 }, 0);
        pose.LinkPickedCommand.Execute(null);
        pose.PlaysBackAndForth = true;
        pose.PickFrames(new[] { 2, 3 }, 0);
        pose.MirrorView = true;
        UiSnapshotTests.Pump(800);
        UiSnapshotTests.Save(window, Path.Combine(output, "90-linked-backforth-mirror.png"));
        pose.MirrorView = false;

        // An outfit's frames in groups.
        studio.NewOutfit("cloak");
        UiSnapshotTests.Pump(800);
        UiSnapshotTests.Save(window, Path.Combine(output, "91-outfit-groups.png"));

        shell.Document!.Save();
        window.Close();
    }
}
