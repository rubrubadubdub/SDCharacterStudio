using System.IO;
using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Palette;
using SDCharacterStudio.Editor.Infrastructure;
using SDCharacterStudio.Editor.Preview;
using SDCharacterStudio.Editor.Studio;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>
/// Roadmap M8 AC (D-39), native half, in the AC's own steps: in project A, choreography using a custom pose is made in a
/// scene, saved to the library and to My library, and the pack exports and plays. Project B uses it from My library, cast
/// on Demetrius at another spot and mirrored (with his copy of the pose), exports, and plays with neither the library nor
/// project A present. The Character Studio preview plays too. Off by default: the orchestration script stages an isolated
/// game (disposable in-memory farm in ScienceHouse, no save) and sets SDCS_NATIVE_PIPE, SDCS_NATIVE_DRIVER and
/// SDCS_NATIVE_SUITE=m8.
/// </summary>
public class NativeAnimationTests
{
    private readonly List<string> log = new();
    private string evidence = "";
    private void Note(string line) { log.Add($"{DateTime.Now:HH:mm:ss.fff} {line}"); File.WriteAllLines(Path.Combine(evidence, "editor-native-m8.log"), log); }

    [Fact]
    public async Task LibraryAnimationPlaysCastMirroredInTheGame()
    {
        string? pipe = Environment.GetEnvironmentVariable("SDCS_NATIVE_PIPE");
        string? driver = Environment.GetEnvironmentVariable("SDCS_NATIVE_DRIVER");
        if (string.IsNullOrEmpty(pipe) || string.IsNullOrEmpty(driver) || Environment.GetEnvironmentVariable("SDCS_NATIVE_SUITE") != "m8") return;
        evidence = Environment.GetEnvironmentVariable("SDCS_NATIVE_EVIDENCE") ?? Path.Combine(driver, "editor-evidence");
        Directory.CreateDirectory(evidence);
        using var shelf = new TempFolder();
        var library = new PersonalLibrary(Path.Combine(shelf.Path, "Library"));
        string packA;

        // ---------- Project A: Robin steps left and bows, made in a scene ----------
        using (var editorA = new EditorHarness(pipeName: pipe))
        {
            var docA = editorA.NewProject("Bow Source", "Player.D39Source", "ScienceHouse");
            packA = docA.PackFolder;
            var shell = editorA.Shell;
            Watch(shell);
            var scene = shell.Scene!;
            docA.Edit("Stage", () =>
            {
                scene.Actors[0].Start = new Position { Tile = new[] { 16, 9 } };
                scene.Actors[0].Facing = "up";
            });
            shell.Stage.DropCharacter("Robin", 17, 6);
            var studio = shell.CharacterStudio;
            studio.Personal = library;
            shell.IsCharacterStudio = true;
            studio.Character = studio.Characters.First(c => c.Key == "Robin");
            studio.Settings.AdvancedPoseTools = true;   // layers: isolate the head (D-48)
            studio.NewPose("character", "bow");
            var pose = (PoseDocumentViewModel)studio.ActiveDocument!;
            pose.IsolateHead();
            studio.Palette.Primary = ColorSlots.SkinMid;
            pose.PointerDown(4, 20, PointerButton.Primary); pose.PointerMove(11, 20); pose.PointerUp(11, 20);

            // The scene's own clip: record one tile left at 600 ms, bow from there.
            string clipId = studio.NewSceneClip("curtsy")!;
            var clip = (AnimationDocumentViewModel)studio.ActiveDocument!;
            clip.IsRecording = true; clip.Playhead = 600;
            clip.BeginDrag("Robin");
            clip.EndDrag((16 - clip.StageLeft) * 16 + 8, (6 - clip.StageTop) * 16 + 15, false);
            clip.AddKey(clip.Rows.First(r => r.Owner.Id == "Robin" && r.Track == TrackKind.Pose), 600);
            clip.SelectedKey = null;
            editorA.Ui.Prompts.Enqueue("step and bow");
            clip.Fields.OfType<ActionField>().Single(f => f.ButtonText.StartsWith("Save to library")).Action.Execute(null);
            Xunit.Assert.NotNull(studio.Library!.Clip("step-and-bow"));
            Xunit.Assert.NotNull(studio.SaveToMyLibrary(LibraryKind.Clip, "step-and-bow"));
            Note("project A: scene clip saved to the library and to My library: " + string.Join(", ", library.Entries().Select(e => $"{e.Title} ({e.Detail})")));

            shell.IsCharacterStudio = false;
            shell.Storyboard.Insert(LaneNames.Main, 0, Schema.BeatCatalog.Kind("Clip"), b => ((ClipBeat)b).Clip = clipId);
            docA.Edit("Line", () => scene.Beats.Add(new SayBeat { Actor = "Robin", Text = "Please. I mean it." }));
            Export(shell, "A");
            await WaitReady(shell);
            var robin = await PlayAndFollow(shell, driver, "Robin", "Player.D39Source.DevPreview");
            Xunit.Assert.Equal(new[] { 16, 6 }, robin);   // one tile left of 17,6
        }
        Xunit.Assert.False(Directory.Exists(packA));   // project A is gone with its harness

        // ---------- Project B: Demetrius, elsewhere, mirrored, with his own copy of the pose ----------
        using var editor = new EditorHarness(pipeName: pipe);
        var shellB = editor.Shell;
        Watch(shellB);
        var doc = editor.NewProject("Bow Reuse", "Player.D39Reuse", "ScienceHouse");
        var studioB = shellB.CharacterStudio;
        studioB.Personal = library;
        shellB.IsCharacterStudio = true;
        studioB.Character = studioB.Characters.First(c => c.Key == "Demetrius");
        studioB.ShowMyLibrary = true;
        studioB.UseFromMyLibrary(studioB.MyAnimations.Single());
        string copy = studioB.CopyPoseFor("robin-bow", studioB.Characters.First(c => c.Key == "Demetrius"))!;
        Note($"project B: used from My library; copied the pose as {copy}");
        shellB.IsCharacterStudio = false;
        var sceneB = shellB.Scene!;
        doc.Edit("Stage", () =>
        {
            sceneB.Actors[0].Start = new Position { Tile = new[] { 16, 9 } };
            sceneB.Actors[0].Facing = "up";
            sceneB.Actors.Add(new Actor { Id = "Demetrius", Kind = "Npc", Npc = "Demetrius", Start = new Position { Tile = new[] { 13, 7 } }, Facing = "down" });
        });
        shellB.Select(new ActorSelection(sceneB.Id!, sceneB.Actors.FindIndex(a => a.Id == "Demetrius")));
        Xunit.Assert.True(studioB.UseClipInScene("step-and-bow", mirror: true, poseMap: new Dictionary<string, string> { ["robin-bow"] = copy }));
        doc.Edit("Line", () => sceneB.Beats.Add(new SayBeat { Actor = "Demetrius", Text = "There. Mirrored and recast." }));
        Export(shellB, "B");
        Directory.Delete(library.Root, recursive: true);
        Note("My library deleted before playing project B");

        await WaitReady(shellB);
        var demetrius = await PlayAndFollow(shellB, driver, "Demetrius", "Player.D39Reuse.DevPreview");
        Xunit.Assert.Equal(new[] { 14, 7 }, demetrius);   // mirrored: one tile right of 13,7

        // ---------- Character Studio: Preview in game plays the animation in a temporary scene that ends by itself ----------
        shellB.IsCharacterStudio = true;
        studioB.OpenItem(LibraryKind.Clip, "step-and-bow");
        await shellB.PreviewInGameCommand.ExecuteAsync(null);
        Note("studio toasts: " + string.Join(" | ", editor.Ui.Toasts.Select(t => t.Title + ": " + t.Message)));
        var playing = await WaitState(driver, s => (bool?)s["RuntimePlaying"] == true, TimeSpan.FromSeconds(30));
        Note("studio preview playing: " + playing["Event"]);
        Xunit.Assert.Contains(CharacterStudioViewModel.PreviewSceneId, (string?)playing["Event"]);
        await WaitState(driver, s => (bool?)s["RuntimePlaying"] == false && (bool?)s["EventUp"] == false && (bool?)s["PlayerFree"] == true, TimeSpan.FromSeconds(30));
        Note("studio preview ended by itself: " + DriverCheck(driver, "stopped", TimeSpan.FromSeconds(20)));
        Note("PASS");
    }

