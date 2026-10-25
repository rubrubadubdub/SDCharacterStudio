using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Xml.Linq;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Views;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Tests;

public sealed class PointerFactAttribute : FactAttribute
{
    public PointerFactAttribute()
    { if (Environment.GetEnvironmentVariable("SDCS_UI_POINTER_TESTS") != "1") Skip = "Set SDCS_UI_POINTER_TESTS=1 for foreground Windows input."; }
}

/// <summary>Real Windows input against the production views. These tests own the foreground cursor.</summary>
public sealed partial class PointerGestureTests
{
    [PointerFact]
    public void FloatingPanelKeepsCommandsAndDocksBackIntoSavedWorkspace() => EditorUiThread.Run(() =>
    {
        using var h = new EditorHarness();
        h.NewProject();
        h.Shell.Settings.WindowMaximized = false;
        h.Shell.Settings.WindowWidth = 1280; h.Shell.Settings.WindowHeight = 850;
        var window = new MainWindow { Left = 40, Top = 40, WindowStartupLocation = WindowStartupLocation.Manual };
        NativeInput.GetCursorPos(out var cursor);
        window.Attach(h.Shell, new SDCharacterStudio.Editor.Infrastructure.WpfUserInterface(window));
        try
        {
            window.Show(); Pump();
            var host = window.WorkspaceHost!;
            var assets = Assert.IsType<AvalonDock.Layout.LayoutAnchorable>(host.Find(SDCharacterStudio.Editor.Workspace.WorkspacePaneIds.Assets));
            assets.Float(); Pump();
            var floating = Assert.Single(host.Manager.FloatingWindows);
            Assert.True(assets.IsFloating);
            Assert.Same(h.Shell.Assets, Assert.IsAssignableFrom<FrameworkElement>(assets.Content).DataContext);
            Assert.Same(h.Shell, floating.DataContext);
            string before = h.Shell.Scene!.Title ?? "";
            h.Shell.Edit("floating command", () => h.Shell.Scene!.Title = "floating edit");
            Assert.True(NativeInput.Foreground(new WindowInteropHelper(floating).Handle)); Pump();
            var keys = Task.Run(() => { NativeInput.KeyDown(0x11); NativeInput.Key(0x5A); NativeInput.KeyUp(0x11); });
            EditorUiThread.PumpUntil(() => keys.IsCompleted); keys.GetAwaiter().GetResult(); Pump();
            Assert.Equal(before, h.Shell.Scene!.Title ?? "");
            string xml = host.Capture();
            var saved = System.Xml.Linq.XDocument.Parse(xml);
            foreach (var attribute in saved.Descendants().Attributes().Where(a => a.Name.LocalName is "FloatingLeft" or "FloatingTop")) attribute.Value = "250000";
            xml = saved.ToString();
            assets.Dock(); Pump();
            Assert.False(assets.IsFloating);
            Assert.True(host.Restore(xml, out var error), error); Pump();
            var restoredWindow = Assert.Single(host.Manager.FloatingWindows);
            Assert.InRange(restoredWindow.Left, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth);
            Assert.InRange(restoredWindow.Top, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight);
            assets = Assert.IsType<AvalonDock.Layout.LayoutAnchorable>(host.Find(SDCharacterStudio.Editor.Workspace.WorkspacePaneIds.Assets));
            Assert.True(assets.IsFloating);
            assets.Dock(); Pump();
            Assert.False(assets.IsFloating);
            Assert.Same(h.Shell.Assets, Assert.IsAssignableFrom<FrameworkElement>(assets.Content).DataContext);
        }
        finally { NativeInput.KeyUp(0x11); NativeInput.SetCursorPos(cursor.X, cursor.Y); window.Close(); Pump(); }
    });

