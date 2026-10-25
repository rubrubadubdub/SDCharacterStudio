using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using SDCharacterStudio.Editor.Views.Dialogs;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SDCharacterStudio.Editor.Infrastructure;

/// <summary>The real window system behind <see cref="IUserInterface"/>: toasts, themed dialogs, file pickers and timers.</summary>
public sealed class WpfUserInterface : IUserInterface
{
    private readonly Window owner;
    private readonly SnackbarService snackbar = new();
    private readonly Dictionary<string, DispatcherTimer> debounces = new();

    public WpfUserInterface(Window owner) => this.owner = owner;

    public void SetSnackbarPresenter(SnackbarPresenter presenter) => snackbar.SetSnackbarPresenter(presenter);

    public void Toast(string title, string message, ToastKind kind = ToastKind.Info)
    {
        if (snackbar.GetSnackbarPresenter() == null) return;
        var (appearance, symbol, seconds) = kind switch
        {
            ToastKind.Success => (ControlAppearance.Success, SymbolRegular.CheckmarkCircle24, 3),
            ToastKind.Warning => (ControlAppearance.Caution, SymbolRegular.Warning24, 5),
            ToastKind.Error => (ControlAppearance.Danger, SymbolRegular.ErrorCircle24, 7),
            _ => (ControlAppearance.Secondary, SymbolRegular.Info24, 3)
        };
        snackbar.Show(title, message, appearance, new SymbolIcon(symbol), TimeSpan.FromSeconds(seconds));
    }

    public bool Confirm(string title, string message, string confirm = "OK", string cancel = "Cancel") => MessageWindow.Confirm(owner, title, message, confirm, cancel);

    public bool? Ask(string title, string message, string yes, string no) => MessageWindow.Ask(owner, title, message, yes, no);

    public string? Prompt(string title, string label, string initial, Func<string, string?>? validate = null) => MessageWindow.Prompt(owner, title, label, initial, validate);

    public string? OpenFile(string title, string filter, string? folder = null)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, InitialDirectory = ExistingFolder(folder) };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public string? OpenFolder(string title, string? folder = null)
    {
        var dialog = new OpenFolderDialog { Title = title, InitialDirectory = ExistingFolder(folder) };
        return dialog.ShowDialog(owner) == true ? dialog.FolderName : null;
    }

    public string? SaveFile(string title, string filter, string fileName, string? folder = null)
    {
        var dialog = new SaveFileDialog { Title = title, Filter = filter, FileName = fileName, InitialDirectory = ExistingFolder(folder) };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    private static string ExistingFolder(string? folder)
    {
        for (string? f = folder; !string.IsNullOrEmpty(f); f = System.IO.Path.GetDirectoryName(f))
            if (System.IO.Directory.Exists(f)) return f;
        return "";
    }

    public bool ShowDialog(object viewModel)
    {
        var window = new DialogWindow(viewModel);
        if (owner.IsLoaded) window.Owner = owner;
        return window.ShowDialog() == true;
    }

    public void FlushPendingEdits() => Behaviors.Commit(Keyboard.FocusedElement as DependencyObject);

    public void Post(Action action) => owner.Dispatcher.BeginInvoke(action);

    public void Debounce(string key, TimeSpan delay, Action action)
    {
        if (debounces.TryGetValue(key, out var existing)) existing.Stop();
        var timer = new DispatcherTimer(DispatcherPriority.Background, owner.Dispatcher) { Interval = delay };
        timer.Tick += (_, _) => { timer.Stop(); debounces.Remove(key); action(); };
        debounces[key] = timer;
        timer.Start();
    }
}
