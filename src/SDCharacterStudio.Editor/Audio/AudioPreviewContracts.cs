using Newtonsoft.Json;

namespace SDCharacterStudio.Editor.Audio;

public enum AudioHostState { Unavailable, Starting, Ready, Playing, Faulted }

public sealed record AudioHostStatus(AudioHostState State, string Message);

public sealed record AudioPreviewResult(bool Ok, string? Error = null, string? Cue = null)
{
    public static AudioPreviewResult Success(string cue) => new(true, null, cue);
    public static AudioPreviewResult Failure(string error) => new(false, error);
}

public interface IAudioPreviewService : IAsyncDisposable
{
    AudioHostStatus Status { get; }
    Task<AudioPreviewResult> PlayAsync(string cue, int? pitch = null, CancellationToken cancellation = default);
    Task StopAsync(CancellationToken cancellation = default);
}

public sealed class AudioPreviewCatalog
{
    [JsonProperty("Format")] public int Format { get; init; }
    [JsonProperty("GameDirectory")] public string? GameDirectory { get; init; }
    [JsonProperty("Cues")] public Dictionary<string, AudioPreviewCue> Cues { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AudioPreviewCue
{
    [JsonProperty("Category")] public string? Category { get; init; }
    [JsonProperty("Looped")] public bool Looped { get; init; }
    [JsonProperty("UseReverb")] public bool UseReverb { get; init; }
    [JsonProperty("StreamedVorbis")] public bool StreamedVorbis { get; init; }
    [JsonProperty("Variants")] public List<AudioPreviewVariant> Variants { get; init; } = new();
    [JsonProperty("Error")] public string? Error { get; init; }
}

public sealed class AudioPreviewVariant
{
    [JsonProperty("File")] public string File { get; init; } = "";
}
