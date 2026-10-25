using System.Windows;
using SDCharacterStudio.Editor.Infrastructure;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>A window sized for a bigger monitor (a 4K window moved to a 2K screen) is brought back inside the one it's on.</summary>
public sealed class WindowFitTests
{
    [Fact]
    public void AWindowBiggerThanItsMonitorIsBroughtInside() => EditorUiThread.Run(() =>
    {
        var window = new Wpf.Ui.Controls.FluentWindow { Width = 12000, Height = 9000, MinWidth = 1040, MinHeight = 660, ShowActivated = false, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = SystemParameters.VirtualScreenLeft - 200, Top = SystemParameters.VirtualScreenTop - 200 };
        WindowFit.Attach(window);
        window.Show();
        try
        {
            EditorUiThread.PumpUntil(() => window.IsLoaded);
            var work = WindowFit.WorkArea(window)!.Value;   // physical pixels
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window);
            double widthPx = window.ActualWidth * dpi.DpiScaleX, heightPx = window.ActualHeight * dpi.DpiScaleY;
            Assert.True(widthPx <= work.Width + 1, $"{widthPx} px wide on a {work.Width} px work area");
            Assert.True(heightPx <= work.Height + 1, $"{heightPx} px tall on a {work.Height} px work area");
            Assert.True(window.MinWidth <= work.Width / dpi.DpiScaleX + 0.5 && window.MinHeight <= work.Height / dpi.DpiScaleY + 0.5);
        }
        finally { window.Close(); }
    });
}
