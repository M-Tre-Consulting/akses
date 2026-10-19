using Akses.Core;
using Akses.Core.Engines;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.System;

namespace Akses.App;

/// <summary>
/// Cut audio and video: drag the two handles (or type the times), listen, and "Taglia". By default
/// the streams are copied, so even a two-hour film is cut in a second; "Taglio preciso" re-encodes
/// for a frame-exact video cut. Can also remove the selected part and join what's left.
/// </summary>
internal sealed class TrimPage : UserControl
{
    /// <summary>\"← Indietro\": back to the file's page.</summary>
    public event Action? BackRequested;

    private readonly Grid _dropZone;
    private readonly Grid _editor = new() { RowSpacing = 12 };
    private readonly TextBlock _fileName = new() { FontSize = 16, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _fileInfo = new() { Opacity = 0.7, FontSize = 12 };
    private readonly MediaPlayerElement _video = new() { AreTransportControlsEnabled = false, Stretch = Stretch.Uniform };
    private readonly Image _frame = new() { Stretch = Stretch.Uniform };
    private readonly Image _wave = new() { Stretch = Stretch.Fill, Margin = new Thickness(0, 20, 0, 20) };
    private readonly TextBlock _noPreview = new() { Opacity = 0.7, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(20, 0, 20, 6) };
    private readonly RangeTrack _track = new();
    private readonly TextBox _startBox = new() { Width = 120 }, _endBox = new() { Width = 120 };
    private readonly TextBlock _position = new() { FontFamily = new FontFamily("Cascadia Mono, Consolas"), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _length = new() { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
    private readonly Button _play = new();
    private readonly RadioButtons _mode = new() { MaxColumns = 2 };
    private readonly ToggleSwitch _precise = new();
    private readonly StackPanel _preciseBox = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private readonly Button _cut = new();
    private readonly ProgressBar _progress = new() { Maximum = 100, Width = 220, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _resultButtons = new() { Orientation = Orientation.Horizontal, Spacing = 6, Visibility = Visibility.Collapsed };

    private readonly MediaPlayer _player = new() { AutoPlay = false };
    private string? _path, _output;
    private MediaInfo? _info;
    private bool _playerWorks, _playSelectionOnly;
    private double _fallbackPosition;
    private CancellationTokenSource? _loadCts, _cutCts, _frameCts;

    public TrimPage()
    {
        var root = new Grid { Padding = new Thickness(28, 4, 28, 16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(Ui.EditorHeader(Strings.TrimTitle, Strings.TrimSubtitle, () => BackRequested?.Invoke()));

        _dropZone = Ui.DropZone(Strings.TrimDropTitle, Strings.TrimDropSubtitle, Strings.ChooseFile, PickAsync);
        Grid.SetRow(_dropZone, 1);
        root.Children.Add(_dropZone);

        BuildEditor();
        Grid.SetRow(_editor, 1);
        root.Children.Add(_editor);
        _editor.Visibility = Visibility.Collapsed;

        Content = root;
        Ui.AcceptFileDrops(root, files => { if (files.Count > 0) Load(files[0]); });

        _video.SetMediaPlayer(_player);
        _player.MediaOpened += (_, _) => DispatcherQueue.TryEnqueue(() => { _playerWorks = true; ShowPreviewMode(); });
        _player.MediaFailed += (s, e) => DispatcherQueue.TryEnqueue(() =>
        {
            AppLog.Write($"Anteprima non riproducibile ({_path}): {e.Error} {e.ErrorMessage}");
            _playerWorks = false;
            ShowPreviewMode();
            _ = ShowFrameAsync(_fallbackPosition);
        });
        _player.PlaybackSession.PositionChanged += (s, _) => DispatcherQueue.TryEnqueue(() => OnPlayerPosition(s.Position.TotalSeconds));
        _player.PlaybackSession.PlaybackStateChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdatePlayGlyph);

        var space = new KeyboardAccelerator { Key = VirtualKey.Space };
        space.Invoked += (_, e) =>
        {
            if (_path is null || FocusManager.GetFocusedElement(XamlRoot) is TextBox) return;
            e.Handled = true;
            TogglePlay(false);
        };
        KeyboardAccelerators.Add(space);
    }

    private void BuildEditor()
    {
        _editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                      // file
        _editor.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // preview
        _editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                      // transport
        _editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                      // track
        _editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                      // times
        _editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                      // options
        _editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                      // action

        var fileRow = new Grid();
        fileRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fileRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var names = new StackPanel();
        names.Children.Add(_fileName);
        names.Children.Add(_fileInfo);
        fileRow.Children.Add(names);
        var change = new Button { Content = Strings.ChangeFile };
        change.Click += async (_, _) => await PickAsync();
        Grid.SetColumn(change, 1);
        fileRow.Children.Add(change);
        _editor.Children.Add(fileRow);

        var preview = new Grid { CornerRadius = new CornerRadius(8), Background = FileCard.Brush("ControlFillColorSecondaryBrush"), MinHeight = 160 };
        preview.Children.Add(_wave);
        preview.Children.Add(_video);
        preview.Children.Add(_frame);
        preview.Children.Add(_noPreview);
        Grid.SetRow(preview, 1);
        _editor.Children.Add(preview);

        var transport = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        _play.Click += (_, _) => TogglePlay(false);
        var playSelection = new Button { Content = Strings.PlaySelection };
        playSelection.Click += (_, _) => TogglePlay(true);
        transport.Children.Add(_play);
        transport.Children.Add(playSelection);
        transport.Children.Add(_position);
        UpdatePlayGlyph();
        Grid.SetRow(transport, 2);
        _editor.Children.Add(transport);

        _track.RangeChanged += OnTrackRangeChanged;
        _track.SeekRequested += Seek;
        Grid.SetRow(_track, 3);
        _editor.Children.Add(_track);

        var times = new WrapPanel { HorizontalSpacing = 10, VerticalSpacing = 8 };
        times.Children.Add(Labeled(Strings.StartLabel, _startBox));
        times.Children.Add(Button(Strings.SetStartHere, "\uE76B", () => SetRange(_track.Position, _track.End)));
        times.Children.Add(Labeled(Strings.EndLabel, _endBox));
        times.Children.Add(Button(Strings.SetEndHere, "\uE76C", () => SetRange(_track.Start, _track.Position)));
        times.Children.Add(_length);
        foreach (TextBox box in new[] { _startBox, _endBox })
        {
            box.LostFocus += (_, _) => ApplyTypedTimes();
            box.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter) { ApplyTypedTimes(); e.Handled = true; } };
        }
        Grid.SetRow(times, 4);
        _editor.Children.Add(times);

        _mode.Items.Add(Strings.KeepSelection);
        _mode.Items.Add(Strings.RemoveSelection);
        _mode.SelectedIndex = 0;
        _mode.SelectionChanged += (_, _) => UpdateTimes();
        _precise.OnContent = _precise.OffContent = Strings.PreciseCut;
        _preciseBox.Children.Add(_precise);
        var options = new WrapPanel { HorizontalSpacing = 28, VerticalSpacing = 6 };
        options.Children.Add(_mode);
        options.Children.Add(_preciseBox);
        Grid.SetRow(options, 5);
        _editor.Children.Add(options);

        _cut.Content = Strings.TrimButton;
        _cut.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        _cut.Padding = new Thickness(22, 7, 22, 7);
        _cut.Click += async (_, _) => await CutAsync();
        _resultButtons.Children.Add(FileCard.IconButton("\uE8E5", Strings.Open, () => { if (_output is not null) Ui.Open(_output); }));
        _resultButtons.Children.Add(FileCard.IconButton("\uE838", Strings.ShowInFolder, () => { if (_output is not null) Ui.ShowInFolder(_output); }));
        var action = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        action.Children.Add(_cut);
        action.Children.Add(_progress);
        action.Children.Add(_status);
        action.Children.Add(_resultButtons);
        Grid.SetRow(action, 6);
        _editor.Children.Add(action);
    }

    private static StackPanel Labeled(string label, FrameworkElement control)
    {
        var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        s.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        s.Children.Add(control);
        return s;
    }

    private static Button Button(string text, string glyph, Action onClick)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 12 });
        content.Children.Add(new TextBlock { Text = text });
        var b = new Button { Content = content };
        b.Click += (_, _) => onClick();
        return b;
    }