    [PointerFact]
    public void ActorTilePixelAndCancelledDragsHaveOneUndoStep() => EditorUiThread.Run(() =>
    {
        Trace("harness before"); using var h = new EditorHarness(); Trace("harness after");
        h.NewProject(location: "ScienceHouse"); Trace("project after");
        h.Shell.Stage.DropCharacter("Robin", 10, 10);
        h.Shell.Stage.Zoom = 2;
        h.Shell.Stage.ShowCamera = false;
        var stage = new StageView { DataContext = h.Shell.Stage };
        WithWindow(stage, window =>
        {
            stage.CenterOnCamera(); Pump();
            Point ActorPoint()
            {
                var actor = Find<FrameworkElement>(stage, e => Equals(e.Tag, "actor") && e.DataContext is StageActorViewModel { Id: "Robin" })!;
                Assert.NotNull(actor);
                return actor.PointToScreen(new Point(8, 8));
            }
            int index = h.Shell.Scene!.Actors.FindIndex(a => a.Id == "Robin");
            int[] Before() => h.Shell.Scene!.Actors[index].Start!.Tile!.ToArray();
            var before = Before();
            Trace("actor point before"); Point p = ActorPoint(); Trace("actor point=" + p);
            AssertHit(window, p, e => Equals(e.Tag, "actor"));
            Trace("hit passed"); Gesture(p, p + new Vector(64, 0)); Trace("gesture returned");
            Assert.Equal(new[] { before[0] + 2, before[1] }, Before());
            Assert.True(h.Shell.Document!.Undo());
            Assert.Equal(before, Before());
            Assert.Equal("Add Robin", h.Shell.Document.History.UndoLabel);
            Pump();
            p = ActorPoint();
            var dpi = VisualTreeHelper.GetDpi(stage);
            Gesture(p, p + new Vector(6 * dpi.DpiScaleX, 4 * dpi.DpiScaleY), alt: true);
            Assert.Equal(before, Before());
            Assert.Equal(new[] { 3, 2 }, h.Shell.Scene!.Actors[index].Start!.Px);
            Assert.True(h.Shell.Document.Undo());
            Assert.Equal(new[] { 0, 0 }, h.Shell.Scene!.Actors[index].Start!.Px);
            Assert.Equal("Add Robin", h.Shell.Document.History.UndoLabel);
            Pump();
            string unchanged = h.Shell.Document.CaptureState();
            p = ActorPoint();
            Gesture(p, p + new Vector(64, 0), escape: true);
            Assert.Equal(unchanged, h.Shell.Document.CaptureState());
            Assert.Null(Mouse.Captured);
            p = ActorPoint();
            Gesture(p, p + new Vector(64, 0), loseCapture: () => Mouse.Capture(null));
            Assert.Equal(unchanged, h.Shell.Document.CaptureState());
        });
    });

    [PointerFact]
    public void CameraDragAndCancellationUseTheRealTab() => EditorUiThread.Run(() =>
    {
        Trace("harness before"); using var h = new EditorHarness(); Trace("harness after");
        h.NewProject(location: "ScienceHouse"); Trace("project after");
        h.Shell.Stage.Zoom = 1;
        h.Shell.Stage.MoveCamera(16, 10);
        var stage = new StageView { DataContext = h.Shell.Stage };
        WithWindow(stage, window =>
        {
            stage.CenterOnCamera(); Pump();
            var tab = (FrameworkElement)stage.FindName("CameraTab");
            Assert.NotNull(tab);
            var before = h.Shell.Scene!.Camera!["Tile"]!.ToObject<int[]>()!;
            Point p = tab.PointToScreen(new Point(tab.ActualWidth / 2, tab.ActualHeight / 2));
            AssertHit(window, p, e => ReferenceEquals(e, tab));
            Gesture(p, p + new Vector(32, 16));
            Assert.Equal(new[] { before[0] + 2, before[1] + 1 }, h.Shell.Scene!.Camera!["Tile"]!.ToObject<int[]>());
            Assert.True(h.Shell.Document!.Undo());
            Assert.Equal(before, h.Shell.Scene!.Camera!["Tile"]!.ToObject<int[]>());
            Pump();
            string unchanged = h.Shell.Document.CaptureState();
            p = tab.PointToScreen(new Point(tab.ActualWidth / 2, tab.ActualHeight / 2));
            Gesture(p, p + new Vector(32, 16), escape: true);
            Assert.Equal(unchanged, h.Shell.Document.CaptureState());
            Assert.Null(Mouse.Captured);
        });
    });

