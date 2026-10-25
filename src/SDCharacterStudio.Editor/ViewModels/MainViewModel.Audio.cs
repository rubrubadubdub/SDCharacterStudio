using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using SDCharacterStudio.Editor.Audio;
using SDCharacterStudio.Editor.Infrastructure;
using SDCharacterStudio.Core.Audio;
using SDCharacterStudio.Editor.Project;

namespace SDCharacterStudio.Editor.ViewModels;

public sealed partial class MainViewModel
{
    public IAudioPreviewService? Audio { get; private set; }
    private int audioRequest;
    [ObservableProperty] private string audioStatus = "Choose a sound to preview.";

    private void ConfigureAudio()
    {
        audioRequest++;
        Audio?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Audio = null;
        string? game = Settings.GameModsFolder is { } mods ? Path.GetDirectoryName(mods.TrimEnd('\\', '/')) : null;
        string catalogRoot = Catalog?.Root ?? Path.Combine(EditorSettings.LocalFolder, "Audio");
        string metadata = Path.Combine(catalogRoot, "sound-previews.json");
        try
        {
            if (File.Exists(metadata)) game = JsonConvert.DeserializeObject<AudioPreviewCatalog>(File.ReadAllText(metadata))?.GameDirectory ?? game;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        { Output.Add("Warning", "Sound preview metadata could not be read: " + ex.Message); }
        Audio = new AudioPreviewService(Path.Combine(AppContext.BaseDirectory, "AudioHost", "SDCharacterStudio.AudioHost.exe"), catalogRoot, game ?? "");
        AudioStatus = "Play a sound; previews stop after 10 seconds.";
        OnPropertyChanged(nameof(Audio));
    }

    [RelayCommand]
    private Task PlaySoundPreview(string? cue) => PreviewSoundAsync(cue);

    public async Task PreviewSoundAsync(string? cue, int? pitch = null)
    {
        Ui.FlushPendingEdits();
        if (cue is "none" or "continue") { await StopSoundPreview(); return; }
        if (Audio == null) ConfigureAudio();
        int request = ++audioRequest;
        if (Audio == null || string.IsNullOrWhiteSpace(cue))
        { AudioStatus = "Load a game export and choose a sound first."; return; }
        AudioStatus = "Loading " + cue + "…";
        AudioPreviewResult result;
        try
        {
            if (PackMusic.TryGetId(cue, out _))
            {
                if (Document == null || Audio is not AudioPreviewService service) throw new InvalidDataException("Open the pack containing this music first.");
                string catalog = PackMusicPreview.Prepare(Document, cue);
                result = await service.PlayFromCatalogAsync(cue, catalog, pitch);
            }
            else result = await Audio.PlayAsync(cue, pitch);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        { result = AudioPreviewResult.Failure(ex.Message); }
        if (request != audioRequest) return;
        AudioStatus = result.Ok ? "Playing " + cue + " · stops after 10 seconds" : result.Error ?? "Sound preview failed.";
        if (!result.Ok) Output.Add("Warning", AudioStatus);
    }

    public void ImportMusic(Action<string> assign)
    {
        if (Document == null) return;
        Ui.FlushPendingEdits();
        string? source = Ui.OpenFile("Import music", "Music (WAV, OGG)|*.wav;*.ogg");
        if (source == null) return;
        try
        {
            MusicImporter.Import(Document, source, assign);
            Ui.Toast("Music imported", Path.GetFileName(source) + " was copied into this pack.", ToastKind.Success);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        { Ui.Toast("Couldn't import music", ex.Message, ToastKind.Error); }
    }

    [RelayCommand]
    private async Task StopSoundPreview()
    {
        audioRequest++;
        if (Audio != null) await Audio.StopAsync();
        AudioStatus = "Stopped";
    }
}
