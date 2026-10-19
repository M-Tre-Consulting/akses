using System.Globalization;
using System.Text.Json;

namespace Akses.Core.Engines;

public sealed record MediaInfo(TimeSpan Duration, bool HasVideo, bool HasAudio, int Width, int Height, string? VideoCodec, string? AudioCodec);

/// <summary>Audio, video and subtitles, through ffmpeg/ffprobe.</summary>
public static class MediaEngine
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static async Task<MediaInfo> ProbeAsync(string input, CancellationToken ct = default)
    {
        string json = await Tools.RunAsync(Tools.Ffprobe,
            ["-v", "error", "-print_format", "json", "-show_format", "-show_streams", input], ct).ConfigureAwait(false);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        double seconds = 0;
        if (root.TryGetProperty("format", out JsonElement format) && format.TryGetProperty("duration", out JsonElement d))
            double.TryParse(d.GetString(), NumberStyles.Float, Inv, out seconds);

        bool hasVideo = false, hasAudio = false;
        int width = 0, height = 0;
        string? vcodec = null, acodec = null;
        if (root.TryGetProperty("streams", out JsonElement streams))
        {
            foreach (JsonElement s in streams.EnumerateArray())
            {
                string type = s.TryGetProperty("codec_type", out JsonElement t) ? t.GetString() ?? "" : "";
                string codec = s.TryGetProperty("codec_name", out JsonElement c) ? c.GetString() ?? "" : "";
                bool attachedPic = s.TryGetProperty("disposition", out JsonElement disp)
                    && disp.TryGetProperty("attached_pic", out JsonElement ap) && ap.GetInt32() == 1;
                if (type == "video" && !attachedPic && !hasVideo)
                {
                    hasVideo = true;
                    vcodec = codec;
                    width = s.TryGetProperty("width", out JsonElement w) ? w.GetInt32() : 0;
                    height = s.TryGetProperty("height", out JsonElement h) ? h.GetInt32() : 0;
                }
                else if (type == "audio" && !hasAudio)
                {
                    hasAudio = true;
                    acodec = codec;
                }
            }
        }
        return new MediaInfo(TimeSpan.FromSeconds(seconds), hasVideo, hasAudio, width, height, vcodec, acodec);
    }

    // ---- convert ---------------------------------------------------------------------------

    public static async Task ConvertAsync(string input, string output, TargetFormat target, Quality quality, int? maxSide,
        IProgress<double>? progress, CancellationToken ct)
    {
        string ext = target.Extension;
        if (FormatCatalog.KindOf(input) == FileKind.Subtitle)
        {
            await Tools.RunAsync(Tools.Ffmpeg, ["-hide_banner", "-loglevel", "error", "-y", "-i", input, output], ct).ConfigureAwait(false);
            return;
        }

        MediaInfo info = await ProbeAsync(input, ct).ConfigureAwait(false);
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostats", "-progress", "pipe:1", "-y", "-i", input };

        bool audioOnly = ext is "mp3" or "wav" or "flac" or "m4a" or "ogg" or "opus" or "aiff";
        if (audioOnly)
        {
            if (!info.HasAudio) throw new InvalidDataException("no audio track");
            args.AddRange(["-map", "0:a:0", "-vn", "-sn", "-dn"]);
            args.AddRange(CanCopyAudio(info.AudioCodec, ext) && quality == Quality.High ? ["-c:a", "copy"] : AudioEncoder(ext, quality));
            if (ext == "mp3") args.AddRange(["-id3v2_version", "3"]);
        }
        else if (ext == "gif")
        {
            int fps = quality switch { Quality.High => 15, Quality.Balanced => 12, _ => 10 };
            int width = maxSide ?? quality switch { Quality.High => 720, Quality.Balanced => 480, _ => 360 };
            args.AddRange(["-an", "-sn", "-vf",
                $"fps={fps},scale='min({width},iw)':-2:flags=lanczos,split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=5",
                "-loop", "0"]);
        }
        else
        {
            if (!info.HasVideo) throw new InvalidDataException("no video track");
            bool copyVideo = quality == Quality.High && maxSide is null && CanCopyVideo(info.VideoCodec, ext);
            bool copyAudio = copyVideo && CanCopyAudio(info.AudioCodec, ext);
            args.AddRange(["-map", "0:v:0", "-map", "0:a:0?", "-sn", "-dn"]);
            if (copyVideo)
                args.AddRange(["-c:v", "copy"]);
            else
            {
                args.AddRange(VideoEncoder(ext, quality));
                args.AddRange(["-vf", ScaleFilter(maxSide)]);
            }
            args.AddRange(copyAudio ? ["-c:a", "copy"] : VideoAudioEncoder(ext, quality));
            if (ext is "mp4" or "mov") args.AddRange(["-movflags", "+faststart"]);
            if (ext == "mp4" && (copyVideo ? info.VideoCodec : "h264") == "hevc") args.AddRange(["-tag:v", "hvc1"]);
        }
        args.Add(output);
        await RunWithProgressAsync(args, info.Duration, progress, ct).ConfigureAwait(false);
    }

    /// <summary>Even dimensions (H.264 needs them), optionally shrunk so the long side is at most <paramref name="maxSide"/>.</summary>
    private static string ScaleFilter(int? maxSide) => maxSide is int m
        ? $"scale='if(gte(iw,ih),min({m},iw),-2)':'if(gte(iw,ih),-2,min({m},ih))':flags=lanczos,scale=trunc(iw/2)*2:trunc(ih/2)*2,format=yuv420p"
        : "scale=trunc(iw/2)*2:trunc(ih/2)*2,format=yuv420p";

    private static string[] VideoEncoder(string ext, Quality q) => ext switch
    {
        "webm" => ["-c:v", "libvpx-vp9", "-crf", q switch { Quality.High => "30", Quality.Balanced => "34", _ => "40" }, "-b:v", "0",
                   "-row-mt", "1", "-deadline", "good", "-cpu-used", "4"],
        "avi" => ["-c:v", "mpeg4", "-q:v", q switch { Quality.High => "3", Quality.Balanced => "5", _ => "8" }],
        _ => ["-c:v", "libx264", "-preset", "veryfast", "-crf", q switch { Quality.High => "20", Quality.Balanced => "23", _ => "28" }],
    };

    private static string[] VideoAudioEncoder(string ext, Quality q) => ext switch
    {
        "webm" => ["-c:a", "libopus", "-b:a", q == Quality.Small ? "96k" : "128k"],
        "avi" => ["-c:a", "libmp3lame", "-b:a", q == Quality.Small ? "128k" : "192k"],
        _ => ["-c:a", "aac", "-b:a", q switch { Quality.High => "192k", Quality.Balanced => "160k", _ => "128k" }],
    };

    private static string[] AudioEncoder(string ext, Quality q) => ext switch
    {
        "mp3" => ["-c:a", "libmp3lame", "-b:a", q switch { Quality.High => "320k", Quality.Balanced => "192k", _ => "128k" }],
        "m4a" => ["-c:a", "aac", "-b:a", q switch { Quality.High => "256k", Quality.Balanced => "192k", _ => "128k" }],
        "ogg" => ["-c:a", "libvorbis", "-q:a", q switch { Quality.High => "7", Quality.Balanced => "5", _ => "3" }],
        "opus" => ["-c:a", "libopus", "-b:a", q switch { Quality.High => "192k", Quality.Balanced => "128k", _ => "96k" }],
        "flac" => ["-c:a", "flac"],
        "aiff" => ["-c:a", "pcm_s16be"],
        _ => ["-c:a", "pcm_s16le"],
    };

    private static bool CanCopyVideo(string? codec, string ext) => ext switch
    {
        "mp4" or "mov" => codec is "h264" or "hevc" or "av1" or "mpeg4",
        "mkv" => codec is not null and not "rawvideo",
        "webm" => codec is "vp8" or "vp9" or "av1",
        _ => false,
    };

    private static bool CanCopyAudio(string? codec, string ext) => ext switch
    {
        "mp4" or "mov" => codec is "aac" or "mp3" or "alac" or "ac3",
        "mkv" => codec is not null,
        "webm" => codec is "opus" or "vorbis",
        "mp3" => codec == "mp3",
        "m4a" => codec is "aac" or "alac",
        "ogg" => codec == "vorbis",
        "opus" => codec == "opus",
        "flac" => codec == "flac",
        _ => false,
    };

    /// <summary>The same video without its audio tracks, copied as is (instant).</summary>
    public static async Task RemoveAudioAsync(string input, string output, CancellationToken ct)
    {
        try
        {
            await Tools.RunAsync(Tools.Ffmpeg,
                ["-hide_banner", "-loglevel", "error", "-y", "-i", input, "-map", "0", "-map", "-0:a", "-c", "copy", output], ct).ConfigureAwait(false);
        }
        catch
        {
            OutputPaths.TryDelete(output);
            throw;
        }
    }

    // ---- trim ------------------------------------------------------------------------------

    /// <summary>
    /// Keeps <paramref name="start"/>–<paramref name="end"/> (or, with <paramref name="removeSelection"/>,
    /// everything except it). Fast mode copies the streams without re-encoding — done in a second, but
    /// a video cut snaps to the nearest keyframe; precise mode re-encodes and is frame-exact.
    /// </summary>
    public static async Task TrimAsync(string input, string output, TimeSpan start, TimeSpan end, bool precise, bool removeSelection,
        IProgress<double>? progress, CancellationToken ct)
    {
        MediaInfo info = await ProbeAsync(input, ct).ConfigureAwait(false);
        string ext = Path.GetExtension(output).TrimStart('.').ToLowerInvariant();
        if (end <= start) throw new ArgumentException("end must be after start");
        // A container ffmpeg can't write back (APE, DSD, MIDI…) gets a different output extension
        // from the caller: then the streams can't be copied and have to be re-encoded.
        bool reencode = precise || !string.Equals(Path.GetExtension(input), Path.GetExtension(output), StringComparison.OrdinalIgnoreCase);

        if (!removeSelection)
        {
            var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostats", "-progress", "pipe:1", "-y",
                "-ss", Seconds(start), "-i", input, "-t", Seconds(end - start), "-map", "0:v?", "-map", "0:a?" };
            if (reencode) args.AddRange(info.HasVideo ? ReencodeFor(ext, info) : AudioEncoder(ext, Quality.High));
            else args.AddRange(["-c", "copy", "-avoid_negative_ts", "make_zero"]);
            if (ext is "mp4" or "mov" or "m4a") args.AddRange(["-movflags", "+faststart"]);
            args.Add(output);
            await RunWithProgressAsync(args, end - start, progress, ct).ConfigureAwait(false);
            return;
        }

        // Remove the selection: [0, start) + [end, duration).
        TimeSpan total = info.Duration;
        bool hasHead = start > TimeSpan.FromMilliseconds(50);
        bool hasTail = end < total - TimeSpan.FromMilliseconds(50);
        if (!hasHead && !hasTail) throw new ArgumentException("the selection covers the whole file");
        if (!hasHead || !hasTail)
        {
            // Only one side is left: that's a plain trim.
            await TrimAsync(input, output, hasHead ? TimeSpan.Zero : end, hasHead ? start : total, reencode, false, progress, ct).ConfigureAwait(false);
            return;
        }

        TimeSpan keptLength = start + (total - end);
        if (reencode || !info.HasVideo)
        {
            // One pass with trim/atrim + concat: exact, re-encoded.
            string v = info.HasVideo
                ? $"[0:v]trim=0:{Seconds(start)},setpts=PTS-STARTPTS[v0];[0:v]trim=start={Seconds(end)},setpts=PTS-STARTPTS[v1];"
                : "";
            string a = info.HasAudio
                ? $"[0:a]atrim=0:{Seconds(start)},asetpts=PTS-STARTPTS[a0];[0:a]atrim=start={Seconds(end)},asetpts=PTS-STARTPTS[a1];"
                : "";
            string concat = (info.HasVideo, info.HasAudio) switch
            {
                (true, true) => "[v0][a0][v1][a1]concat=n=2:v=1:a=1[v][a]",
                (true, false) => "[v0][v1]concat=n=2:v=1:a=0[v]",
                _ => "[a0][a1]concat=n=2:v=0:a=1[a]",
            };
            var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostats", "-progress", "pipe:1", "-y",
                "-i", input, "-filter_complex", v + a + concat };
            if (info.HasVideo) args.AddRange(["-map", "[v]"]);
            if (info.HasAudio) args.AddRange(["-map", "[a]"]);
            args.AddRange(info.HasVideo ? ReencodeFor(ext, info) : AudioEncoder(ext, Quality.High));
            if (ext is "mp4" or "mov" or "m4a") args.AddRange(["-movflags", "+faststart"]);
            args.Add(output);
            await RunWithProgressAsync(args, keptLength, progress, ct).ConfigureAwait(false);
            return;
        }

        // Fast: copy both parts, then join them with the concat demuxer (no re-encode).
        string work = Path.Combine(Path.GetTempPath(), "Akses", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            string part1 = Path.Combine(work, "1." + ext), part2 = Path.Combine(work, "2." + ext);
            var p1 = new Progress<double>(f => progress?.Report(f * start / keptLength * 0.9));
            var p2 = new Progress<double>(f => progress?.Report((start + f * (total - end)) / keptLength * 0.9));
            await TrimAsync(input, part1, TimeSpan.Zero, start, false, false, p1, ct).ConfigureAwait(false);
            await TrimAsync(input, part2, end, total, false, false, p2, ct).ConfigureAwait(false);
            string list = Path.Combine(work, "list.txt");
            await File.WriteAllTextAsync(list, $"file '{part1.Replace("'", "'\\''")}'\nfile '{part2.Replace("'", "'\\''")}'\n", ct).ConfigureAwait(false);
            var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-f", "concat", "-safe", "0", "-i", list, "-c", "copy" };
            if (ext is "mp4" or "mov" or "m4a") args.AddRange(["-movflags", "+faststart"]);
            args.Add(output);
            await Tools.RunAsync(Tools.Ffmpeg, args, ct).ConfigureAwait(false);
            progress?.Report(1);
        }
        finally
        {
            OutputPaths.TryDelete(work);
        }
    }

    /// <summary>Encoder settings that keep the trimmed file in the same container, at top quality.</summary>
    private static string[] ReencodeFor(string ext, MediaInfo info)
    {
        string[] video = ext switch
        {
            "webm" => VideoEncoder("webm", Quality.High),
            "avi" => VideoEncoder("avi", Quality.High),
            _ => ["-c:v", "libx264", "-preset", "fast", "-crf", "18", "-pix_fmt", "yuv420p"],
        };
        string[] audio = info.HasAudio ? VideoAudioEncoder(ext, Quality.High) : [];
        return [.. video, .. audio];
    }

    // ---- previews --------------------------------------------------------------------------

    /// <summary>
    /// The extension a trimmed copy of <paramref name="input"/> gets: the same one, unless ffmpeg
    /// can't write that container (then FLAC for audio, MP4 for video, re-encoded).
    /// </summary>
    public static string TrimExtension(string input, bool hasVideo)
    {
        string ext = Path.GetExtension(input).TrimStart('.').ToLowerInvariant();
        return ext switch
        {
            "ape" or "dsf" or "dff" or "mid" or "midi" or "tta" or "spx" or "voc" or "gsm" or "alac" or "mpa" => "flac",
            "aac" or "m4b" => "m4a",
            "h264" or "hevc" or "y4m" or "rm" or "rmvb" or "divx" or "vob" or "dv" or "mxf" or "f4v" or "asf" => hasVideo ? "mp4" : "m4a",
            _ => ext,
        };
    }

    /// <summary>A waveform picture of the audio, for the trim timeline.</summary>
    public static Task WaveformAsync(string input, string pngOutput, int width, int height, string color, CancellationToken ct) =>
        Tools.RunAsync(Tools.Ffmpeg,
            ["-hide_banner", "-loglevel", "error", "-y", "-i", input, "-filter_complex",
             $"[0:a:0]aformat=channel_layouts=mono,showwavespic=s={width}x{height}:colors={color}:scale=sqrt", "-frames:v", "1", pngOutput], ct);

    /// <summary>One video frame at <paramref name="at"/>, scaled to <paramref name="height"/> px high.</summary>
    public static Task FrameAsync(string input, TimeSpan at, string jpgOutput, int height, CancellationToken ct) =>
        Tools.RunAsync(Tools.Ffmpeg,
            ["-hide_banner", "-loglevel", "error", "-y", "-ss", Seconds(at), "-i", input, "-frames:v", "1",
             "-vf", $"scale=-2:{height}", "-q:v", "4", jpgOutput], ct);

    // ---- plumbing --------------------------------------------------------------------------

    private static async Task RunWithProgressAsync(List<string> args, TimeSpan duration, IProgress<double>? progress, CancellationToken ct)
    {
        string output = args[^1];
        try
        {
            await Tools.RunAsync(Tools.Ffmpeg, args, ct, line =>
            {
                // -progress pipe:1 prints key=value lines; out_time_us is the position written so far.
                if (progress is null || duration <= TimeSpan.Zero || !line.StartsWith("out_time_us=", StringComparison.Ordinal)) return;
                if (long.TryParse(line.AsSpan("out_time_us=".Length), NumberStyles.Integer, Inv, out long us) && us > 0)
                    progress.Report(Math.Clamp(us / 1_000_000.0 / duration.TotalSeconds, 0, 1));
            }).ConfigureAwait(false);
        }
        catch
        {
            OutputPaths.TryDelete(output);
            throw;
        }
        progress?.Report(1);
    }

    internal static string Seconds(TimeSpan t) => t.TotalSeconds.ToString("0.000", Inv);
}
