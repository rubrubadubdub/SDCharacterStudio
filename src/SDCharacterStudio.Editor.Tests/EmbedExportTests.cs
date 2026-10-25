using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Palette;
using SDCharacterStudio.Editor.Export;
using SDCharacterStudio.Editor.Studio;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>
/// Q-17: players install nothing but the mod. Export › As a complete mod writes the pack with the engine inside;
/// Export › My C# mod writes the scenes for a mod that embeds the engine with Sdcs.cs. Two opt-in helpers build the
/// bench content (SDCS_BENCH_WRITE=1) and export the no-code bench (SDCS_BENCH_EXPORT=1) for benches/Run-Benches.ps1.
/// </summary>
public class EmbedExportTests
{
    private static string AssemblyNameOf(string dll)
    {
        using var stream = File.OpenRead(dll);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        return reader.GetString(reader.GetAssemblyDefinition().Name);
    }

    [Fact]
    public void AGameExportThatFinishesWhileTheEditorIsOpenIsPickedUp()
    {
        using var editor = new EditorHarness();
        string exports = Path.Combine(editor.Folder, "Export");
        editor.Shell.Settings.ExportFolder = exports; editor.Shell.Settings.ExportProfile = null;
        editor.Shell.LoadCatalog();
        Assert.Null(editor.Shell.Catalog);
        editor.Shell.ReloadCatalogIfChanged();   // nothing yet: nothing happens
        Assert.Empty(editor.Ui.Toasts);

        // The Bridge finishes its (one) export: no Reload needed.
        PackExporter.CopyDirectory(TestEnvironment.MiniExport, Path.Combine(exports, "default"));
        editor.Shell.ReloadCatalogIfChanged();
        Assert.NotNull(editor.Shell.Catalog);
        Assert.Contains(editor.Ui.Toasts, t => t.Title == "Game export ready");
        int toasts = editor.Ui.Toasts.Count;
        editor.Shell.ReloadCatalogIfChanged();   // unchanged: not reloaded again
        Assert.Equal(toasts, editor.Ui.Toasts.Count);
    }

    [Fact]
    public void TheExportDialogSaysWhatPlayersWillSee()
    {
        using var editor = new EditorHarness();
        var doc = editor.NewProject("Spring Day", "Player.SpringDay", "ScienceHouse");
        // New scenes play by themselves: a mod on its own has nothing else to start them (D-46).
        Assert.NotNull(editor.Shell.Scene!.Trigger);
        editor.Ui.Prompts.Enqueue("Beach day");
        editor.Shell.AddCutsceneCommand.Execute(null);
        Assert.NotNull(editor.Shell.Scene!.Trigger);
        var export = new ExportViewModel(editor.Shell);
        Assert.False(export.ShowsNeverPlays);
        Assert.Equal("Writes the folder “Spring Day” in the Export folder next to your project.", export.OutputHint);

        // A scene set to "only when started by another mod" is pointed out for a complete mod, with a way to it.
        doc.Edit("manual", () => doc.Cutscenes[0].Scene.Trigger = null);
        export = new ExportViewModel(editor.Shell);
        Assert.True(export.ShowsNeverPlays);
        Assert.Contains("“First scene” doesn't play by itself", export.NeverPlaysText);
        export.Target = ExportTarget.CodeModScenes;   // a C# mod starts its scenes from code
        Assert.False(export.ShowsNeverPlays);
        export.Target = ExportTarget.CompleteMod;
        editor.Shell.Select(new ActorSelection(doc.Cutscenes[1].Scene.Id!, 0));
        export.ShowNeverPlaysCommand.Execute(null);
        Assert.Equal(doc.Cutscenes[0].Scene.Id, editor.Shell.Scene!.Id);
        Assert.IsType<SceneSelection>(editor.Shell.Selection);   // the scene's settings, with When does it play?

        // Done: the dialog says so, and what to do next.
        export.ExportCommand.Execute(null);
        Assert.True(export.Done, export.Error);
        Assert.Equal("Exported.", export.StatusText);
        Assert.Contains("Mods folder", export.NextSteps);
    }