    private void Watch(MainViewModel shell) =>
        shell.Output.Lines.CollectionChanged += (_, e) => { foreach (OutputLine line in e.NewItems ?? Array.Empty<OutputLine>()) Note($"output [{line.Level}] {line.Text}"); };

    /// <summary>Pack › Export, as the designer does it.</summary>
    private void Export(MainViewModel shell, string name)
    {
        var export = new ExportViewModel(shell);
        Xunit.Assert.False(export.HasErrors, string.Join("\n", export.Validation.Problems.Select(p => $"{p.Location}: {p.Message}")));
        export.ExportCommand.Execute(null);
        Xunit.Assert.True(export.Done, export.Error);
        Note($"project {name} exported to {shell.Document!.PackFolder}: " + string.Join(", ", Directory.EnumerateFiles(shell.Document.PackFolder, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(shell.Document.PackFolder, f).Replace('\\', '/'))));
    }

    private async Task WaitReady(MainViewModel shell)
    {
        var until = DateTime.UtcNow.AddMinutes(5);
        do { await shell.RefreshBridgeStatus(); if (shell.Bridge.State == BridgeState.Ready) break; await Task.Delay(1000); } while (DateTime.UtcNow < until);
        Xunit.Assert.Equal(BridgeState.Ready, shell.Bridge.State);
    }

