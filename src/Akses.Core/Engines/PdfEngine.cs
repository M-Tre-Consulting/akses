using System.Text;
using ImageMagick;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
#if WINDOWS
using Windows.Storage;
using Windows.Storage.Streams;
using WinPdf = Windows.Data.Pdf;
#endif

namespace Akses.Core.Engines;

/// <summary>
/// PDF tools: merge, split, extract and rotate pages (PDFsharp), pages to images (Windows' own PDF
/// renderer; poppler's pdftoppm on Linux), text (PdfPig).
/// </summary>
public static class PdfEngine
{
    public static int PageCount(string pdf)
    {
        using PdfDocument doc = PdfReader.Open(pdf, PdfDocumentOpenMode.Import);
        return doc.PageCount;
    }

    /// <summary>One PDF from PDFs and images, in order. Each image becomes a page as wide as an A4 sheet, with the image's own proportions.</summary>
    public static Task MergeAsync(IReadOnlyList<string> inputs, string output, IProgress<double>? progress, CancellationToken ct) => Task.Run(() =>
    {
        using var result = new PdfDocument();
        for (int i = 0; i < inputs.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            string input = inputs[i];
            if (FormatCatalog.KindOf(input) == FileKind.Pdf)
            {
                using PdfDocument source = PdfReader.Open(input, PdfDocumentOpenMode.Import);
                foreach (PdfPage page in source.Pages) result.AddPage(page);
            }
            else
            {
                AddImagePage(result, input);
            }
            progress?.Report((i + 1.0) / inputs.Count);
        }
        if (result.PageCount == 0) throw new InvalidDataException("nothing to merge");
        result.Save(output);
    }, ct);

    private static void AddImagePage(PdfDocument doc, string imagePath)
    {
        // PDFsharp reads JPEG and PNG itself; anything else (HEIC, WebP, RAW…) goes through ImageMagick first.
        using var img = new MagickImage(imagePath, new MagickReadSettings { FrameIndex = 0, FrameCount = 1 });
        img.AutoOrient();
        bool photo = !img.HasAlpha;
        var bytes = img.ToByteArray(photo ? MagickFormat.Jpeg : MagickFormat.Png);
        using var stream = new MemoryStream(bytes);
        using XImage ximage = XImage.FromStream(stream);

        const double A4Width = 595.28;
        PdfPage page = doc.AddPage();
        page.Width = XUnit.FromPoint(A4Width);
        page.Height = XUnit.FromPoint(A4Width * img.Height / img.Width);
        using XGraphics gfx = XGraphics.FromPdfPage(page);
        gfx.DrawImage(ximage, 0, 0, page.Width.Point, page.Height.Point);
    }

    /// <summary>Every page in its own PDF, inside <paramref name="outputFolder"/> ("name-1.pdf", "name-2.pdf"…).</summary>
    public static Task SplitAsync(string input, string outputFolder, IProgress<double>? progress, CancellationToken ct) => Task.Run(() =>
    {
        using PdfDocument source = PdfReader.Open(input, PdfDocumentOpenMode.Import);
        string name = Path.GetFileNameWithoutExtension(input);
        Directory.CreateDirectory(outputFolder);
        int digits = source.PageCount.ToString().Length;
        for (int i = 0; i < source.PageCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            using var single = new PdfDocument();
            single.AddPage(source.Pages[i]);
            single.Save(Path.Combine(outputFolder, $"{name}-{(i + 1).ToString().PadLeft(digits, '0')}.pdf"));
            progress?.Report((i + 1.0) / source.PageCount);
        }
    }, ct);

    /// <summary>A new PDF with only <paramref name="pages"/> (1-based, in the order given).</summary>
    public static Task ExtractAsync(string input, string output, IReadOnlyList<int> pages, CancellationToken ct) => Task.Run(() =>
    {
        using PdfDocument source = PdfReader.Open(input, PdfDocumentOpenMode.Import);
        using var result = new PdfDocument();
        foreach (int p in pages)
        {
            ct.ThrowIfCancellationRequested();
            if (p >= 1 && p <= source.PageCount) result.AddPage(source.Pages[p - 1]);
        }
        if (result.PageCount == 0) throw new ArgumentException("no valid pages");
        result.Save(output);
    }, ct);

    /// <summary>Rotates <paramref name="pages"/> (all when null) clockwise by <paramref name="degrees"/>.</summary>
    public static Task RotateAsync(string input, string output, int degrees, IReadOnlyList<int>? pages, CancellationToken ct) => Task.Run(() =>
    {
        using PdfDocument doc = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        var selected = pages is null ? null : new HashSet<int>(pages);
        for (int i = 0; i < doc.PageCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (selected is not null && !selected.Contains(i + 1)) continue;
            PdfPage page = doc.Pages[i];
            page.Rotate = ((page.Rotate + degrees) % 360 + 360) % 360;
        }
        doc.Save(output);
    }, ct);

#if WINDOWS
    /// <summary>Renders every page to PNG or JPG in <paramref name="outputFolder"/>, at <paramref name="dpi"/>.</summary>
    public static async Task ToImagesAsync(string input, string outputFolder, bool jpg, int dpi, IProgress<double>? progress, CancellationToken ct)
    {
        StorageFile file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(input));
        WinPdf.PdfDocument doc = await WinPdf.PdfDocument.LoadFromFileAsync(file);
        Directory.CreateDirectory(outputFolder);
        string name = Path.GetFileNameWithoutExtension(input);
        int digits = doc.PageCount.ToString().Length;

