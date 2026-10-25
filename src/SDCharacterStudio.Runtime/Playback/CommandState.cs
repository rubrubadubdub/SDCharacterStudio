namespace SDCharacterStudio.Runtime.Playback;

public sealed class CommandState<T> where T : class
{
    private object? owner;
    private object? script;
    private int index;
    private T? value;
    public T? Get(object owner, object script, int index)
    {
        if (!ReferenceEquals(this.owner, owner) || !ReferenceEquals(this.script, script) || this.index != index) Clear();
        return value;
    }
    public void Set(object owner, object script, int index, T value)
    { this.owner = owner; this.script = script; this.index = index; this.value = value; }
    public void Clear() { owner = null; script = null; value = null; index = -1; }
}
