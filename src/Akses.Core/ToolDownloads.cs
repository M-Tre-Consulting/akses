using System.IO.Compression;
using System.Security.Cryptography;

namespace Akses.Core;

/// <summary>
/// pandoc (documents) is 225 MB, more than the rest of Akses put together, and only needed by
/// people who convert Word/Markdown/ePub files. So the installer leaves it out and Akses downloads
/// the official release the first time it's needed, after asking: pinned version, checked against
/// its SHA-256 before anything is extracted (same pin as installer/fetch-tools.ps1).
/// </summary>
public static class ToolDownloads
{
    public const string PandocVersion = "3.12.1";
    private const string PandocUrl = "https://github.com/jgm/pandoc/releases/download/3.12.1/pandoc-3.12.1-windows-x86_64.zip";
    private const string PandocSha256 = "6ef431cb20b2c24a0371fc19200dc49d452be54b1b5c9239f3462841ef53e587";
    /// <summary>Download size, for the question shown to the user.</summary>
    public const int PandocDownloadMb = 41;

    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static bool PandocInstalled => Tools.IsAvailable("pandoc.exe");

    /// <summary>Downloads and unpacks pandoc into <see cref="Tools.DownloadFolder"/>. Safe to call twice: the second call finds it there.</summary>
    public static async Task InstallPandocAsync(IProgress<double>? progress, CancellationToken ct)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (PandocInstalled) return;
            string target = Path.Combine(Tools.DownloadFolder, "pandoc");
            string zip = Path.Combine(Path.GetTempPath(), $"akses-pandoc-{Guid.NewGuid():N}.zip");
            try
            {
                using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
                {
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("Akses");
                    using HttpResponseMessage response = await http.GetAsync(PandocUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    long total = response.Content.Headers.ContentLength ?? PandocDownloadMb * 1_000_000L;
                    await using Stream source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    await using FileStream file = File.Create(zip);
                    byte[] buffer = new byte[1 << 16];
                    long done = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                    {
                        await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                        done += read;
                        progress?.Report(Math.Min(0.95, done / (double)total));
                    }
                }

                string hash;
                await using (FileStream check = File.OpenRead(zip))
                    hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(check, ct).ConfigureAwait(false));
                if (hash != PandocSha256) throw new InvalidDataException("the downloaded component didn't pass the integrity check");

                // Unpack into a temporary folder next to the target and rename it at the end, so a
                // half-written pandoc.exe is never picked up.
                string staging = target + ".part";
                OutputPaths.TryDelete(staging);
                Directory.CreateDirectory(staging);
                using (ZipArchive archive = ZipFile.OpenRead(zip))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        string name = Path.GetFileName(entry.FullName);
                        if (name.Equals("pandoc.exe", StringComparison.OrdinalIgnoreCase) || name.StartsWith("COPY", StringComparison.OrdinalIgnoreCase))
                            entry.ExtractToFile(Path.Combine(staging, name), overwrite: true);
                    }
                }
                if (!File.Exists(Path.Combine(staging, "pandoc.exe"))) throw new InvalidDataException("pandoc.exe missing from the download");
                OutputPaths.TryDelete(target);
                Directory.Move(staging, target);
                progress?.Report(1);
            }
            finally
            {
                OutputPaths.TryDelete(zip);
            }
        }
        finally
        {
            Gate.Release();
        }
    }
}
