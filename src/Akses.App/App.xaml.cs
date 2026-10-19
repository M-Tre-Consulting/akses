using Microsoft.UI.Xaml;

namespace Akses.App;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            AppLog.Write($"!!! UnhandledException (XAML): {e.Message}\n{e.Exception}");
            // A failed conversion must never take the whole window down.
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            AppLog.Write($"!!! UnhandledException (AppDomain, terminating={e.IsTerminating}): {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.Write($"!!! UnobservedTaskException: {e.Exception}");
            e.SetObserved();
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Previews and work files of the previous session (the privacy policy promises this).
        _ = Task.Run(() => Akses.Core.OutputPaths.TryDelete(Path.Combine(Path.GetTempPath(), "Akses")));

        _window = new MainWindow();
        _window.Activate();
        _window.HandleArgs(Program.InitialArgs);

        Program.ListenForOtherLaunches(more =>
            _window.DispatcherQueue.TryEnqueue(() => _window.HandleArgs(more, bringToFront: true)));
    }
}
