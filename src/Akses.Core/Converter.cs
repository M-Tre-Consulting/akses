using Akses.Core.Engines;

namespace Akses.Core;

public sealed record ConversionOptions
{
    public Quality Quality { get; init; } = Quality.High;
    /// <summary>Longest side in pixels for images and videos; null = original size.</summary>
    public int? MaxSide { get; init; }
    /// <summary>Fixed output folder; null = next to the original.</summary>
    public string? OutputFolder { get; init; }
}

/// <summary>
/// Converts one file to one target format, picking the engine from the input kind and the target.
/// Returns the path of the result: a file, or a folder for multi-page results (PDF → images).
/// </summary>
public static class Converter
{
    public static IHtmlToPdf? HtmlToPdf { get; set; }

    public static async Task<string> ConvertAsync(string input, TargetFormat target, ConversionOptions options,
        IProgress<double>? progress, CancellationToken ct)
    {
        FileKind kind = FormatCatalog.KindOf(input);
        string folder = OutputPaths.FolderFor(input, options.OutputFolder);
        string name = Path.GetFileNameWithoutExtension(input);

        if (kind == FileKind.Pdf && target.Id is "png-pages" or "jpg-pages")
        {
            string outFolder = OutputPaths.UniqueFolder(folder, name);
            try
            {
                int dpi = options.Quality switch { Quality.High => 200, Quality.Balanced => 150, _ => 100 };
                await PdfEngine.ToImagesAsync(input, outFolder, target.Id == "jpg-pages", dpi, progress, ct).ConfigureAwait(false);
            }
            catch
            {
                OutputPaths.TryDelete(outFolder);
                throw;
            }
            return outFolder;
        }

        string output = OutputPaths.Unique(folder, name, target.Extension);
        try
        {
            switch (kind)
            {
                case FileKind.Image when target.Id is "mp4" or "webm":
                case FileKind.Video or FileKind.Audio or FileKind.Subtitle:
                    await MediaEngine.ConvertAsync(input, output, target, options.Quality, options.MaxSide, progress, ct).ConfigureAwait(false);
                    break;
                case FileKind.Image:
                    await ImageEngine.ConvertAsync(input, output, target, options.Quality, options.MaxSide, ct).ConfigureAwait(false);
                    break;
                case FileKind.Pdf when target.Id == "txt":
                    await PdfEngine.ToTextAsync(input, output, ct).ConfigureAwait(false);
                    break;
                case FileKind.Document:
                    await DocumentEngine.ConvertAsync(input, output, target, HtmlToPdf, ct).ConfigureAwait(false);
                    break;
                case FileKind.Data:
                    await DataEngine.ConvertAsync(input, output, target, ct).ConfigureAwait(false);
                    break;
                default:
                    throw new NotSupportedException($"{kind} → {target.Id}");
            }
        }
        catch
        {
            OutputPaths.TryDelete(output);
            throw;
        }
        progress?.Report(1);
        return output;
    }
}
