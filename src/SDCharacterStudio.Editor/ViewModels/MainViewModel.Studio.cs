using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SDCharacterStudio.Editor.ViewModels;

/// <summary>The Scenes | Character Studio workspace switch (doc 07 §15, D-39).</summary>
public sealed partial class MainViewModel
{
    private CharacterStudioViewModel? characterStudio;
    private bool studioStale = true;

    [ObservableProperty] private bool isCharacterStudio;

    public CharacterStudioViewModel CharacterStudio => characterStudio ??= new CharacterStudioViewModel(this);

    partial void OnIsCharacterStudioChanged(bool value)
    {
        if (value && studioStale) RefreshStudio();
    }

    [RelayCommand] private void ShowCharacterStudio() => IsCharacterStudio = true;
    [RelayCommand] private void ShowScenes() => IsCharacterStudio = false;

    partial void OnDocumentChanged(Project.PackDocument? oldValue, Project.PackDocument? newValue)
    {
        if (oldValue != null) oldValue.Changed -= OnStudioPackChanged;
        if (newValue != null) newValue.Changed += OnStudioPackChanged;
        if (characterStudio != null) foreach (var open in characterStudio.Documents.ToArray()) characterStudio.Close(open);
        StudioChanged();
    }

    partial void OnCatalogChanged(ExportCatalog? oldValue, ExportCatalog? newValue) => StudioChanged();

    private void OnStudioPackChanged(object? sender, string label) => StudioChanged();

    /// <summary>The studio redraws right away when it's showing (or has items open), otherwise when it's next shown.</summary>
    private void StudioChanged()
    {
        studioStale = true;
        if (characterStudio != null && (IsCharacterStudio || characterStudio.HasDocuments)) RefreshStudio();
    }

    private void RefreshStudio()
    {
        studioStale = false;
        CharacterStudio.Refresh();
    }
}
