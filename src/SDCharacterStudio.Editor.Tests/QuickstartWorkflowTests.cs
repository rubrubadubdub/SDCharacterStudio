using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core.Compiler;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Serialization;
using SDCharacterStudio.Editor.Infrastructure;
using SDCharacterStudio.Editor.Schema;
using SDCharacterStudio.Editor.Studio;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>The three-beat designer guide, using the same actions and fields as the UI.</summary>
public sealed class QuickstartWorkflowTests
{
    [Fact]
    public void ThreeBeatCustomPoseGuideExportsACompleteMod()
    {
        using var editor = new EditorHarness();
        Walkthrough(editor);
    }

    /// <summary>
    /// The walkthrough's mod as the third bench (benches/Run-Benches.ps1, only with SDCS_BENCH_EXPORT=1): what a first-time
    /// creator exports, played in the game with nothing from SDCharacterStudio installed.
    /// </summary>
    [Fact]
    public void ExportWalkthroughBench()
    {
        if (Environment.GetEnvironmentVariable("SDCS_BENCH_EXPORT") != "1") return;
        using var editor = new EditorHarness();
        var export = Walkthrough(editor);
        string target = Path.Combine(TestEnvironment.RepoRoot ?? throw new InvalidOperationException("Run from the repository."), "benches", ".run", "walkthrough", "My First Scene");
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        Export.PackExporter.CopyDirectory(export.OutputFolder, target);
    }

    [Fact]
    public void CaptureGuideScreens()
    {
        if (Environment.GetEnvironmentVariable("SDCS_GUIDE_IMAGES") is not { Length: > 0 } output) return;
        EditorUiThread.Run(() =>
        {
            Directory.CreateDirectory(output);
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Dark,
                Wpf.Ui.Controls.WindowBackdropType.None, updateAccent: false);
            Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(Color.FromRgb(0x4C, 0xB0, 0x7A), Wpf.Ui.Appearance.ApplicationTheme.Dark);
            using var editor = new EditorHarness(Environment.GetEnvironmentVariable("SDCS_UI_EXPORT"));
            editor.Shell.Settings.WindowMaximized = false;
            editor.Shell.Settings.WindowWidth = 1440; editor.Shell.Settings.WindowHeight = 960;
            var window = new MainWindow { ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual,
                Left = SystemParameters.VirtualScreenLeft - 4000, Top = 0 };
            window.Attach(editor.Shell, new WpfUserInterface(window));
            try
            {
                window.Show();
                Walkthrough(editor, name =>
                {
                    EditorUiThread.PumpUntil(() => window.IsLoaded);
                    window.UpdateLayout();
                    // Allow the production data templates and deferred studio render to settle.
                    var until = DateTime.UtcNow.AddMilliseconds(400);
                    EditorUiThread.PumpUntil(() => DateTime.UtcNow >= until);
                    var visual = (FrameworkElement)window.Content;
                    var bitmap = new RenderTargetBitmap((int)visual.ActualWidth, (int)visual.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(visual);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
                });
            }
            finally { window.Close(); }
        });
    }

    private static ExportViewModel Walkthrough(EditorHarness editor, Action<string>? capture = null)
    {
        var doc = editor.NewProject("My First Scene", "Player.FirstScene", "ScienceHouse", "Hello Robin");
        var shell = editor.Shell;
        shell.Stage.DropCharacter("Robin", 17, 6);
        editor.Choose("Facing", "left");
        capture?.Invoke("quickstart-stage");

        shell.IsCharacterStudio = true;
        var studio = shell.CharacterStudio;
        studio.Character = studio.Characters.First(c => c.Key == "Robin");
        editor.Ui.Prompts.Enqueue("blue-scarf");
        studio.NewCommand.Execute(LibraryKind.Pose);
        var pose = Assert.IsType<PoseDocumentViewModel>(studio.ActiveDocument);
        // Painting on the live frame adds a paint layer; the source still follows sprite mods.
        studio.Palette.Primary = 0xFF2E6FD8;
        pose.Tool = StudioTool.Rectangle; pose.FilledShapes = true;
        pose.PointerDown(5, 16, PointerButton.Primary); pose.PointerMove(10, 18); pose.PointerUp(10, 18);
        Assert.Contains(pose.Frame!.Layers, l => l is LiveLayer { Visible: true });
        Assert.Contains(pose.Frame.Layers, l => l is PaintLayer);
        capture?.Invoke("quickstart-pose");

        shell.IsCharacterStudio = false;
        shell.Assets.SelectedCategory = shell.Assets.Categories.Single(c => c.Id == "poses");
        shell.Assets.Items.Single(i => i.Key == pose.ItemId).UseCommand.Execute(null);
        editor.Choose("Who", "Robin");
        editor.Choose("Play", "hold-first");
        shell.Storyboard.Insert(LaneNames.Main, shell.Scene!.Beats.Count, BeatCatalog.Kind("Say"));
        editor.Choose("Speaker", "Robin");
        editor.Field<TranslatableTextField>("Line").Text = "Hello! This is my first custom pose.";
        shell.Storyboard.Insert(LaneNames.Main, shell.Scene.Beats.Count, BeatCatalog.Kind("End"));
        var validation = shell.ValidateNow();
        Assert.True(validation.Errors == 0, string.Join("\n", validation.Problems.Select(p => p.Message)));
        Assert.Collection(shell.Scene.Beats, b => Assert.IsType<PoseBeat>(b), b => Assert.IsType<SayBeat>(b), b => Assert.IsType<EndBeat>(b));
        capture?.Invoke("quickstart-storyboard");

        var export = new ExportViewModel(shell);
        Assert.False(export.ShowsNeverPlays);   // the new scene plays by itself when the player walks in
        export.ExportCommand.Execute(null);
        Assert.True(export.Done, export.Error);
        var manifest = JObject.Parse(File.ReadAllText(Path.Combine(export.OutputFolder, "manifest.json")));
        Assert.Null(manifest["ContentPackFor"]);
        Assert.True(File.Exists(Path.Combine(export.OutputFolder, (string)manifest["EntryDll"]!)));
        var saved = PackJson.Deserialize<Cutscene>(File.ReadAllText(Path.Combine(export.OutputFolder, "cutscenes", "hello-robin.json")))!;
        Assert.Equal(3, saved.Beats.Count);
        Assert.NotNull(saved.Trigger);
        Assert.DoesNotContain(new EventCompiler().Compile(doc.PackId, saved, doc.Library).Issues, i => i.Severity == Core.Validation.ValidationSeverity.Error);
        return export;
    }
}
