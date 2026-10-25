namespace SDCharacterStudio.Editor.Project;

/// <summary>
/// Snapshot (memento) undo over the document's serialized editable state. Each committed user action is one step;
/// committing an unchanged state is ignored, so a field edit that changes nothing never pollutes the history.
/// </summary>
public sealed class History
{
    public const int Capacity = 200;
    private readonly LinkedList<Entry> undo = new();
    private readonly Stack<Entry> redo = new();
    private string current;

    private sealed record Entry(string Label, string State);

    public History(string initial) => current = initial;
    public string Current => current;
    public bool CanUndo => undo.Count > 0;
    public int UndoCount => undo.Count;
    public bool CanRedo => redo.Count > 0;
    public string? UndoLabel => undo.Last?.Value.Label;
    public string? RedoLabel => redo.Count > 0 ? redo.Peek().Label : null;
    public event EventHandler? Changed;

    /// <summary>Record a new state after a user action. Returns false when nothing changed.</summary>
    public bool Commit(string label, string state)
    {
        if (state == current) return false;
        undo.AddLast(new Entry(label, current));
        while (undo.Count > Capacity) undo.RemoveFirst();
        redo.Clear();
        current = state;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Returns the state to restore, or null.</summary>
    public string? Undo()
    {
        if (undo.Last is not { } last) return null;
        undo.RemoveLast();
        redo.Push(new Entry(last.Value.Label, current));
        current = last.Value.State;
        Changed?.Invoke(this, EventArgs.Empty);
        return current;
    }

    public string? Redo()
    {
        if (redo.Count == 0) return null;
        var next = redo.Pop();
        undo.AddLast(new Entry(next.Label, current));
        current = next.State;
        Changed?.Invoke(this, EventArgs.Empty);
        return current;
    }

    /// <summary>Make the last two steps one (an action that needed two commits), under a new label.</summary>
    public void MergeLast(string label)
    {
        if (undo.Count < 2) return;
        undo.RemoveLast();
        var first = undo.Last!.Value;
        undo.RemoveLast();
        undo.AddLast(new Entry(label, first.State));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Forget history, e.g. after loading another project or restoring an autosave.</summary>
    public void Reset(string state) { undo.Clear(); redo.Clear(); current = state; Changed?.Invoke(this, EventArgs.Empty); }
}