        for (uint i = 0; i < doc.PageCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            using WinPdf.PdfPage page = doc.GetPage(i);
            // Page size is in DIPs (1/96 inch).
            byte[] png = await RenderAsync(page, page.Size.Width * dpi / 96.0);

            string target = Path.Combine(outputFolder, $"{name}-{(i + 1).ToString().PadLeft(digits, '0')}.{(jpg ? "jpg" : "png")}");
            if (jpg)
            {
                using var img = new MagickImage(png);
                img.Quality = 90;
                await img.WriteAsync(target, MagickFormat.Jpeg, ct);
            }
            else
            {
                await File.WriteAllBytesAsync(target, png, ct);
            }
            progress?.Report((i + 1.0) / doc.PageCount);
        }
    }

    /// <summary>PNG of one page (0-based), <paramref name="width"/> pixels wide: the thumbnail of a PDF.</summary>
    public static async Task<byte[]> RenderPageAsync(string input, int pageIndex, int width)
    {
        StorageFile file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(input));
        WinPdf.PdfDocument doc = await WinPdf.PdfDocument.LoadFromFileAsync(file);
        using WinPdf.PdfPage page = doc.GetPage((uint)Math.Clamp(pageIndex, 0, (int)doc.PageCount - 1));
        return await RenderAsync(page, width);
    }

    private static async Task<byte[]> RenderAsync(WinPdf.PdfPage page, double width)
    {
        var options = new WinPdf.PdfPageRenderOptions
        {
            DestinationWidth = (uint)Math.Max(1, Math.Round(width)),
            DestinationHeight = (uint)Math.Max(1, Math.Round(width * page.Size.Height / page.Size.Width)),
            BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255),
        };
        using var stream = new InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(stream, options);
        byte[] png = new byte[stream.Size];
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size);
        reader.ReadBytes(png);
        return png;
    }

#else
    /// <summary>Renders every page to PNG or JPG in <paramref name="outputFolder"/>, at <paramref name="dpi"/>.</summary>
    public static async Task ToImagesAsync(string input, string outputFolder, bool jpg, int dpi, IProgress<double>? progress, CancellationToken ct)
    {
        int count = PageCount(input);
        Directory.CreateDirectory(outputFolder);
        string name = Path.GetFileNameWithoutExtension(input);
        int digits = count.ToString().Length;

        for (int i = 1; i <= count; i++)
        {
            ct.ThrowIfCancellationRequested();
            string target = Path.Combine(outputFolder, $"{name}-{i.ToString().PadLeft(digits, '0')}");
            // -singlefile: "target.png" / "target.jpg", no page number added by pdftoppm.
            await Tools.RunAsync(Tools.Pdftoppm,
                [jpg ? "-jpeg" : "-png", .. (jpg ? new[] { "-jpegopt", "quality=90" } : []), "-r", dpi.ToString(),
                 "-f", i.ToString(), "-l", i.ToString(), "-singlefile", input, target], ct).ConfigureAwait(false);
            progress?.Report((double)i / count);
        }
    }

    /// <summary>PNG of one page (0-based), <paramref name="width"/> pixels wide: the thumbnail of a PDF.</summary>
    public static async Task<byte[]> RenderPageAsync(string input, int pageIndex, int width)
    {
        int page = Math.Clamp(pageIndex, 0, PageCount(input) - 1) + 1;
        string prefix = Path.Combine(Path.GetTempPath(), "Akses", "pdf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(prefix)!);
        try
        {
            await Tools.RunAsync(Tools.Pdftoppm,
                ["-png", "-f", page.ToString(), "-l", page.ToString(), "-scale-to-x", width.ToString(), "-scale-to-y", "-1",
                 "-singlefile", input, prefix], CancellationToken.None).ConfigureAwait(false);
            return await File.ReadAllBytesAsync(prefix + ".png").ConfigureAwait(false);
        }
        finally
        {
            OutputPaths.TryDelete(prefix + ".png");
        }
    }

#endif

    /// <summary>The text of every page, in reading order, pages separated by a blank line.</summary>
    public static Task ToTextAsync(string input, string output, CancellationToken ct) => Task.Run(() =>
    {
        var sb = new StringBuilder();
        using (var doc = UglyToad.PdfPig.PdfDocument.Open(input))
        {
            foreach (var page in doc.GetPages())
            {
                ct.ThrowIfCancellationRequested();
                sb.AppendLine(ContentOrderTextExtractor.GetText(page).TrimEnd());
                sb.AppendLine();
            }
        }
        File.WriteAllText(output, sb.ToString(), new UTF8Encoding(true));
    }, ct);

    /// <summary>
    /// Parses "1-3, 5, 8-" (1-based; a trailing dash means "to the end") into page numbers, in order,
    /// without duplicates. Returns null when the text isn't a valid list.
    /// </summary>
    public static List<int>? ParsePages(string text, int pageCount)
    {
        var pages = new List<int>();
        var seen = new HashSet<int>();
        foreach (string raw in text.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            string part = raw.Trim();
            int from, to;
            int dash = part.IndexOfAny(['-', '–']);
            if (dash < 0)
            {
                if (!int.TryParse(part, out from)) return null;
                to = from;
            }
            else
            {
                string a = part[..dash].Trim(), b = part[(dash + 1)..].Trim();
                from = a.Length == 0 ? 1 : int.TryParse(a, out int x) ? x : -1;
                to = b.Length == 0 ? pageCount : int.TryParse(b, out int y) ? y : -1;
                if (from < 0 || to < 0) return null;
            }
            if (from < 1 || to < 1 || from > pageCount) return null;
            to = Math.Min(to, pageCount);
            int step = from <= to ? 1 : -1;
            for (int p = from; p != to + step; p += step)
                if (seen.Add(p)) pages.Add(p);
        }
        return pages.Count == 0 ? null : pages;
    }
}
