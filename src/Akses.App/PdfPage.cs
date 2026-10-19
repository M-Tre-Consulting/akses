using Akses.Core;
using Akses.Core.Engines;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Akses.App;

internal enum PdfTool { Merge, Split, Extract, Rotate, Images }

/// <summary>
/// PDF tools on a list of files: merge (PDFs and images, in the order shown, which can be changed),
/// split into pages, extract pages, rotate, and turn pages into images. The single-file tools run on
/// every PDF in the list.
/// </summary>
internal sealed class PdfPage : UserControl
{
    /// <summary>\"← Indietro\": back to the file's page.</summary>
    public event Action? BackRequested;


    private readonly List<FileItem> _items = [];
    private readonly StackPanel _list = new() { Spacing = 8 };
    private readonly SelectorBar _modes = new();
    private readonly TextBlock _modeHint = new() { Opacity = 0.75, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _pages = new() { Width = 220 };
    private readonly ComboBox _rotation = new() { MinWidth = 200 };
    private readonly ComboBox _imageFormat = new() { MinWidth = 140 };
    private readonly ComboBox _dpi = new() { MinWidth = 160 };
    private readonly StackPanel _options = new() { Orientation = Orientation.Horizontal, Spacing = 12 };
    private readonly Button _run = new();
    private readonly ProgressBar _progress = new() { Maximum = 100, Width = 220, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly Button _showResult = new();
    private readonly Grid _dropZone;
    private readonly Grid _workArea = new() { RowSpacing = 12 };
    private string? _lastOutput;

    public PdfPage()
    {
        var root = new Grid { Padding = new Thickness(28, 4, 28, 16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(Ui.EditorHeader(Strings.PdfTitle, Strings.PdfSubtitle, () => BackRequested?.Invoke()));

        foreach ((PdfTool mode, string text, string glyph) in new[]
        {
            (PdfTool.Merge, Strings.PdfMerge, "\uE71D"), (PdfTool.Split, Strings.PdfSplit, "\uE8C6"),
            (PdfTool.Extract, Strings.PdfExtract, "\uE8B3"), (PdfTool.Rotate, Strings.PdfRotate, "\uE7AD"),
            (PdfTool.Images, Strings.PdfToImages, "\uEB9F"),
        })
            _modes.Items.Add(new SelectorBarItem { Text = text, Tag = mode, Icon = new FontIcon { Glyph = glyph } });
        _modes.SelectionChanged += (_, _) => UpdateMode();
        _modes.Margin = new Thickness(-12, 0, 0, 8);
        // One tool at a time, chosen on the file's page: the bar stays hidden.
        _modes.Visibility = Visibility.Collapsed;
        Grid.SetRow(_modes, 1);
        root.Children.Add(_modes);

        _dropZone = Ui.DropZone(Strings.PdfDropTitle, Strings.PdfDropSubtitle, Strings.ChooseFiles, PickAsync);
        Grid.SetRow(_dropZone, 2);
        root.Children.Add(_dropZone);

        BuildWorkArea();
        Grid.SetRow(_workArea, 2);
        root.Children.Add(_workArea);

        Content = root;
        Ui.AcceptFileDrops(root, AddFiles);
        _modes.SelectedItem = _modes.Items[0];
        Refresh();
    }

    private void BuildWorkArea()
    {
        _workArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _workArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _workArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _workArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _workArea.Children.Add(_modeHint);

        _pages.Header = Strings.PagesLabel;
        _pages.PlaceholderText = Strings.PagesPlaceholder;
        _rotation.Header = Strings.RotationLabel;
        _rotation.Items.Add(Strings.Rotate90Right);
        _rotation.Items.Add(Strings.Rotate180);
        _rotation.Items.Add(Strings.Rotate90Left);
        _rotation.SelectedIndex = 0;
        _imageFormat.Header = Strings.FormatLabel;
        _imageFormat.Items.Add("PNG");
        _imageFormat.Items.Add("JPG");
        _imageFormat.SelectedIndex = 0;
        _dpi.Header = Strings.ResolutionLabel;
        foreach (int d in new[] { 96, 150, 200, 300 }) _dpi.Items.Add($"{d} dpi");
        _dpi.SelectedIndex = 1;

        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        top.Children.Add(_options);
        var add = new Button { Content = Strings.AddFiles, VerticalAlignment = VerticalAlignment.Bottom };
        add.Click += async (_, _) => await PickAsync();
        var clear = new Button { Content = Strings.ClearList, VerticalAlignment = VerticalAlignment.Bottom };
        clear.Click += (_, _) => { _items.Clear(); _list.Children.Clear(); Refresh(); };
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
        _showResult.Content = Strings.ShowInFolder;
        _showResult.Visibility = Visibility.Collapsed;
        _showResult.Click += (_, _) => { if (_lastOutput is not null) Ui.ShowInFolder(_lastOutput); };
        var action = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        action.Children.Add(_run);
        action.Children.Add(_progress);
        action.Children.Add(_status);
        action.Children.Add(_showResult);
        Grid.SetRow(action, 3);
        _workArea.Children.Add(action);
    }

    private PdfTool CurrentMode => _modes.SelectedItem?.Tag is PdfTool m ? m : PdfTool.Merge;

    private void UpdateMode()
    {
        PdfTool mode = CurrentMode;
        _options.Children.Clear();
        switch (mode)
        {
            case PdfTool.Extract: _options.Children.Add(_pages); break;
            case PdfTool.Rotate: _options.Children.Add(_rotation); _options.Children.Add(_pages); break;
            case PdfTool.Images: _options.Children.Add(_imageFormat); _options.Children.Add(_dpi); break;
        }
        _pages.PlaceholderText = mode == PdfTool.Rotate ? Strings.PagesAllPlaceholder : Strings.PagesPlaceholder;
        _modeHint.Text = mode switch
        {
            PdfTool.Merge => Strings.PdfMergeHint,
            PdfTool.Split => Strings.PdfSplitHint,
            PdfTool.Extract => Strings.PdfExtractHint,
            PdfTool.Rotate => Strings.PdfRotateHint,
            _ => Strings.PdfImagesHint,
        };
        _run.Content = mode switch
        {
            PdfTool.Merge => Strings.PdfMergeButton,
            PdfTool.Split => Strings.PdfSplitButton,
            PdfTool.Extract => Strings.PdfExtractButton,
            PdfTool.Rotate => Strings.PdfRotateButton,
            _ => Strings.PdfImagesButton,
        };
        _status.Text = "";
        _showResult.Visibility = Visibility.Collapsed;
        RebuildList();
    }

    private async Task PickAsync() => AddFiles(await Ui.PickFilesAsync());

    /// <summary>Opens <paramref name="tool"/> on <paramref name="files"/> (replacing the list).</summary>
    public void Open(PdfTool tool, IReadOnlyList<string> files)
    {
        _items.Clear();
        _modes.SelectedItem = _modes.Items.First(i => i.Tag is PdfTool t && t == tool);
        AddFiles(files);
        UpdateMode();
    }

    public void AddFiles(IReadOnlyList<string> files)
    {
        foreach (string f in files)
        {
            FileKind kind = FormatCatalog.KindOf(f);
            if (kind is not (FileKind.Pdf or FileKind.Image)) continue;
            if (_items.Any(i => string.Equals(i.Path, f, StringComparison.OrdinalIgnoreCase))) continue;
            var item = new FileItem(f);
            if (kind == FileKind.Pdf)
            {
                try { item.Details = Strings.PageCount(PdfEngine.PageCount(f)); }
                catch (Exception ex) { item.SetError(Ui.Describe(ex)); }
            }
            _items.Add(item);
        }
        Refresh();
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
        bool merge = CurrentMode == PdfTool.Merge;
        for (int i = 0; i < _items.Count; i++)
        {
            FileItem item = _items[i];
            int index = i;
            StackPanel? reorder = null;
            if (merge)
            {
                reorder = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 4, 0, 0) };
                var up = FileCard.IconButton("\uE70E", Strings.MoveUp, () => Move(index, -1));
                var down = FileCard.IconButton("\uE70D", Strings.MoveDown, () => Move(index, +1));
                up.IsEnabled = index > 0;
                down.IsEnabled = index < _items.Count - 1;
                reorder.Children.Add(up);
                reorder.Children.Add(down);
            }
            var card = new FileCard(item, extra: reorder);
            card.Opacity = merge || item.Kind == FileKind.Pdf ? 1 : 0.45;
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
        PdfTool mode = CurrentMode;
        List<FileItem> pdfs = _items.Where(i => i.Kind == FileKind.Pdf && i.Error.Length == 0).ToList();
        if (mode == PdfTool.Merge ? _items.Count < 2 : pdfs.Count == 0)
        {
            _status.Text = mode == PdfTool.Merge ? Strings.MergeNeedsTwo : Strings.NeedPdf;
            return;
        }

        _run.IsEnabled = false;
        _showResult.Visibility = Visibility.Collapsed;
        _progress.Visibility = Visibility.Visible;
        _progress.Value = 0;
        _status.Text = Strings.Working;
        int done = 0;
        string? output = null;
        try
        {
            if (mode == PdfTool.Merge)
            {
                string first = _items[0].Path;
                output = OutputPaths.Unique(OutputPaths.FolderFor(first, Settings.Current.OutputFolder), Path.GetFileNameWithoutExtension(first), "pdf", Strings.MergedSuffix);
                var progress = new Progress<double>(p => _progress.Value = p * 100);
                await PdfEngine.MergeAsync(_items.Where(i => i.Error.Length == 0).Select(i => i.Path).ToList(), output, progress, CancellationToken.None);
                done = 1;
            }
            else
            {
                foreach (FileItem item in pdfs)
                {
                    output = await RunOneAsync(mode, item);
                    if (output is not null) { item.SetDone(output); done++; }
                    _progress.Value = 100.0 * (pdfs.IndexOf(item) + 1) / pdfs.Count;
                }
            }
            _lastOutput = output;
            _status.Text = done == 0 ? _status.Text : output is not null && done == 1 ? Strings.DoneSaved(Path.GetFileName(output)) : Strings.DoneCount(done);
            _showResult.Visibility = done > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            if (output is not null) OutputPaths.TryDelete(output);
            _status.Text = Ui.Describe(ex);
        }
        finally
        {
            _progress.Visibility = Visibility.Collapsed;
            _run.IsEnabled = true;
        }
    }

    /// <summary>One PDF through a single-file tool; returns the result path, or null (and a message) when the pages typed don't make sense.</summary>
    private async Task<string?> RunOneAsync(PdfTool mode, FileItem item)
    {
        string input = item.Path;
        string folder = OutputPaths.FolderFor(input, Settings.Current.OutputFolder);
        string name = Path.GetFileNameWithoutExtension(input);
        int pageCount = PdfEngine.PageCount(input);
        switch (mode)
        {
            case PdfTool.Split:
            {
                string outFolder = OutputPaths.UniqueFolder(folder, name + Strings.PagesSuffix);
                await PdfEngine.SplitAsync(input, outFolder, null, CancellationToken.None);
                return outFolder;
            }
            case PdfTool.Extract:
            {
                List<int>? pages = PdfEngine.ParsePages(_pages.Text, pageCount);
                if (pages is null) { _status.Text = Strings.InvalidPages(pageCount); return null; }
                string output = OutputPaths.Unique(folder, name, "pdf", Strings.ExtractSuffix);
                await PdfEngine.ExtractAsync(input, output, pages, CancellationToken.None);
                return output;
            }
            case PdfTool.Rotate:
            {
                List<int>? pages = null;
                if (!string.IsNullOrWhiteSpace(_pages.Text))
                {
                    pages = PdfEngine.ParsePages(_pages.Text, pageCount);
                    if (pages is null) { _status.Text = Strings.InvalidPages(pageCount); return null; }
                }
                int degrees = _rotation.SelectedIndex switch { 1 => 180, 2 => 270, _ => 90 };
                string output = OutputPaths.Unique(folder, name, "pdf", Strings.RotatedSuffix);
                await PdfEngine.RotateAsync(input, output, degrees, pages, CancellationToken.None);
                return output;
            }
            default:
            {
                string outFolder = OutputPaths.UniqueFolder(folder, name + Strings.ImagesSuffix);
                int dpi = _dpi.SelectedIndex switch { 0 => 96, 2 => 200, 3 => 300, _ => 150 };
                await PdfEngine.ToImagesAsync(input, outFolder, _imageFormat.SelectedIndex == 1, dpi, null, CancellationToken.None);
                return outFolder;
            }
        }
    }
}
