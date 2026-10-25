namespace SDCharacterStudio.Bridge;

public sealed class BridgeConfig
{
    public string ExportFolder { get; set; } = @"%LOCALAPPDATA%\SDCharacterStudio\Export";
    public string Profile { get; set; } = "default";
    public bool AutoExportOnSaveLoaded { get; set; } = true;
    public bool ExportLocations { get; set; } = true;
    public string PreviewPipeName { get; set; } = "SDCharacterStudio.Preview";
    public string PreviewRequestFolder { get; set; } = @"%LOCALAPPDATA%\SDCharacterStudio\Preview\requests";
    public string OutputPath()
    {
        if (string.IsNullOrWhiteSpace(Profile) || Profile.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Profile is "." or ".." || Profile.Contains('/') || Profile.Contains('\\'))
            throw new ArgumentException("Profile must be a single directory name.");
        return Path.Combine(Path.GetFullPath(Environment.ExpandEnvironmentVariables(ExportFolder)), Profile);
    }
}
