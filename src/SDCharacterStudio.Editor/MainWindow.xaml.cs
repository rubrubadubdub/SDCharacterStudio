using System.ComponentModel;
using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SDCharacterStudio.Editor.ViewModels;
using SDCharacterStudio.Editor.Views.Dialogs;
using SDCharacterStudio.Editor.Workspace;
using Wpf.Ui.Controls;

namespace SDCharacterStudio.Editor;

public partial class MainWindow : FluentWindow
{
    private MainViewModel? shell;
    private readonly DispatcherTimer bridgeTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly DispatcherTimer autosaveTimer = new() { Interval = Autosave.Interval };
    private bool closingConfirmed;
    private PackDocument? layoutProject;
    public WorkspaceHost? WorkspaceHost { get; private set; }

    public MainWindow()
    {
        InitializeComponent();
        Infrastructure.WindowFit.Attach(this);   // a size saved on a bigger monitor, or a drag to a smaller one, still fits
    }

    public void Attach(MainViewModel viewModel, WpfUserInterface ui)
    {
        shell = viewModel;
        DataContext = viewModel;
        ui.SetSnackbarPresenter(Snackbar);
        WorkspaceHost = new WorkspaceHost(DockManager, this, viewModel);
        RestoreLayout(viewModel.Settings);
        viewModel.Problems.PropertyChanged += OnProblemsChanged;
        viewModel.PropertyChanged += OnShellChanged;

        bridgeTimer.Tick += async (_, _) => { if (!viewModel.IsBusy) await viewModel.RefreshBridgeStatus(); };
        bridgeTimer.Start();
        autosaveTimer.Tick += (_, _) => viewModel.AutosaveTick();
        autosaveTimer.Start();
        Loaded += async (_, _) => await viewModel.RefreshBridgeStatus();
    }

