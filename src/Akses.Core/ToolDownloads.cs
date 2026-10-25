using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Akses.Core;

/// <summary>
/// pandoc (documents) is 225 MB, more than the rest of Akses put together, and only needed by
/// people who convert Word/Markdown/ePub files. So the installer leaves it out and Akses downloads
/// the official release the first time it's needed, after asking: pinned version, checked against
/// its SHA-256 before anything is extracted (same pin as installer/fetch-tools.ps1). On Linux the
/// same version, as the official static linux-amd64 build.
/// </summary>
public static class ToolDownloads
{
    public const string PandocVersion = "3.12.1";
    private static readonly string PandocUrl = OperatingSystem.IsWindows()
        ? "https://github.com/jgm/pandoc/releases/download/3.12.1/pandoc-3.12.1-windows-x86_64.zip"
        : "https://github.com/jgm/pandoc/releases/download/3.12.1/pandoc-3.12.1-linux-amd64.tar.gz";
    private static readonly string PandocSha256 = OperatingSystem.IsWindows()
        ? "6ef431cb20b2c24a0371fc19200dc49d452be54b1b5c9239f3462841ef53e587"
        : "d0c90410e90204c9ca83b8539fac5c7aed01fd537207e4585849f8abc5df20b8";
    /// <summary>Download size, for the question shown to the user.</summary>
    public static int PandocDownloadMb => OperatingSystem.IsWindows() ? 41 : 36;

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
            string zip = Path.Combine(Path.GetTempPath(), $"akses-pandoc-{Guid.NewGuid():N}.download");
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
                string exe = OperatingSystem.IsWindows() ? "pandoc.exe" : "pandoc";
                if (OperatingSystem.IsWindows())
                {
                    using ZipArchive archive = ZipFile.OpenRead(zip);
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        string name = Path.GetFileName(entry.FullName);
                        if (name.Equals(exe, StringComparison.OrdinalIgnoreCase) || name.StartsWith("COPY", StringComparison.OrdinalIgnoreCase))
                            entry.ExtractToFile(Path.Combine(staging, name), overwrite: true);
                    }
                }
                else
                {
                    // pandoc-3.12.1/bin/pandoc: only the program itself.
                    await using FileStream compressed = File.OpenRead(zip);
                    await using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
                    using var tar = new TarReader(gzip);
                    while (await tar.GetNextEntryAsync(cancellationToken: ct).ConfigureAwait(false) is { } entry)
                    {
                        if (entry.EntryType != TarEntryType.RegularFile || entry.Name.Split('/') is not [_, "bin", "pandoc"]) continue;
                        string unpacked = Path.Combine(staging, exe);
                        await entry.ExtractToFileAsync(unpacked, overwrite: true, ct).ConfigureAwait(false);
                        File.SetUnixFileMode(unpacked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                    }
                }
                if (!File.Exists(Path.Combine(staging, exe))) throw new InvalidDataException("pandoc missing from the download");
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