    [PointerFact]
    public void AssetDropPaletteDropAndCardReorderPassThroughWpfDragDrop() => EditorUiThread.Run(() =>
    {
        Trace("harness before"); using var h = new EditorHarness(); Trace("harness after");
        h.NewProject(location: "ScienceHouse"); Trace("project after");
        h.Shell.Stage.Zoom = 1;
        h.Shell.Stage.ShowCamera = false;
        var browser = new AssetBrowserView { DataContext = h.Shell.Assets };
        var stage = new StageView { DataContext = h.Shell.Stage };
        var board = new StoryboardView { DataContext = h.Shell.Storyboard };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(300) }); grid.ColumnDefinitions.Add(new());
        grid.RowDefinitions.Add(new() { Height = new GridLength(380) }); grid.RowDefinitions.Add(new());
        Grid.SetRowSpan(browser, 2); grid.Children.Add(browser);
        Grid.SetColumn(stage, 1); grid.Children.Add(stage);
        Grid.SetRow(board, 1); Grid.SetColumn(board, 1); grid.Children.Add(board);
        WithWindow(grid, window =>
        {
            stage.CenterOnCamera(); Pump();
            var asset = Find<FrameworkElement>(browser, e => e.DataContext is AssetItemViewModel { Key: "Robin" } && e.ActualWidth >= 70 && e.ActualHeight >= 80)!;
            Assert.NotNull(asset);
            var surface = (FrameworkElement)stage.FindName("Surface");
            Point target = surface.PointToScreen(new Point(9 * 16 + 4, 9 * 16 + 4));
            Gesture(Center(asset), target);
            Assert.Contains(h.Shell.Scene!.Actors, a => a.Id == "Robin" && a.Start!.Tile!.SequenceEqual(new[] { 9, 9 }));
            Assert.True(h.Shell.Document!.Undo());
            Assert.DoesNotContain(h.Shell.Scene!.Actors, a => a.Id == "Robin");
            Pump();
            var palette = Find<FrameworkElement>(board, e => Equals(e.Tag, "palette") && e.DataContext is PaletteEntry { Name: "Say" })!;
            var lane = Find<FrameworkElement>(board, e => Equals(e.Tag, "lane") && e.DataContext is LaneViewModel { IsMain: true })!;
            Assert.NotNull(palette); Assert.NotNull(lane);
            Gesture(Center(palette), lane.PointToScreen(new Point(220, 35)));
            Assert.Single(h.Shell.Scene.Beats); Assert.IsType<SayBeat>(h.Shell.Scene.Beats[0]);
            Assert.True(h.Shell.Document.Undo()); Assert.Empty(h.Shell.Scene!.Beats);
            h.Shell.Storyboard.Insert(LaneNames.Main, 0, SDCharacterStudio.Editor.Schema.BeatCatalog.Kind("Wait"), b => ((WaitBeat)b).Duration = 111);
            h.Shell.Storyboard.Insert(LaneNames.Main, 1, SDCharacterStudio.Editor.Schema.BeatCatalog.Kind("Wait"), b => ((WaitBeat)b).Duration = 222);
            Pump();
            var first = Find<FrameworkElement>(board, e => Equals(e.Tag, "card") && e.DataContext is BeatCardViewModel { Index: 0 })!;
            var second = Find<FrameworkElement>(board, e => Equals(e.Tag, "card") && e.DataContext is BeatCardViewModel { Index: 1 })!;
            Gesture(Center(first), second.PointToScreen(new Point(second.ActualWidth - 8, 22)));
            Assert.Equal(new[] { 222, 111 }, h.Shell.Scene!.Beats.Cast<WaitBeat>().Select(b => b.Duration));
            Assert.True(h.Shell.Document.Undo());
            Assert.Equal(new[] { 111, 222 }, h.Shell.Scene!.Beats.Cast<WaitBeat>().Select(b => b.Duration));
        });
    });

    private static void WithWindow(FrameworkElement content, Action<Window> action)
    {
        NativeInput.GetCursorPos(out var saved);
        var window = new Window { Content = content, Title = "SDCS pointer verification", Width = 1280, Height = 850, Left = 40, Top = 40, WindowStartupLocation = WindowStartupLocation.Manual };
        try
        {
            Trace("window before show"); window.Show(); window.Activate(); Trace("window shown"); Pump(); Trace("first pump complete");
            Assert.True(NativeInput.Foreground(new WindowInteropHelper(window).Handle));
            Pump();
            Assert.Equal(new WindowInteropHelper(window).Handle, NativeInput.GetForegroundWindow());
            Trace("action before"); action(window); Trace("action complete");
        }
        finally { NativeInput.LeftUp(); NativeInput.KeyUp(0x12); NativeInput.SetCursorPos(saved.X, saved.Y); window.Close(); Pump(); }
    }
    private static Point Center(FrameworkElement element) => element.PointToScreen(new Point(element.ActualWidth / 2, element.ActualHeight / 2));
    private static void AssertHit(Window window, Point point, Func<FrameworkElement, bool> predicate)
    {
        var hit = window.InputHitTest(window.PointFromScreen(point)) as DependencyObject;
        for (var current = hit; current != null; current = current is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
            if (current is FrameworkElement element && predicate(element)) return;
        throw new Xunit.Sdk.XunitException("Pointer target is obscured or outside the viewport: " + point + ", hit " + hit);
    }
    private static void Gesture(Point from, Point to, bool alt = false, bool escape = false, Action? loseCapture = null)
    {
        Trace("gesture starting"); var input = Task.Run(() =>
        {
            try
            {
                if (alt) NativeInput.KeyDown(0x12);
                Trace("input started"); NativeInput.Move(from); Trace("input down"); NativeInput.LeftDown(); Thread.Sleep(160);
                // Cross the WPF drag threshold before entering the destination.
                for (int i = 1; i <= 8; i++) NativeInput.Move(from + (to - from) * (i / 8d));
                if (escape) NativeInput.Key(0x1B);
                if (loseCapture != null) Application.Current.Dispatcher.Invoke(loseCapture);
                Trace("input up"); Thread.Sleep(100); NativeInput.LeftUp(); Trace("input done");
            }
            finally { NativeInput.LeftUp(); if (alt) NativeInput.KeyUp(0x12); }
        });
        EditorUiThread.PumpUntil(() => input.IsCompleted, TimeSpan.FromSeconds(10));
        Trace("input pump done"); input.GetAwaiter().GetResult();
        Pump();
    }
    private static void Trace(string value) { if (Environment.GetEnvironmentVariable("SDCS_POINTER_TRACE") is { } path) System.IO.File.AppendAllText(path, DateTime.Now + " " + value + Environment.NewLine); }
    private static void Pump()
    {
        var end = DateTime.UtcNow.AddMilliseconds(180);
        EditorUiThread.PumpUntil(() => DateTime.UtcNow >= end);
    }
    private static T? Find<T>(DependencyObject root, Func<FrameworkElement, bool> predicate) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T result && child is FrameworkElement element && predicate(element)) return result;
            if (Find<T>(child, predicate) is { } found) return found;
        }
        return null;
    }
}

