using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Xml;
using System.Xml.Linq;
using AvalonDock;
using AvalonDock.Layout;
using AvalonDock.Layout.Serialization;
using AvalonDock.Themes;
using SDCharacterStudio.Editor.ViewModels;
using SDCharacterStudio.Editor.Views;

namespace SDCharacterStudio.Editor.Workspace;

/// <summary>Stable tool/document identities shared by persisted layouts and future M8 editors.</summary>
public static class WorkspacePaneIds
{
    public const string Assets = "tool.assets", Properties = "tool.properties", Storyboard = "tool.storyboard",
        Problems = "tool.problems", Output = "tool.output", Story = "tool.story", Stage = "document.stage";
    public static readonly string[] Tools = { Assets, Properties, Storyboard, Story, Problems, Output };
}

/// <summary>Owns docking and document registration; editing state remains in the view models.</summary>
public sealed class WorkspaceHost : IDisposable
{
    private sealed record Pane(string Id, string Title, Func<FrameworkElement> Create, bool Document, bool CanClose);
    private readonly DockingManager manager;
    private readonly Window owner;
    private readonly MainViewModel shell;
    private readonly Dictionary<string, Pane> panes = new(StringComparer.Ordinal);
    private readonly HashSet<Window> wiredWindows = new();
    public DockingManager Manager => manager;
    public IReadOnlyList<string> ContentIds => panes.Keys.ToArray();

    public WorkspaceHost(DockingManager manager, Window owner, MainViewModel shell)
    {
        this.manager = manager; this.owner = owner; this.shell = shell;
        Add(WorkspacePaneIds.Assets, "Assets", () => new AssetBrowserView { DataContext = shell.Assets });
        Add(WorkspacePaneIds.Properties, "Properties", () => new InspectorView { DataContext = shell.Inspector });
        Add(WorkspacePaneIds.Storyboard, "Storyboard", () => new StoryboardView { DataContext = shell.Storyboard });
        Add(WorkspacePaneIds.Story, "Story", () => new StoryView { DataContext = shell.Story });
        Add(WorkspacePaneIds.Problems, "Problems", () => new ProblemsView { DataContext = shell.Problems });
        Add(WorkspacePaneIds.Output, "Output", () => new OutputView { DataContext = shell.Output });
        panes.Add(WorkspacePaneIds.Stage, new(WorkspacePaneIds.Stage, "Stage", () => new StageView { DataContext = shell.Stage }, true, false));
        manager.Theme = new Vs2013DarkTheme();
        manager.LayoutUpdated += OnLayoutUpdated;
        Reset();
    }

    private void Add(string id, string title, Func<FrameworkElement> create) => panes.Add(id, new(id, title, create, false, true));
    private LayoutAnchorable Tool(string id) => new() { ContentId = id, Title = panes[id].Title, Content = panes[id].Create(),
        CanClose = false, CanHide = true, CanFloat = true, CanDockAsTabbedDocument = false };
    private LayoutDocument Document(string id)
    {
        var document = new LayoutDocument { ContentId = id, Title = panes[id].Title, Content = panes[id].Create(), CanClose = panes[id].CanClose, CanFloat = true };
        WireDocumentClose(document);
        return document;
    }
    private void WireDocumentClose(LayoutDocument document)
    {
        document.Closed += (_, _) =>
        {
            string? id = document.ContentId;
            if (id != null && id != WorkspacePaneIds.Stage && Find(id) == null) panes.Remove(id);
        };
    }
    public void CloseProjectDocuments()
    {
        foreach (string id in panes.Values.Where(p => p.Document && p.Id != WorkspacePaneIds.Stage).Select(p => p.Id).ToArray())
        {
            if (Find(id) is { } content)
            {
                // Project switches must also remove the container: a blank old
                // document would otherwise intercept OpenDocument for the new pack.
                content.Content = null;
                content.Parent?.RemoveChild(content);
            }
            panes.Remove(id);
        }
        manager.Layout.CollectGarbage();
    }

    /// <summary>M8 opens Pose/Portrait documents here with a stable ID and an existing editing view model.</summary>
    public void OpenDocument(string id, string title, Func<FrameworkElement> create, bool canClose = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(create);
        if (id.Length > 256 || WorkspacePaneIds.Tools.Contains(id, StringComparer.Ordinal)) throw new ArgumentException("Use a unique document identity.", nameof(id));
        if (Find(id) is { } existing) { existing.IsActive = true; return; }
        panes[id] = new(id, title, create, true, canClose);
        var pane = manager.Layout.Descendents().OfType<LayoutDocumentPane>().FirstOrDefault();
        if (pane == null) throw new InvalidOperationException("The Stage document pane is unavailable. Reset the workspace layout.");
        var document = Document(id); pane.Children.Add(document); document.IsActive = true;
    }

    public LayoutContent? Find(string id) => manager.Layout.Descendents().OfType<LayoutContent>().FirstOrDefault(p => p.ContentId == id);
    public void Show(string id)
    {
        if (Find(id) is LayoutAnchorable tool) { tool.Show(); tool.IsActive = true; }
        else if (Find(id) is { } document) document.IsActive = true;
    }

