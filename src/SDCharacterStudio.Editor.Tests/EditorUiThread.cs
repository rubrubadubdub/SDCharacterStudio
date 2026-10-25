using System.Windows;
using System.Windows.Threading;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>One real WPF application/dispatcher for view tests; no user settings or game process.</summary>
internal static class EditorUiThread
{
    private static Exception? dispatchFailure;
    private static readonly Lazy<Dispatcher> Ui = new(() =>
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                // Use the production dictionaries, without App.OnStartup (which
                // would launch another editor and install a modal crash dialog).
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary { Theme = Wpf.Ui.Appearance.ApplicationTheme.Dark });
                app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/SDCharacterStudio.Editor;component/Themes/Studio.xaml", UriKind.Relative) });
                app.DispatcherUnhandledException += (_, e) => { dispatchFailure = e.Exception; e.Handled = true; };
                ready.SetResult(Dispatcher.CurrentDispatcher);
                Dispatcher.Run();
            }
            catch (Exception ex) { ready.TrySetException(ex); }
        }) { IsBackground = true, Name = "SDCS editor view tests" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task.GetAwaiter().GetResult();
    });

    public static void Run(Action action) => Ui.Value.Invoke(() => { dispatchFailure = null; action(); ThrowDispatchFailure(); });
    public static T Run<T>(Func<T> action) => Ui.Value.Invoke(action);

    public static void PumpUntil(Func<bool> done, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (!done())
        {
            ThrowDispatchFailure();
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("The editor interaction did not complete.");
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(5);
        }
        ThrowDispatchFailure();
    }
    private static void ThrowDispatchFailure()
    { if (dispatchFailure is { } failure) { dispatchFailure = null; throw new InvalidOperationException("Unhandled WPF interaction failure.", failure); } }
}
