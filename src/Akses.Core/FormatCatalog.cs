namespace Akses.Core;

public enum FileKind { Image, Video, Audio, Pdf, Document, Data, Subtitle, Unknown }

/// <summary>Quality presets shared by every lossy encoder (JPG/WebP/AVIF, H.264/VP9, MP3/AAC/Opus…).</summary>
public enum Quality { High, Balanced, Small }

/// <summary>
/// A format a file can be converted to. <see cref="Id"/> is stable (used on the command line by the
/// Explorer menu, e.g. <c>Akses.exe --to png file.webp</c>); <see cref="Extension"/> is what the
/// output file gets.
/// </summary>
public sealed record TargetFormat(string Id, string Label, string Extension);

/// <summary>
/// Which extensions Akses reads, what kind of file each one is, and what each kind converts to.
/// The single source of truth for the convert screen, the Explorer menu and the engines.
/// </summary>
public static class FormatCatalog
{
    private static readonly Dictionary<string, FileKind> Inputs = new(StringComparer.OrdinalIgnoreCase);

    static FormatCatalog()
    {
        Add(FileKind.Image,
            "jpg", "jpeg", "jpe", "jfif", "png", "apng", "webp", "gif", "bmp", "dib", "tif", "tiff", "heic", "heif", "avif",
            "jxl", "jp2", "j2k", "ico", "cur", "svg", "svgz", "psd", "psb", "tga", "dds", "exr", "hdr", "pcx", "ppm",
            "pgm", "pbm", "pnm", "qoi", "xcf", "wbmp", "xpm", "xbm", "sgi", "ras", "fits", "jng", "mng", "miff",
            // camera RAW
            "dng", "cr2", "cr3", "crw", "nef", "nrw", "arw", "srf", "sr2", "orf", "rw2", "raf", "pef", "srw", "x3f",
            "3fr", "erf", "kdc", "mrw", "mos", "iiq", "rwl");
        Add(FileKind.Video,
            "mp4", "m4v", "mkv", "mov", "avi", "webm", "wmv", "asf", "flv", "f4v", "mpg", "mpeg", "mpe", "m2v", "ts",
            "mts", "m2ts", "vob", "3gp", "3g2", "ogv", "dv", "mxf", "rm", "rmvb", "divx", "y4m", "h264", "hevc");
        Add(FileKind.Audio,
            "mp3", "wav", "flac", "aac", "m4a", "m4b", "alac", "ogg", "oga", "opus", "wma", "aiff", "aif", "aifc",
            "amr", "ape", "wv", "ac3", "eac3", "dts", "mka", "caf", "mp2", "mpa", "au", "snd", "voc", "tta", "spx",
            "mid", "midi", "gsm", "w64", "dsf", "dff");
        Add(FileKind.Pdf, "pdf");
        Add(FileKind.Document,
            "docx", "odt", "rtf", "md", "markdown", "mdown", "mkd", "html", "htm", "xhtml", "txt", "text", "epub",
            "rst", "tex", "latex", "org", "ipynb", "fb2", "textile", "dbk", "opml", "typ", "adoc", "asciidoc",
            "man", "muse", "t2t", "wiki", "mediawiki", "pptx", "xlsx");
        Add(FileKind.Data, "json", "jsonc", "geojson", "yaml", "yml", "xml", "csv", "tsv", "tab");
        Add(FileKind.Subtitle, "srt", "vtt", "ass", "ssa", "sub", "sbv", "lrc");
    }

    private static void Add(FileKind kind, params string[] extensions)
    {
        foreach (string ext in extensions) Inputs[ext] = kind;
    }

    /// <summary>Every extension Akses opens, without the dot.</summary>
    public static IReadOnlyCollection<string> InputExtensions => Inputs.Keys;

    public static IEnumerable<string> ExtensionsOf(FileKind kind) => Inputs.Where(p => p.Value == kind).Select(p => p.Key);

    public static FileKind KindOf(string path)
    {
        string ext = Path.GetExtension(path).TrimStart('.');
        return Inputs.TryGetValue(ext, out FileKind kind) ? kind : FileKind.Unknown;
    }

    // ---- targets ------------------------------------------------------------------------------

    public static readonly TargetFormat Jpg = new("jpg", "JPG", "jpg");
    public static readonly TargetFormat Png = new("png", "PNG", "png");
    public static readonly TargetFormat Webp = new("webp", "WEBP", "webp");
    public static readonly TargetFormat Avif = new("avif", "AVIF", "avif");
    public static readonly TargetFormat Gif = new("gif", "GIF", "gif");
    public static readonly TargetFormat Bmp = new("bmp", "BMP", "bmp");
    public static readonly TargetFormat Tiff = new("tiff", "TIFF", "tiff");
    public static readonly TargetFormat Ico = new("ico", "ICO", "ico");
    public static readonly TargetFormat Jxl = new("jxl", "JXL", "jxl");
    public static readonly TargetFormat Pdf = new("pdf", "PDF", "pdf");

    public static readonly TargetFormat Mp4 = new("mp4", "MP4", "mp4");
    public static readonly TargetFormat Mkv = new("mkv", "MKV", "mkv");
    public static readonly TargetFormat Webm = new("webm", "WEBM", "webm");
    public static readonly TargetFormat Mov = new("mov", "MOV", "mov");
    public static readonly TargetFormat Avi = new("avi", "AVI", "avi");

