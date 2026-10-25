using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Controls;

namespace SDCharacterStudio.Editor.ViewModels;

/// <summary>An item open in the Character Studio (pose, animation, mask, rig, expression or portrait).</summary>
public abstract partial class StudioDocumentViewModel : ObservableObject
{
    private int ownCommits;

    protected StudioDocumentViewModel(CharacterStudioViewModel studio, LibraryKind kind, string itemId)
    {
        Studio = studio; Kind = kind; this.itemId = itemId; title = itemId;
        CloseCommand = new RelayCommand(() => studio.Close(this));
    }

    public CharacterStudioViewModel Studio { get; }
    public LibraryKind Kind { get; }
    [ObservableProperty] private string itemId;
    [ObservableProperty] private string title;
    [ObservableProperty] private string? problems;
    public abstract SymbolRegular Icon { get; }
    public IRelayCommand CloseCommand { get; }
    /// <summary>The properties panel for the current selection (reuses the scene inspector's field editors).</summary>
    public ObservableCollection<FieldViewModel> Fields { get; } = new();

    protected PackLibrary? Library => Studio.Library;
    protected StudioContext Context => Studio.Context;
    protected IUserInterface Ui => Studio.Shell.Ui;
    /// <summary>True while this document's own change is being applied (it already shows the new state).</summary>
    protected bool IsOwnCommit => ownCommits > 0;

    /// <summary>Re-read the item after any pack change (edit, undo, redo). False when it no longer exists.</summary>
    public abstract bool Reload();
    public virtual void Activated() { }
    public virtual void Closing() { }

    /// <summary>One undo step for this document.</summary>
    protected void Commit(string label, Action<PackLibrary> apply)
    {
        ownCommits++;
        try { Studio.Edit(label, apply); }
        finally { ownCommits--; }
    }

    /// <summary>Field commit for the properties panel: mutate, save, then rebuild the fields when asked.</summary>
    protected FieldCommit FieldCommit(Action<PackLibrary> save) => (label, apply, rebuild) =>
    {
        Commit(label, lib => { apply(); save(lib); });
        if (rebuild) BuildFields();
    };

    public abstract void BuildFields();
}