    public void Reset()
    {
        // Let WPF release old logical parents before rebuilding containers, including floating tools.
        foreach (var content in manager.Layout.Descendents().OfType<LayoutContent>().ToArray()) content.Content = null;
        var left = new LayoutAnchorablePane { DockWidth = new GridLength(300), DockMinWidth = 200 };
        left.Children.Add(Tool(WorkspacePaneIds.Assets));
        var documents = new LayoutDocumentPane();
        foreach (var pane in panes.Values.Where(p => p.Document)) documents.Children.Add(Document(pane.Id));
        var middle = new LayoutDocumentPaneGroup(); middle.Children.Add(documents);
        var right = new LayoutAnchorablePane { DockWidth = new GridLength(360), DockMinWidth = 250 };
        right.Children.Add(Tool(WorkspacePaneIds.Properties));
        var top = new LayoutPanel { Orientation = Orientation.Horizontal };
        top.Children.Add(left); top.Children.Add(middle); top.Children.Add(right);
        var bottom = new LayoutAnchorablePane { DockHeight = new GridLength(300), DockMinHeight = 120 };
        bottom.Children.Add(Tool(WorkspacePaneIds.Storyboard)); bottom.Children.Add(Tool(WorkspacePaneIds.Story));
        bottom.Children.Add(Tool(WorkspacePaneIds.Problems)); bottom.Children.Add(Tool(WorkspacePaneIds.Output));
        bottom.SelectedContentIndex = 0;
        var root = new LayoutPanel { Orientation = Orientation.Vertical };
        root.Children.Add(top); root.Children.Add(bottom);
        manager.Layout = new LayoutRoot { RootPanel = root };
    }

    public string Capture()
    {
        using var text = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        new XmlLayoutSerializer(manager).Serialize(text);
        return text.ToString();
    }

    /// <summary>Only registered panes can be restored. Bad data falls back to a usable workspace.</summary>
    public bool Restore(string? xml, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(xml)) { Reset(); return true; }
        try
        {
            if (xml.Length > 1024 * 1024) throw new InvalidDataException("Layout exceeds the supported size.");
            var serializer = new XmlLayoutSerializer(manager);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            serializer.LayoutSerializationCallback += (_, args) =>
            {
                string id = args.Model.ContentId ?? "";
                if (!panes.TryGetValue(id, out var pane) || !seen.Add(id)) { args.Cancel = true; return; }
                args.Content = pane.Create();
                args.Model.Title = pane.Title;
                args.Model.CanClose = pane.Document && pane.CanClose;
                if (args.Model is LayoutDocument document) WireDocumentClose(document);
                ClampFloatingBounds(args.Model);
            };
            using var input = new StringReader(xml);
            using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 });
            var layout = XDocument.Load(reader);
            // Geometry also lives on pane groups and floating windows, not just
            // their content. Clamp it before AvalonDock creates native windows.
            var desktop = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            foreach (var element in layout.Descendants().Where(e => e.Attribute("FloatingLeft") != null))
            {
                double Number(string name, double fallback) => double.TryParse((string?)element.Attribute(name), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : fallback;
                double width = Math.Clamp(Number("FloatingWidth", 480), 200, Math.Max(200, desktop.Width));
                double height = Math.Clamp(Number("FloatingHeight", 360), 150, Math.Max(150, desktop.Height));
                element.SetAttributeValue("FloatingWidth", width); element.SetAttributeValue("FloatingHeight", height);
                element.SetAttributeValue("FloatingLeft", Math.Clamp(Number("FloatingLeft", desktop.Left), desktop.Left, Math.Max(desktop.Left, desktop.Right - width)));
                element.SetAttributeValue("FloatingTop", Math.Clamp(Number("FloatingTop", desktop.Top), desktop.Top, Math.Max(desktop.Top, desktop.Bottom - height)));
            }
            using var sanitizedText = new StringReader(layout.ToString());
            using var sanitized = XmlReader.Create(sanitizedText, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            serializer.Deserialize(sanitized);
            // A panel added since the layout was saved (the Story panel, D-51) joins the Storyboard's tabs, so a
            // saved layout is never thrown away for it.
            foreach (string id in WorkspacePaneIds.Tools.Where(id => Find(id) == null).ToArray())
                if (Find(WorkspacePaneIds.Storyboard)?.Parent is LayoutAnchorablePane near) near.Children.Add(Tool(id));
            if (!WorkspacePaneIds.Tools.Append(WorkspacePaneIds.Stage).All(id => Find(id) != null))
                throw new InvalidDataException("Layout is missing an editor panel.");
            return true;
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException or ArgumentException or InvalidDataException or IOException)
        { error = ex.GetBaseException().Message; Reset(); return false; }
    }

    private static void ClampFloatingBounds(LayoutContent pane)
    {
        var desktop = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        double width = double.IsFinite(pane.FloatingWidth) && pane.FloatingWidth > 0 ? pane.FloatingWidth : 480;
        double height = double.IsFinite(pane.FloatingHeight) && pane.FloatingHeight > 0 ? pane.FloatingHeight : 360;
        pane.FloatingWidth = Math.Clamp(width, 200, Math.Max(200, desktop.Width));
        pane.FloatingHeight = Math.Clamp(height, 150, Math.Max(150, desktop.Height));
        pane.FloatingLeft = double.IsFinite(pane.FloatingLeft) ? Math.Clamp(pane.FloatingLeft, desktop.Left, Math.Max(desktop.Left, desktop.Right - pane.FloatingWidth)) : desktop.Left;
        pane.FloatingTop = double.IsFinite(pane.FloatingTop) ? Math.Clamp(pane.FloatingTop, desktop.Top, Math.Max(desktop.Top, desktop.Bottom - pane.FloatingHeight)) : desktop.Top;
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        foreach (var window in manager.FloatingWindows)
        {
            if (!wiredWindows.Add(window)) continue;
            window.DataContext = shell;
            foreach (var binding in owner.InputBindings.OfType<System.Windows.Input.InputBinding>()) window.InputBindings.Add((System.Windows.Input.InputBinding)binding.Clone());
            window.Closed += (_, _) => wiredWindows.Remove(window);
        }
    }

    public void Dispose()
    {
        manager.LayoutUpdated -= OnLayoutUpdated;
        foreach (var window in manager.FloatingWindows.ToArray()) window.Close();
        wiredWindows.Clear();
    }
}
