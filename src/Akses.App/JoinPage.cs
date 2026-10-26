using Akses.Core;
using Akses.Core.Engines;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Akses.App;

/// <summary>
/// Joins videos (or audio files) one after the other, in the order of the list, which can be
/// changed. The list holds one kind at a time: the kind of the first file added.
/// </summary>
internal sealed class JoinPage : UserControl
{
    /// <summary>"← Indietro": back to the file's page.</summary>
    public event Action? BackRequested;

    private readonly List<FileItem> _items = [];
    private readonly StackPanel _list = new() { Spacing = 8 };
    private readonly Button _run = new() { Content = Strings.JoinButton };
    private readonly Button _cancel = new() { Content = Strings.Cancel, Visibility = Visibility.Collapsed };
    private readonly ProgressBar _progress = new() { Maximum = 100, Width = 220, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly Button _showResult = new() { Content = Strings.ShowInFolder, Visibility = Visibility.Collapsed };
    private readonly Grid _dropZone;
    private readonly Grid _workArea = new() { RowSpacing = 12 };
    private CancellationTokenSource? _cts;
    private string? _lastOutput;

    public JoinPage()
    {
        var root = new Grid { Padding = new Thickness(28, 4, 28, 16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(Ui.EditorHeader(Strings.JoinTitle, Strings.JoinSubtitle, () => BackRequested?.Invoke()));

        _dropZone = Ui.DropZone(Strings.JoinDropTitle, Strings.JoinDropSubtitle, Strings.ChooseFiles, PickAsync);
        Grid.SetRow(_dropZone, 1);
        root.Children.Add(_dropZone);

        BuildWorkArea();
        Grid.SetRow(_workArea, 1);
        root.Children.Add(_workArea);

        Content = root;
        Ui.AcceptFileDrops(root, AddFiles);
        Refresh();
    }

    private void BuildWorkArea()
    {
        _workArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _workArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _workArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _workArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _workArea.Children.Add(new TextBlock { Text = Strings.JoinHint, Opacity = 0.75, TextWrapping = TextWrapping.Wrap });

        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var add = new Button { Content = Strings.AddFiles };
        add.Click += async (_, _) => await PickAsync();
        var clear = new Button { Content = Strings.ClearList };
        clear.Click += (_, _) => { _items.Clear(); Refresh(); };
        top.Children.Add(add);
        top.Children.Add(clear);
        Grid.SetRow(top, 1);
        _workArea.Children.Add(top);

        var scroller = new ScrollViewer { Content = _list, Padding = new Thickness(0, 0, 12, 0) };
        Grid.SetRow(scroller, 2);
        _workArea.Children.Add(scroller);

        _run.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        _run.Padding = new Thickness(22, 7, 22, 7);
        _run.Click += async (_, _) => await RunAsync();
        _cancel.Click += (_, _) => _cts?.Cancel();
        _showResult.Click += (_, _) => { if (_lastOutput is not null) Ui.ShowInFolder(_lastOutput); };
        var action = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        action.Children.Add(_run);
        action.Children.Add(_cancel);
        action.Children.Add(_progress);
        action.Children.Add(_status);
        action.Children.Add(_showResult);
        Grid.SetRow(action, 3);
        _workArea.Children.Add(action);
    }

    private async Task PickAsync() => AddFiles(await Ui.PickFilesAsync());

    /// <summary>Opens the page on <paramref name="files"/> (replacing the list).</summary>
    public void Open(IReadOnlyList<string> files)
    {
        if (_cts is not null) return; // a join is running: keep its list
        _items.Clear();
        _status.Text = "";
        _showResult.Visibility = Visibility.Collapsed;
        AddFiles(files);
    }

    public void AddFiles(IReadOnlyList<string> files)
    {
        foreach (string f in files)
        {
            FileKind kind = FormatCatalog.KindOf(f);
            if (kind is not (FileKind.Video or FileKind.Audio)) continue;
            if (_items.Count > 0 && kind != _items[0].Kind) continue;
            if (_items.Any(i => string.Equals(i.Path, f, StringComparison.OrdinalIgnoreCase))) continue;
            var item = new FileItem(f);
            _items.Add(item);
            _ = LoadDurationAsync(item);
        }
        Refresh();
    }

    private static async Task LoadDurationAsync(FileItem item)
    {
        try
        {
            MediaInfo info = await MediaEngine.ProbeAsync(item.Path);
            item.Details = info.HasVideo
                ? $"{Ui.FormatTime(info.Duration, false)} · {info.Width}×{info.Height}"
                : Ui.FormatTime(info.Duration, false);
        }
        catch (Exception ex) { item.SetError(Ui.Describe(ex)); }
    }

    private void Refresh()
    {
        bool any = _items.Count > 0;
        _dropZone.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        _workArea.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        RebuildList();
    }

    private void RebuildList()
    {
        _list.Children.Clear();
        for (int i = 0; i < _items.Count; i++)
        {
            int index = i;
            var reorder = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 4, 0, 0) };
            var up = FileCard.IconButton("", Strings.MoveUp, () => Move(index, -1));
            var down = FileCard.IconButton("", Strings.MoveDown, () => Move(index, +1));
            up.IsEnabled = index > 0;
            down.IsEnabled = index < _items.Count - 1;
            reorder.Children.Add(up);
            reorder.Children.Add(down);
            var card = new FileCard(_items[i], extra: reorder);
            card.RemoveRequested += c => { _items.Remove(c.Item); Refresh(); };
            _list.Children.Add(card);
        }
    }

    private void Move(int index, int delta)
    {
        int target = index + delta;
        if (target < 0 || target >= _items.Count) return;
        (_items[index], _items[target]) = (_items[target], _items[index]);
        RebuildList();
    }

    private async Task RunAsync()
    {
        List<string> files = _items.Where(i => i.Error.Length == 0).Select(i => i.Path).ToList();
        if (files.Count < 2)
        {
            _status.Text = Strings.MergeNeedsTwo;
            return;
        }

        _cts = new CancellationTokenSource();
        _run.IsEnabled = false;
        _cancel.Visibility = Visibility.Visible;
        _showResult.Visibility = Visibility.Collapsed;
        _progress.Visibility = Visibility.Visible;
        _progress.Value = 0;
        _status.Text = Strings.Working;
        string first = files[0];
        bool video = _items[0].Kind == FileKind.Video;
        string output = OutputPaths.Unique(OutputPaths.FolderFor(first, Settings.Current.OutputFolder),
            Path.GetFileNameWithoutExtension(first), MediaEngine.JoinExtension(files, video), Strings.MergedSuffix);
        try
        {
            await MediaEngine.JoinAsync(files, output, new Progress<double>(p => _progress.Value = p * 100), _cts.Token);
            _lastOutput = output;
            _status.Text = Strings.DoneSaved(Path.GetFileName(output));
            _showResult.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException)
        {
            OutputPaths.TryDelete(output);
            _status.Text = "";
        }
        catch (Exception ex)
        {
            OutputPaths.TryDelete(output);
            _status.Text = Ui.Describe(ex);
        }
        finally
        {
            _cts = null;
            _progress.Visibility = Visibility.Collapsed;
            _cancel.Visibility = Visibility.Collapsed;
            _run.IsEnabled = true;
        }
    }
}
