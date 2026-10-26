using SDCharacterStudio.Core.Audio;

namespace SDCharacterStudio.Editor.ViewModels;

public sealed partial class InspectorViewModel
{
    private void MusicFields(string key, Func<string?> get, Action<string?> set)
    {
        Fields.Add(new ChoiceField("Music", key, MusicOptions(), get, set, Commit)
        { IsEditable = true, Rebuild = true, Help = "Choose a game cue or import your own WAV/OGG file below." });
        Fields.Add(new ActionField("Import music…", () => shell.ImportMusic(cue => set(cue))));
        Fields.Add(new ActionField("Play music", () => _ = shell.PreviewSoundAsync(get())));
        Fields.Add(new ActionField("Stop preview", () => shell.StopSoundPreviewCommand.Execute(null)));
        if (PackMusic.TryGetId(get(), out var id) && shell.Document?.Studio.Music is { } tracks && tracks.TryGetValue(id, out var track) && track != null)
            Fields.Add(new BoolField("Loop music", "", () => track.Loop, v => track.Loop = v, Commit)
            { Description = "Repeat until the scene changes or stops the music. Applies wherever this imported track is used." });
    }
}