internal static class NativeInput
{
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] internal static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")] internal static extern bool Foreground(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public MOUSEKEYBDATA data; }
    [StructLayout(LayoutKind.Explicit)] private struct MOUSEKEYBDATA { [FieldOffset(0)] public MOUSEINPUT mouse; [FieldOffset(0)] public KEYBDINPUT key; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx, dy; public uint mouseData, flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort vk, scan; public uint flags, time; public IntPtr extra; }
    public static void Move(Point point) { Assert.True(SetCursorPos((int)point.X, (int)point.Y)); Thread.Sleep(45); }
    public static void LeftDown() => Send(0, new() { mouse = new() { flags = 2 } });
    public static void LeftUp() => Send(0, new() { mouse = new() { flags = 4 } });
    public static void KeyDown(ushort key) => Send(1, new() { key = new() { vk = key } });
    public static void KeyUp(ushort key) => Send(1, new() { key = new() { vk = key, flags = 2 } });
    public static void Key(ushort key) { KeyDown(key); KeyUp(key); }
    private static void Send(uint type, MOUSEKEYBDATA data)
    {
        if (SendInput(1, new[] { new INPUT { type = type, data = data } }, Marshal.SizeOf<INPUT>()) != 1) throw new InvalidOperationException("SendInput failed: " + Marshal.GetLastWin32Error());
        Thread.Sleep(40);
    }
}
