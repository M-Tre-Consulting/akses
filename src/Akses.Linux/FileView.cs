using Akses.Core;
using Akses.Core.Engines;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Akses.App;

/// <summary>
/// The page of a file: what it is (preview + details) and everything Akses can do with it, on one
/// screen. "Converti in" is a row of one-click formats; "Modifica" holds the actions for that kind
/// of file: the instant ones run right here and land in "Risultati", the ones that need a tool
/// (trim, crop, pick pages) open it. With several files, a row of tabs picks the one shown, and
/// conversions and instant actions can apply to all the files of the same kind at once.
/// Same page as the Windows app's FileView, in Avalonia.
/// </summary>
internal sealed class FileView : UserControl
{
    private readonly List<string> _files = [];
    private int _selected;

    private readonly SpacedWrapPanel _tabs = new() { HorizontalSpacing = 6, VerticalSpacing = 6 };
    private readonly Border _thumbBox = Ui.SubtlePanel();
    private readonly TextBlock _name = new() { FontSize = 22, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _info = new() { Opacity = 0.75, TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox _applyAll = new();
    private readonly SpacedWrapPanel _formats = new();
    private readonly SpacedWrapPanel _options = new() { HorizontalSpacing = 12, VerticalSpacing = 10 };
    private readonly ComboBox _size = new() { MinWidth = 210 };
    private readonly StackPanel _sizeBox;
    private readonly TextBlock _actionsTitle = Ui.SectionTitle(Strings.EditSection);
    private readonly SpacedWrapPanel _actions = new() { HorizontalSpacing = 10, VerticalSpacing = 10 };
    private readonly TextBlock _resultsTitle = Ui.SectionTitle(Strings.ResultsSection);
    private readonly StackPanel _results = new() { Spacing = 8 };
    private readonly Button _saveTo = new() { MinWidth = 210, HorizontalContentAlignment = HorizontalAlignment.Left };
    private CancellationTokenSource? _thumbCts;
    // ffmpeg and ImageMagick are multi-threaded themselves: a few jobs at once is the sweet spot.
    private readonly SemaphoreSlim _slots = new(Math.Clamp(Environment.ProcessorCount / 2, 2, 4));

    private static readonly int?[] Sizes = [null, 3840, 1920, 1280, 800];

    public event Action? NewFileRequested;
    public event Action? SettingsRequested;
    /// <summary>A tool that needs its own screen: trim, image editor, PDF tools.</summary>
    public event Action<Tool, IReadOnlyList<string>>? ToolRequested;

    public enum Tool { Trim, Crop, Image, PdfMerge, PdfExtract, PdfRotate }

    public FileView()
    {
        var stack = new StackPanel { Spacing = 12, Margin = new Thickness(28, 4, 28, 24) };

        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var back = Ui.Link(Strings.OtherFiles, () => NewFileRequested?.Invoke(), Icons.Back);
        back.Margin = new Thickness(-6, 0, 0, 0);
        back.HorizontalAlignment = HorizontalAlignment.Left;
        top.Children.Add(back);
        var topButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var add = new Button { Content = Strings.AddFiles };
        add.Click += async (_, _) => AddFiles(await Ui.PickFilesAsync());
        topButtons.Children.Add(add);
        topButtons.Children.Add(Ui.IconButton(Icons.Settings, Strings.SettingsTitle, () => SettingsRequested?.Invoke()));
        Grid.SetColumn(topButtons, 1);
        top.Children.Add(topButtons);
        stack.Children.Add(top);

        stack.Children.Add(_tabs);

        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 4, 0, 4) };
        _thumbBox.Width = 200;
        _thumbBox.Height = 140;
        _thumbBox.Margin = new Thickness(0, 0, 20, 0);
        head.Children.Add(_thumbBox);
        var titles = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(_name);
        titles.Children.Add(_info);
        var fileLinks = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(-6, 2, 0, 0) };
        fileLinks.Children.Add(Ui.Link(Strings.OpenOriginal, () => Ui.Open(Current)));
        fileLinks.Children.Add(Ui.Link(Strings.ShowInFolder, () => Ui.ShowInFolder(Current)));
        titles.Children.Add(fileLinks);
        Grid.SetColumn(titles, 1);
        head.Children.Add(titles);
        stack.Children.Add(head);

