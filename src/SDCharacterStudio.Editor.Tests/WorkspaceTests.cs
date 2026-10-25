using System.Windows;
using System.Windows.Controls;
using AvalonDock.Layout;
using SDCharacterStudio.Editor.Infrastructure;
using SDCharacterStudio.Editor.Workspace;

namespace SDCharacterStudio.Editor.Tests;

public sealed class WorkspaceTests
{
    [Fact]
    public void EditorUsesDockingManagerRatherThanFixedPanelGrid() => EditorUiThread.Run(() =>
    {
        var window = new MainWindow();
        try
        {
            var manager = window.FindName("DockManager");
            Assert.NotNull(manager);
            Assert.Equal("DockingManager", manager.GetType().Name);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void ToolsCanHideAndReturnAndLayoutRestoresWithOriginalViewModels() => EditorUiThread.Run(() =>
    {
        using var harness = new EditorHarness();
        var window = new MainWindow();
        window.Attach(harness.Shell, new WpfUserInterface(window));
        try
        {
            var host = window.WorkspaceHost!;
            var assets = Assert.IsType<LayoutAnchorable>(host.Find(WorkspacePaneIds.Assets));
            Assert.Same(harness.Shell.Assets, Assert.IsAssignableFrom<FrameworkElement>(assets.Content).DataContext);
            assets.Hide();
            Assert.True(assets.IsHidden);
            string hidden = host.Capture();
            host.Show(WorkspacePaneIds.Assets);
            Assert.False(assets.IsHidden);
            Assert.True(host.Restore(hidden, out _));
            var restored = Assert.IsType<LayoutAnchorable>(host.Find(WorkspacePaneIds.Assets));
            Assert.True(restored.IsHidden);
            host.Show(WorkspacePaneIds.Assets);
            Assert.False(restored.IsHidden);
            Assert.Same(harness.Shell.Assets, Assert.IsAssignableFrom<FrameworkElement>(restored.Content).DataContext);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void BadOrMissingPanelLayoutsRecoverAndM8DocumentsUseStableIdentities() => EditorUiThread.Run(() =>
    {
        using var harness = new EditorHarness();
        var window = new MainWindow();
        window.Attach(harness.Shell, new WpfUserInterface(window));
        try
        {
            var host = window.WorkspaceHost!;
            Assert.False(host.Restore("<!DOCTYPE root [<!ENTITY a SYSTEM 'file:///unavailable'>]><root>&a;</root>", out var error));
            Assert.NotNull(error);
            Assert.All(WorkspacePaneIds.Tools, id => Assert.NotNull(host.Find(id)));
            host.OpenDocument("pose:robin-bow", "Robin bow", () => new TextBlock { Text = "pose view" });
            host.OpenDocument("pose:robin-bow", "Robin bow", () => throw new Exception("Duplicate document must be activated, not recreated"));
            Assert.Single(host.Manager.Layout.Descendents().OfType<LayoutDocument>(), p => p.ContentId == "pose:robin-bow");
            string xml = host.Capture();
            Assert.True(host.Restore(xml, out _));
            Assert.NotNull(host.Find("pose:robin-bow"));
            Assert.NotNull(host.Find(WorkspacePaneIds.Stage));
            Assert.IsType<LayoutDocument>(host.Find("pose:robin-bow")).Close();
            host.Reset();
            Assert.Null(host.Find("pose:robin-bow"));
            host.OpenDocument("portrait:robin", "Robin portrait", () => new TextBlock());
            host.CloseProjectDocuments();
            Assert.Null(host.Find("portrait:robin"));
            host.OpenDocument("portrait:robin", "Another project's Robin", () => new TextBlock { Text = "new project" });
            Assert.Equal("new project", Assert.IsType<TextBlock>(host.Find("portrait:robin")!.Content).Text);
            host.CloseProjectDocuments();
            Assert.DoesNotContain("portrait:robin", host.ContentIds);
            host.Reset();
            Assert.Null(host.Find("portrait:robin"));
        }
        finally { window.Close(); }
    });
}
