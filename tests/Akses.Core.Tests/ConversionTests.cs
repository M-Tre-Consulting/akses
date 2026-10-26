using Akses.Core;
using Akses.Core.Engines;
using ImageMagick;

namespace Akses.Core.Tests;

/// <summary>
/// End-to-end conversions on files generated on the fly, with the real ffmpeg/pandoc from tools\
/// (run installer\fetch-tools.ps1 once first).
/// </summary>
public sealed class ConversionTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("akses-conv").FullName;
    private static readonly ConversionOptions Defaults = new();

    public void Dispose() => Directory.Delete(_dir, true);

    private string Image(string name)
    {
        string path = Path.Combine(_dir, name);
        using var img = new MagickImage("gradient:#ff6a3d-#ffb224", 64, 48);
        img.Write(path);
        return path;
    }

    /// <summary>
    /// A test clip: a moving gradient (raw RGB frames) and a 440 Hz tone (WAV), both written here and
    /// encoded by the shipped ffmpeg. The slim build has no lavfi test sources, on purpose.
    /// </summary>
    private async Task<string> Media(string name, bool video, double seconds = 3, int w = 160, int h = 120, bool audio = true)
    {
        const int fps = 25, rate = 44100;
        string path = Path.Combine(_dir, name);
        string wav = Path.Combine(_dir, Path.GetFileNameWithoutExtension(name) + "-src.wav");
        int samples = (int)(seconds * rate);
        using (var bw = new BinaryWriter(File.Create(wav)))
        {
            bw.Write("RIFF"u8); bw.Write(36 + samples * 2); bw.Write("WAVEfmt "u8); bw.Write(16);
            bw.Write((short)1); bw.Write((short)1); bw.Write(rate); bw.Write(rate * 2); bw.Write((short)2); bw.Write((short)16);
            bw.Write("data"u8); bw.Write(samples * 2);
            for (int i = 0; i < samples; i++) bw.Write((short)(Math.Sin(2 * Math.PI * 440 * i / rate) * 8000));
        }

        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y" };
        if (video)
        {
            string raw = Path.Combine(_dir, Path.GetFileNameWithoutExtension(name) + "-src.rgb");
            int frames = (int)(seconds * fps);
            var frame = new byte[w * h * 3];
            using (FileStream fs = File.Create(raw))
            {
                for (int f = 0; f < frames; f++)
                {
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            int o = (y * w + x) * 3;
                            frame[o] = (byte)(x + f * 4); frame[o + 1] = (byte)(y * 2); frame[o + 2] = (byte)(f * 6);
                        }
                    fs.Write(frame);
                }
            }
            args.AddRange(["-f", "rawvideo", "-pix_fmt", "rgb24", "-s", $"{w}x{h}", "-r", $"{fps}", "-i", raw]);
        }
        if (audio) args.AddRange(["-i", wav]);
        if (video) args.AddRange(["-c:v", "libx264", "-pix_fmt", "yuv420p", "-g", "25"]);
        if (video && audio) args.AddRange(["-c:a", "aac", "-shortest"]);
        args.Add(path);
        await Tools.RunAsync(Tools.Ffmpeg, args, CancellationToken.None);
        return path;
    }

    [Theory]
    [InlineData("in.webp", "jpg")]
    [InlineData("in.png", "webp")]
    [InlineData("in.jpg", "ico")]
    [InlineData("in.png", "avif")]
    [InlineData("in.bmp", "pdf")]
    [InlineData("in.tiff", "png")]
    public async Task Images(string input, string target)
    {
        string src = Image(input);
        string output = await Converter.ConvertAsync(src, FormatCatalog.FindTarget(FileKind.Image, target)!, Defaults, null, CancellationToken.None);
        Assert.True(new FileInfo(output).Length > 0);
        Assert.EndsWith("." + target, output);
    }

    [Fact]
    public async Task Image_max_side_only_shrinks()
    {
        string src = Image("big.png");
        string output = await Converter.ConvertAsync(src, FormatCatalog.Jpg, Defaults with { MaxSide = 32 }, null, CancellationToken.None);
        using var img = new MagickImage(output);
        Assert.Equal(32u, img.Width);
    }

    [Fact]
    public async Task Video_to_mp3_and_gif_and_webm()
    {
        string src = await Media("clip.mp4", video: true);
        double last = 0;
        var progress = new Progress<double>(p => last = p);
        foreach (TargetFormat t in new[] { FormatCatalog.Mp3, FormatCatalog.Gif, FormatCatalog.Webm, FormatCatalog.Mkv })
        {
            string output = await Converter.ConvertAsync(src, t, Defaults, progress, CancellationToken.None);
            Assert.True(new FileInfo(output).Length > 0, t.Id);
        }
        MediaInfo mp3 = await MediaEngine.ProbeAsync(Path.Combine(_dir, "clip.mp3"));
        Assert.True(mp3.HasAudio && !mp3.HasVideo);
    }

    [Fact]
    public async Task Audio_formats()
    {
        string src = await Media("tone.wav", video: false);
        foreach (string id in new[] { "mp3", "m4a", "flac", "ogg", "opus", "aiff" })
        {
            string output = await Converter.ConvertAsync(src, FormatCatalog.FindTarget(FileKind.Audio, id)!, Defaults with { Quality = Quality.Small }, null, CancellationToken.None);
            Assert.True((await MediaEngine.ProbeAsync(output)).Duration > TimeSpan.FromSeconds(2.5), id);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Trim_keep_and_remove(bool precise, bool remove)
    {
        string src = await Media($"trim-{precise}-{remove}.mp4", video: true, seconds: 6);
        string output = Path.Combine(_dir, $"out-{precise}-{remove}.mp4");
        await MediaEngine.TrimAsync(src, output, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), precise, remove, null, CancellationToken.None);
        double seconds = (await MediaEngine.ProbeAsync(output)).Duration.TotalSeconds;
        Assert.InRange(seconds, remove ? 3.5 : 1.5, remove ? 4.6 : 2.6);
    }

    [Fact]
    public async Task Join_alike_videos_by_copy()
    {
        string a = await Media("join-a.mp4", video: true, seconds: 2), b = await Media("join-b.mp4", video: true, seconds: 3);
        Assert.Equal("mp4", MediaEngine.JoinExtension([a, b], video: true));
        string output = Path.Combine(_dir, "joined.mp4");
        await MediaEngine.JoinAsync([a, b], output, null, CancellationToken.None);
        MediaInfo info = await MediaEngine.ProbeAsync(output);
        Assert.True(info.HasVideo && info.HasAudio);
        Assert.InRange(info.Duration.TotalSeconds, 4.6, 5.5);
    }

    [Fact]
    public async Task Join_different_videos_reencodes_to_the_first()
    {
        string a = await Media("big.mp4", video: true, seconds: 2, w: 320, h: 180);
        string b = await Media("small-silent.mp4", video: true, seconds: 2, w: 120, h: 160, audio: false);
        string c = await Media("other.mkv", video: true, seconds: 1);
        Assert.Equal("mp4", MediaEngine.JoinExtension([a, b, c], video: true));
        string output = Path.Combine(_dir, "joined-mixed.mp4");
        await MediaEngine.JoinAsync([a, b, c], output, null, CancellationToken.None);
        MediaInfo info = await MediaEngine.ProbeAsync(output);
        Assert.Equal((320, 180), (info.Width, info.Height));
        Assert.True(info.HasAudio);
        Assert.InRange(info.Duration.TotalSeconds, 4.6, 5.5);
    }

    [Fact]
    public async Task Join_follows_the_rotation_of_a_phone_video()
    {
        string landscape = await Media("land.mp4", video: true, seconds: 1);
        string rotated = Path.Combine(_dir, "portrait.mp4");
        await Tools.RunAsync(Tools.Ffmpeg, ["-hide_banner", "-loglevel", "error", "-y", "-display_rotation", "90", "-i", landscape, "-c", "copy", rotated], CancellationToken.None);
        MediaInfo portrait = await MediaEngine.ProbeAsync(rotated);
        Assert.Equal((120, 160), (portrait.Width, portrait.Height));
        string output = Path.Combine(_dir, "joined-portrait.mp4");
        await MediaEngine.JoinAsync([rotated, landscape], output, null, CancellationToken.None);
        MediaInfo info = await MediaEngine.ProbeAsync(output);
        Assert.Equal((120, 160), (info.Width, info.Height));
    }

    [Fact]
    public async Task Join_audio()
    {
        string a = await Media("tone-a.wav", video: false, seconds: 2), b = await Media("tone-b.wav", video: false, seconds: 1);
        string mp3 = await Converter.ConvertAsync(b, FormatCatalog.Mp3, Defaults, null, CancellationToken.None);
        Assert.Equal("m4a", MediaEngine.JoinExtension([a, mp3], video: false));
        string output = Path.Combine(_dir, "joined.m4a");
        await MediaEngine.JoinAsync([a, mp3], output, null, CancellationToken.None);
        MediaInfo info = await MediaEngine.ProbeAsync(output);
        Assert.True(info.HasAudio && !info.HasVideo);
        Assert.InRange(info.Duration.TotalSeconds, 2.8, 3.3);
    }

    [Fact]
    public async Task Pdf_merge_split_extract_rotate_images_text()
    {
        string a = await Converter.ConvertAsync(Image("a.png"), FormatCatalog.Pdf, Defaults, null, CancellationToken.None);
        string merged = Path.Combine(_dir, "merged.pdf");
        await PdfEngine.MergeAsync([a, Image("b.jpg"), a], merged, null, CancellationToken.None);
        Assert.Equal(3, PdfEngine.PageCount(merged));

        string split = Path.Combine(_dir, "split");
        await PdfEngine.SplitAsync(merged, split, null, CancellationToken.None);
        Assert.Equal(3, Directory.GetFiles(split, "*.pdf").Length);

        string extracted = Path.Combine(_dir, "extract.pdf");
        await PdfEngine.ExtractAsync(merged, extracted, [3, 1], CancellationToken.None);
        Assert.Equal(2, PdfEngine.PageCount(extracted));

        string rotated = Path.Combine(_dir, "rotated.pdf");
        await PdfEngine.RotateAsync(merged, rotated, 90, null, CancellationToken.None);
        Assert.Equal(3, PdfEngine.PageCount(rotated));

        string pages = await Converter.ConvertAsync(merged, FormatCatalog.PngPages, Defaults, null, CancellationToken.None);
        Assert.Equal(3, Directory.GetFiles(pages, "*.png").Length);
    }

    [Fact]
    public async Task Documents_through_pandoc()
    {
        string md = Path.Combine(_dir, "note.md");
        await File.WriteAllTextAsync(md, "# Titolo\n\nCiao **mondo**, città.\n\n| a | b |\n|---|---|\n| 1 | 2 |\n");
        foreach (string id in new[] { "docx", "odt", "html", "txt", "rtf", "epub" })
        {
            string output = await Converter.ConvertAsync(md, FormatCatalog.FindTarget(FileKind.Document, id)!, Defaults, null, CancellationToken.None);
            Assert.True(new FileInfo(output).Length > 0, id);
        }
        // and back: docx → markdown
        string back = await Converter.ConvertAsync(Path.Combine(_dir, "note.docx"), FormatCatalog.Md, Defaults, null, CancellationToken.None);
        Assert.Contains("mondo", await File.ReadAllTextAsync(back));
    }

    [Fact]
    public async Task Data_round_trip()
    {
        string json = Path.Combine(_dir, "people.json");
        await File.WriteAllTextAsync(json, """{"people":[{"name":"Anna","age":30,"address":{"city":"Savona"}},{"name":"Luca","age":25}]}""");
        string csv = await Converter.ConvertAsync(json, FormatCatalog.Csv, Defaults, null, CancellationToken.None);
        string text = await File.ReadAllTextAsync(csv);
        Assert.StartsWith("name,age,address.city", text.TrimStart('﻿'));
        foreach (TargetFormat t in new[] { FormatCatalog.Yaml, FormatCatalog.Xml })
            Assert.True(new FileInfo(await Converter.ConvertAsync(json, t, Defaults, null, CancellationToken.None)).Length > 0);
        string xmlBack = await Converter.ConvertAsync(Path.Combine(_dir, "people.xml"), FormatCatalog.Json, Defaults, null, CancellationToken.None);
        Assert.Contains("Savona", await File.ReadAllTextAsync(xmlBack));
    }

    [Fact]
    public async Task Remove_audio_strip_metadata_render_page()
    {
        string video = await Media("talk.mp4", video: true);
        string mute = Path.Combine(_dir, "mute.mp4");
        await MediaEngine.RemoveAudioAsync(video, mute, CancellationToken.None);
        MediaInfo info = await MediaEngine.ProbeAsync(mute);
        Assert.True(info.HasVideo && !info.HasAudio);

        string photo = Path.Combine(_dir, "gps.jpg");
        using (var img = new MagickImage("gradient:red-blue", 40, 30))
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Software, "Akses test");
            img.SetProfile(exif);
            img.Write(photo);
        }
        string clean = Path.Combine(_dir, "clean.jpg");
        await ImageEngine.StripMetadataAsync(photo, clean, FormatCatalog.Jpg, CancellationToken.None);
        using (var img = new MagickImage(clean)) Assert.Null(img.GetExifProfile());

        string pdf = await Converter.ConvertAsync(Image("page.png"), FormatCatalog.Pdf, Defaults, null, CancellationToken.None);
        byte[] png = await PdfEngine.RenderPageAsync(pdf, 0, 120);
        using (var page = new MagickImage(png)) Assert.Equal(120u, page.Width);
    }

    [Fact]
    public async Task Image_edit_rotate_crop_resize()
    {
        string src = Image("edit.png"); // 64x48
        string output = Path.Combine(_dir, "edited.png");
        var edit = new ImageEdit { Rotation = 90, Crop = (0, 0, 40, 40), Resize = (20, 20) };
        await ImageEngine.EditAsync(src, output, edit, FormatCatalog.Png, Quality.High, CancellationToken.None);
        using var img = new MagickImage(output);
        Assert.Equal((20u, 20u), (img.Width, img.Height));

        using ImagePreview preview = await ImagePreview.LoadAsync(src, 32);
        var (_, w, h) = preview.Render(90, false, false);
        Assert.Equal((48, 64), (w, h));
    }
}
