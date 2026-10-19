using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Akses.App;

/// <summary>
/// Custom entry point (see <c>DISABLE_XAML_GENERATED_MAIN</c> in the csproj): one Akses window per
/// user session, which receives the files of every later launch.
///
/// Explorer's "Converti con Akses" menu starts Akses once per selected file, all at the same time.
/// The first process becomes the window; every other one hands its command line over a named pipe
/// to that window and exits silently, so selecting 20 photos and choosing "PNG" converts all 20 in
/// one list. A named <see cref="Mutex"/> decides who is first: the kernel releases it the instant the
/// owner dies, so there is never a stale lock.
/// </summary>
internal static class Program
{
    private static readonly string Session = $"{Process.GetCurrentProcess().SessionId}.{Environment.UserName}";
    private const string MutexName = @"Local\Akses.SingleInstance";
    private static readonly string PipeName = $"Akses.Open.{Session}";

    private static Mutex? _instanceMutex;

    /// <summary>The command line this process was started with (files and flags), for the first window.</summary>
    public static string[] InitialArgs { get; private set; } = [];

    [STAThread]
    private static void Main(string[] args)
    {
        // Run by the installer / uninstaller: no window.
        if (args.Contains("--register-shell")) { ShellIntegration.Register(); return; }
        if (args.Contains("--unregister-shell")) { ShellIntegration.Unregister(); return; }

        bool isFirst = true;
        try { _instanceMutex = new Mutex(initiallyOwned: false, MutexName, out isFirst); }
        catch { isFirst = true; } // fail open: never be the reason the app doesn't start

        if (!isFirst)
        {
            if (SendToRunningInstance(args)) return;
            // The other instance is starting up or closing and never answered: open our own window.
        }

        InitialArgs = args;
        // WebView2 (documents → PDF) would otherwise create its profile next to the exe.
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", Path.Combine(Settings.DataFolder, "WebView2"));

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
        GC.KeepAlive(_instanceMutex);
    }

    /// <summary>Hands <paramref name="args"/> to the running window. Retries for a few seconds, since with a multi-selection the first process may still be starting.</summary>
    private static bool SendToRunningInstance(string[] args)
    {
        // Relative paths only mean something in this process's working directory. Only existing
        // files/folders are paths: "png" after --to is a format, not a file.
        string[] absolute = args.Select(a => File.Exists(a) || Directory.Exists(a) ? Path.GetFullPath(a) : a).ToArray();
        byte[] payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(absolute));
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                pipe.Connect(1000);
                pipe.Write(payload);
                pipe.Flush();
                return true;
            }
            catch
            {
                Thread.Sleep(200);
            }
        }
        return false;
    }

    /// <summary>Background loop that delivers every later launch's arguments to <paramref name="onArgs"/>. Called once by <see cref="App"/>.</summary>
    public static void ListenForOtherLaunches(Action<string[]> onArgs)
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly);
                    server.WaitForConnection();
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    string[]? args = JsonSerializer.Deserialize<string[]>(reader.ReadToEnd());
                    if (args is not null) onArgs(args);
                }
                catch (Exception ex)
                {
                    AppLog.Write($"Pipe: {ex.Message}");
                    Thread.Sleep(200);
                }
            }
        })
        {
            IsBackground = true,
            Name = "Akses.OpenListener",
        };
        thread.Start();
    }
}
