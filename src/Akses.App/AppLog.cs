namespace Akses.App;

/// <summary>
/// Plain-text technical log in %LOCALAPPDATA%\Akses\akses.log: failed conversions and crashes, to
/// understand a problem after the fact. Trimmed to the last ~1 MB at startup, never sent anywhere
/// (see the privacy policy in Strings.cs).
/// </summary>
internal static class AppLog
{
    private static readonly string LogPath = Path.Combine(Settings.DataFolder, "akses.log");
    private static readonly object Gate = new();

    static AppLog()
    {
        try
        {
            var info = new FileInfo(LogPath);
            if (info.Exists && info.Length > 2_000_000)
            {
                string tail = File.ReadAllText(LogPath)[^1_000_000..];
                File.WriteAllText(LogPath, tail);
            }
        }
        catch
        {
            // Logging itself must never be what breaks the app.
        }
    }

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging itself must never be what breaks the app.
        }
    }
}
