using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SDCharacterStudio.Editor.Export;
using SDCharacterStudio.Editor.Infrastructure;
using SDCharacterStudio.Editor.ViewModels;
using SDCharacterStudio.Editor.Views.Dialogs;
using Wpf.Ui.Appearance;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>
/// Renders the real windows to PNG for visual review (doc 10). Off by default: set SDCS_UI_SNAPSHOTS to an output folder.
/// SDCS_UI_EXPORT may point at a Bridge export profile (defaults to the native modded export when present, else the mini one).
/// </summary>
public class UiSnapshotTests
{
    [Fact]
    public void RenderScreens()
    {
        string? output = Environment.GetEnvironmentVariable("SDCS_UI_SNAPSHOTS");
        if (string.IsNullOrEmpty(output) || TestEnvironment.ExamplePack is not { } example) return;
        EditorUiThread.Run(() => Run(output, example));
    }

    internal static string ExportProfile()
    {
        if (Environment.GetEnvironmentVariable("SDCS_UI_EXPORT") is { Length: > 0 } chosen) return chosen;
        string? root = TestEnvironment.RepoRoot;
        string native = root == null ? "" : Path.Combine(root, ".superpowers", "sdd", "2026-10-24-m6-bridge-preview", "native", "Export", "modded-final");
        return File.Exists(Path.Combine(native, "catalog.json")) ? native : TestEnvironment.MiniExport;
    }

