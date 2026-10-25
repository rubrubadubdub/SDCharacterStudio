using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace SDCharacterStudio.Editor.Infrastructure;

/// <summary>
/// Keeps a window inside the work area of the monitor it's on: when it opens (a size saved on a bigger screen), when a
/// move or resize ends (dragged from a 4K monitor to a 2K one), and after a DPI change. Its minimum size never exceeds
/// that monitor, so it can always be resized. Works in physical pixels, so mixed-DPI setups don't confuse it.
/// </summary>
public static class WindowFit
{
    public static void Attach(Window window)
    {
        double wantedMinWidth = window.MinWidth, wantedMinHeight = window.MinHeight;
        void Fit() => Apply(window, wantedMinWidth, wantedMinHeight);
        window.SourceInitialized += (_, _) =>
        {
            Fit();
            if (PresentationSource.FromVisual(window) is HwndSource source)
                source.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
                {
                    if (msg == WmExitSizeMove) window.Dispatcher.BeginInvoke(Fit);
                    return IntPtr.Zero;
                });
        };
        window.DpiChanged += (_, _) => window.Dispatcher.BeginInvoke(Fit, System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>The usable area (without the taskbar) of the monitor a window is on, in physical pixels.</summary>
    public static (int Left, int Top, int Width, int Height)? WorkArea(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (hwnd == IntPtr.Zero || !GetMonitorInfo(MonitorFromWindow(hwnd, MonitorDefaultToNearest), ref info)) return null;
        return (info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
    }

    private static void Apply(Window window, double wantedMinWidth, double wantedMinHeight)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, MonitorDefaultToNearest), ref info)) return;
        var work = info.Work;
        int workWidth = work.Right - work.Left, workHeight = work.Bottom - work.Top;
        if (workWidth <= 0 || workHeight <= 0) return;

        // The minimum size, in this monitor's DIPs, never bigger than the monitor.
        var dpi = VisualTreeHelper.GetDpi(window);
        window.MinWidth = Math.Min(wantedMinWidth, workWidth / dpi.DpiScaleX);
        window.MinHeight = Math.Min(wantedMinHeight, workHeight / dpi.DpiScaleY);
        if (window.WindowState != WindowState.Normal || !GetWindowRect(hwnd, out var rect)) return;

        int width = Math.Min(rect.Right - rect.Left, workWidth), height = Math.Min(rect.Bottom - rect.Top, workHeight);
        int left = Math.Clamp(rect.Left, work.Left, work.Right - width), top = Math.Clamp(rect.Top, work.Top, work.Bottom - height);
        if (width == rect.Right - rect.Left && height == rect.Bottom - rect.Top && left == rect.Left && top == rect.Top) return;
        SetWindowPos(hwnd, IntPtr.Zero, left, top, width, height, SwpNoZOrder | SwpNoActivate);
    }

    private const int WmExitSizeMove = 0x0232;
    private const uint MonitorDefaultToNearest = 2;
    private const uint SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010;

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor; public Rect Work; public uint Flags; }

    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}