    private void OnProblemsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ProblemsViewModel.IsOpenRequested) || shell?.Problems.IsOpenRequested != true) return;
        WorkspaceHost?.Show(WorkspacePaneIds.Problems);
        shell.Problems.IsOpenRequested = false;
    }

    // ---------- layout ----------

    private void RestoreLayout(EditorSettings settings)
    {
        Width = Math.Max(MinWidth, settings.WindowWidth);
        Height = Math.Max(MinHeight, settings.WindowHeight);
        if (settings.WindowMaximized) WindowState = WindowState.Maximized;
        WorkspaceHost?.Restore(settings.DockLayoutXml, out _);
        RestoreProjectLayout();
    }

    private void SaveLayout(EditorSettings settings)
    {
        settings.WindowMaximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal) { settings.WindowWidth = Width; settings.WindowHeight = Height; }
        settings.DockLayoutXml = WorkspaceHost?.Capture();
        SaveProjectLayout();
        settings.Save();
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.Document)) return;
        SaveProjectLayout();
        WorkspaceHost?.CloseProjectDocuments();
        RestoreProjectLayout();
    }

    private void RestoreProjectLayout()
    {
        layoutProject = shell?.Document;
        string? saved = layoutProject?.Project.DockLayoutXml ?? shell?.Settings.DockLayoutXml;
        if (WorkspaceHost != null && !WorkspaceHost.Restore(saved, out string? error))
            shell?.Ui.Toast("Panel layout reset", error ?? "The saved layout couldn't be restored.", ToastKind.Warning);
    }

    private void SaveProjectLayout()
    {
        if (layoutProject == null || WorkspaceHost == null) return;
        layoutProject.Project.DockLayoutXml = WorkspaceHost.Capture();
        // Persist only view state. Discarding content edits must not accidentally save
        // dependency/settings edits merely because a panel moved or a scene was selected.
        try
        {
            if (!File.Exists(layoutProject.ProjectPath)) return;
            var saved = ProjectFile.Read(layoutProject.ProjectPath);
            saved.DockLayoutXml = layoutProject.Project.DockLayoutXml;
            saved.OpenCutscene = layoutProject.Project.OpenCutscene;
            saved.StageZoom = layoutProject.Project.StageZoom;
            saved.ShowGrid = layoutProject.Project.ShowGrid;
            saved.ShowPassability = layoutProject.Project.ShowPassability;
            saved.StoryboardGraph = layoutProject.Project.StoryboardGraph;
            PackWriter.WriteIfChanged(layoutProject.ProjectPath, saved.ToJson());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException)
        { shell?.Ui.Toast("Couldn't save the panel layout", ex.Message, ToastKind.Warning); }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (shell != null && !closingConfirmed)
        {
            if (!shell.ConfirmDiscard()) { e.Cancel = true; return; }
            closingConfirmed = true;
        }
        base.OnClosing(e);
        if (!e.Cancel && shell != null) SaveLayout(shell.Settings);
        if (e.Cancel) closingConfirmed = false;
    }

    protected override void OnClosed(EventArgs e)
    {
        bridgeTimer.Stop(); autosaveTimer.Stop();
        if (shell != null) { shell.Problems.PropertyChanged -= OnProblemsChanged; shell.PropertyChanged -= OnShellChanged; }
        WorkspaceHost?.Dispose();
        base.OnClosed(e);
    }

    // ---------- menu and status bar ----------

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
    private void ShowStoryboard_Click(object sender, RoutedEventArgs e) => WorkspaceHost?.Show(WorkspacePaneIds.Storyboard);
    private void ShowProblems_Click(object sender, RoutedEventArgs e) => WorkspaceHost?.Show(WorkspacePaneIds.Problems);
    private void ShowOutput_Click(object sender, RoutedEventArgs e) => WorkspaceHost?.Show(WorkspacePaneIds.Output);
    private void TryItAs_Opened(object sender, RoutedEventArgs e) => shell?.TryItAs.Refresh();
    private void ShowTool_Click(object sender, RoutedEventArgs e) { if (sender is System.Windows.Controls.MenuItem { Tag: string id }) WorkspaceHost?.Show(id); }
    private void ResetLayout_Click(object sender, RoutedEventArgs e) { shell?.Ui.FlushPendingEdits(); WorkspaceHost?.Reset(); }
    private void CatalogStatus_Click(object sender, RoutedEventArgs e) => shell?.LoadCatalogCommand.Execute(null);
    private async void BridgePill_Click(object sender, RoutedEventArgs e) { if (shell != null) await shell.RefreshBridgeStatus(); }

    private void QuickStart_Click(object sender, RoutedEventArgs e) => MessageWindow.Info(this, "Quick start",
        "1. Start Stardew Valley with SDCharacterStudio and the SDCS Bridge, and load a save. The editor reads your game (characters, maps, items, mods) from the Bridge's export.\n\n" +
        "2. File › New project. Pick where the scene happens; the stage shows that map.\n\n" +
        "3. Drag characters from the left onto the stage. Drag them to move, double-click to turn.\n\n" +
        "4. Build the storyboard at the bottom: click a beat in the palette (Say, Move, Emote, Choice…) to add it after the selected one, then fill in its properties on the right.\n\n" +
        "5. Press F5 to play it in the game. Shift+F5 plays from the selected beat.\n\n" +
        "6. Export (Ctrl+E) checks the pack, fills in the mods it depends on, and can make a zip or copy it into your Mods folder.");

    private void Shortcuts_Click(object sender, RoutedEventArgs e) => MessageWindow.Info(this, "Keyboard shortcuts",
        "F5  Preview in game\nShift+F5  Preview from the selected beat\nCtrl+F5  Stop preview\n\n" +
        "Ctrl+S  Save\nCtrl+Z / Ctrl+Y  Undo / Redo\nCtrl+E  Export\nCtrl+N / Ctrl+O  New / Open project\n\n" +
        "Storyboard: Delete  remove beat · Ctrl+D  duplicate · Ctrl+←/→  move · ←/→  previous/next beat · F2  rename branch\n\n" +
        "Stage: Ctrl+= / Ctrl+-  zoom · Ctrl+wheel  zoom · middle-drag  pan · Alt+drag  1-pixel steps · double-click  turn · Delete  remove actor · Esc  cancel tile pick\n\n" +
        "Text: Enter commits a field (Ctrl+Enter in dialogue boxes).");

    private void About_Click(object sender, RoutedEventArgs e)
    {
        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
        MessageWindow.Info(this, "About SDCharacterStudio",
            $"SDCharacterStudio editor {version}\n\nMakes cutscene content packs for the SDCharacterStudio runtime mod: stage, storyboard, preview in game, export with dependencies.\n\n" +
            $"Settings: {EditorSettings.AppDataFolder}");
    }
}