    private static void Run(string output, string example)
    {
        Directory.CreateDirectory(output);
        using var temp = new TempFolder();
        // EditorUiThread owns the single STA WPF Application for all view tests.
        ApplicationThemeManager.Apply(ApplicationTheme.Dark, Wpf.Ui.Controls.WindowBackdropType.None, updateAccent: false);
        ApplicationAccentColorManager.Apply(Color.FromRgb(0x4C, 0xB0, 0x7A), ApplicationTheme.Dark);

        string profile = ExportProfile();
        var settings = new EditorSettings
        {
            ExportFolder = Path.GetDirectoryName(profile)!, ExportProfile = profile, ProjectsFolder = temp.Path, GameModsFolder = @"C:\Games\Stardew Valley\Mods",
            PipeName = "SDCS.UiSnapshots." + Guid.NewGuid().ToString("N"), WindowMaximized = false, WindowWidth = 1600, WindowHeight = 1000,
            RecentProjects = new() { @"C:\Users\Player\Documents\SDCharacterStudio\My First Scene\My First Scene.sdcs" }
        };
        var window = new MainWindow { WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.None, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
        var ui = new WpfUserInterface(window);
        var shell = new MainViewModel(ui, settings);
        window.Attach(shell, ui);
        window.Left = SystemParameters.VirtualScreenLeft - 4000; window.Top = 0;
        window.Show();
        shell.LoadCatalog();
        Pump(600);
        Save(window, Path.Combine(output, "01-welcome.png"));

        // A real pack: the example pack (with poses), copied so nothing in the repo changes.
        string pack = Path.Combine(temp.Path, Path.GetFileName(example));
        CopyDirectory(example, pack);
        shell.OpenPack(pack);
        Pump(1200);
        Save(window, Path.Combine(output, "02-scene.png"));

        var scene = shell.Scene!;
        shell.Select(new BeatSelection(scene.Id!, LaneNames.Main, scene.Beats.FindIndex(b => b is SDCharacterStudio.Core.Models.SayBeat)));
        Pump(500);
        Save(window, Path.Combine(output, "03-say-beat.png"));

        shell.Select(new BeatSelection(scene.Id!, LaneNames.Main, scene.Beats.FindIndex(b => b is SDCharacterStudio.Core.Models.ChoiceBeat)));
        Pump(400);
        Save(window, Path.Combine(output, "04-choice-beat.png"));
        shell.Storyboard.GraphMode = true;
        Pump(400);
        Save(window, Path.Combine(output, "04b-branch-graph.png"));
        shell.Storyboard.GraphMode = false;

        shell.Select(new BeatSelection(scene.Id!, LaneNames.Main, scene.Beats.FindIndex(b => b is SDCharacterStudio.Core.Models.MoveBeat)));
        Pump(400);
        Save(window, Path.Combine(output, "05-move-beat.png"));

        shell.Select(new ActorSelection(scene.Id!, scene.Actors.FindIndex(a => a.Id == "Robin")));
        shell.Assets.SelectedCategory = shell.Assets.Categories.First(c => c.Id == "sprites");
        shell.Assets.Search = "Robin";
        Pump(800);
        Save(window, Path.Combine(output, "06-actor-and-sprites.png"));

        shell.Select(new SceneSelection(scene.Id!));
        shell.Assets.Search = "";
        shell.Assets.SelectedCategory = shell.Assets.Categories.First(c => c.Id == "objects");
        Pump(800);
        Save(window, Path.Combine(output, "07-scene-settings-items.png"));
        shell.Assets.SelectedCategory = shell.Assets.Categories.First(c => c.Id == "sounds");
        shell.Assets.Selected = shell.Assets.Items.FirstOrDefault();
        Pump(400);
        Save(window, Path.Combine(output, "07b-sound-audition.png"));

        shell.Assets.SelectedCategory = shell.Assets.Categories.First(c => c.Id == "locations");
        shell.Problems.IsOpenRequested = true;
        Pump(400);
        _ = shell.ValidateAsync();
        Pump(2500);
        Save(window, Path.Combine(output, "08-problems-locations.png"));

        // Character Studio (M8): the library, the reference pose, and a new pose after Isolate head.
        shell.IsCharacterStudio = true;
        var studio = shell.CharacterStudio;
        studio.Character = studio.Characters.FirstOrDefault(c => c.Key == "Robin") ?? studio.Character;
        Pump(800);
        Save(window, Path.Combine(output, "20-studio-library.png"));
        if (studio.Library!.Items(SDCharacterStudio.Editor.Studio.LibraryKind.Pose).FirstOrDefault() is { } reference)
        {
            studio.Open(reference);
            Pump(1200);
            Save(window, Path.Combine(output, "21-studio-pose.png"));
        }
        studio.NewPose("character", "bow");
        if (studio.ActiveDocument is PoseDocumentViewModel fresh)
        {
            fresh.IsolateHead();
            fresh.PointerDown(5, 18, PointerButton.Primary); fresh.PointerMove(10, 18); fresh.PointerUp(10, 18);
        }
        Pump(1000);
        Save(window, Path.Combine(output, "22-studio-isolate-head.png"));

        // A flipbook (the simple editor, D-48): bow, walk two tiles left, then her own look.
        studio.Settings.AdvancedPoseTools = false;
        if (studio.NewAnimation("bow and walk") != null && studio.ActiveDocument is AnimationDocumentViewModel flipbook && flipbook.IsFlipbook)
        {
            flipbook.AddStepCommand.Execute(null);
            flipbook.Steps[1].Do = "Walk";
            flipbook.Steps[1].Distance = 2;
            flipbook.AddStepCommand.Execute(null);
            flipbook.Steps[2].Do = "Own";
            flipbook.Playhead = 300;
            Pump(1000);
            Save(window, Path.Combine(output, "23a-studio-flipbook.png"));
        }

        // An animation using the reference pose on the timeline (Advanced): a step left, then the pose.
        studio.Settings.AdvancedPoseTools = true;
        studio.Personal = new SDCharacterStudio.Editor.Studio.PersonalLibrary(Path.Combine(temp.Path, "Library"));
        if (studio.NewAnimation("step and bow") != null && studio.ActiveDocument is AnimationDocumentViewModel clip)
        {
            clip.IsRecording = true;
            clip.Playhead = 600;
            clip.BeginDrag(clip.Owners[0].Id);
            clip.EndDrag((3 - clip.StageLeft) * 16 + 8, (3 - clip.StageTop) * 16 + 15, false);
            clip.AddKey(clip.Rows.First(r => r.Track == TrackKind.Pose), 600);
            clip.AddKey(clip.Rows.First(r => r.Track == TrackKind.Emote), 2400);
            clip.SelectedKey = clip.Rows.SelectMany(r => r.Keys).First(k => k.Key is SDCharacterStudio.Core.Models.PoseKey);
            clip.IsRecording = false;
            clip.Playhead = 1400;
            Pump(1000);
            Save(window, Path.Combine(output, "23-studio-animation.png"));
            studio.SaveToMyLibrary(SDCharacterStudio.Editor.Studio.LibraryKind.Clip, clip.ItemId);
            studio.ShowMyLibrary = true;
            Pump(600);
            Save(window, Path.Combine(output, "24-studio-my-library.png"));
            studio.ShowMyLibrary = false;
        }

        // Masks with the fit check, the rig, an expression and a portrait (M8.3, M8.6).
        if (studio.Library!.Items(SDCharacterStudio.Editor.Studio.LibraryKind.Mask).FirstOrDefault(m => m.Id.Contains("head", StringComparison.OrdinalIgnoreCase)) is { } headMask)
        {
            studio.Open(headMask);
            Pump(1000);
            Save(window, Path.Combine(output, "25-studio-mask-fit.png"));
        }
        studio.MakeRig();
        Pump(800);
        Save(window, Path.Combine(output, "26-studio-rig.png"));
        if (studio.NewExpression("eyes closed") != null && studio.ActiveDocument is ExpressionDocumentViewModel expression)
        {
            var box = expression.Box;
            studio.Palette.Primary = SDCharacterStudio.Core.Palette.ColorSlots.Erase;
            expression.PointerDown(box[0], box[1] + 1, PointerButton.Primary, false); expression.PointerMove(box[0] + box[2] - 1, box[1] + 1); expression.PointerUp();
            studio.Palette.Primary = SDCharacterStudio.Core.Palette.ColorSlots.Outline;
            expression.PointerDown(box[0], box[1] + 2, PointerButton.Primary, false); expression.PointerMove(box[0] + box[2] - 1, box[1] + 2); expression.PointerUp();
            Pump(800);
            Save(window, Path.Combine(output, "27-studio-expression.png"));
        }
        if (studio.NewPortrait("raincoat") != null && studio.ActiveDocument is PortraitDocumentViewModel portrait)
        {
            portrait.IsolateHead();
            Pump(1000);
            Save(window, Path.Combine(output, "28-studio-portrait.png"));
        }
        shell.IsCharacterStudio = false;
        shell.Document!.Save();   // the temporary copy: closing must not ask to save
        Pump(300);

        // Dialogs, shown without blocking.
        foreach (var (name, vm) in new (string, object)[]
        {
            ("10-new-project", new NewProjectViewModel(shell)),
            ("11-dependencies", new DependenciesViewModel(shell)),
            ("12-export", new ExportViewModel(shell)),
            ("12b-export-csharp", new ExportViewModel(shell) { Target = ExportTarget.CodeModScenes }),
            ("12c-export-content-pack", new ExportViewModel(shell) { Target = ExportTarget.ContentPack }),
            ("12d-export-done", Exported(new ExportViewModel(shell))),
            ("13-settings", new SettingsViewModel(shell)),
            ("14-pack-settings", new PackSettingsViewModel(shell)),
            ("15-video-import", new VideoImportViewModel(shell)),
            ("16-item-picker", new ItemPickerViewModel(shell, "(O)709", null)),
        })
            ShowDialog(name, vm);
        ShowDialog("12e-export-never-plays", NeverPlays(shell));   // last: it changes the scene's trigger
        shell.Document!.Save();   // closing must not ask to save (a hidden prompt would wait forever)

        void ShowDialog(string name, object vm)
        {
            var dialog = new DialogWindow(vm) { WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.None, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
            dialog.Left = SystemParameters.VirtualScreenLeft - 4000; dialog.Top = 0;
            dialog.Show();
            Pump(700);
            Save(dialog, Path.Combine(output, name + ".png"));
            dialog.Close();
        }
        window.Close();
        Pump(200);
        shell.Dispose();
    }

    /// <summary>The dialog after a successful export (the default target, into the temporary project's Export folder).</summary>
    private static ExportViewModel Exported(ExportViewModel export)
    {
        export.ExportCommand.Execute(null);
        Assert.True(export.Done, export.Error);
        return export;
    }

    /// <summary>A complete mod whose scene nothing would start: the dialog points it out.</summary>
    private static ExportViewModel NeverPlays(MainViewModel shell)
    {
        var doc = shell.Document!;
        doc.Edit("manual", () => doc.Cutscenes[0].Scene.Trigger = null);
        return new ExportViewModel(shell);
    }

    internal static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(milliseconds), DispatcherPriority.ApplicationIdle, (s, _) => { ((DispatcherTimer)s!).Stop(); frame.Continue = false; }, Dispatcher.CurrentDispatcher);
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    internal static void Save(Window window, string path)
    {
        var root = (FrameworkElement)window.Content;
        int width = (int)Math.Ceiling(root.ActualWidth), height = (int)Math.Ceiling(root.ActualHeight);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var background = (Brush?)Application.Current.TryFindResource("ApplicationBackgroundBrush") ?? new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20));
            dc.DrawRectangle(background, null, new Rect(0, 0, width, height));
            dc.DrawRectangle(new VisualBrush(root) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, width, height));
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void CopyDirectory(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }
}
