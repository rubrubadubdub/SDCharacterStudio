namespace SDCharacterStudio.Runtime.Playback;

public sealed class MenuOwnership<T> where T : class
{
    private T? owned;
    public void Capture(T? menu) => owned = menu;
    public void Release(T? active, Action<T> close)
    {
        var menu = owned;
        owned = null;
        if (menu != null && ReferenceEquals(menu, active)) close(menu);
    }
}
