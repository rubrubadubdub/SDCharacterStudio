using System.IO;
using System.Windows;
using System.Windows.Media;
using SDCharacterStudio.Editor.Infrastructure;
using SDCharacterStudio.Editor.ViewModels;
using Wpf.Ui.Appearance;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>
/// The sprite editor (D-49) rendered for review on real art. Off by default: set SDCS_STUDIO_SNAPSHOTS to an output
/// folder, and SDCS_UI_EXPORT to a Bridge export profile (read only).
/// </summary>
public class SpriteEditorSnapshotTests
{
    [Fact]
    public void RenderSpriteEditor()
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
            PipeName = "SDCS.SpriteSnapshots." + Guid.NewGuid().ToString("N"), WindowMaximized = false, WindowWidth = 1600, WindowHeight = 1000,
        };
        var window = new MainWindow { WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.None, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
        var ui = new WpfUserInterface(window);
        var shell = new MainViewModel(ui, settings);
        window.Attach(shell, ui);
        window.Left = SystemParameters.VirtualScreenLeft - 4000; window.Top = 0;
        window.Show();
        shell.LoadCatalog();
        var dialog = new NewProjectViewModel(shell) { Name = "Sprite Review", Author = "Player", UniqueId = "Player.SpriteReview", FirstScene = "First scene", Location = "Town", ParentFolder = temp.Path };
        dialog.CreateCommand.Execute(null);
        shell.Attach(dialog.Created!);
        // The stage: the player puts on earmuffs; that beat selected shows them (as an icon until the game draws them).
        shell.Stage.DropCharacter("farmer", 8, 8);
        var scene = shell.Scene!;
        shell.Document!.Edit("earmuffs", () => scene.Beats.Add(new SDCharacterStudio.Core.Models.CostumeBeat { Actor = "farmer", Set = new() { ["Hat"] = "(H)11" } }));
        shell.Select(new BeatSelection(scene.Id!, LaneNames.Main, scene.Beats.Count - 1));
        shell.Stage.Zoom = 5;
        UiSnapshotTests.Pump(800);
        UiSnapshotTests.Save(window, Path.Combine(output, "29-stage-costume.png"));
        if (shell.Stage.HasMapWarning)
        {
            shell.Stage.MakeVariantCommand.Execute(null);   // the copy's banner says what it is
            UiSnapshotTests.Pump(500);
            UiSnapshotTests.Save(window, Path.Combine(output, "29b-stage-variant.png"));
            shell.Document!.Undo();
            UiSnapshotTests.Pump(300);
        }
        var farmerOnStage = shell.Stage.Actors.First(a => a.Kind == "Farmer");
        File.WriteAllText(Path.Combine(output, "29-stage-costume.txt"),
            $"Outfit: {farmerOnStage.Outfit} | NeedsRender: {farmerOnStage.NeedsRender} | Badges: {farmerOnStage.Badges.Count} | Pending: {shell.Stage.PendingOutfits.Count}");

        // D-50, the owner's scene: the player walks up the road, Abigail walks over, a Moon Table stands by the road.
        var owner = shell.Scene!;   // undo reloads the scene: use the current one
        shell.Document!.Edit("owner's scene", () =>
        {
            owner.Beats.Clear();
            owner.Actors[0].Start = new SDCharacterStudio.Core.Models.Position { Tile = new[] { 33, 61 } };
            owner.Camera = new Newtonsoft.Json.Linq.JObject { ["Tile"] = new Newtonsoft.Json.Linq.JArray(40, 59) };
        });
        shell.Stage.DropCharacter("Abigail", 49, 60);
        shell.Document!.Edit("beats", () =>
        {
            owner.Beats.Add(new SDCharacterStudio.Core.Models.MoveBeat { Actor = "farmer", To = new SDCharacterStudio.Core.Models.Position { Tile = new[] { 38, 59 } } });
            owner.Beats.Add(new SDCharacterStudio.Core.Models.MoveBeat { Actor = "Abigail", To = new SDCharacterStudio.Core.Models.Position { Tile = new[] { 41, 59 } } });
        });
        shell.Select(new BeatSelection(owner.Id!, LaneNames.Main, 1));
        shell.Stage.PlaceItem("(F)1130", 42, 61);   // dropped from the browser: after the walk shown
        shell.Select(new BeatSelection(owner.Id!, LaneNames.Main, 1));
        shell.Stage.Zoom = 4;
        UiSnapshotTests.Pump(500);
        if (Descendant<Views.StageView>(window) is { } stageView) stageView.CenterOnCamera();
        UiSnapshotTests.Pump(500);
        UiSnapshotTests.Save(window, Path.Combine(output, "40-stage-walk.png"));
        shell.Select(new BeatSelection(owner.Id!, LaneNames.Main, 2));
        UiSnapshotTests.Pump(500);
        UiSnapshotTests.Save(window, Path.Combine(output, "41-stage-prop.png"));
        shell.Stage.ShowStartCommand.Execute(null);
        UiSnapshotTests.Pump(500);
        UiSnapshotTests.Save(window, Path.Combine(output, "42-stage-start.png"));
        File.WriteAllText(Path.Combine(output, "40-stage.txt"), string.Join(Environment.NewLine,
            shell.Stage.Actors.Select(a => $"{a.Label} at {a.TileX},{a.TileY} facing {a.Facing}")
                .Concat(owner.Beats.Select((b, i) => $"beat {i + 1}: {Schema.BeatCatalog.Summary(b, shell.Catalog)}"))));


        shell.IsCharacterStudio = true;
        var studio = shell.CharacterStudio;
        studio.Settings.AdvancedPoseTools = false;
        studio.Character = studio.Characters.FirstOrDefault(c => c.Key == "Abigail") ?? studio.Character;
        studio.NewPose("character", "wave", "down");
        var pose = (PoseDocumentViewModel)studio.ActiveDocument!;
        pose.Zoom = 14;
        UiSnapshotTests.Pump(800);
        UiSnapshotTests.Save(window, Path.Combine(output, "30-sprite-editor.png"));

        // Pencil with a 3-px brush and symmetry; a stroke in her skin colour.
        pose.Tool = StudioTool.Pencil;
        pose.BrushSize = 2; pose.Symmetry = true;
        if (studio.Palette.VisibleSlots.FirstOrDefault(s => s.Name == "Skin mid") is { } skin) studio.Palette.Primary = skin.Color;
        pose.PointerDown(3, 20, PointerButton.Primary); pose.PointerMove(3, 24); pose.PointerUp(3, 24);
        UiSnapshotTests.Pump(500);
        UiSnapshotTests.Save(window, Path.Combine(output, "31-sprite-editor-pencil.png"));

        // Onion skin over three frames: the head lowered a pixel per frame.
        pose.Symmetry = false;
        pose.AddFrameCommand.Execute(null);
        pose.Tool = StudioTool.Select;
        pose.Selection = new Studio.PixelRect(0, 0, 16, 14);
        pose.NudgeSelection(0, 1);
        pose.AddFrameCommand.Execute(null);
        pose.Selection = new Studio.PixelRect(0, 1, 16, 14);
        pose.NudgeSelection(0, 1);
        pose.Selection = null;
        pose.OnionBefore = 2; pose.OnionAfter = 0; pose.OnionTint = true; pose.OnionOpacity = 0.4;
        pose.ShowOnion = true;
        UiSnapshotTests.Pump(500);
        UiSnapshotTests.Save(window, Path.Combine(output, "32-sprite-editor-onion.png"));

        // Phase 2: a lasso around her head, isolated; then a transform, rotated and floating.
        pose.ShowOnion = false;
        pose.Tool = StudioTool.Lasso;
        var path = new[] { (3, 2), (12, 2), (14, 7), (12, 13), (4, 13), (1, 8) };
        pose.PointerDown(path[0].Item1, path[0].Item2, PointerButton.Primary);
        foreach (var (x, y) in path.Skip(1)) pose.PointerMove(x, y);
        pose.PointerUp(path[^1].Item1, path[^1].Item2);
        pose.Isolate = true;
        UiSnapshotTests.Pump(500);
        UiSnapshotTests.Save(window, Path.Combine(output, "35-sprite-editor-lasso-isolate.png"));
        pose.Isolate = false;
        pose.BeginTransform();
        pose.TransformAngle = -20;
        UiSnapshotTests.Pump(500);
        UiSnapshotTests.Save(window, Path.Combine(output, "36-sprite-editor-transform.png"));
        pose.CancelTransformCommand.Execute(null);

        // Phase 4: her Flower Dance outfit, her frames, her body parts and presets made for her.
        if (studio.Outfits.FirstOrDefault(o => o.Label == "Flower Dance") is { } dance) studio.Outfit = dance;
        pose.ShowSheetFrames = true;
        pose.ShowParts = true;
        UiSnapshotTests.Pump(600);
        UiSnapshotTests.Save(window, Path.Combine(output, "37-sprite-editor-outfit-parts.png"));
        pose.ShowParts = false;
        pose.ShowSheetFrames = false;
        UiSnapshotTests.Pump(400);
        UiSnapshotTests.Save(window, Path.Combine(output, "38-sprite-editor-presets.png"));
        pose.Backdrop = "scene";
        pose.Zoom = 12;
        UiSnapshotTests.Pump(500);
        UiSnapshotTests.Save(window, Path.Combine(output, "39-sprite-editor-scene-floor.png"));
        pose.Backdrop = "checker";
        studio.Outfit = studio.Outfits.FirstOrDefault();

        pose.IsShortcutsOpen = true;
        UiSnapshotTests.Pump(400);
        UiSnapshotTests.Save(window, Path.Combine(output, "33-sprite-editor-shortcuts.png"));
        pose.IsShortcutsOpen = false;

        // The colour picker (a popup, so rendered on its own).
        var picker = new Views.Studio.ColorPicker { Color = 0xFFE07090 };
        var host = new System.Windows.Controls.Border { Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2D, 0x33)), Padding = new Thickness(12), Child = picker };
        host.Measure(new Size(400, 400)); host.Arrange(new Rect(host.DesiredSize)); host.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)host.ActualWidth, (int)host.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(host);
        var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
        png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var stream = File.Create(Path.Combine(output, "34-colour-picker.png"))) png.Save(stream);

        shell.Document!.Save();   // no "save changes?" prompt on close
        window.Close();
    }

    private static T? Descendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T found) return found;
            if (Descendant<T>(child) is { } deeper) return deeper;
        }
        return null;
    }
}