    public static readonly TargetFormat Mp3 = new("mp3", "MP3", "mp3");
    public static readonly TargetFormat Wav = new("wav", "WAV", "wav");
    public static readonly TargetFormat Flac = new("flac", "FLAC", "flac");
    public static readonly TargetFormat M4a = new("m4a", "M4A", "m4a");
    public static readonly TargetFormat Ogg = new("ogg", "OGG", "ogg");
    public static readonly TargetFormat Opus = new("opus", "OPUS", "opus");
    public static readonly TargetFormat Aiff = new("aiff", "AIFF", "aiff");

    public static readonly TargetFormat PngPages = new("png-pages", "PNG", "png");
    public static readonly TargetFormat JpgPages = new("jpg-pages", "JPG", "jpg");
    public static readonly TargetFormat Txt = new("txt", "TXT", "txt");

    public static readonly TargetFormat Docx = new("docx", "DOCX", "docx");
    public static readonly TargetFormat Odt = new("odt", "ODT", "odt");
    public static readonly TargetFormat Rtf = new("rtf", "RTF", "rtf");
    public static readonly TargetFormat Html = new("html", "HTML", "html");
    public static readonly TargetFormat Md = new("md", "Markdown", "md");
    public static readonly TargetFormat Epub = new("epub", "EPUB", "epub");

    public static readonly TargetFormat Json = new("json", "JSON", "json");
    public static readonly TargetFormat Yaml = new("yaml", "YAML", "yaml");
    public static readonly TargetFormat Xml = new("xml", "XML", "xml");
    public static readonly TargetFormat Csv = new("csv", "CSV", "csv");

    public static readonly TargetFormat Srt = new("srt", "SRT", "srt");
    public static readonly TargetFormat Vtt = new("vtt", "VTT", "vtt");
    public static readonly TargetFormat Ass = new("ass", "ASS", "ass");

    private static readonly TargetFormat[] ImageTargets = [Jpg, Png, Webp, Pdf, Avif, Gif, Ico, Bmp, Tiff, Jxl];
    private static readonly TargetFormat[] VideoTargets = [Mp4, Mp3, Gif, Webm, Mkv, Mov, Avi, M4a, Wav, Flac, Ogg, Opus];
    private static readonly TargetFormat[] AudioTargets = [Mp3, Wav, M4a, Flac, Ogg, Opus, Aiff];
    private static readonly TargetFormat[] PdfTargets = [PngPages, JpgPages, Txt];
    private static readonly TargetFormat[] DocumentTargets = [Pdf, Docx, Odt, Html, Md, Txt, Rtf, Epub];
    private static readonly TargetFormat[] DataTargets = [Json, Yaml, Xml, Csv];
    private static readonly TargetFormat[] SubtitleTargets = [Srt, Vtt, Ass];

    /// <summary>
    /// What a file of this kind converts to, most useful first. Animated images also get the video
    /// targets (a GIF to MP4 is one of the most common requests).
    /// </summary>
    public static IReadOnlyList<TargetFormat> TargetsFor(FileKind kind) => kind switch
    {
        FileKind.Image => ImageTargets,
        FileKind.Video => VideoTargets,
        FileKind.Audio => AudioTargets,
        FileKind.Pdf => PdfTargets,
        FileKind.Document => DocumentTargets,
        FileKind.Data => DataTargets,
        FileKind.Subtitle => SubtitleTargets,
        _ => [],
    };

    /// <summary>Targets for a specific file: like <see cref="TargetsFor"/>, minus converting a file to its own format where that would do nothing useful.</summary>
    public static IReadOnlyList<TargetFormat> TargetsFor(string path)
    {
        FileKind kind = KindOf(path);
        string ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        IEnumerable<TargetFormat> targets = TargetsFor(kind);
        if (kind == FileKind.Image && ext is "gif" or "webp" or "apng")
            targets = targets.Concat([Mp4, Webm]);
        // Same format in, same format out still makes sense for lossy media (re-compress / resize),
        // not for documents, data or subtitles.
        if (kind is FileKind.Document or FileKind.Data or FileKind.Subtitle)
            targets = targets.Where(t => !SameExtension(t.Extension, ext));
        return targets.ToList();
    }

    public static TargetFormat? FindTarget(FileKind kind, string id) =>
        TargetsFor(kind).Concat(kind == FileKind.Image ? [Mp4, Webm] : []).FirstOrDefault(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private static bool SameExtension(string a, string b) =>
        Normalize(a) == Normalize(b);

    /// <summary>True when converting a ".<paramref name="extension"/>" file to <paramref name="target"/> keeps the same format.</summary>
    public static bool IsSameFormat(TargetFormat target, string extension) =>
        SameExtension(target.Extension, extension.TrimStart('.').ToLowerInvariant());

    private static string Normalize(string ext) => ext switch
    {
        "jpeg" or "jpe" or "jfif" => "jpg",
        "tif" => "tiff",
        "yml" => "yaml",
        "htm" or "xhtml" => "html",
        "markdown" or "mdown" or "mkd" => "md",
        "text" => "txt",
        "aif" or "aifc" => "aiff",
        _ => ext,
    };
}