    [Fact]
    public void ACompleteModIsTheScenesWithTheEngineInside()
    {
        using var editor = new EditorHarness();
        var doc = editor.NewProject("Spring Day", "Player.SpringDay", "ScienceHouse");
        var export = new ExportViewModel(editor.Shell);
        Assert.Equal(ExportTarget.CompleteMod, export.Target);   // the default: players need nothing else
        export.ExportCommand.Execute(null);
        Assert.True(export.Done, export.Error);

        string folder = export.OutputFolder;
        var manifest = JObject.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")));
        Assert.Equal("Player.SpringDay", (string?)manifest["UniqueID"]);
        Assert.Equal("Player.SpringDay.dll", (string?)manifest["EntryDll"]);
        Assert.Null(manifest["ContentPackFor"]);
        Assert.Equal("Player.SpringDay", AssemblyNameOf(Path.Combine(folder, "Player.SpringDay.dll")));   // its own assembly name
        Assert.True(File.Exists(Path.Combine(folder, "studio.json")));
        Assert.True(File.Exists(Path.Combine(folder, "cutscenes", "first-scene.json")));
        // The project's pack folder is untouched: still a content pack, still the source.
        Assert.Equal("Player.SDCharacterStudio", JObject.Parse(File.ReadAllText(Path.Combine(doc.PackFolder, "manifest.json")))["ContentPackFor"]?["UniqueID"]?.ToString());

        // Exporting again replaces the earlier export; a folder holding something else is never overwritten.
        export.ExportCommand.Execute(null);
        Assert.True(export.Done, export.Error);
        string foreign = Path.Combine(editor.Folder, "someone-else");
        Directory.CreateDirectory(foreign);
        File.WriteAllText(Path.Combine(foreign, "notes.txt"), "mine");
        export.OutputFolder = foreign;
        export.ExportCommand.Execute(null);
        Assert.NotNull(export.Error);
        Assert.True(File.Exists(Path.Combine(foreign, "notes.txt")));
    }

