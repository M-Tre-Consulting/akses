using System.Net;
using System.Text;

namespace Akses.Core.Engines;

/// <summary>
/// Prints an HTML file to PDF. Implemented by the app with WebView2 (Edge's engine, already on every
/// Windows 10/11 PC), so documents become PDFs without shipping a LaTeX distribution; on Linux with
/// LibreOffice, which also lays out Word and OpenDocument files by itself (<see cref="DirectFormats"/>).
/// </summary>
public interface IHtmlToPdf
{
    Task PrintAsync(string htmlPath, string pdfOutput, CancellationToken ct);

    /// <summary>Extensions (lowercase, no dot) <see cref="PrintAsync"/> also takes as they are, without pandoc.</summary>
    IReadOnlySet<string> DirectFormats => new HashSet<string>();
}

/// <summary>Text documents through pandoc: Word, OpenDocument, Markdown, HTML, EPUB, RTF… and PDF via <see cref="IHtmlToPdf"/>.</summary>
public static class DocumentEngine
{
    /// <summary>Clean, readable print style for documents turned into PDF.</summary>
    private const string PrintCss = """
        @page { size: A4; }
        html { font-family: "Segoe UI", "Helvetica Neue", Arial, sans-serif; font-size: 11pt; line-height: 1.5; color: #1a1a1a; }
        body { margin: 0; max-width: none; }
        h1, h2, h3, h4 { line-height: 1.25; margin: 1.2em 0 .5em; break-after: avoid; }
        h1 { font-size: 1.9em; } h2 { font-size: 1.45em; } h3 { font-size: 1.2em; }
        p, li { orphans: 3; widows: 3; }
        img, svg { max-width: 100%; height: auto; }
        table { border-collapse: collapse; width: 100%; margin: 1em 0; font-size: .95em; }
        th, td { border: 1px solid #cfcfcf; padding: 4px 8px; vertical-align: top; }
        th { background: #f2f2f2; }
        pre, code { font-family: Consolas, "Cascadia Mono", monospace; font-size: .92em; }
        pre { background: #f6f6f6; padding: 10px 12px; border-radius: 4px; white-space: pre-wrap; word-wrap: break-word; }
        blockquote { margin: 1em 0; padding-left: 1em; border-left: 3px solid #ddd; color: #555; }
        tr, img, pre { break-inside: avoid; }
        """;

    /// <summary>
    /// True when this conversion goes through pandoc, which Akses downloads on first use
    /// (<see cref="ToolDownloads"/>). HTML and plain text to PDF only need WebView2 (on Linux
    /// LibreOffice, which also takes Word and OpenDocument files directly).
    /// </summary>
    public static bool NeedsPandoc(string input, TargetFormat target) =>
        FormatCatalog.KindOf(input) == FileKind.Document
        && !(target.Id == "pdf" && Path.GetExtension(input).TrimStart('.').ToLowerInvariant() is var ext
             && (ext is "html" or "htm" or "xhtml" or "txt" or "text" || Converter.HtmlToPdf?.DirectFormats.Contains(ext) == true));

    public static async Task ConvertAsync(string input, string output, TargetFormat target, IHtmlToPdf? htmlToPdf, CancellationToken ct)
    {
        string inExt = Path.GetExtension(input).TrimStart('.').ToLowerInvariant();
        string title = Path.GetFileNameWithoutExtension(input);

        if (target.Id == "pdf")
        {
            if (htmlToPdf is null) throw new NotSupportedException("PDF printing is not available");
            if (inExt is "html" or "htm" or "xhtml" || htmlToPdf.DirectFormats.Contains(inExt))
            {
                await htmlToPdf.PrintAsync(input, output, ct).ConfigureAwait(false);
                return;
            }
            string work = Path.Combine(Path.GetTempPath(), "Akses", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                string html = Path.Combine(work, "document.html");
                if (inExt is "txt" or "text")
                {
                    // Plain text stays exactly as written: no Markdown interpretation.
                    string text = await File.ReadAllTextAsync(input, ct).ConfigureAwait(false);
                    await File.WriteAllTextAsync(html,
                        $"<!doctype html><html><head><meta charset=\"utf-8\"><title>{WebUtility.HtmlEncode(title)}</title><style>{PrintCss} pre{{background:none;padding:0;font-size:10pt}}</style></head>" +
                        $"<body><pre>{WebUtility.HtmlEncode(text)}</pre></body></html>", Encoding.UTF8, ct).ConfigureAwait(false);
                }
                else
                {
                    string css = Path.Combine(work, "print.css");
                    await File.WriteAllTextAsync(css, PrintCss, ct).ConfigureAwait(false);
                    await RunPandocAsync(input, html, "html5", ["--standalone", "--embed-resources", "--css", css,
                        "--metadata", $"pagetitle={title}"], ct).ConfigureAwait(false);
                }
                await htmlToPdf.PrintAsync(html, output, ct).ConfigureAwait(false);
            }
            finally
            {
                OutputPaths.TryDelete(work);
            }
            return;
        }

        (string format, string[] extra) = target.Id switch
        {
            "docx" => ("docx", Array.Empty<string>()),
            "odt" => ("odt", []),
            "rtf" => ("rtf", ["--standalone"]),
            "html" => ("html5", ["--standalone", "--embed-resources", "--metadata", $"pagetitle={title}"]),
            "md" => ("gfm", []),
            "txt" => ("plain", ["--wrap=none"]),
            "epub" => ("epub3", ["--metadata", $"title={title}"]),
            _ => throw new NotSupportedException(target.Id),
        };
        await RunPandocAsync(input, output, format, extra, ct).ConfigureAwait(false);
    }

    private static async Task RunPandocAsync(string input, string output, string to, IEnumerable<string> extra, CancellationToken ct)
    {
        var args = new List<string> { "--from", ReaderFor(input), "--to", to, "--output", output,
            "--resource-path", Path.GetDirectoryName(Path.GetFullPath(input)) ?? "." };
        args.AddRange(extra);
        args.Add(input);
        try
        {
            await Tools.RunAsync(Tools.Pandoc, args, ct).ConfigureAwait(false);
        }
        catch
        {
            OutputPaths.TryDelete(output);
            throw;
        }
    }

    private static string ReaderFor(string input) => Path.GetExtension(input).TrimStart('.').ToLowerInvariant() switch
    {
        "md" or "markdown" or "mdown" or "mkd" => "markdown",
        "txt" or "text" => "commonmark+hard_line_breaks",
        "html" or "htm" or "xhtml" => "html",
        "tex" or "latex" => "latex",
        "dbk" => "docbook",
        "typ" => "typst",
        "adoc" or "asciidoc" => "asciidoc",
        "wiki" or "mediawiki" => "mediawiki",
        string other => other, // docx, odt, rtf, epub, rst, org, ipynb, fb2, textile, opml, man, muse, t2t, pptx, xlsx
    };
}
