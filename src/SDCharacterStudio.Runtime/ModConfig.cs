namespace SDCharacterStudio.Runtime;

public sealed class ModConfig
{
    public bool Enabled { get; set; } = true;
    public bool LogCompiledScripts { get; set; }
    public bool MuteStandInSoundsDuringDialogue { get; set; } = true;
}
