using Akses.Core;
using Akses.Core.Engines;

namespace Akses.App;

/// <summary>
/// Documents → PDF on Linux, where there is no WebView2: LibreOffice (already on Linux Mint and
/// Ubuntu) prints pandoc's HTML, and Word / OpenDocument / RTF files directly, which also keeps their
/// own layout. It runs headless with a profile of its own in the data folder, so it works even while
/// the user has LibreOffice open. One document at a time.
/// </summary>
internal sealed class LibreOfficePdfPrinter : IHtmlToPdf
{
    private static readonly HashSet<string> Direct = ["docx", "doc", "odt", "rtf", "fodt", "wpd", "pptx", "ppt", "odp", "xlsx", "xls", "ods"];
    private readonly SemaphoreSlim _gate = new(1, 1);

    public IReadOnlySet<string> DirectFormats => Direct;

    public async Task PrintAsync(string htmlPath, string pdfOutput, CancellationToken ct)
    {
        string soffice = Locate() ?? throw new ToolMissingException("soffice");
        string ext = Path.GetExtension(htmlPath).TrimStart('.').ToLowerInvariant();
        // HTML opens in Writer/Web, which has its own PDF export; everything else picks it by itself.
        string filter = ext is "html" or "htm" or "xhtml" ? "pdf:writer_web_pdf_Export" : "pdf";
        string profile = new Uri(Path.Combine(Settings.DataFolder, "libreoffice") + "/").AbsoluteUri;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        string work = Path.Combine(Path.GetTempPath(), "Akses", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(work);
            await Tools.RunAsync(soffice,
                [$"-env:UserInstallation={profile}", "--headless", "--norestore", "--nologo", "--nodefault", "--nolockcheck",
                 "--convert-to", filter, "--outdir", work, Path.GetFullPath(htmlPath)], ct, workingDirectory: work).ConfigureAwait(false);
            string result = Path.Combine(work, Path.GetFileNameWithoutExtension(htmlPath) + ".pdf");
            // soffice exits 0 even when it couldn't open the file: no PDF means it failed.
            if (!File.Exists(result)) throw new IOException("LibreOffice could not convert the document");
            File.Move(result, pdfOutput, overwrite: true);
        }
        finally
        {
            OutputPaths.TryDelete(work);
            _gate.Release();
        }
    }

    private static string? Locate() =>
        new[] { "soffice", "libreoffice" }
            .SelectMany(name => (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries)
                .Select(dir => Path.Combine(dir, name)))
            .Append("/usr/lib/libreoffice/program/soffice")
            .Append("/opt/libreoffice/program/soffice")
            .FirstOrDefault(File.Exists);
}
