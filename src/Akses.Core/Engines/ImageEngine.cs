using ImageMagick;

namespace Akses.Core.Engines;

/// <summary>What the image editor does to a picture, applied in this order: rotate, flip, crop, resize.</summary>
public sealed record ImageEdit
{
    /// <summary>Clockwise, a multiple of 90.</summary>
    public int Rotation { get; init; }
    public bool FlipHorizontal { get; init; }
    public bool FlipVertical { get; init; }
    /// <summary>In pixels of the rotated/flipped full-size image; null = no crop.</summary>
    public (int X, int Y, int Width, int Height)? Crop { get; init; }
    /// <summary>Final size in pixels; null = keep.</summary>
    public (int Width, int Height)? Resize { get; init; }
}

/// <summary>Images through Magick.NET (ImageMagick): convert, resize, crop, rotate.</summary>
public static class ImageEngine
{
    static ImageEngine()
    {
        // Big RAW/PSD files are fine, decompression bombs are not.
        ResourceLimits.Width = 60_000;
        ResourceLimits.Height = 60_000;
    }

    private static MagickReadSettings ReadSettings(string input) => new()
    {
        // SVG/EPS have no pixel size: render them sharp.
        Density = Path.GetExtension(input).ToLowerInvariant() is ".svg" or ".svgz" ? new Density(192) : null,
    };

