using System.IO;
using System.Windows;
using System.Windows.Media;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Infrastructure;
using SDCharacterStudio.Editor.Schema;
using SDCharacterStudio.Editor.ViewModels;
using Wpf.Ui.Appearance;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>
/// Scene logic (D-51) rendered for review on real art: a Check's rows and cases, and the scene's "only when" rows.
/// Off by default: set SDCS_STUDIO_SNAPSHOTS to an output folder, and SDCS_UI_EXPORT to a Bridge export profile (read only).
/// </summary>
public class LogicSnapshotTests
{
    [Fact]
    public void RenderLogic()
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
            PipeName = "SDCS.LogicSnapshots." + Guid.NewGuid().ToString("N"), WindowMaximized = false, WindowWidth = 1600, WindowHeight = 1000,
        };
        var window = new MainWindow { WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.None, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
        var ui = new WpfUserInterface(window);
        var shell = new MainViewModel(ui, settings);
        window.Attach(shell, ui);
        window.Left = SystemParameters.VirtualScreenLeft - 4000; window.Top = 0;
        window.Show();
        shell.LoadCatalog();
        var dialog = new NewProjectViewModel(shell) { Name = "Abigail Stories", Author = "Player", UniqueId = "Player.AbigailStories", FirstScene = "Joke time", Location = "Town", ParentFolder = temp.Path };
        dialog.CreateCommand.Execute(null);
        shell.Attach(dialog.Created!);
        shell.Stage.DropCharacter("farmer", 38, 59);
        shell.Stage.DropCharacter("Abigail", 41, 59);
        var scene = shell.Scene!;
        string flag = $"SDCS.Player.AbigailStories.{scene.Id}.trusts-you";
        shell.Document!.Edit("logic", () =>
        {
            scene.Beats.Add(new SayBeat { Actor = "Abigail", Text = "Want to hear a joke?" });
            scene.Beats.Add(new MailBeat { Add = flag });
            scene.Beats.Add(new CheckBeat
            {
                Cases =
                {
                    new CheckCase { When = $"PLAYER_HEARTS Current Abigail 6, PLAYER_HAS_MAIL Current {flag}", Then = "close-friends" },
                    new CheckCase { When = "ANY \"SEASON Fall\" \"WEATHER Here Rain\"", Then = "rainy-walk" },
                },
            });
            scene.Beats.Add(new SayBeat { Actor = "Abigail", Text = "Anyway…" });
            scene.Branches["close-friends"] = new() { new SayBeat { Actor = "Abigail", Text = "You're my favourite person here." } };
            scene.Branches["rainy-walk"] = new() { new MoveBeat { Actor = "Abigail", To = new Position { Tile = new[] { 40, 66 } } } };
            scene.Trigger = new SDCharacterStudio.Core.Models.Trigger { Conditions = "PLAYER_HEARTS Current Abigail 4, SEASON Fall, !IS_FESTIVAL_DAY" };
        });
        shell.Select(new BeatSelection(scene.Id!, LaneNames.Main, 2));
        UiSnapshotTests.Pump(900);
        UiSnapshotTests.Save(window, Path.Combine(output, "50-check.png"));
        // Step 2: a Chance with three outcomes, and a random item bucket.
        shell.Document!.Edit("chance", () =>
        {
            scene.Beats.Insert(1, new ChanceBeat { Outcomes = { new ChanceOutcome { Weight = 5, Then = "laughs" }, new ChanceOutcome { Weight = 3, Then = "groans" }, new ChanceOutcome { Weight = 2, Then = "snorts-soda" } } });
            foreach (string branch in new[] { "laughs", "groans", "snorts-soda" }) scene.Branches[branch] = new() { new SayBeat { Actor = "Abigail", Text = branch == "laughs" ? "Ha! Okay, that one got me." : branch == "groans" ? "That was terrible." : "Don't. Make me. Laugh." } };
            scene.Beats.Add(new ItemBeat { OneOf = new() { new ItemChoice { Item = "(H)15", Weight = 4 }, new ItemChoice { Item = "(H)0", Weight = 3 }, new ItemChoice { Item = "(H)11", Weight = 2 }, new ItemChoice { Item = "(O)789", Weight = 1 } }, HoldUp = true, Message = "You found a {item}!", Remember = true });
        });
        shell.Select(new BeatSelection(scene.Id!, LaneNames.Main, 1));
        UiSnapshotTests.Pump(800);
        UiSnapshotTests.Save(window, Path.Combine(output, "52-chance.png"));
        shell.Select(new BeatSelection(scene.Id!, LaneNames.Main, scene.Beats.Count - 1));
        UiSnapshotTests.Pump(800);
        UiSnapshotTests.Save(window, Path.Combine(output, "53-item-bucket.png"));

        // Step 3: story numbers and the Story panel (one flag misspelled in a Check, one number never changed).
        shell.Document!.Edit("numbers", () =>
        {
            scene.Beats.Insert(2, new NumberBeat { Name = "jokes-told", Amount = 1 });
            ((CheckBeat)scene.Beats.First(b => b is CheckBeat)).Cases.Add(new CheckCase { When = $"PLAYER_HAS_MAIL Current {flag.Replace("trusts", "trust")}", Then = "close-friends" });
            scene.Trigger!.Conditions += ", PLAYER_STAT Current SDCS.Player.AbigailStories.visits 2";
        });
        shell.Story.Entries.First(e => e.Label == "jokes-told").Note = "How many of Abigail's jokes the player sat through.";
        window.WorkspaceHost!.Show(Workspace.WorkspacePaneIds.Story);
        shell.Select(new BeatSelection(scene.Id!, LaneNames.Main, 2));
        UiSnapshotTests.Pump(900);
        UiSnapshotTests.Save(window, Path.Combine(output, "54-story.png"));
        window.WorkspaceHost!.Show(Workspace.WorkspacePaneIds.Storyboard);

        // Step 4: an answer offered only if…, and Try it as… (a popup, rendered on its own).
        shell.Document!.Edit("choice", () =>
        {
            scene.Beats.Add(new ChoiceBeat { Prompt = "So, what now?", Options =
            {
                new ChoiceOption { Text = "Tell her the truth", Then = "close-friends" },
                new ChoiceOption { Text = "Ask about the necklace", Then = "rainy-walk", When = $"PLAYER_HAS_MAIL Current {flag}" },
            } });
        });
        shell.Select(new BeatSelection(scene.Id!, LaneNames.Main, scene.Beats.Count - 1));
        UiSnapshotTests.Pump(800);
        UiSnapshotTests.Save(window, Path.Combine(output, "55-choice-only-if.png"));
        if (window.FindName("TryItAsToggle") is System.Windows.Controls.Primitives.ToggleButton toggle)
        {
            shell.TryItAs.Refresh();
            shell.TryItAs.Entries.First().Selected = shell.TryItAs.Entries.First().Options[1];
            toggle.IsChecked = true;
            UiSnapshotTests.Pump(600);
            if (System.Windows.LogicalTreeHelper.GetChildren((System.Windows.DependencyObject)toggle.Parent).OfType<System.Windows.Controls.Primitives.Popup>().FirstOrDefault()?.Child is FrameworkElement popup)
            {
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)popup.ActualWidth + 8, (int)popup.ActualHeight + 8, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(popup);
                var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
                png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(output, "56-try-it-as.png"));
                png.Save(stream);
            }
            toggle.IsChecked = false;
        }

        shell.Select(new SceneSelection(scene.Id!));
        window.Height = 1900;   // the scene's settings are long: show down to "Only when"
        UiSnapshotTests.Pump(700);
        UiSnapshotTests.Save(window, Path.Combine(output, "51-scene-only-when.png"));
        File.WriteAllText(Path.Combine(output, "50-check.txt"), string.Join(Environment.NewLine,
            scene.Beats.Select((b, i) => $"beat {i + 1}: {BeatCatalog.Summary(b, shell.Catalog)}")
                .Append("trigger: " + Conditions.Describe(scene.Trigger!.Conditions))));
        shell.Document!.Save();
        window.Close();
    }
}
