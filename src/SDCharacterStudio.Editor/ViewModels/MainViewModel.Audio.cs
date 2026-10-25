using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using SDCharacterStudio.Editor.Audio;
using SDCharacterStudio.Editor.Infrastructure;

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
        if (Catalog == null) return;
        string? game = Settings.GameModsFolder is { } mods ? Path.GetDirectoryName(mods.TrimEnd('\\', '/')) : null;
        string metadata = Path.Combine(Catalog.Root, "sound-previews.json");
        try
        {
            if (File.Exists(metadata)) game = JsonConvert.DeserializeObject<AudioPreviewCatalog>(File.ReadAllText(metadata))?.GameDirectory ?? game;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        { Output.Add("Warning", "Sound preview metadata could not be read: " + ex.Message); }
        Audio = new AudioPreviewService(Path.Combine(AppContext.BaseDirectory, "AudioHost", "SDCharacterStudio.AudioHost.exe"), Catalog.Root, game ?? "");
        AudioStatus = "Play a sound; previews stop after 10 seconds.";
        OnPropertyChanged(nameof(Audio));
    }

    [RelayCommand]
    private Task PlaySoundPreview(string? cue) => PreviewSoundAsync(cue);

    public async Task PreviewSoundAsync(string? cue, int? pitch = null)
    {
        Ui.FlushPendingEdits();
        int request = ++audioRequest;
        if (Audio == null || string.IsNullOrWhiteSpace(cue))
        { AudioStatus = "Load a game export and choose a sound first."; return; }
        AudioStatus = "Loading " + cue + "…";
        var result = await Audio.PlayAsync(cue, pitch);
        if (request != audioRequest) return;
        AudioStatus = result.Ok ? "Playing " + cue + " · stops after 10 seconds" : result.Error ?? "Sound preview failed.";
        if (!result.Ok) Output.Add("Warning", AudioStatus);
    }

    [RelayCommand]
    private async Task StopSoundPreview()
    {
        audioRequest++;
        if (Audio != null) await Audio.StopAsync();
        AudioStatus = "Stopped";
    }
}
