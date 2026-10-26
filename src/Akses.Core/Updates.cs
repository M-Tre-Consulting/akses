using System.Text.Json;

namespace Akses.Core;

/// <summary>A newer release: its version and release notes.</summary>
public sealed record UpdateInfo(Version Version, string Notes);

/// <summary>
/// Updates from the M-Tre site, as in the Mac apps (m-tre-site/brand/DISTRIBUZIONE.md): the site's
/// /aggiornamenti/&lt;app&gt; says which version the latest GitHub release is, and
/// /aggiornamenti/&lt;app&gt;/scarica redirects to its installer (the .exe on Windows, the .deb on
/// Linux). The site sees the IP address and "Akses/&lt;version&gt;" in the User-Agent, nothing else.
/// </summary>
public static class Updates
{
    /// <summary>The site; AKSES_UPDATE_SITE points it elsewhere to try an update before a release.</summary>
    public static readonly string Site = Environment.GetEnvironmentVariable("AKSES_UPDATE_SITE") is { Length: > 0 } test
        ? test.TrimEnd('/') + "/" : "https://mtre-consulting.it/aggiornamenti/";

    /// <summary>The site's name for this platform's installer (config/downloads.ts of m-tre-site).</summary>
    public static string AppSlug => OperatingSystem.IsWindows() ? "akses" : "akses-linux";

    /// <summary>Where the installer is downloaded: in %TEMP%\Akses, emptied at every start.</summary>
    public static string Folder => Path.Combine(Path.GetTempPath(), "Akses", "update");

    private static HttpClient NewClient(Version current, TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"Akses/{current.ToString(3)}");
        return http;
    }

    /// <summary>The latest release, when it is newer than <paramref name="current"/>; null otherwise.</summary>
    public static async Task<UpdateInfo?> CheckAsync(Version current, CancellationToken ct)
    {
        using HttpClient http = NewClient(current, TimeSpan.FromSeconds(30));
        string json = await http.GetStringAsync(Site + AppSlug, ct).ConfigureAwait(false);
        using JsonDocument doc = JsonDocument.Parse(json);
        string tag = doc.RootElement.TryGetProperty("version", out JsonElement v) ? v.GetString() ?? "" : "";
        string notes = doc.RootElement.TryGetProperty("notes", out JsonElement n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";
        Version? latest = ParseVersion(tag);
        return latest is not null && latest > Normalize(current) ? new UpdateInfo(latest, notes) : null;
    }

    /// <summary>"v1.2.0" or "1.2" → 1.2.0; null when it isn't a version.</summary>
    public static Version? ParseVersion(string tag)
    {
        tag = tag.Trim().TrimStart('v', 'V');
        int dash = tag.IndexOfAny(['-', '+']);
        if (dash >= 0) tag = tag[..dash];
        return Version.TryParse(tag, out Version? v) ? Normalize(v) : null;
    }

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    /// <summary>
    /// Downloads the installer of <paramref name="update"/> into <see cref="Folder"/> and returns its
    /// path. A file already there from an earlier download of the same version is reused.
    /// </summary>
    public static async Task<string> DownloadAsync(Version current, UpdateInfo update, IProgress<double>? progress, CancellationToken ct)
    {
        string name = OperatingSystem.IsWindows() ? $"Akses-Setup-{update.Version.ToString(3)}.exe" : $"akses_{update.Version.ToString(3)}_amd64.deb";
        string target = Path.Combine(Folder, name);
        if (File.Exists(target) && LooksComplete(target)) return target;
        Directory.CreateDirectory(Folder);
        string part = target + ".part";
        try
        {
            using HttpClient http = NewClient(current, TimeSpan.FromMinutes(20));
            using HttpResponseMessage response = await http.GetAsync(Site + AppSlug + "/scarica", HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            long? total = response.Content.Headers.ContentLength;
            await using (Stream source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (FileStream file = File.Create(part))
            {
                byte[] buffer = new byte[1 << 16];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    done += read;
                    if (total > 0) progress?.Report(Math.Min(1, done / (double)total));
                }
                if (total is long expected && done != expected) throw new InvalidDataException("the update download was cut short");
            }
            // The installer isn't signed yet, so this is all that can be checked: once it is, check the
            // Authenticode signature here too (DISTRIBUZIONE.md, "Firma digitale").
            if (!LooksComplete(part)) throw new InvalidDataException("the downloaded update is not an installer");
            File.Move(part, target, overwrite: true);
            return target;
        }
        finally
        {
            OutputPaths.TryDelete(part);
        }
    }

    /// <summary>
    /// The automatic check: 10 seconds after start and then every 6 hours, while
    /// <paramref name="enabled"/> says so. A newer version is downloaded straight away and
    /// reported through <paramref name="ready"/> (on a background thread); errors through <paramref name="failed"/>.
    /// </summary>
    public static async Task RunAsync(Version current, Func<bool> enabled, Action<UpdateInfo, string> ready, Action<Exception> failed, CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
        while (!ct.IsCancellationRequested)
        {
            if (enabled())
            {
                try
                {
                    if (await CheckAsync(current, ct).ConfigureAwait(false) is { } update)
                    {
                        ready(update, await DownloadAsync(current, update, null, ct).ConfigureAwait(false));
                        return; // one is enough: it is installed on the next restart
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                catch (Exception ex) { failed(ex); }
            }
            await Task.Delay(TimeSpan.FromHours(6), ct).ConfigureAwait(false);
        }
    }

    /// <summary>A Windows program starts with "MZ", a .deb with "!&lt;arch&gt;".</summary>
    private static bool LooksComplete(string path)
    {
        try
        {
            using FileStream f = File.OpenRead(path);
            if (f.Length < 1_000_000) return false;
            Span<byte> head = stackalloc byte[7];
            if (f.Read(head) < 7) return false;
            return OperatingSystem.IsWindows() ? head[0] == 'M' && head[1] == 'Z' : head.SequenceEqual("!<arch>"u8);
        }
        catch { return false; }
    }
}