    [Fact]
    public void ACodeModGetsTheScenesWithoutAManifest()
    {
        using var editor = new EditorHarness();
        editor.NewProject("Code Scenes", "Player.CodeScenes", "Town");
        var export = new ExportViewModel(editor.Shell) { Target = ExportTarget.CodeModScenes };
        Assert.False(export.CopyToMods);
        export.ExportCommand.Execute(null);
        Assert.True(export.Done, export.Error);
        string scenes = Path.Combine(export.OutputFolder, "scenes");
        Assert.False(File.Exists(Path.Combine(scenes, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(scenes, "studio.json")));
        Assert.Contains("internal static class Sdcs", File.ReadAllText(Path.Combine(export.OutputFolder, "Sdcs.cs")));
        Assert.Contains("Sdcs.Register(this)", File.ReadAllText(Path.Combine(export.OutputFolder, "README.txt")));
        Assert.Contains("Sdcs.Register", export.Result);
        // Exporting again replaces it.
        export.ExportCommand.Execute(null);
        Assert.True(export.Done, export.Error);
    }

    [Fact]
    public void TwoExportedModsNeverShareAnAssemblyName()
    {
        Assert.Equal("Player.SpringDay", ModExporter.AssemblyName("Player.SpringDay"));
        Assert.Equal("Bob_s.Mod", ModExporter.AssemblyName("Bob's.Mod"));
        Assert.Equal("Sdcs_1Up.Mod", ModExporter.AssemblyName("1Up.Mod"));
        Assert.NotEqual(ModExporter.AssemblyName("A.One"), ModExporter.AssemblyName("A.Two"));
    }

    // ---------------- benches (opt-in) ----------------

    private static string Benches => Path.Combine(TestEnvironment.RepoRoot ?? throw new InvalidOperationException("Run from the repository."), "benches");

    /// <summary>Make the benches' scenes with the editor, as a designer would (only with SDCS_BENCH_WRITE=1).</summary>
    [Fact]
    public void WriteBenchContent()
    {
        if (Environment.GetEnvironmentVariable("SDCS_BENCH_WRITE") != "1") return;

        // Code bench: Robin steps left and bows (a library animation with a custom pose), then speaks.
        using (var editor = new EditorHarness())
        {
            var doc = editor.NewProject("Code Bench Scenes", "Bench.SdcsCode", "ScienceHouse", "bow");
            Stage(editor, "Robin", 17, 6, "left");
            var studio = OpenStudio(editor, "Robin");
            MakePose(studio, "bow");
            MakeAnimation(studio, "step and bow", 3, 3);
            Finish(editor, studio, "step-and-bow", "Robin", "Code bench: the engine lives in my mod's DLL.");
            var export = new ExportViewModel(editor.Shell) { Target = ExportTarget.CodeModScenes, OutputFolder = Path.Combine(editor.Folder, "for my mod") };
            export.ExportCommand.Execute(null);
            Assert.True(export.Done, export.Error);
            // What a modder does with it: the scenes folder into the mod (Sdcs.cs comes from the engine build, see Run-Benches.ps1).
            string target = Path.Combine(Benches, "SdcsBench.CodeMod", "scenes");
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
            PackExporter.CopyDirectory(Path.Combine(export.OutputFolder, "scenes"), target);
        }

        // No-code bench: an editor project; Demetrius hops right and cheers, then speaks.
        string project = Path.Combine(Benches, "SdcsBench.NoCode", "NoCode Bench");
        if (Directory.Exists(project)) Directory.Delete(project, recursive: true);
        using (var editor = new EditorHarness())
        {
            var dialog = new NewProjectViewModel(editor.Shell) { Name = "NoCode Bench", Author = "SDCharacterStudio", UniqueId = "Bench.SdcsNoCode", FirstScene = "wave", Location = "ScienceHouse", ParentFolder = Path.Combine(Benches, "SdcsBench.NoCode") };
            dialog.CreateCommand.Execute(null);
            Assert.Null(dialog.Error);
            editor.Shell.Attach(dialog.Created!);
            Stage(editor, "Demetrius", 13, 7, "down");
            var studio = OpenStudio(editor, "Demetrius");
            MakePose(studio, "cheer");
            MakeAnimation(studio, "hop", 5, 3);
            Finish(editor, studio, "hop", "Demetrius", "No-code bench: I'm one complete mod.");
            editor.Shell.Document!.Save();
        }
    }

    /// <summary>
    /// The studio bench (only with SDCS_BENCH_EXPORT=1, D-48, D-49): Robin's pose made as one flat picture (her head
    /// selected by her body map and dragged down, her legs copied in live from one of her walking frames, so the engine
    /// draws pixels from two of her frames) and a flipbook (that pose, a walk two tiles left, her own look), exported as a
    /// complete mod to benches/.run/studio. The native run checks it plays with nothing from SDCharacterStudio installed.
    /// </summary>
    [Fact]
    public void ExportStudioBench()
    {
        if (Environment.GetEnvironmentVariable("SDCS_BENCH_EXPORT") != "1") return;
        using var editor = new EditorHarness();
        editor.NewProject("Studio Bench", "Bench.SdcsStudio", "ScienceHouse", "greet");
        Stage(editor, "Robin", 17, 6, "left");
        var studio = OpenStudio(editor, "Robin");
        studio.Settings.AdvancedPoseTools = false;
        Assert.NotNull(studio.NewPose("character", "curtsy"));
        var pose = (PoseDocumentViewModel)studio.ActiveDocument!;
        Assert.True(pose.IsFlat);
        pose.SelectPartCommand.Execute("head");
        var (hx, hy) = pose.SelectionShape!.Cells().First();
        pose.PointerDown(hx, hy, PointerButton.Primary); pose.PointerMove(hx, hy + 2); pose.PointerUp(hx, hy + 2);
        // Her legs from a walking step (sheet frame 13), copied in live: blue-channel map cells naming another frame.
        pose.PeekSheetFrameCommand.Execute(13);
        pose.Selection = new PixelRect(0, 24, 16, 8);
        pose.CopyFromPeekCommand.Execute(null);
        pose.ApplyTransformCommand.Execute(null);
        Assert.Equal("Copy from her frame 13", editor.Shell.Document!.History.UndoLabel);

        studio.NewAnimation("greet");
        var flipbook = (AnimationDocumentViewModel)studio.ActiveDocument!;
        Assert.True(flipbook.IsFlipbook);
        flipbook.Steps[0].Pose = pose.Model.Id;
        flipbook.Steps[0].Seconds = 0.8;
        flipbook.AddStepCommand.Execute(null);
        flipbook.Steps[1].Do = "Walk";
        flipbook.Steps[1].Distance = 2;
        flipbook.AddStepCommand.Execute(null);
        flipbook.Steps[2].Do = "Own";
        flipbook.Steps[2].Seconds = 0.5;
        editor.Shell.IsCharacterStudio = false;
        Finish(editor, studio, "greet", "Robin", "Studio bench: made with parts and a flipbook.");
        editor.Shell.Document!.Save();
        var export = new ExportViewModel(editor.Shell) { Target = ExportTarget.CompleteMod, OutputFolder = Path.Combine(Benches, ".run", "studio", "Studio Bench") };
        export.ExportCommand.Execute(null);
        Assert.True(export.Done, export.Error);
    }

    /// <summary>Export the no-code bench as a complete mod for the native run (only with SDCS_BENCH_EXPORT=1).</summary>
    [Fact]
    public void ExportNoCodeBench()
    {
        if (Environment.GetEnvironmentVariable("SDCS_BENCH_EXPORT") != "1") return;
        using var editor = new EditorHarness();
        editor.Shell.OpenProjectFile(Path.Combine(Benches, "SdcsBench.NoCode", "NoCode Bench", "NoCode Bench.sdcs"));
        var export = new ExportViewModel(editor.Shell) { Target = ExportTarget.CompleteMod, OutputFolder = Path.Combine(Benches, "SdcsBench.NoCode", "export", "Bench.SdcsNoCode") };
        Assert.False(export.HasErrors, string.Join("\n", export.Validation.Problems.Select(p => $"{p.Location}: {p.Message}")));
        export.ExportCommand.Execute(null);
        Assert.True(export.Done, export.Error);
    }

    private static void Stage(EditorHarness editor, string npc, int x, int y, string facing)
    {
        var doc = editor.Shell.Document!;
        var scene = editor.Shell.Scene!;
        doc.Edit("Stage", () =>
        {
            scene.Actors[0].Start = new Position { Tile = new[] { 16, 9 } };
            scene.Actors[0].Facing = "up";
            scene.Trigger = null;   // played by the mod (or another mod), not by walking in
        });
        editor.Shell.Stage.DropCharacter(npc, x, y);
        doc.Edit("Face", () => scene.Actors.Single(a => a.Id == npc).Facing = facing);
    }

    private static CharacterStudioViewModel OpenStudio(EditorHarness editor, string npc)
    {
        editor.Shell.IsCharacterStudio = true;
        var studio = editor.Shell.CharacterStudio;
        studio.Character = studio.Characters.First(c => c.Key == npc);
        return studio;
    }

    private static void MakePose(CharacterStudioViewModel studio, string name)
    {
        studio.NewPose("character", name);
        var pose = (PoseDocumentViewModel)studio.ActiveDocument!;
        pose.IsolateHead();
        studio.Palette.Primary = ColorSlots.SkinMid;
        pose.PointerDown(4, 20, PointerButton.Primary); pose.PointerMove(11, 20); pose.PointerUp(11, 20);
        studio.Palette.Primary = 0xFF2E6FD8;
        pose.PointerDown(5, 22, PointerButton.Primary); pose.PointerMove(10, 26); pose.PointerUp(10, 26);
    }

    private static void MakeAnimation(CharacterStudioViewModel studio, string name, int toX, int toY)
    {
        studio.Settings.AdvancedPoseTools = true;   // recorded on the timeline (Advanced, D-48)
        studio.NewAnimation(name);
        var clip = (AnimationDocumentViewModel)studio.ActiveDocument!;
        clip.IsRecording = true; clip.Playhead = 600;
        clip.BeginDrag(clip.Owners[0].Id);
        clip.EndDrag((toX - clip.StageLeft) * 16 + 8, (toY - clip.StageTop) * 16 + 15, false);
        clip.AddKey(clip.Rows.First(r => r.Track == TrackKind.Pose), 600);
    }

    private static void Finish(EditorHarness editor, CharacterStudioViewModel studio, string clipId, string actor, string line)
    {
        var shell = editor.Shell;
        var scene = shell.Scene!;
        Assert.True(studio.UseClipInScene(clipId, actor: actor));
        shell.Document!.Edit("Line", () => scene.Beats.Add(new SayBeat { Actor = actor, Text = line }));
        var result = shell.ValidateNow();
        Assert.True(result.Errors == 0, string.Join("\n", result.Problems.Select(p => p.Message)));
    }
}
