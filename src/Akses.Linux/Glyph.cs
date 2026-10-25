using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Akses.App;

/// <summary>
/// A line icon (24×24 path data, drawn with a 2 px round stroke, or filled) in the current text
/// colour. Linux has no Segoe Fluent Icons, so the glyphs of the Windows app are redrawn here as
/// paths (<see cref="Icons"/>).
/// </summary>
internal sealed class Glyph : Control
{
    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<Glyph>();

    private readonly Geometry _geometry;
    private readonly bool _fill;

    static Glyph() => AffectsRender<Glyph>(ForegroundProperty);

    public Glyph(string data, double size = 16, bool fill = false)
    {
        _geometry = Geometry.Parse(data);
        _fill = fill;
        Width = Height = size;
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        double scale = Math.Min(Bounds.Width, Bounds.Height) / 24;
        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
        {
            context.DrawGeometry(_fill ? Foreground : null,
                _fill ? null : new Pen(Foreground, 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round),
                _geometry);
        }
    }
}

/// <summary>Path data for every icon the app shows (lucide-style, 24×24).</summary>
internal static class Icons
{
    public const string Image = "M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z M7 9a2 2 0 1 0 4 0a2 2 0 1 0-4 0 M21 15l-4-4L6 21";
    public const string Video = "M4 6h10a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2z M16 10.5l6-3.5v10l-6-3.5";
    public const string Audio = "M9 18V5l12-2v13 M3 18a3 3 0 1 0 6 0a3 3 0 1 0-6 0 M15 16a3 3 0 1 0 6 0a3 3 0 1 0-6 0";
    public const string Document = "M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z M14 2v6h6 M16 13H8 M16 17H8 M10 9H8";
    public const string Data = "M8 3H7a2 2 0 0 0-2 2v5a2 2 0 0 1-2 2a2 2 0 0 1 2 2v5a2 2 0 0 0 2 2h1 M16 21h1a2 2 0 0 0 2-2v-5a2 2 0 0 1 2-2a2 2 0 0 1-2-2V5a2 2 0 0 0-2-2h-1";
    public const string Subtitle = "M5 5h14a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2z M7 15h4 M14 15h3 M7 11h2 M12 11h5";
    public const string File = "M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z M14 2v6h6";
    public const string Settings = "M21 4h-7 M10 4H3 M21 12h-9 M8 12H3 M21 20h-5 M12 20H3 M14 2v4 M8 10v4 M16 18v4";
    public const string Back = "M19 12H5 M12 19l-7-7 7-7";
    public const string Open = "M15 3h6v6 M10 14L21 3 M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6";
    public const string Folder = "M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-8l-2-3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2z";
    public const string Close = "M18 6L6 18 M6 6l12 12";
    public const string Crop = "M6 2v14a2 2 0 0 0 2 2h14 M18 22V8a2 2 0 0 0-2-2H2";
    public const string Resize = "M15 3h6v6 M9 21H3v-6 M21 3l-7 7 M3 21l7-7";
    public const string RotateRight = "M21 12a9 9 0 1 1-9-9c2.5 0 4.9 1 6.7 2.7L21 8 M21 3v5h-5";
    public const string RotateLeft = "M3 12a9 9 0 1 0 9-9a9.8 9.8 0 0 0-6.7 2.7L3 8 M3 3v5h5";
    public const string FlipHorizontal = "M3 7l5 5-5 5z M21 7l-5 5 5 5z M12 20v2 M12 14v2 M12 8v2 M12 2v2";
    public const string FlipVertical = "M17 3l-5 5-5-5z M17 21l-5-5-5 5z M4 12H2 M10 12H8 M16 12h-2 M22 12h-2";
    public const string Compress = "M4 14h6v6 M20 10h-6V4 M14 10l7-7 M3 21l7-7";
    public const string Shield = "M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z M9 12l2 2 4-4";
    public const string Scissors = "M3 6a3 3 0 1 0 6 0a3 3 0 1 0-6 0 M3 18a3 3 0 1 0 6 0a3 3 0 1 0-6 0 M20 4L8.1 15.9 M14.5 14.5L20 20 M8.1 8.1L12 12";
    public const string Film = "M3 5h18v14H3z M7 5v14 M17 5v14 M3 9.5h4 M3 14.5h4 M17 9.5h4 M17 14.5h4";
    public const string Mute = "M11 5L6 9H2v6h4l5 4z M22 9l-6 6 M16 9l6 6";
    public const string Split = "M8 3H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h3 M16 3h3a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2h-3 M12 2v20";
    public const string ExtractPages = "M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z M14 2v6h6 M12 18v-6 M9 15l3 3 3-3";
    public const string Merge = "M12 5v14 M5 12h14";
    public const string Text = "M4 7V4h16v3 M9 20h6 M12 4v16";
    public const string Up = "M18 15l-6-6-6 6";
    public const string Down = "M6 9l6 6 6-6";
    public const string Left = "M15 18l-6-6 6-6";
    public const string Right = "M9 18l6-6-6-6";
    public const string Play = "M7 4.5v15a1 1 0 0 0 1.5.9l12-7.5a1 1 0 0 0 0-1.8l-12-7.5A1 1 0 0 0 7 4.5z";
    public const string Pause = "M6 4h4v16H6z M14 4h4v16h-4z";
    public const string StartHere = "M10 4H6v16h4 M13 12h8";
    public const string EndHere = "M14 4h4v16h-4 M3 12h8";
    public const string Upload = "M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4 M17 8l-5-5-5 5 M12 3v12";

    public static string For(Akses.Core.FileKind kind) => kind switch
    {
        Akses.Core.FileKind.Image => Image,
        Akses.Core.FileKind.Video => Video,
        Akses.Core.FileKind.Audio => Audio,
        Akses.Core.FileKind.Pdf or Akses.Core.FileKind.Document => Document,
        Akses.Core.FileKind.Data => Data,
        Akses.Core.FileKind.Subtitle => Subtitle,
        _ => File,
    };
}