    private async Task PickAsync()
    {
        IReadOnlyList<string> files = await Ui.PickFilesAsync(multiple: false);
        if (files.Count > 0) Load(files[0]);
    }

    public void PausePlayback()
    {
        if (_player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing) _player.Pause();
    }

    // ---- loading ---------------------------------------------------------------------------

    public async void Load(string path)
    {
        FileKind kind = FormatCatalog.KindOf(path);
        bool animatedImage = kind == FileKind.Image && Path.GetExtension(path).ToLowerInvariant() is ".gif";
        if (kind is not (FileKind.Video or FileKind.Audio) && !animatedImage)
        {
            await Message(Strings.NotMediaTitle, Strings.NotMediaBody(Path.GetFileName(path)));
            return;
        }

        _loadCts?.Cancel();
        _cutCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        _path = path;
        _output = null;
        _info = null;
        _playerWorks = false;
        _fallbackPosition = 0;
        _resultButtons.Visibility = _progress.Visibility = Visibility.Collapsed;
        _status.Text = "";
        _fileName.Text = Path.GetFileName(path);
        _fileInfo.Text = Strings.Loading;
        _dropZone.Visibility = Visibility.Collapsed;
        _editor.Visibility = Visibility.Visible;
        _track.SetStrip(null);
        _wave.Source = null;
        _frame.Source = null;
        _cut.IsEnabled = false;

        try
        {
            MediaInfo info = await MediaEngine.ProbeAsync(path, cts.Token);
            if (cts.IsCancellationRequested) return;
            if (info.Duration <= TimeSpan.Zero || (!info.HasAudio && !info.HasVideo)) throw new InvalidDataException("no media streams");
            _info = info;
            _fileInfo.Text = Strings.MediaInfoLine(info);
            _preciseBox.Visibility = info.HasVideo ? Visibility.Visible : Visibility.Collapsed;
            _track.SetRange(info.Duration.TotalSeconds, 0, info.Duration.TotalSeconds);
            _track.SetPosition(0);
            UpdateTimes();
            _cut.IsEnabled = true;

            ShowPreviewMode();
            _player.Source = MediaSource.CreateFromUri(new Uri(path));

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
        _video.Visibility = video && _playerWorks ? Visibility.Visible : Visibility.Collapsed;
        _frame.Visibility = video && !_playerWorks ? Visibility.Visible : Visibility.Collapsed;
        _wave.Visibility = video ? Visibility.Collapsed : Visibility.Visible;
        _noPreview.Visibility = _info is not null && !_playerWorks && _player.Source is not null && !video ? Visibility.Visible : Visibility.Collapsed;
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

        var strip = new Grid { ColumnSpacing = 0 };
        for (int i = 0; i < count; i++)
        {
            strip.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (frames[i] is not { } f || !File.Exists(f)) continue;
            var img = new Image { Stretch = Stretch.UniformToFill, Source = new BitmapImage(new Uri(f)) };
            Grid.SetColumn(img, i);
            strip.Children.Add(img);
        }
        _track.SetStrip(strip);
        if (!_playerWorks) await ShowFrameAsync(0);
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
        _wave.Source = new BitmapImage(new Uri(file));
        _track.SetStrip(new Image { Stretch = Stretch.Fill, Source = new BitmapImage(new Uri(file)), Margin = new Thickness(0, 6, 0, 6) });
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
        if (_path is null) return;
        if (!_playerWorks)
        {
            if (_info?.HasVideo != true) _noPreview.Visibility = Visibility.Visible;
            return;
        }
        MediaPlaybackSession session = _player.PlaybackSession;
        if (session.PlaybackState == MediaPlaybackState.Playing && !selectionOnly)
        {
            _player.Pause();
            return;
        }
        _playSelectionOnly = selectionOnly;
        if (selectionOnly) session.Position = TimeSpan.FromSeconds(_track.Start);
        _player.Play();
    }

    private void UpdatePlayGlyph()
    {
        bool playing = _player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;
        _play.Content = new FontIcon { Glyph = playing ? "\uE769" : "\uE768", FontSize = 14 };
        ToolTipService.SetToolTip(_play, playing ? Strings.Pause : Strings.Play);
    }

    private void OnPlayerPosition(double seconds)
    {
        if (_info is null) return;
        if (_playSelectionOnly && seconds >= _track.End && _player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
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
        _fallbackPosition = seconds;
        _track.SetPosition(seconds);
        UpdatePosition();
        if (_playerWorks) _player.PlaybackSession.Position = TimeSpan.FromSeconds(seconds);
        else if (_info.HasVideo) _ = ShowFrameAsync(seconds);
    }

    /// <summary>Preview for videos Windows can't play (some MKV/AVI codecs): the frame at the playhead, grabbed by ffmpeg.</summary>
    private async Task ShowFrameAsync(double seconds)
    {
        if (_path is null || _info?.HasVideo != true || _playerWorks) return;
        _frameCts?.Cancel();
        var cts = _frameCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(120, cts.Token); // drag debounce
            string file = Path.Combine(TempFolder(), "frame.jpg");
            await MediaEngine.FrameAsync(_path, TimeSpan.FromSeconds(Math.Min(seconds, _info.Duration.TotalSeconds - 0.05)), file, 540, cts.Token);
            if (!cts.IsCancellationRequested) _frame.Source = new BitmapImage(new Uri(file));
        }
        catch { }
    }

    // ---- selection -------------------------------------------------------------------------

    private void OnTrackRangeChanged() => UpdateTimes();

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
        TimeSpan kept = _mode.SelectedIndex == 1 ? TimeSpan.FromSeconds(_track.Duration) - selected : selected;
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
        bool remove = _mode.SelectedIndex == 1;
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
        _resultButtons.Visibility = Visibility.Collapsed;
        _progress.Visibility = Visibility.Visible;
        _progress.IsIndeterminate = true;
        _status.Text = Strings.Cutting;
        try
        {
            var progress = new Progress<double>(p => { _progress.IsIndeterminate = false; _progress.Value = p * 100; });
            await MediaEngine.TrimAsync(_path, output, TimeSpan.FromSeconds(_track.Start), TimeSpan.FromSeconds(_track.End),
                _precise.IsOn && _info.HasVideo, remove, progress, cts.Token);
            _output = output;
            _status.Text = Strings.DoneSaved(Path.GetFileName(output));
            _resultButtons.Visibility = Visibility.Visible;
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
            _progress.Visibility = Visibility.Collapsed;
            _cut.IsEnabled = true;
        }
    }

    private async Task Message(string title, string body)
    {
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = body, CloseButtonText = Strings.Ok };
        try { await dialog.ShowAsync(); } catch { }
    }
}