    public static async Task ConvertAsync(string input, string output, TargetFormat target, Quality quality, int? maxSide, CancellationToken ct)
    {
        await Task.Run(() =>
        {
            using var frames = new MagickImageCollection();
            frames.Read(input, ReadSettings(input));
            ct.ThrowIfCancellationRequested();

            bool keepAnimation = frames.Count > 1 && target.Id is "gif" or "webp" or "pdf" or "tiff";
            if (keepAnimation && target.Id is "gif" or "webp") frames.Coalesce();

            if (!keepAnimation)
            {
                // A multi-image file (ICO with several sizes, animated GIF, multi-page TIFF) to a
                // single-image format: keep the biggest frame (the first frame for animations).
                IMagickImage<byte> pick = Path.GetExtension(input).ToLowerInvariant() is ".ico" or ".cur"
                    ? frames.OrderByDescending(f => f.Width * f.Height).First()
                    : frames[0];
                using IMagickImage<byte> single = pick.Clone();
                Prepare(single, target, maxSide);
                Write(single, output, target, quality);
                return;
            }

            foreach (IMagickImage<byte> frame in frames) Prepare(frame, target, maxSide);
            if (target.Id == "gif") frames.Optimize();
            foreach (IMagickImage<byte> frame in frames) ApplyQuality(frame, target, quality);
            frames.Write(output, FormatOf(target));
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Saves an edited copy of <paramref name="input"/> (see <see cref="ImageEdit"/>).</summary>
    public static async Task EditAsync(string input, string output, ImageEdit edit, TargetFormat target, Quality quality, CancellationToken ct)
    {
        await Task.Run(() =>
        {
            using var frames = new MagickImageCollection();
            frames.Read(input, ReadSettings(input));
            bool animated = frames.Count > 1 && target.Id is "gif" or "webp";
            if (animated) frames.Coalesce();
            int count = animated ? frames.Count : 1;

            for (int i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested();
                IMagickImage<byte> img = frames[i];
                img.AutoOrient();
                if (edit.Rotation % 360 != 0) img.Rotate(edit.Rotation % 360);
                if (edit.FlipHorizontal) img.Flop();
                if (edit.FlipVertical) img.Flip();
                if (edit.Crop is { } c)
                {
                    img.Crop(new MagickGeometry(c.X, c.Y, (uint)c.Width, (uint)c.Height));
                    img.ResetPage();
                }
                if (edit.Resize is { } r && (r.Width != img.Width || r.Height != img.Height))
                    img.Resize(new MagickGeometry((uint)r.Width, (uint)r.Height) { IgnoreAspectRatio = true });
                PrepareForFormat(img, target);
                ApplyQuality(img, target, quality);
            }

            if (animated)
            {
                if (target.Id == "gif") frames.Optimize();
                frames.Write(output, FormatOf(target));
            }
            else
            {
                Write(frames[0], output, target, quality);
            }
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// A copy without metadata (EXIF, GPS position, camera, editing software…), same format and
    /// pixels. The orientation is applied first so the photo doesn't turn sideways once EXIF is gone.
    /// </summary>
    public static Task StripMetadataAsync(string input, string output, TargetFormat target, CancellationToken ct) => Task.Run(() =>
    {
        using var frames = new MagickImageCollection();
        frames.Read(input, ReadSettings(input));
        foreach (IMagickImage<byte> frame in frames)
        {
            ct.ThrowIfCancellationRequested();
            frame.AutoOrient();
            frame.Strip();
            // Quality stays the one ImageMagick read from the original (JPEG/WebP estimate), so the
            // copy isn't heavier than the photo it came from.
        }
        if (frames.Count > 1 && target.Id is "gif" or "webp" or "tiff") frames.Write(output, FormatOf(target));
        else frames[0].Write(output, FormatOf(target));
    }, ct);

    private static void Prepare(IMagickImage<byte> img, TargetFormat target, int? maxSide)
    {
        img.AutoOrient();
        if (maxSide is int m && Math.Max(img.Width, img.Height) > m)
            img.Resize(new MagickGeometry((uint)m, (uint)m) { Greater = true });
        PrepareForFormat(img, target);
    }

    private static void PrepareForFormat(IMagickImage<byte> img, TargetFormat target)
    {
        switch (target.Id)
        {
            case "jpg" or "bmp":
                // No transparency in these formats: put it on white rather than black.
                if (img.HasAlpha)
                {
                    img.BackgroundColor = MagickColors.White;
                    img.Alpha(AlphaOption.Remove);
                }
                break;
            case "ico":
                // An icon holds sizes up to 256: square it (transparent padding) and let ImageMagick
                // write the usual set of sizes.
                if (img.Width > 256 || img.Height > 256)
                    img.Resize(new MagickGeometry(256, 256));
                uint side = Math.Max(img.Width, img.Height);
                img.BackgroundColor = MagickColors.Transparent;
                img.Extent(side, side, Gravity.Center);
                img.Settings.SetDefine(MagickFormat.Icon, "auto-resize", "256,128,64,48,32,16");
                break;
        }
    }

    private static void ApplyQuality(IMagickImage<byte> img, TargetFormat target, Quality quality)
    {
        img.Quality = target.Id switch
        {
            "png" => 95, // zlib level 9, adaptive filtering: lossless either way
            "avif" or "jxl" => quality switch { Quality.High => 85, Quality.Balanced => 70, _ => 55 },
            _ => quality switch { Quality.High => 92, Quality.Balanced => 82, _ => 68 },
        };
        if (quality == Quality.Small) img.Strip();
        if (target.Id == "webp" && quality == Quality.High) img.Settings.SetDefine(MagickFormat.WebP, "method", "6");
    }

    private static void Write(IMagickImage<byte> img, string output, TargetFormat target, Quality quality)
    {
        ApplyQuality(img, target, quality);
        img.Write(output, FormatOf(target));
    }

    private static MagickFormat FormatOf(TargetFormat target) => target.Id switch
    {
        "jpg" => MagickFormat.Jpeg,
        "png" => MagickFormat.Png,
        "webp" => MagickFormat.WebP,
        "avif" => MagickFormat.Avif,
        "gif" => MagickFormat.Gif,
        "bmp" => MagickFormat.Bmp,
        "tiff" => MagickFormat.Tiff,
        "ico" => MagickFormat.Ico,
        "jxl" => MagickFormat.Jxl,
        "pdf" => MagickFormat.Pdf,
        _ => throw new NotSupportedException(target.Id),
    };

    /// <summary>True for files with more than one frame (animated GIF/WebP), which the editor treats frame by frame.</summary>
    public static bool IsAnimated(string input)
    {
        try
        {
            using var frames = new MagickImageCollection();
            frames.Ping(input);
            return frames.Count > 1 && Path.GetExtension(input).ToLowerInvariant() is ".gif" or ".webp" or ".apng" or ".png";
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// A downscaled copy of an image kept in memory for the editor's preview, so rotating and flipping
/// redraw instantly even for a 50-megapixel RAW. Reports the full-size dimensions after the same
/// rotation, which is the coordinate space crops are expressed in.
/// </summary>
public sealed class ImagePreview : IDisposable
{
    private readonly MagickImage _base;

    public int FullWidth { get; }
    public int FullHeight { get; }

    private ImagePreview(MagickImage image, int fullWidth, int fullHeight)
    {
        _base = image;
        FullWidth = fullWidth;
        FullHeight = fullHeight;
    }

    public static Task<ImagePreview> LoadAsync(string input, int maxSide, CancellationToken ct = default) => Task.Run(() =>
    {
        var settings = new MagickReadSettings
        {
            FrameIndex = 0,
            FrameCount = 1,
            Density = Path.GetExtension(input).ToLowerInvariant() is ".svg" or ".svgz" ? new Density(192) : null,
        };
        var img = new MagickImage(input, settings);
        ct.ThrowIfCancellationRequested();
        img.AutoOrient();
        int w = (int)img.Width, h = (int)img.Height;
        if (Math.Max(w, h) > maxSide) img.Resize(new MagickGeometry((uint)maxSide, (uint)maxSide));
        return new ImagePreview(img, w, h);
    }, ct);

    /// <summary>PNG bytes of the preview with <paramref name="rotation"/>/flips applied, plus the matching full size.</summary>
    public (byte[] Png, int FullWidth, int FullHeight) Render(int rotation, bool flipH, bool flipV)
    {
        using var img = (MagickImage)_base.Clone();
        if (rotation % 360 != 0) img.Rotate(rotation % 360);
        if (flipH) img.Flop();
        if (flipV) img.Flip();
        bool swapped = rotation % 180 != 0;
        return (img.ToByteArray(MagickFormat.Png), swapped ? FullHeight : FullWidth, swapped ? FullWidth : FullHeight);
    }

    public void Dispose() => _base.Dispose();
}