        stack.Children.Add(Ui.SectionTitle(Strings.ConvertToSection));
        stack.Children.Add(_applyAll);
        stack.Children.Add(_formats);
        _sizeBox = Ui.Labeled(Strings.SizeLabel, _size);
        BuildOptions();
        stack.Children.Add(_options);

        stack.Children.Add(_actionsTitle);
        stack.Children.Add(_actions);

        stack.Children.Add(_resultsTitle);
        stack.Children.Add(_results);
        _resultsTitle.IsVisible = false;

        Content = new ScrollViewer { Content = stack };
        Ui.AcceptFileDrops(this, AddFiles);
    }

    private string Current => _files[_selected];
    private FileKind CurrentKind => FormatCatalog.KindOf(Current);

    /// <summary>Files of the same kind as the one shown: what "apply to all" acts on.</summary>
    private List<string> SameKind => _files.Where(f => FormatCatalog.KindOf(f) == CurrentKind).ToList();

    private List<string> Targets => _applyAll.IsChecked == true ? SameKind : [Current];

    private void BuildOptions()
    {
        var quality = new ComboBox { MinWidth = 190 };
        quality.Items.Add(Strings.QualityHigh);
        quality.Items.Add(Strings.QualityBalanced);
        quality.Items.Add(Strings.QualitySmall);
        quality.SelectedIndex = (int)Settings.Current.Quality;
        quality.SelectionChanged += (_, _) => { Settings.Current.Quality = (Quality)Math.Max(0, quality.SelectedIndex); Settings.Current.Save(); };

        foreach (int? s in Sizes) _size.Items.Add(s is int px ? Strings.MaxSide(px) : Strings.OriginalSize);
        _size.SelectedIndex = Math.Max(0, Array.IndexOf(Sizes, Settings.Current.MaxSide));
        _size.SelectionChanged += (_, _) => { Settings.Current.MaxSide = Sizes[Math.Max(0, _size.SelectedIndex)]; Settings.Current.Save(); };

        var menu = new MenuFlyout();
        var nextTo = new MenuItem { Header = Strings.SaveNextToOriginal };
        nextTo.Click += (_, _) => { Settings.Current.OutputFolder = null; Settings.Current.Save(); UpdateSaveTo(); };
        var choose = new MenuItem { Header = Strings.ChooseFolder };
        choose.Click += async (_, _) =>
        {
            if (await Ui.PickFolderAsync() is { } folder) { Settings.Current.OutputFolder = folder; Settings.Current.Save(); UpdateSaveTo(); }
        };
        menu.Items.Add(nextTo);
        menu.Items.Add(choose);
        _saveTo.Flyout = menu;
        UpdateSaveTo();

        _options.Children.Add(Ui.Labeled(Strings.QualityLabel, quality));
        _options.Children.Add(_sizeBox);
        _options.Children.Add(Ui.Labeled(Strings.SaveToLabel, _saveTo));
    }

    /// <summary>Called again whenever Impostazioni changes the output folder.</summary>
    public void UpdateSaveTo() =>
        _saveTo.Content = (Settings.Current.OutputFolder is { } f ? Path.GetFileName(f.TrimEnd('/')) : Strings.SaveNextToOriginal) + "  ▾";

    public bool HasFiles => _files.Count > 0;

    public void Clear()
    {
        _files.Clear();
        _results.Children.Clear();
        _resultsTitle.IsVisible = false;
    }

    /// <summary>Adds files and shows the first new one.</summary>
    public void AddFiles(IReadOnlyList<string> files)
    {
        int firstNew = -1;
        foreach (string f in files)
        {
            // Linux paths are case-sensitive: two names that differ only in case are two files.
            int existing = _files.FindIndex(x => string.Equals(x, f, StringComparison.Ordinal));
            if (existing >= 0) { if (firstNew < 0) firstNew = existing; continue; }
            _files.Add(f);
            if (firstNew < 0) firstNew = _files.Count - 1;
        }
        if (_files.Count == 0) return;
        Select(firstNew >= 0 ? firstNew : _selected);
    }

    /// <summary>Command line: <c>--to png</c> on these files.</summary>
    public void ConvertNow(IReadOnlyList<string> files, string targetId)
    {
        foreach (string f in files)
            if (FormatCatalog.FindTarget(FormatCatalog.KindOf(f), targetId) is { } t)
                ConvertFiles([f], t);
    }

    private void Select(int index)
    {
        _selected = Math.Clamp(index, 0, _files.Count - 1);
        RebuildTabs();
        string path = Current;
        FileKind kind = CurrentKind;
        _name.Text = Path.GetFileName(path);
        _info.Text = Strings.KindLabel(kind) + " · " + Strings.FormatSize(SafeSize(path));
        _ = LoadDetailsAsync(path, kind);

        int same = SameKind.Count;
        _applyAll.Content = Strings.ApplyToAll(same);
        _applyAll.IsChecked = same > 1;
        _applyAll.IsVisible = same > 1;
        _sizeBox.IsVisible = kind is FileKind.Image or FileKind.Video;

        RebuildFormats(path, kind);
        RebuildActions(kind);
    }

    private static long SafeSize(string path)
    {
        try { return new FileInfo(path).Length; } catch { return 0; }
    }

    private void RebuildTabs()
    {
        _tabs.Children.Clear();
        _tabs.IsVisible = _files.Count > 1;
        for (int i = 0; i < _files.Count; i++)
        {
            int index = i;
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            content.Children.Add(new Glyph(Icons.For(FormatCatalog.KindOf(_files[i])), 13) { VerticalAlignment = VerticalAlignment.Center });
            content.Children.Add(new TextBlock { Text = Path.GetFileName(_files[i]), MaxWidth = 180, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            var tab = new ToggleButton { Content = content, IsChecked = i == _selected };
            tab.Click += (_, _) => Select(index);
            _tabs.Children.Add(tab);
        }
    }

    // ---- what the file is ----------------------------------------------------------------------

    private async Task LoadDetailsAsync(string path, FileKind kind)
    {
        _thumbCts?.Cancel();
        var cts = _thumbCts = new CancellationTokenSource();
        _thumbBox.Child = new Glyph(Icons.For(kind), 44) { Opacity = 0.6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        string size = Strings.FormatSize(SafeSize(path));
        try
        {
            Bitmap? thumb = null;
            switch (kind)
            {
                case FileKind.Image:
                {
                    using ImagePreview preview = await ImagePreview.LoadAsync(path, 480, cts.Token);
                    thumb = new Bitmap(new MemoryStream(preview.Render(0, false, false).Png));
                    _info.Text = Strings.InfoImage(preview.FullWidth, preview.FullHeight, Ext(path), size);
                    break;
                }
                case FileKind.Video or FileKind.Audio:
                {
                    MediaInfo info = await MediaEngine.ProbeAsync(path, cts.Token);
                    _info.Text = info.HasVideo
                        ? Strings.InfoVideo(Ui.FormatTime(info.Duration, false), info.Width, info.Height, Ext(path), size)
                        : Strings.InfoAudio(Ui.FormatTime(info.Duration, false), Ext(path), size);
                    string file = Path.Combine(Path.GetTempPath(), "Akses", "thumbs", Guid.NewGuid().ToString("N") + ".png");
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    if (info.HasVideo) await MediaEngine.FrameAsync(path, info.Duration * 0.1, file, 280, cts.Token);
                    else await MediaEngine.WaveformAsync(path, file, 400, 280, "FF6A3D", cts.Token);
                    thumb = new Bitmap(file);
                    break;
                }
                case FileKind.Pdf:
                {
                    int pages = PdfEngine.PageCount(path);
                    _info.Text = Strings.InfoPdf(pages, size);
                    thumb = new Bitmap(new MemoryStream(await PdfEngine.RenderPageAsync(path, 0, 300)));
                    break;
                }
                default:
                    _info.Text = Strings.InfoOther(Strings.KindLabel(kind), Ext(path), size);
                    break;
            }
            if (cts.IsCancellationRequested || thumb is null) return;
            _thumbBox.Child = new Image { Source = thumb, Stretch = kind == FileKind.Pdf ? Stretch.Uniform : Stretch.UniformToFill };
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested) _info.Text = Ui.Describe(ex);
        }
    }

    private static string Ext(string path) => Path.GetExtension(path).TrimStart('.').ToUpperInvariant();

    // ---- what you can do with it ---------------------------------------------------------------

    private void RebuildFormats(string path, FileKind kind)
    {
        _formats.Children.Clear();
        if (kind == FileKind.Unknown)
        {
            _formats.Children.Add(new TextBlock { Text = Strings.UnsupportedFormat, Opacity = 0.75 });
            _options.IsVisible = false;
            return;
        }
        _options.IsVisible = true;
        bool animated = kind == FileKind.Image && Path.GetExtension(path).ToLowerInvariant() is ".gif" or ".webp" or ".apng" && ImageEngine.IsAnimated(path);
        foreach (TargetFormat t in FormatCatalog.TargetsFor(path).Where(t => kind != FileKind.Image || t.Id is not ("mp4" or "webm") || animated))
        {
            var chip = new Button { Content = Strings.TargetLabel(kind, t), MinWidth = 64, HorizontalContentAlignment = HorizontalAlignment.Center };
            chip.Click += (_, _) => ConvertFiles(Targets, t);
            _formats.Children.Add(chip);
        }
    }

    private void RebuildActions(FileKind kind)
    {
        _actions.Children.Clear();
        switch (kind)
        {
            case FileKind.Image:
                Add(Icons.Crop, Strings.ActCrop, Strings.ActCropText, () => OpenTool(Tool.Crop));
                Add(Icons.Resize, Strings.ActResize, Strings.ActResizeText, () => OpenTool(Tool.Image));
                Add(Icons.RotateRight, Strings.ActRotate, Strings.ActRotateText, () => OpenTool(Tool.Image));
                Add(Icons.Compress, Strings.ActCompress, Strings.ActCompressImageText, () => RunOnTargets(Strings.ActCompress, CompressImageAsync));
                Add(Icons.Shield, Strings.ActStrip, Strings.ActStripText, () => RunOnTargets(Strings.ActStrip, StripAsync));
                Add(Icons.Document, Strings.ActMakePdf, Strings.ActMakePdfText, () => ToolRequested?.Invoke(Tool.PdfMerge, PdfMergeCandidates()));
                break;
            case FileKind.Video:
                Add(Icons.Scissors, Strings.ActTrim, Strings.ActTrimText, () => OpenTool(Tool.Trim));
                Add(Icons.Audio, Strings.ActExtractAudio, Strings.ActExtractAudioText, () => ConvertFiles(Targets, FormatCatalog.Mp3));
                Add(Icons.Film, Strings.ActGif, Strings.ActGifText, () => ConvertFiles(Targets, FormatCatalog.Gif));
                Add(Icons.Compress, Strings.ActCompress, Strings.ActCompressVideoText, () => RunOnTargets(Strings.ActCompress, CompressVideoAsync));
                Add(Icons.Mute, Strings.ActRemoveAudio, Strings.ActRemoveAudioText, () => RunOnTargets(Strings.ActRemoveAudio, RemoveAudioAsync));
                break;
            case FileKind.Audio:
                Add(Icons.Scissors, Strings.ActTrim, Strings.ActTrimText, () => OpenTool(Tool.Trim));
                Add(Icons.Compress, Strings.ActCompress, Strings.ActCompressAudioText, () => RunOnTargets(Strings.ActCompress, CompressAudioAsync));
                break;
            case FileKind.Pdf:
                Add(Icons.Split, Strings.ActSplit, Strings.ActSplitText, () => RunOnTargets(Strings.ActSplit, SplitAsync));
                Add(Icons.ExtractPages, Strings.ActExtractPages, Strings.ActExtractPagesText, () => OpenTool(Tool.PdfExtract));
                Add(Icons.RotateRight, Strings.ActRotatePages, Strings.ActRotatePagesText, () => OpenTool(Tool.PdfRotate));
                Add(Icons.Merge, Strings.ActMerge, Strings.ActMergeText, () => ToolRequested?.Invoke(Tool.PdfMerge, PdfMergeCandidates()));
                Add(Icons.Image, Strings.ActPdfImages, Strings.ActPdfImagesText, () => ConvertFiles(Targets, FormatCatalog.PngPages));
                Add(Icons.Text, Strings.ActPdfText, Strings.ActPdfTextText, () => ConvertFiles(Targets, FormatCatalog.Txt));
                break;
        }
        _actionsTitle.IsVisible = _actions.IsVisible = _actions.Children.Count > 0;

        void Add(string icon, string title, string text, Action onClick) => _actions.Children.Add(ActionCard(icon, title, text, onClick));
    }

    private void OpenTool(Tool tool) => ToolRequested?.Invoke(tool, tool is Tool.PdfExtract or Tool.PdfRotate ? Targets : [Current]);

    /// <summary>All the PDFs and images in the list, the one shown first: what "unisci" starts from.</summary>
    private List<string> PdfMergeCandidates()
    {
        var list = _files.Where(f => FormatCatalog.KindOf(f) is FileKind.Pdf or FileKind.Image).ToList();
        list.Remove(Current);
        list.Insert(0, Current);
        return list;
    }

    private static Button ActionCard(string icon, string title, string text, Action onClick)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Width = 250 };
        grid.Children.Add(new Glyph(icon, 20) { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 12, 0) }
            .Res(Glyph.ForegroundProperty, Palette.AccentText));
        var texts = new StackPanel { Spacing = 2 };
        texts.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold });
        texts.Children.Add(new TextBlock { Text = text, FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);
        var button = new Button
        {
            Content = grid,
            MinHeight = 74,
            Padding = new Thickness(14, 12),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        Avalonia.Automation.AutomationProperties.SetName(button, title);
        button.Click += (_, _) => onClick();
        return button;
    }

    // ---- instant actions -----------------------------------------------------------------------

    private static string OutputFor(string input, string extension, string suffix) =>
        OutputPaths.Unique(OutputPaths.FolderFor(input, Settings.Current.OutputFolder), Path.GetFileNameWithoutExtension(input), extension, suffix);

    /// <summary>Same format (JPG for those that can't be written back), lighter.</summary>
    private static TargetFormat WritableImageFormat(string input)
    {
        string ext = Path.GetExtension(input).TrimStart('.').ToLowerInvariant();
        return FormatCatalog.TargetsFor(FileKind.Image).FirstOrDefault(t => t.Id != "pdf" && FormatCatalog.IsSameFormat(t, ext)) ?? FormatCatalog.Jpg;
    }

    private static async Task<string> CompressImageAsync(string input, IProgress<double> p, CancellationToken ct)
    {
        TargetFormat t = WritableImageFormat(input);
        // PNG/BMP/TIFF are lossless: "lighter" for them means WebP.
        if (t.Id is "png" or "bmp" or "tiff") t = FormatCatalog.Webp;
        string output = OutputFor(input, t.Extension, Strings.CompressedSuffix);
        await ImageEngine.ConvertAsync(input, output, t, Quality.Balanced, 2560, ct);
        return output;
    }

    private static async Task<string> StripAsync(string input, IProgress<double> p, CancellationToken ct)
    {
        TargetFormat t = WritableImageFormat(input);
        string output = OutputFor(input, t.Extension, Strings.CleanSuffix);
        await ImageEngine.StripMetadataAsync(input, output, t, ct);
        return output;
    }

    private static async Task<string> CompressVideoAsync(string input, IProgress<double> p, CancellationToken ct)
    {
        string output = OutputFor(input, "mp4", Strings.CompressedSuffix);
        await MediaEngine.ConvertAsync(input, output, FormatCatalog.Mp4, Quality.Small, 1280, p, ct);
        return output;
    }

    private static async Task<string> CompressAudioAsync(string input, IProgress<double> p, CancellationToken ct)
    {
        string output = OutputFor(input, "mp3", Strings.CompressedSuffix);
        await MediaEngine.ConvertAsync(input, output, FormatCatalog.Mp3, Quality.Small, null, p, ct);
        return output;
    }

    private static async Task<string> RemoveAudioAsync(string input, IProgress<double> p, CancellationToken ct)
    {
        string output = OutputFor(input, MediaEngine.TrimExtension(input, hasVideo: true), Strings.NoAudioSuffix);
        await MediaEngine.RemoveAudioAsync(input, output, ct);
        return output;
    }

    private static async Task<string> SplitAsync(string input, IProgress<double> p, CancellationToken ct)
    {
        string folder = OutputPaths.UniqueFolder(OutputPaths.FolderFor(input, Settings.Current.OutputFolder), Path.GetFileNameWithoutExtension(input) + Strings.PagesSuffix);
        await PdfEngine.SplitAsync(input, folder, p, ct);
        return folder;
    }

    // ---- jobs ----------------------------------------------------------------------------------

    private async void ConvertFiles(IReadOnlyList<string> files, TargetFormat target)
    {
        if (files.Any(f => DocumentEngine.NeedsPandoc(f, target)) && !ToolDownloads.PandocInstalled && !await DownloadPandocAsync())
            return;
        foreach (string f in files)
        {
            FileKind kind = FormatCatalog.KindOf(f);
            RunJob(f, "→ " + Strings.TargetLabel(kind, target), Strings.ConvertingTo(target.Label),
                (p, ct) => Converter.ConvertAsync(f, target, Settings.Current.ToOptions(), p, ct));
        }
    }

    /// <summary>
    /// First document conversion that needs pandoc: asks whether to download it (see ToolDownloads),
    /// then shows the download with a Cancel button. True when pandoc is ready.
    /// </summary>
    private static async Task<bool> DownloadPandocAsync()
    {
        if (!await Dialogs.ShowAsync(Strings.PandocTitle, Strings.PandocBody(ToolDownloads.PandocDownloadMb), Strings.Cancel, Strings.Download))
            return false;

        var bar = new ProgressBar { Maximum = 100, Margin = new Thickness(0, 12, 0, 0) };
        var text = new TextBlock { Text = Strings.PandocDownloading, TextWrapping = TextWrapping.Wrap };
        var body = new StackPanel();
        body.Children.Add(text);
        body.Children.Add(bar);
        var cts = new CancellationTokenSource();
        Dialogs.Handle dialog = Dialogs.Show(Strings.PandocTitle, body, Strings.Cancel);
        _ = dialog.Task.ContinueWith(_ => cts.Cancel(), TaskScheduler.Default);

        try
        {
            await ToolDownloads.InstallPandocAsync(new Progress<double>(p => bar.Value = p * 100), cts.Token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            AppLog.Write($"Download di pandoc fallito: {ex}");
            text.Text = Strings.PandocFailed;
            bar.IsVisible = false;
            dialog.SetCloseText(Strings.Close);
            await dialog.Task;
            return false;
        }
        dialog.Hide();
        return true;
    }

    private void RunOnTargets(string label, Func<string, IProgress<double>, CancellationToken, Task<string>> work)
    {
        foreach (string f in Targets) RunJob(f, label, Strings.Working, (p, ct) => work(f, p, ct));
    }

    /// <summary>One job = one card in "Risultati", newest on top, with progress, Apri and Mostra.</summary>
    private async void RunJob(string source, string label, string runningText, Func<IProgress<double>, CancellationToken, Task<string>> work)
    {
        var item = new FileItem(source) { Details = label };
        var card = new FileCard(item);
        var cts = new CancellationTokenSource();
        item.Cancellation = cts;
        card.RemoveRequested += c =>
        {
            cts.Cancel();
            _results.Children.Remove(c);
            _resultsTitle.IsVisible = _results.Children.Count > 0;
        };
        _results.Children.Insert(0, card);
        _resultsTitle.IsVisible = true;
        // The newest result is at the top of "Risultati": scroll so it's in view.
        Avalonia.Threading.Dispatcher.UIThread.Post(() => card.BringIntoView(), Avalonia.Threading.DispatcherPriority.Background);
        item.SetQueued(Strings.Waiting);

        try { await _slots.WaitAsync(cts.Token); }
        catch (OperationCanceledException) { item.SetCancelled(); return; }
        try
        {
            item.SetRunning(runningText);
            string output = await work(new Progress<double>(item.SetProgress), cts.Token);
            item.SetDone(output);
        }
        catch (OperationCanceledException) { item.SetCancelled(); }
        catch (Exception ex) { item.SetError(Ui.Describe(ex)); }
        finally { _slots.Release(); }
    }
}
