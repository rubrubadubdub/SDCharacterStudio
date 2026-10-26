using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Audio;
using SDCharacterStudio.Editor.Project;
using SDCharacterStudio.Editor.Export;
using System.IO;
using System.IO.Compression;
using SDCharacterStudio.Editor.Audio;
using Newtonsoft.Json;
using System.Diagnostics;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Tests;

public sealed class MusicImportTests
{
    [Fact]
    public void ImportSelectsTrackAndMediaSurvivesUndoRedoReopenPreviewAndZip()
    {
        using var editor = new EditorHarness();
        var document = editor.NewProject();
        string source = Path.Combine(editor.Folder, "My Song.wav");
        byte[] bytes = Wave(); File.WriteAllBytes(source, bytes);
        editor.Ui.OpenFiles.Enqueue(source);
        ImportAction(editor).Action.Execute(null);
        var entry = Assert.Single(document.Studio.Music);
        Assert.Equal("My Song", entry.Value.Name);
        Assert.Equal(PackMusic.Reference(entry.Key), editor.Shell.Scene!.Music);
        Assert.Equal(bytes, document.ReadBinaryAsset(entry.Value.File!));
        Assert.Contains(editor.Field<ChoiceField>("Music").Options, o => o.Value == editor.Shell.Scene.Music && o.Label == "My Song");
        File.Delete(source);
        Assert.True(document.Undo());
        Assert.Empty(document.Studio.Music);
        Assert.Equal("none", editor.Shell.Scene!.Music);
        Assert.False(document.CreateOverlayFileSystem().Exists(entry.Value.File!));
        Assert.True(document.Redo());
        var preview = editor.Shell.Preview.PreparePreviewPack(document, editor.Shell.Scene!.Id!);
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(preview.Folder, entry.Value.File!)));
        string zip = Path.Combine(editor.Folder, "pack.zip");
        var result = PackExporter.Export(document, editor.Shell.Catalog, new ExportOptions { ZipPath = zip });
        Assert.True(result.Success, result.Message);
        using (var archive = ZipFile.OpenRead(zip))
            Assert.Contains(archive.Entries, e => e.FullName.EndsWith(entry.Value.File!, StringComparison.Ordinal));
        var reopened = PackDocument.Open(document.ProjectPath);
        Assert.Equal("My Song", Assert.Single(reopened.Studio.Music).Value.Name);
        Assert.Equal(bytes, reopened.ReadBinaryAsset(entry.Value.File!));
    }

    [Fact]
    public void DuplicateNamesNeverOverwriteAndMusicBeatGetsImportedTrack()
    {
        using var editor = new EditorHarness(); var doc = editor.NewProject();
        string source = Path.Combine(editor.Folder, "song.wav"); File.WriteAllBytes(source, Wave());
        editor.Ui.OpenFiles.Enqueue(source); ImportAction(editor).Action.Execute(null);
        string first = editor.Shell.Scene!.Music;
        editor.Shell.Edit("Add music", () => editor.Shell.Scene.Beats.Add(new MusicBeat { Cue = "none" }));
        editor.Shell.Select(new BeatSelection(editor.Shell.Scene.Id!, LaneNames.Main, 0));
        editor.Ui.OpenFiles.Enqueue(source); ImportAction(editor).Action.Execute(null);
        Assert.Equal(2, doc.Studio.Music.Count);
        Assert.Equal(2, doc.Studio.Music.Values.Select(t => t.File).Distinct().Count());
        Assert.NotEqual(first, Assert.IsType<MusicBeat>(editor.Shell.Scene.Beats[0]).Cue);
        Assert.Equal(first, editor.Shell.Scene.Music);
        string second = Assert.IsType<MusicBeat>(editor.Shell.Scene.Beats[0]).Cue!;
        var choice = editor.Field<ChoiceField>("Music");
        choice.Text = choice.Text;
        Assert.Equal(second, Assert.IsType<MusicBeat>(editor.Shell.Scene.Beats[0]).Cue);
        Assert.Equal(2, doc.Studio.Music.Values.Select(t => t.Name).Distinct().Count());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("bad.wav")]
    [InlineData("bad.mp3")]
    public void CancelOrInvalidMusicDoesNotChangeDocument(string? filename)
    {
        using var editor = new EditorHarness(); var doc = editor.NewProject();
        string before = doc.CaptureState();
        if (filename != null) { string file = Path.Combine(editor.Folder, filename); File.WriteAllText(file, "not audio"); editor.Ui.OpenFiles.Enqueue(file); }
        ImportAction(editor).Action.Execute(null);
        Assert.Equal(before, doc.CaptureState());
        if (filename != null) Assert.Contains(editor.Ui.Toasts, t => t.Kind == Infrastructure.ToastKind.Error);
    }

    private static ActionField ImportAction(EditorHarness editor) =>
        Assert.Single(editor.Shell.Inspector.Fields.OfType<ActionField>(), f => f.ButtonText == "Import music…");

    [Fact]
    public void UnsavedMusicAuditionUsesManagedBytesAndCurrentLoopSetting()
    {
        using var editor = new EditorHarness(); var doc = editor.NewProject();
        string file = Path.Combine(editor.Folder, "audition.wav"); File.WriteAllBytes(file, Wave());
        editor.Ui.OpenFiles.Enqueue(file); ImportAction(editor).Action.Execute(null);
        File.Delete(file);
        string cue = editor.Shell.Scene!.Music;
        string catalog = PackMusicPreview.Prepare(doc, cue);
        var metadata = JsonConvert.DeserializeObject<AudioPreviewCatalog>(File.ReadAllText(Path.Combine(catalog, "sound-previews.json")))!;
        Assert.True(metadata.Cues[cue].Looped);
        Assert.Equal(Wave(), File.ReadAllBytes(Path.Combine(catalog, metadata.Cues[cue].Variants[0].File)));
        editor.Field<BoolField>("Loop music").Value = false;
        string updated = PackMusicPreview.Prepare(doc, cue);
        Assert.NotEqual(catalog, updated);
        Assert.False(JsonConvert.DeserializeObject<AudioPreviewCatalog>(File.ReadAllText(Path.Combine(updated, "sound-previews.json")))!.Cues[cue].Looped);
        Assert.True(doc.Undo());
        Assert.Equal(catalog, PackMusicPreview.Prepare(doc, cue));
    }

    [Fact]
    public void OversizedAndTruncatedAudioAreRejectedWithoutMutation()
    {
        using var editor = new EditorHarness(); var doc = editor.NewProject();
        string before = doc.CaptureState(), path = Path.Combine(editor.Folder, "broken.wav");
        var wave = Wave(); File.WriteAllBytes(path, wave[..^2]);
        Assert.Throws<InvalidDataException>(() => MusicImporter.Import(doc, path, _ => { }));
        using (var file = File.Create(path)) file.SetLength(MusicImporter.MaximumBytes + 1L);
        Assert.Throws<InvalidDataException>(() => MusicImporter.Import(doc, path, _ => { }));
        Assert.Equal(before, doc.CaptureState());
    }

    [Fact]
    public void OggSignatureWithoutValidVorbisStreamIsRejected()
    {
        using var editor = new EditorHarness(); var doc = editor.NewProject();
        byte[] invalid = new byte[58]; "OggS"u8.CopyTo(invalid); invalid[5] = 4; invalid[26] = 1; invalid[27] = 30;
        invalid[28] = 1; "vorbis"u8.CopyTo(invalid.AsSpan(29));
        string file = Path.Combine(editor.Folder, "fake.ogg"); File.WriteAllBytes(file, invalid);
        string before = doc.CaptureState();
        Assert.Throws<InvalidDataException>(() => MusicImporter.Import(doc, file, cue => editor.Shell.Scene!.Music = cue));
        Assert.Equal(before, doc.CaptureState());
    }

    [Fact]
    public void MusicNamedLikeVanillaCueDoesNotChangeVanillaSelectionOnTextCommit()
    {
        using var editor = new EditorHarness(); var doc = editor.NewProject();
        string file = Path.Combine(editor.Folder, "coin.wav"); File.WriteAllBytes(file, Wave());
        editor.Ui.OpenFiles.Enqueue(file); ImportAction(editor).Action.Execute(null);
        editor.Choose("Music", "coin");
        Assert.Equal("coin", editor.Shell.Scene!.Music);
        var field = editor.Field<ChoiceField>("Music"); field.Text = field.Text;
        Assert.Equal("coin", editor.Shell.Scene.Music);
        editor.Choose("Music", "music:coin");
        field = editor.Field<ChoiceField>("Music"); field.Text = field.Text;
        Assert.Equal("music:coin", editor.Shell.Scene.Music);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MalformedMusicMetadataLeavesInspectorUsable(bool nullDictionary)
    {
        using var editor = new EditorHarness(); var doc = editor.NewProject();
        editor.Shell.Edit("Malformed external edit", () =>
        {
            if (nullDictionary) doc.Studio.Music = null!;
            else doc.Studio.Music["broken"] = null!;
            editor.Shell.Scene!.Music = "music:broken";
        });
        Assert.NotNull(ImportAction(editor));
        Assert.Throws<InvalidDataException>(() => PackMusicPreview.Prepare(doc, "music:broken"));
    }

    [AudioFact]
    public async Task ImportedWavAndOggPlayOfflineThroughPackCatalog()
    {
        using var editor = new EditorHarness(); var doc = editor.NewProject();
        string wave = Path.Combine(editor.Folder, "tone.wav"), ogg = Path.Combine(editor.Folder, "tone.ogg");
        File.WriteAllBytes(wave, Wave());
        var encode = new ProcessStartInfo(Environment.GetEnvironmentVariable("SDCS_FFMPEG") ?? "ffmpeg") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-v", "error", "-i", wave, "-c:a", "libvorbis", ogg }) encode.ArgumentList.Add(arg);
        using (var process = Process.Start(encode)!)
        {
            string error = await process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync(); Assert.True(process.ExitCode == 0, error);
        }
        string game = Environment.GetEnvironmentVariable("SDCS_GAME_DIR") ?? @"C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley";
        string host = Path.Combine(TestEnvironment.RepoRoot!, "src/SDCharacterStudio.AudioHost/bin/Debug/net8.0-windows/SDCharacterStudio.AudioHost.exe");
        await using var service = new AudioPreviewService(host, editor.Folder, game);
        foreach (var file in new[] { wave, ogg })
        {
            string cue = MusicImporter.Import(doc, file, value => editor.Shell.Scene!.Music = value);
            File.Delete(file);
            string catalog = PackMusicPreview.Prepare(doc, cue);
            var result = await service.PlayFromCatalogAsync(cue, catalog);
            Assert.True(result.Ok, result.Error);
            Assert.Equal(AudioHostState.Playing, service.Status.State);
            await service.StopAsync();
            Assert.Equal(AudioHostState.Ready, service.Status.State);
        }
    }

    internal static byte[] Wave()
    {
        using var data = new MemoryStream(); using var writer = new BinaryWriter(data);
        int count = 2205;
        writer.Write("RIFF"u8); writer.Write(36 + count * 2); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(22050); writer.Write(44100); writer.Write((short)2); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(count * 2);
        for (int i = 0; i < count; i++) writer.Write((short)(Math.Sin(i * 2 * Math.PI * 440 / 22050) * 1000));
        return data.ToArray();
    }

    [Fact]
    public void RenderImportedMusicControls()
    {
        if (Environment.GetEnvironmentVariable("SDCS_UI_SNAPSHOTS") is not { Length: > 0 } output) return;
        EditorUiThread.Run(() =>
        {
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Dark, Wpf.Ui.Controls.WindowBackdropType.None, updateAccent: false);
            using var editor = new EditorHarness(); editor.NewProject();
            string source = Path.Combine(editor.Folder, "Evening theme.wav"); File.WriteAllBytes(source, Wave());
            editor.Ui.OpenFiles.Enqueue(source); ImportAction(editor).Action.Execute(null);
            var window = new MainWindow { Width = 1400, Height = 1000, ShowActivated = false, WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.None,
                WindowStartupLocation = System.Windows.WindowStartupLocation.Manual, Left = System.Windows.SystemParameters.VirtualScreenLeft - 4000, Top = 0 };
            editor.Shell.Settings.WindowMaximized = false;
            window.Attach(editor.Shell, new Infrastructure.WpfUserInterface(window));
            try
            {
                window.Show(); window.UpdateLayout();
                var deadline = DateTime.UtcNow.AddMilliseconds(300); EditorUiThread.PumpUntil(() => DateTime.UtcNow >= deadline);
                var image = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                image.Render(window);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
                using var file = File.Create(Path.Combine(output, "music-inspector.png")); encoder.Save(file);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void SceneAndMusicBeatOfferImportAndAudition()
    {
        using var editor = new EditorHarness();
        editor.NewProject();
        AssertMusicControls();
        editor.Shell.Edit("Add music", () => editor.Shell.Scene!.Beats.Add(new MusicBeat { Cue = "none" }));
        editor.Shell.Select(new BeatSelection(editor.Shell.Scene!.Id!, LaneNames.Main, 0));
        AssertMusicControls();

        void AssertMusicControls()
        {
            var actions = editor.Shell.Inspector.Fields.OfType<ActionField>().Select(f => f.ButtonText).ToArray();
            Assert.Contains("Import music…", actions);
            Assert.Contains("Play music", actions);
            Assert.Contains("Stop preview", actions);
        }
    }
}
