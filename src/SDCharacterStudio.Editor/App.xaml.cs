using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using SDCharacterStudio.Editor.ViewModels;
using Wpf.Ui.Appearance;

namespace SDCharacterStudio.Editor;

public partial class App : Application
{
    private MainViewModel? shell;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;
        ApplicationThemeManager.Apply(ApplicationTheme.Dark, Wpf.Ui.Controls.WindowBackdropType.Mica, updateAccent: false);
        ApplicationAccentColorManager.Apply(Color.FromRgb(0x4C, 0xB0, 0x7A), ApplicationTheme.Dark);

        var settings = EditorSettings.Load();
        var window = new MainWindow();
        var ui = new WpfUserInterface(window);
        shell = new MainViewModel(ui, settings);
        window.Attach(shell, ui);
        MainWindow = window;
        window.Show();

        shell.LoadCatalog();
        string? argument = e.Args.FirstOrDefault();
        if (argument != null && File.Exists(argument) && argument.EndsWith(".sdcs", StringComparison.OrdinalIgnoreCase)) shell.OpenProjectFile(argument);
        else if (argument != null && Directory.Exists(argument)) shell.OpenPack(argument);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Keep the designer's work: autosave first, then report. The editor stays open for anything that isn't fatal.
        try { shell?.AutosaveTick(); } catch (Exception) { }
        string log = Path.Combine(EditorSettings.AppDataFolder, "crash.log");
        try
        {
            Directory.CreateDirectory(EditorSettings.AppDataFolder);
            File.AppendAllText(log, $"[{DateTime.Now:O}] {e.Exception}\n\n");
        }
        catch (IOException) { }
        // WPF's own dropdown hiccup when a pick rebuilds the list the dropdown is in: the pick already happened, so it's
        // logged, not shown (the studio's dropdowns wait a tick to avoid it; this covers any that don't).
        if (e.Exception is InvalidOperationException && e.Exception.StackTrace?.Contains("Selector.OnSelectedItemsCollectionChanged", StringComparison.Ordinal) == true)
        { e.Handled = true; return; }
        shell?.Output.Add("Error", "Unexpected error: " + e.Exception.Message);
        MessageBox.Show(MainWindow!, $"Something went wrong:\n\n{e.Exception.Message}\n\nYour work was autosaved. Details are in {log}.",
            "SDCharacterStudio", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        shell?.Dispose();
        base.OnExit(e);
    }
}
