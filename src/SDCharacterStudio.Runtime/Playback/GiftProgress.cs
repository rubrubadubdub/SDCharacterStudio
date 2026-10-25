namespace SDCharacterStudio.Runtime.Playback;

public sealed class GiftProgress
{
    private readonly double startedAt;
    private bool granted;
    public GiftProgress(double startedAt, string message) { this.startedAt = startedAt; Message = message; }
    public string Message { get; }
    public bool MessageShown { get; private set; }
    public void GrantOnce(Action grant)
    {
        if (granted) return;
        granted = true;
        grant();
    }
    public bool TryShowMessage(double now, bool menuOpen, bool dialogueUp)
    {
        if (MessageShown || now - startedAt < 2500 || menuOpen || dialogueUp) return false;
        MessageShown = true;
        return true;
    }
}
