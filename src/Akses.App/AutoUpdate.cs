using System.Diagnostics;
using Akses.Core;

namespace Akses.App;

/// <summary>
/// Automatic updates (Updates.cs in Akses.Core): the new installer is downloaded in the background,
/// then installed silently when Akses closes, or right away with "Aggiorna e riavvia" (then the
/// installer reopens Akses: /relaunch=1 in installer/Akses.iss).
/// </summary>
internal static class AutoUpdate
{
    private static readonly Version Current = typeof(AutoUpdate).Assembly.GetName().Version ?? new Version(1, 0, 0);
    private static bool _launched;

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

    /// <summary>Starts the silent installer; it closes Akses (CloseApplications) and, with <paramref name="relaunch"/>, reopens it.</summary>
    public static bool Launch(bool relaunch)
    {
        if (Installer is null || _launched || !File.Exists(Installer)) return false;
        string args = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS";
        // The installer remembers the tasks of the first install: keep the Explorer menu off if it was turned off since.
        if (!ShellIntegration.IsRegistered()) args += " /MERGETASKS=\"!explorermenu\"";
        if (relaunch) args += " /relaunch=1";
        try
        {
            Process.Start(new ProcessStartInfo(Installer, args) { UseShellExecute = false });
            _launched = true;
            AppLog.Write($"Aggiornamento a {Pending?.Version.ToString(3)} avviato");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write($"Avvio dell'aggiornamento non riuscito: {ex}");
            return false;
        }
    }
}
