using Akses.Core;
using Akses.Core.Engines;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Akses.App;

/// <summary>
/// Cut audio and video: drag the two handles (or type the times), listen, and "Taglia". By default
/// the streams are copied, so even a two-hour film is cut in a second; "Taglio preciso" re-encodes
/// for a frame-exact video cut. Can also remove the selected part and join what's left.
/// Same page as the Windows app's TrimPage; playback goes through <see cref="VideoPlayer"/> (VLC),
/// and while it isn't playing the video shows ffmpeg's frame at the playhead.
/// </summary>
internal sealed class TrimPage : UserControl
{
    /// <summary>"← Indietro": back to the file's page.</summary>
    public event Action? BackRequested;

    private readonly Grid _dropZone;
    private readonly Grid _editor = new();
    private readonly TextBlock _fileName = new() { FontSize = 16, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _fileInfo = new() { Opacity = 0.7, FontSize = 12 };
    private readonly Image _video = new() { Stretch = Stretch.Uniform };
    private readonly Image _frame = new() { Stretch = Stretch.Uniform };
    private readonly Image _wave = new() { Stretch = Stretch.Fill, Margin = new Thickness(0, 20) };
    private readonly TextBlock _noPreview = new() { Opacity = 0.7, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(20, 0, 20, 6) };
    private readonly RangeTrack _track = new();
    private readonly TextBox _startBox = new() { Width = 120 }, _endBox = new() { Width = 120 };
    private readonly TextBlock _position = new() { FontFamily = new FontFamily("monospace"), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _length = new() { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold };
    private readonly Button _play = new() { Padding = new Thickness(10, 7) };
    private readonly RadioButton _keep = new() { GroupName = "trim-mode", IsChecked = true };
    private readonly RadioButton _remove = new() { GroupName = "trim-mode" };
    private readonly ToggleSwitch _precise = new();
    private readonly Button _cut = Ui.AccentButton(Strings.TrimButton);
    private readonly ProgressBar _progress = new() { Maximum = 100, Width = 220, IsVisible = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _resultButtons = new() { Orientation = Orientation.Horizontal, Spacing = 6, IsVisible = false };

    private VideoPlayer? _player;
    private string? _path, _output;
    private MediaInfo? _info;
    private bool _playSelectionOnly, _live;
    private CancellationTokenSource? _loadCts, _cutCts, _frameCts;

    public TrimPage()
    {
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Margin = new Thickness(28, 4, 28, 16) };
        root.Children.Add(Ui.EditorHeader(Strings.TrimTitle, Strings.TrimSubtitle, () => BackRequested?.Invoke()));

        _dropZone = Ui.DropZone(Strings.TrimDropTitle, Strings.TrimDropSubtitle, Strings.ChooseFile, PickAsync);
        Grid.SetRow(_dropZone, 1);
        root.Children.Add(_dropZone);

        BuildEditor();
        Grid.SetRow(_editor, 1);
        root.Children.Add(_editor);
        _editor.IsVisible = false;

        Content = root;
        Ui.AcceptFileDrops(root, files => { if (files.Count > 0) Load(files[0]); });
    }

    private void BuildEditor()
    {
        // file, preview, transport, track, times, options, action
        _editor.RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto,Auto,Auto,Auto");

        var fileRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var names = new StackPanel();
        names.Children.Add(_fileName);
        names.Children.Add(_fileInfo);
        fileRow.Children.Add(names);
        var change = new Button { Content = Strings.ChangeFile };
        change.Click += async (_, _) => await PickAsync();
        Grid.SetColumn(change, 1);
        fileRow.Children.Add(change);
        Row(fileRow, 0);

        var preview = new Panel { MinHeight = 160 };
        preview.Children.Add(_wave);
        preview.Children.Add(_frame);
        preview.Children.Add(_video);
        preview.Children.Add(_noPreview);
        Row(Ui.SubtlePanel(preview), 1);

        var transport = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        _play.Click += (_, _) => TogglePlay(false);
        var playSelection = new Button { Content = Strings.PlaySelection };
        playSelection.Click += (_, _) => TogglePlay(true);
        transport.Children.Add(_play);
        transport.Children.Add(playSelection);
        transport.Children.Add(_position);
        UpdatePlayGlyph();
        Row(transport, 2);

        _track.RangeChanged += UpdateTimes;
        _track.SeekRequested += Seek;
        Row(_track, 3);

        var times = new SpacedWrapPanel { HorizontalSpacing = 10, VerticalSpacing = 8 };
        times.Children.Add(Labeled(Strings.StartLabel, _startBox));
        times.Children.Add(Ui.TextButton(Strings.SetStartHere, Icons.StartHere, () => SetRange(_track.Position, _track.End)));
        times.Children.Add(Labeled(Strings.EndLabel, _endBox));
        times.Children.Add(Ui.TextButton(Strings.SetEndHere, Icons.EndHere, () => SetRange(_track.Start, _track.Position)));
        times.Children.Add(_length);
        foreach (TextBox box in new[] { _startBox, _endBox })
        {
            box.LostFocus += (_, _) => ApplyTypedTimes();
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { ApplyTypedTimes(); e.Handled = true; } };
        }
        Row(times, 4);

        _keep.Content = Strings.KeepSelection;
        _remove.Content = Strings.RemoveSelection;
        _keep.IsCheckedChanged += (_, _) => UpdateTimes();
        _precise.OnContent = _precise.OffContent = Strings.PreciseCut;
        var modes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        modes.Children.Add(_keep);
        modes.Children.Add(_remove);
        var options = new SpacedWrapPanel { HorizontalSpacing = 28, VerticalSpacing = 6 };
        options.Children.Add(modes);
        options.Children.Add(_precise);
        Row(options, 5);

        _cut.Click += async (_, _) => await CutAsync();
        _resultButtons.Children.Add(Ui.IconButton(Icons.Open, Strings.Open, () => { if (_output is not null) Ui.Open(_output); }));
        _resultButtons.Children.Add(Ui.IconButton(Icons.Folder, Strings.ShowInFolder, () => { if (_output is not null) Ui.ShowInFolder(_output); }));
        var action = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        action.Children.Add(_cut);
        action.Children.Add(_progress);
        action.Children.Add(_status);
        action.Children.Add(_resultButtons);
        Row(action, 6);

        void Row(Control c, int row)
        {
            c.Margin = new Thickness(c.Margin.Left, row == 0 ? 0 : 12, c.Margin.Right, c.Margin.Bottom);
            Grid.SetRow(c, row);
            _editor.Children.Add(c);
        }
    }

    private static StackPanel Labeled(string label, Control control)
    {
        var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        s.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        s.Children.Add(control);
        return s;
    }

    private async Task PickAsync()
    {
        IReadOnlyList<string> files = await Ui.PickFilesAsync(multiple: false);
        if (files.Count > 0) Load(files[0]);
    }

    public void PausePlayback() => _player?.Pause();

    /// <summary>Space bar (MainWindow), when no text box has the focus.</summary>
    public void TogglePlayFromKeyboard() => TogglePlay(false);

    /// <summary>The window is closing: stop VLC before the process goes.</summary>
    public void Shutdown() => _player?.Dispose();

    private VideoPlayer Player()
    {
        if (_player is not null) return _player;
        _player = new VideoPlayer();
        _player.FrameReady += () =>
        {
            if (!_live) return;
            if (_video.Source != _player.Frame) _video.Source = _player.Frame;
            _video.InvalidateVisual();
        };
        _player.PositionChanged += OnPlayerPosition;
        _player.StateChanged += UpdatePlayGlyph;
        _player.Failed += message =>
        {
            AppLog.Write($"Anteprima non riproducibile ({_path}): {message}");
            _live = false;
            ShowPreviewMode();
            UpdatePlayGlyph();
        };
        return _player;
    }

    // ---- loading ---------------------------------------------------------------------------

    public async void Load(string path)
    {
        FileKind kind = FormatCatalog.KindOf(path);
        bool animatedImage = kind == FileKind.Image && Path.GetExtension(path).ToLowerInvariant() is ".gif";
        if (kind is not (FileKind.Video or FileKind.Audio) && !animatedImage)
        {
            await Dialogs.ShowAsync(Strings.NotMediaTitle, Strings.NotMediaBody(Path.GetFileName(path)), Strings.Ok);
            return;
        }

        _loadCts?.Cancel();
        _cutCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        _path = path;
        _output = null;
        _info = null;
        _live = false;
        _resultButtons.IsVisible = _progress.IsVisible = false;
        _status.Text = "";
        _fileName.Text = Path.GetFileName(path);
        _fileInfo.Text = Strings.Loading;
        _dropZone.IsVisible = false;
        _editor.IsVisible = true;
        _track.SetStrip(null);
        _wave.Source = null;
        _frame.Source = null;
        _video.Source = null;
        _cut.IsEnabled = false;

        try
        {
            MediaInfo info = await MediaEngine.ProbeAsync(path, cts.Token);
            if (cts.IsCancellationRequested) return;
            if (info.Duration <= TimeSpan.Zero || (!info.HasAudio && !info.HasVideo)) throw new InvalidDataException("no media streams");
            _info = info;
            _fileInfo.Text = Strings.MediaInfoLine(info);
            _precise.IsVisible = info.HasVideo;
            _track.SetRange(info.Duration.TotalSeconds, 0, info.Duration.TotalSeconds);
            _track.SetPosition(0);
            UpdateTimes();
            _cut.IsEnabled = true;

            Player().Open(path);
            ShowPreviewMode();
            UpdatePlayGlyph();

            if (info.HasVideo) await LoadThumbnailsAsync(path, info, cts.Token);
            else await LoadWaveformAsync(path, cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (cts.IsCancellationRequested) return;
            _fileInfo.Text = Ui.Describe(ex);
        }
    }

    private void ShowPreviewMode()
    {
        bool video = _info?.HasVideo == true;
        _video.IsVisible = video && _live;
        _frame.IsVisible = video && !_live;
        _wave.IsVisible = !video;
        _noPreview.IsVisible = _info is not null && !video && _player?.Available != true;
        _noPreview.Text = Strings.NoAudioPreview;
    }

    private async Task LoadThumbnailsAsync(string path, MediaInfo info, CancellationToken ct)
    {
        const int count = 10;
        string folder = TempFolder();
        double step = info.Duration.TotalSeconds / count;
        var tasks = Enumerable.Range(0, count).Select(async i =>
        {
            string file = Path.Combine(folder, $"{i}.jpg");
            try { await MediaEngine.FrameAsync(path, TimeSpan.FromSeconds(step * (i + 0.5)), file, 90, ct); return file; }
            catch { return null; }
        }).ToList();
        string?[] frames = await Task.WhenAll(tasks);
        if (ct.IsCancellationRequested) return;

        var strip = new Grid();
        for (int i = 0; i < count; i++)
        {
            strip.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            if (frames[i] is not { } f || !File.Exists(f)) continue;
            var img = new Image { Stretch = Stretch.UniformToFill, Source = new Bitmap(f) };
            Grid.SetColumn(img, i);
            strip.Children.Add(img);
        }
        _track.SetStrip(strip);
        if (!_live) await ShowFrameAsync(_track.Position);
    }

    private async Task LoadWaveformAsync(string path, CancellationToken ct)
    {
        string file = Path.Combine(TempFolder(), "wave.png");
        try
        {
            await MediaEngine.WaveformAsync(path, file, 1800, 200, "FF6A3D", ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            AppLog.Write($"Forma d'onda non disponibile: {ex.Message}");
            return;
        }
        _wave.Source = new Bitmap(file);
        _track.SetStrip(new Image { Stretch = Stretch.Fill, Source = new Bitmap(file), Margin = new Thickness(0, 6) });
    }

    private static string TempFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "Akses", "preview", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    // ---- playback --------------------------------------------------------------------------

    private void TogglePlay(bool selectionOnly)
    {
        if (_path is null || _info is null) return;
        VideoPlayer player = Player();
        if (!player.Available)
        {
            if (!_info.HasVideo) _noPreview.IsVisible = true;
            return;
        }
        if (player.IsPlaying && !selectionOnly)
        {
            player.Pause();
            return;
        }
        _playSelectionOnly = selectionOnly;
        double from = selectionOnly ? _track.Start : _track.Position >= _track.Duration - 0.05 ? 0 : _track.Position;
        _live = true;
        ShowPreviewMode();
        player.Play(from);
    }

    private void UpdatePlayGlyph()
    {
        bool playing = _player?.IsPlaying == true;
        _play.Content = new Glyph(playing ? Icons.Pause : Icons.Play, 14, fill: true);
        ToolTip.SetTip(_play, playing ? Strings.Pause : Strings.Play);
    }

    private void OnPlayerPosition(double seconds)
    {
        if (_info is null || _player?.IsPlaying != true) return;
        if (_playSelectionOnly && seconds >= _track.End)
        {
            _player.Pause();
            _playSelectionOnly = false;
        }
        _track.SetPosition(seconds);
        UpdatePosition();
    }

    private void Seek(double seconds)
    {
        if (_info is null) return;
        _track.SetPosition(seconds);
        UpdatePosition();
        if (_player?.IsPlaying == true)
        {
            _player.Seek(seconds);
            return;
        }
        // Paused or stopped: the still frame at the playhead, and play from there next time.
        _player?.Seek(seconds);
        if (_info.HasVideo)
        {
            _live = false;
            ShowPreviewMode();
            _ = ShowFrameAsync(seconds);
        }
    }

    /// <summary>The frame at the playhead, grabbed by ffmpeg: the preview while the video isn't playing.</summary>
    private async Task ShowFrameAsync(double seconds)
    {
        if (_path is null || _info?.HasVideo != true) return;
        _frameCts?.Cancel();
        var cts = _frameCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(120, cts.Token); // drag debounce
            string file = Path.Combine(TempFolder(), "frame.jpg");
            await MediaEngine.FrameAsync(_path, TimeSpan.FromSeconds(Math.Min(seconds, _info.Duration.TotalSeconds - 0.05)), file, 540, cts.Token);
            if (!cts.IsCancellationRequested) _frame.Source = new Bitmap(file);
        }
        catch { }
    }

    // ---- selection -------------------------------------------------------------------------

    private void SetRange(double start, double end)
    {
        if (_info is null) return;
        if (end - start < 0.1) return;
        _track.SetRange(_info.Duration.TotalSeconds, start, end);
        UpdateTimes();
    }

    private void ApplyTypedTimes()
    {
        if (_info is null) return;
        TimeSpan? s = Ui.ParseTime(_startBox.Text), e = Ui.ParseTime(_endBox.Text);
        if (s is { } start && e is { } end && end > start) SetRange(start.TotalSeconds, Math.Min(end.TotalSeconds, _info.Duration.TotalSeconds));
        else UpdateTimes();
    }

    private void UpdateTimes()
    {
        _startBox.Text = Ui.FormatTime(TimeSpan.FromSeconds(_track.Start));
        _endBox.Text = Ui.FormatTime(TimeSpan.FromSeconds(_track.End));
        TimeSpan selected = TimeSpan.FromSeconds(_track.End - _track.Start);
        TimeSpan kept = _remove.IsChecked == true ? TimeSpan.FromSeconds(_track.Duration) - selected : selected;
        _length.Text = Strings.ResultLength(Ui.FormatTime(kept, withMillis: false));
        UpdatePosition();
    }

    private void UpdatePosition()
    {
        if (_info is null) return;
        _position.Text = $"{Ui.FormatTime(TimeSpan.FromSeconds(_track.Position))} / {Ui.FormatTime(_info.Duration)}";
    }

    // ---- cut -------------------------------------------------------------------------------

    private async Task CutAsync()
    {
        if (_path is null || _info is null) return;
        PausePlayback();
        bool remove = _remove.IsChecked == true;
        if (!remove && _track.Start <= 0.01 && _track.End >= _track.Duration - 0.01)
        {
            _status.Text = Strings.NothingToCut;
            return;
        }

        _cutCts?.Cancel();
        var cts = _cutCts = new CancellationTokenSource();
        string ext = MediaEngine.TrimExtension(_path, _info.HasVideo);
        string folder = OutputPaths.FolderFor(_path, Settings.Current.OutputFolder);
        string output = OutputPaths.Unique(folder, Path.GetFileNameWithoutExtension(_path), ext, Strings.TrimSuffix);
        _cut.IsEnabled = false;
        _resultButtons.IsVisible = false;
        _progress.IsVisible = true;
        _progress.IsIndeterminate = true;
        _status.Text = Strings.Cutting;
        try
        {
            var progress = new Progress<double>(p => { _progress.IsIndeterminate = false; _progress.Value = p * 100; });
            await MediaEngine.TrimAsync(_path, output, TimeSpan.FromSeconds(_track.Start), TimeSpan.FromSeconds(_track.End),
                _precise.IsChecked == true && _info.HasVideo, remove, progress, cts.Token);
            _output = output;
            _status.Text = Strings.DoneSaved(Path.GetFileName(output));
            _resultButtons.IsVisible = true;
        }
        catch (OperationCanceledException)
        {
            OutputPaths.TryDelete(output);
            _status.Text = Strings.Cancelled;
        }
        catch (Exception ex)
        {
            OutputPaths.TryDelete(output);
            _status.Text = Ui.Describe(ex);
        }
        finally
        {
            _progress.IsVisible = false;
            _cut.IsEnabled = true;
        }
    }
}