    /// <summary>F5; follow the actor while the clip plays (its stand-in must show) and return its tile at the line; stop.</summary>
    private async Task<int[]> PlayAndFollow(MainViewModel shell, string driver, string actor, string pack)
    {
        int toasts = ((FakeUi)shell.Ui).Toasts.Count;
        await shell.PreviewInGameCommand.ExecuteAsync(null);
        Xunit.Assert.Contains(((FakeUi)shell.Ui).Toasts.Skip(toasts), t => t.Title == "Playing in the game");
        bool hidden = false;
        JObject? sample = null;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            JObject state;
            try { state = JObject.Parse(File.ReadAllText(Path.Combine(driver, "state.json"))); }
            catch (Exception ex) when (ex is IOException or Newtonsoft.Json.JsonException) { await Task.Delay(30); continue; }
            if ((bool?)state["RuntimePlaying"] != true || state["Event"]?.Type != JTokenType.String) { await Task.Delay(50); continue; }
            Xunit.Assert.Contains(pack, (string?)state["Event"]);
            var actors = JObject.Parse(DriverCheck(driver, "actors", TimeSpan.FromSeconds(10)))["Data"]!;
            sample = actors["Actors"]!.OfType<JObject>().FirstOrDefault(a => (string?)a["Name"] == actor);
            if ((bool?)sample?["Invisible"] == true && !hidden) { hidden = true; Note($"{actor} during the clip (stand-in showing): {sample!.ToString(Newtonsoft.Json.Formatting.None)}"); }
            if ((bool?)state["DialogueUp"] == true) break;
        }
        Note($"{actor} at the line: {sample?.ToString(Newtonsoft.Json.Formatting.None)}");
        Xunit.Assert.True(hidden, $"{actor}'s stand-in (the bow pose) never showed.");
        await shell.StopPreviewCommand.ExecuteAsync(null);
        await WaitState(driver, s => (bool?)s["RuntimePlaying"] == false && (bool?)s["EventUp"] == false && (bool?)s["PlayerFree"] == true, TimeSpan.FromSeconds(30));
        Note("stopped: " + DriverCheck(driver, "stopped", TimeSpan.FromSeconds(20)));
        return sample!["Tile"]!.ToObject<int[]>()!;
    }

    private static string DriverCheck(string driver, string check, TimeSpan timeout)
    {
        string id = check + "-" + Guid.NewGuid().ToString("N")[..8];
        string folder = Path.Combine(driver, "assertions");
        string temporary = Path.Combine(folder, id + ".tmp");
        File.WriteAllText(temporary, new JObject { ["Id"] = id, ["Check"] = check }.ToString());
        File.Move(temporary, Path.Combine(folder, id + ".json"));
        string result = Path.Combine(driver, "assertion-results", id + ".json");
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(result))
            {
                try
                {
                    var json = JObject.Parse(File.ReadAllText(result));
                    Xunit.Assert.True((bool?)json["Ok"] == true, $"Driver check '{check}' failed: {json["Error"]}");
                    return json.ToString(Newtonsoft.Json.Formatting.None);
                }
                catch (IOException) { }
                catch (Newtonsoft.Json.JsonException) { }
            }
            Thread.Sleep(30);
        }
        throw new TimeoutException($"Driver check '{check}' didn't answer.");
    }

    private static async Task<JObject> WaitState(string driver, Func<JObject, bool> condition, TimeSpan timeout)
    {
        string path = Path.Combine(driver, "state.json");
        var deadline = DateTime.UtcNow + timeout;
        JObject? last = null;
        while (DateTime.UtcNow < deadline)
        {
            try { last = JObject.Parse(File.ReadAllText(path)); if (condition(last)) return last; }
            catch (IOException) { }
            catch (Newtonsoft.Json.JsonException) { }
            await Task.Delay(50);
        }
        throw new TimeoutException("Native state didn't reach the expected condition. Last: " + last);
    }
}
