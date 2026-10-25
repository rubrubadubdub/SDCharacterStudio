namespace SDCharacterStudio.Editor.Infrastructure;

public enum ToastKind { Info, Success, Warning, Error }

/// <summary>Everything view models ask of the window system. The WPF app implements it; tests use a scripted fake.</summary>
public interface IUserInterface
{
    void Toast(string title, string message, ToastKind kind = ToastKind.Info);
    bool Confirm(string title, string message, string confirm = "OK", string cancel = "Cancel");
    /// <summary>Yes / No / Cancel. Null = cancel.</summary>
    bool? Ask(string title, string message, string yes, string no);
    string? Prompt(string title, string label, string initial, Func<string, string?>? validate = null);
    string? OpenFile(string title, string filter, string? folder = null);
    string? OpenFolder(string title, string? folder = null);
    string? SaveFile(string title, string filter, string fileName, string? folder = null);
    /// <summary>Show a dialog for a dialog view model (matched by type). True = accepted.</summary>
    bool ShowDialog(object viewModel);
    /// <summary>Commit any text box that's still being edited, before saving, exporting or previewing.</summary>
    void FlushPendingEdits();
    /// <summary>Run on the UI thread later (debounce, background completion).</summary>
    void Post(Action action);
    /// <summary>Run <paramref name="action"/> once after <paramref name="delay"/>, replacing any earlier pending call with the same key.</summary>
    void Debounce(string key, TimeSpan delay, Action action);
}
