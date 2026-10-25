using Akses.Core;
using Akses.Core.Engines;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Akses.App;

/// <summary>
/// Crop, resize, rotate and flip one image, then save a copy in any format. The preview is a small
/// copy kept in memory (<see cref="ImagePreview"/>); the edit is applied to the full-size original
/// only when saving, frame by frame for animated GIF/WebP. Same page as the Windows app's ImagePage.
/// </summary>
internal sealed class ImagePage : UserControl
{
    /// <summary>"← Indietro": back to the file's page.</summary>
    public event Action? BackRequested;

    private readonly Grid _dropZone;
    private readonly Grid _editor = new() { ColumnDefinitions = new ColumnDefinitions("*,300") };
    private readonly CropCanvas _canvas = new();
    private readonly TextBlock _fileName = new() { FontSize = 16, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _fileInfo = new() { Opacity = 0.7, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly ToggleSwitch _cropOn = new();
    private readonly ComboBox _ratio = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _cropSize = new() { Opacity = 0.7, FontSize = 12 };
    private readonly NumericUpDown _width = new() { Minimum = 1, Maximum = 60000, FormatString = "0", Increment = 1, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly NumericUpDown _height = new() { Minimum = 1, Maximum = 60000, FormatString = "0", Increment = 1, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly CheckBox _keepRatio = new() { IsChecked = true };
    private readonly ComboBox _format = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _quality = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button _save = Ui.AccentButton(Strings.SaveCopy);
    private readonly ProgressBar _progress = new() { IsIndeterminate = true, IsVisible = false };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly StackPanel _resultButtons = new() { Orientation = Orientation.Horizontal, Spacing = 6, IsVisible = false };

    private static readonly (string Label, double? Ratio)[] Ratios =
    [
        (Strings.RatioFree, null), ("1:1", 1), ("4:3", 4 / 3.0), ("3:2", 1.5), ("16:9", 16 / 9.0),
        ("4:5", 0.8), ("3:4", 0.75), ("2:3", 2 / 3.0), ("9:16", 9 / 16.0),
    ];
    private static readonly TargetFormat[] Formats =
        [FormatCatalog.Jpg, FormatCatalog.Png, FormatCatalog.Webp, FormatCatalog.Avif, FormatCatalog.Gif, FormatCatalog.Ico,
         FormatCatalog.Bmp, FormatCatalog.Tiff, FormatCatalog.Jxl, FormatCatalog.Pdf];

    private string? _path, _output;
    private ImagePreview? _preview;
    private int _rotation;
    private bool _flipH, _flipV, _sizeTouched, _syncing;
    private TargetFormat _sameFormat = FormatCatalog.Png;
    private CancellationTokenSource? _loadCts;

    public ImagePage()
    {
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Margin = new Thickness(28, 4, 28, 16) };
        root.Children.Add(Ui.EditorHeader(Strings.ImageTitle, Strings.ImageSubtitle, () => BackRequested?.Invoke()));

        _dropZone = Ui.DropZone(Strings.ImageDropTitle, Strings.ImageDropSubtitle, Strings.ChooseFile, PickAsync);
        Grid.SetRow(_dropZone, 1);
        root.Children.Add(_dropZone);

        BuildEditor();
        Grid.SetRow(_editor, 1);
        _editor.IsVisible = false;
        root.Children.Add(_editor);

        Content = root;
        Ui.AcceptFileDrops(root, files => { if (files.Count > 0) _ = LoadAsync(files[0]); });
    }

    private void BuildEditor()
    {
        var stage = Ui.SubtlePanel(_canvas);
        stage.Padding = new Thickness(16);
        stage.Margin = new Thickness(0, 0, 20, 0);
        _editor.Children.Add(stage);

        var side = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 14, 0) };
        side.Children.Add(_fileName);
        side.Children.Add(_fileInfo);
        var change = new Button { Content = Strings.ChangeImage };
        change.Click += async (_, _) => await PickAsync();
        side.Children.Add(change);

        side.Children.Add(Ui.SectionTitle(Strings.RotateFlip));
        var rotate = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        rotate.Children.Add(Ui.IconButton(Icons.RotateLeft, Strings.RotateLeft, () => Transform(r: -90)));
        rotate.Children.Add(Ui.IconButton(Icons.RotateRight, Strings.RotateRight, () => Transform(r: 90)));
        rotate.Children.Add(Ui.IconButton(Icons.FlipHorizontal, Strings.FlipHorizontal, () => Transform(h: true)));
        rotate.Children.Add(Ui.IconButton(Icons.FlipVertical, Strings.FlipVertical, () => Transform(v: true)));
        side.Children.Add(rotate);

        side.Children.Add(Ui.SectionTitle(Strings.CropSection));
        _cropOn.OnContent = Strings.On;
        _cropOn.OffContent = Strings.Off;
        _cropOn.IsCheckedChanged += (_, _) => { bool on = _cropOn.IsChecked == true; _canvas.SetEnabled(on); _ratio.IsEnabled = on; SyncSizeFromCrop(); };
        side.Children.Add(_cropOn);
        foreach (var r in Ratios) _ratio.Items.Add(r.Label);
        _ratio.SelectedIndex = 0;
        _ratio.IsEnabled = false;
        _ratio.SelectionChanged += (_, _) => _canvas.SetRatio(Ratios[Math.Max(0, _ratio.SelectedIndex)].Ratio);
        side.Children.Add(Ui.Labeled(Strings.RatioLabel, _ratio));
        side.Children.Add(_cropSize);
        _canvas.CropChanged += SyncSizeFromCrop;

        side.Children.Add(Ui.SectionTitle(Strings.SizeSection));
        var dims = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        var w = Ui.Labeled(Strings.WidthPx, _width);
        w.Margin = new Thickness(0, 0, 4, 0);
        var h = Ui.Labeled(Strings.HeightPx, _height);
        h.Margin = new Thickness(4, 0, 0, 0);
        Grid.SetColumn(h, 1);
        dims.Children.Add(w);
        dims.Children.Add(h);
        side.Children.Add(dims);
        _keepRatio.Content = Strings.KeepProportions;
        side.Children.Add(_keepRatio);
        _width.ValueChanged += (_, _) => OnSizeTyped(widthChanged: true);
        _height.ValueChanged += (_, _) => OnSizeTyped(widthChanged: false);
        var percents = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (int p in new[] { 25, 50, 75, 100 })
        {
            var b = new Button { Content = $"{p}%", Padding = new Thickness(10, 5) };
            b.Click += (_, _) => ScaleTo(p / 100.0);
            percents.Children.Add(b);
        }
        side.Children.Add(percents);

        side.Children.Add(Ui.SectionTitle(Strings.SaveAs));
        side.Children.Add(Ui.Labeled(Strings.FormatLabel, _format));
        _quality.Items.Add(Strings.QualityHigh);
        _quality.Items.Add(Strings.QualityBalanced);
        _quality.Items.Add(Strings.QualitySmall);
        _quality.SelectedIndex = (int)Settings.Current.Quality;
        side.Children.Add(Ui.Labeled(Strings.QualityLabel, _quality));

        _save.HorizontalAlignment = HorizontalAlignment.Stretch;
        _save.HorizontalContentAlignment = HorizontalAlignment.Center;
        _save.Margin = new Thickness(0, 8, 0, 0);
        _save.Click += async (_, _) => await SaveAsync();
        side.Children.Add(_save);
        side.Children.Add(_progress);
        side.Children.Add(_status);
        _resultButtons.Children.Add(Ui.IconButton(Icons.Open, Strings.Open, () => { if (_output is not null) Ui.Open(_output); }));
        _resultButtons.Children.Add(Ui.IconButton(Icons.Folder, Strings.ShowInFolder, () => { if (_output is not null) Ui.ShowInFolder(_output); }));
        side.Children.Add(_resultButtons);

        var scroller = new ScrollViewer { Content = side };
        Grid.SetColumn(scroller, 1);
        _editor.Children.Add(scroller);
    }

    private async Task PickAsync()
    {
        IReadOnlyList<string> files = await Ui.PickFilesAsync(multiple: false);
        if (files.Count > 0) await LoadAsync(files[0]);
    }

    public async Task LoadAsync(string path, bool crop = false)
    {
        if (FormatCatalog.KindOf(path) != FileKind.Image)
        {
            await Dialogs.ShowAsync(Strings.NotImageTitle, Strings.NotImageBody(Path.GetFileName(path)), Strings.Ok);
            return;
        }
        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        _dropZone.IsVisible = false;
        _editor.IsVisible = true;
        _fileName.Text = Path.GetFileName(path);
        _fileInfo.Text = Strings.Loading;
        _status.Text = "";
        _resultButtons.IsVisible = false;
        _save.IsEnabled = false;
        try
        {
            ImagePreview preview = await ImagePreview.LoadAsync(path, 1600, cts.Token);
            if (cts.IsCancellationRequested) { preview.Dispose(); return; }
            _preview?.Dispose();
            _preview = preview;
            _path = path;
            _rotation = 0;
            _flipH = _flipV = false;
            _sizeTouched = false;
            _cropOn.IsChecked = crop;
            _ratio.SelectedIndex = 0;
            bool animated = ImageEngine.IsAnimated(path);
            _fileInfo.Text = Strings.ImageInfo(preview.FullWidth, preview.FullHeight, Path.GetExtension(path).TrimStart('.').ToUpperInvariant(), animated);
            SetupFormats(path);
            Render();
            _save.IsEnabled = true;
        }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested) _fileInfo.Text = Ui.Describe(ex);
        }
    }

    private void SetupFormats(string path)
    {
        string ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        _sameFormat = Formats.FirstOrDefault(f => FormatCatalog.IsSameFormat(f, ext))
            // HEIC, RAW, PSD… can't be written back: photos become JPG, everything else PNG.
            ?? (ext is "heic" or "heif" or "dng" or "cr2" or "cr3" or "nef" or "arw" or "orf" or "rw2" or "raf" ? FormatCatalog.Jpg : FormatCatalog.Png);
        _format.Items.Clear();
        _format.Items.Add(Strings.SameFormat(_sameFormat.Label));
        foreach (TargetFormat f in Formats) _format.Items.Add(f.Label);
        _format.SelectedIndex = 0;
    }

    private void Render()
    {
        if (_preview is null) return;
        (byte[] png, int fw, int fh) = _preview.Render(_rotation, _flipH, _flipV);
        _canvas.SetImage(new Bitmap(new MemoryStream(png)), fw, fh, keepCrop: false);
        _canvas.SetEnabled(_cropOn.IsChecked == true);
        SyncSizeFromCrop();
    }

    private void Transform(int r = 0, bool h = false, bool v = false)
    {
        if (_preview is null) return;
        _rotation = ((_rotation + r) % 360 + 360) % 360;
        // A flip after a quarter turn flips the other axis of the original picture.
        if (h) _flipH = !_flipH;
        if (v) _flipV = !_flipV;
        _sizeTouched = false;
        Render();
    }

    /// <summary>The size the edit starts from: the crop if cropping is on, the whole (rotated) image otherwise.</summary>
    private (double W, double H) BaseSize()
    {
        Rect c = _canvas.Crop;
        return _cropOn.IsChecked == true ? (Math.Round(c.Width), Math.Round(c.Height)) : (_canvas.FullWidth, _canvas.FullHeight);
    }

    private void SyncSizeFromCrop()
    {
        (double w, double h) = BaseSize();
        _cropSize.Text = _cropOn.IsChecked == true ? Strings.CropSize((int)w, (int)h) : "";
        if (_sizeTouched) return;
        _syncing = true;
        _width.Value = (decimal)w;
        _height.Value = (decimal)h;
        _syncing = false;
    }

    private void OnSizeTyped(bool widthChanged)
    {
        if (_syncing || _width.Value is not decimal wv || _height.Value is not decimal hv) return;
        _sizeTouched = true;
        if (_keepRatio.IsChecked != true) return;
        (double bw, double bh) = BaseSize();
        _syncing = true;
        if (widthChanged) _height.Value = Math.Max(1, Math.Round(wv * (decimal)(bh / bw)));
        else _width.Value = Math.Max(1, Math.Round(hv * (decimal)(bw / bh)));
        _syncing = false;
    }

    private void ScaleTo(double factor)
    {
        (double w, double h) = BaseSize();
        _syncing = true;
        _width.Value = (decimal)Math.Max(1, Math.Round(w * factor));
        _height.Value = (decimal)Math.Max(1, Math.Round(h * factor));
        _syncing = false;
        _sizeTouched = factor != 1;
    }

    private async Task SaveAsync()
    {
        if (_path is null || _preview is null) return;
        TargetFormat target = _format.SelectedIndex <= 0 ? _sameFormat : Formats[_format.SelectedIndex - 1];
        Rect c = _canvas.Crop;
        bool cropped = _cropOn.IsChecked == true && (Math.Round(c.Width) < _canvas.FullWidth || Math.Round(c.Height) < _canvas.FullHeight);
        (double bw, double bh) = BaseSize();
        int w = _width.Value is decimal wv ? (int)wv : (int)bw;
        int h = _height.Value is decimal hv ? (int)hv : (int)bh;
        var edit = new ImageEdit
        {
            Rotation = _rotation,
            FlipHorizontal = _flipH,
            FlipVertical = _flipV,
            Crop = cropped ? ((int)Math.Round(c.X), (int)Math.Round(c.Y), Math.Max(1, (int)Math.Round(c.Width)), Math.Max(1, (int)Math.Round(c.Height))) : null,
            Resize = w != (int)bw || h != (int)bh ? (w, h) : null,
        };

        string folder = OutputPaths.FolderFor(_path, Settings.Current.OutputFolder);
        string output = OutputPaths.Unique(folder, Path.GetFileNameWithoutExtension(_path), target.Extension, Strings.EditSuffix);
        _save.IsEnabled = false;
        _progress.IsVisible = true;
        _resultButtons.IsVisible = false;
        _status.Text = Strings.Saving;
        try
        {
            await ImageEngine.EditAsync(_path, output, edit, target, (Quality)Math.Max(0, _quality.SelectedIndex), CancellationToken.None);
            _output = output;
            _status.Text = Strings.DoneSaved(Path.GetFileName(output));
            _resultButtons.IsVisible = true;
        }
        catch (Exception ex)
        {
            OutputPaths.TryDelete(output);
            _status.Text = Ui.Describe(ex);
        }
        finally
        {
            _progress.IsVisible = false;
            _save.IsEnabled = true;
        }
    }
}
