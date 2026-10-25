using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Avalonia;

namespace Akses.App;

/// <summary>
/// Entry point: one Akses window per user, which receives the files of every later launch.
///
/// The file manager's "Apri con Akses" may start Akses while it is already open. The first process
/// becomes the window; every other one hands its command line over a local socket (a .NET named
/// pipe) to that window and exits. Who is first is decided by an exclusive lock on a file in the
/// data folder: the kernel drops it the instant the owner dies, so there is never a stale lock
/// (the Windows app does the same with a named Mutex).
/// </summary>
internal static class Program
{
    private static readonly string PipeName = $"Akses.Open.{Environment.UserName}";
    private static FileStream? _instanceLock;

    /// <summary>The command line this process was started with (files and flags), for the first window.</summary>
    public static string[] InitialArgs { get; private set; } = [];

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--register-shell")) { ShellIntegration.Register(); return 0; }
        if (args.Contains("--unregister-shell")) { ShellIntegration.Unregister(); return 0; }

        bool isFirst;
        try
        {
            // FileShare.None is an exclusive flock() on Linux.
            _instanceLock = new FileStream(Path.Combine(Settings.DataFolder, "instance.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
            isFirst = true;
        }
        catch (IOException)
        {
            isFirst = false;
        }
        catch
        {
            isFirst = true; // fail open: never be the reason the app doesn't start
        }

        if (!isFirst && SendToRunningInstance(args)) return 0;

        InitialArgs = args;
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            AppLog.Write($"!!! Avvio fallito: {ex}");
            throw;
        }
        finally
        {
            GC.KeepAlive(_instanceLock);
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new X11PlatformOptions { WmClass = "akses" })
            .LogToTrace();

    /// <summary>Hands <paramref name="args"/> to the running window. Retries for a few seconds, since the first process may still be starting.</summary>
    private static bool SendToRunningInstance(string[] args)
    {
        string[] absolute = args.Select(a => File.Exists(a) || Directory.Exists(a) ? Path.GetFullPath(a) : a).ToArray();
        byte[] payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(absolute, AppJson.Default.StringArray));
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
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
        // A crashed instance can leave its socket file behind; we hold the lock, so it's ours to clear.
        Akses.Core.OutputPaths.TryDelete(Path.Combine(Path.GetTempPath(), "CoreFxPipe_" + PipeName));
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly);
                    server.WaitForConnection();
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    string[]? args = JsonSerializer.Deserialize(reader.ReadToEnd(), AppJson.Default.StringArray);
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
