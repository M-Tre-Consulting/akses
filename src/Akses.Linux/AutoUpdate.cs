using System.Diagnostics;
using Akses.Core;

namespace Akses.App;

/// <summary>
/// Automatic updates (Updates.cs in Akses.Core): the new .deb is downloaded in the background;
/// "Installa" installs it with apt through pkexec (the system asks for the password), then reopens
/// Akses. If that doesn't work the package is opened with the system's installer instead. Same
/// role as the Windows app's AutoUpdate, which installs silently instead.
/// </summary>
internal static class AutoUpdate
{
    private static readonly Version Current = typeof(AutoUpdate).Assembly.GetName().Version ?? new Version(1, 0, 0);

    public static UpdateInfo? Pending { get; private set; }
    public static string? Installer { get; private set; }

    /// <summary>An update is downloaded and ready (raised on a background thread).</summary>
    public static event Action? Ready;

    public static void Start() => _ = Updates.RunAsync(Current, () => Settings.Current.AutoUpdate, SetReady,
        ex => AppLog.Write($"Controllo aggiornamenti non riuscito: {ex.Message}"), CancellationToken.None);

    /// <summary>"Controlla ora" in Impostazioni: true when an update was found and downloaded.</summary>
    public static async Task<bool> CheckNowAsync()
    {
        if (Installer is not null) return true;
        if (await Updates.CheckAsync(Current, CancellationToken.None) is not { } update) return false;
        SetReady(update, await Updates.DownloadAsync(Current, update, null, CancellationToken.None));
        return true;
    }

    private static void SetReady(UpdateInfo update, string installer)
    {
        Pending = update;
        Installer = installer;
        Ready?.Invoke();
    }

    /// <summary>
    /// Installs the downloaded package. True when it is installed (the caller then reopens Akses);
    /// false when it was handed to the system's package installer, or the user cancelled.
    /// </summary>
    public static async Task<bool> InstallAsync()
    {
        if (Installer is null || !File.Exists(Installer)) return false;
        try
        {
            // pkexec shows the system's password dialog; 126 = the user cancelled it.
            using var apt = Process.Start(new ProcessStartInfo("pkexec", ["apt-get", "install", "-y", "--allow-downgrades", Installer])
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;
            Task<string> err = apt.StandardError.ReadToEndAsync();
            await apt.StandardOutput.ReadToEndAsync();
            await apt.WaitForExitAsync();
            if (apt.ExitCode == 0)
            {
                AppLog.Write($"Aggiornamento a {Pending?.Version.ToString(3)} installato");
                return true;
            }
            if (apt.ExitCode == 126) return false;
            AppLog.Write($"apt-get install dell'aggiornamento: uscita {apt.ExitCode}\n{await err}");
        }
        catch (Exception ex)
        {
            AppLog.Write($"Installazione dell'aggiornamento non riuscita: {ex.Message}");
        }
        Ui.Open(Installer);
        throw new InvalidOperationException(Strings.UpdateInstallFailed);
    }

    /// <summary>
    /// Starts the newly installed Akses once this process is gone: started now, it would just hand its
    /// arguments to this instance (Program.cs, one Akses at a time) and quit. The caller then closes the window.
    /// </summary>
    public static void Relaunch()
    {
        int pid = Environment.ProcessId;
        try { Process.Start(new ProcessStartInfo("sh", ["-c", $"while kill -0 {pid} 2>/dev/null; do sleep 0.3; done; exec /usr/bin/akses"]) { UseShellExecute = false }); }
        catch (Exception ex) { AppLog.Write($"Riavvio dopo l'aggiornamento non riuscito: {ex.Message}"); }
    }
}
