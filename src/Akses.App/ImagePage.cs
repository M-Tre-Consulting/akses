using System.Runtime.InteropServices.WindowsRuntime;
using Akses.Core;
using Akses.Core.Engines;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;

namespace Akses.App;

/// <summary>
/// Crop, resize, rotate and flip one image, then save a copy in any format. The preview is a small
/// copy kept in memory (<see cref="ImagePreview"/>); the edit is applied to the full-size original
/// only when saving, frame by frame for animated GIF/WebP.
/// </summary>
internal sealed class ImagePage : UserControl
{
    /// <summary>\"← Indietro\": back to the file's page.</summary>
    public event Action? BackRequested;

    private readonly Grid _dropZone;
    private readonly Grid _editor = new() { ColumnSpacing = 20 };
    private readonly CropCanvas _canvas = new();
    private readonly TextBlock _fileName = new() { FontSize = 16, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _fileInfo = new() { Opacity = 0.7, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly ToggleSwitch _cropOn = new();
    private readonly ComboBox _ratio = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _cropSize = new() { Opacity = 0.7, FontSize = 12 };
    private readonly NumberBox _width = new() { Minimum = 1, Maximum = 60000, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
    private readonly NumberBox _height = new() { Minimum = 1, Maximum = 60000, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
    private readonly CheckBox _keepRatio = new() { IsChecked = true };
    private readonly ComboBox _format = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _quality = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button _save = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ProgressBar _progress = new() { IsIndeterminate = true, Visibility = Visibility.Collapsed };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly StackPanel _resultButtons = new() { Orientation = Orientation.Horizontal, Spacing = 6, Visibility = Visibility.Collapsed };

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
        var root = new Grid { Padding = new Thickness(28, 4, 28, 16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(Ui.EditorHeader(Strings.ImageTitle, Strings.ImageSubtitle, () => BackRequested?.Invoke()));

        _dropZone = Ui.DropZone(Strings.ImageDropTitle, Strings.ImageDropSubtitle, Strings.ChooseFile, PickAsync);
        Grid.SetRow(_dropZone, 1);
        root.Children.Add(_dropZone);

        BuildEditor();
        Grid.SetRow(_editor, 1);
        _editor.Visibility = Visibility.Collapsed;
        root.Children.Add(_editor);

        Content = root;
        Ui.AcceptFileDrops(root, files => { if (files.Count > 0) _ = LoadAsync(files[0]); });
    }

    private void BuildEditor()
    {
        _editor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _editor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });

        var stage = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = FileCard.Brush("ControlFillColorSecondaryBrush"),
            Padding = new Thickness(16),
            Child = _canvas,
        };
        _editor.Children.Add(stage);

        var side = new StackPanel { Spacing = 10, Padding = new Thickness(0, 0, 14, 0) };
        side.Children.Add(_fileName);
        side.Children.Add(_fileInfo);
        var change = new Button { Content = Strings.ChangeImage };
        change.Click += async (_, _) => await PickAsync();
        side.Children.Add(change);

        side.Children.Add(Ui.SectionTitle(Strings.RotateFlip));
        var rotate = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        rotate.Children.Add(FileCard.IconButton("\uE7AD", Strings.RotateLeft, () => Transform(r: -90)));
        rotate.Children.Add(FileCard.IconButton("\uE7AD", Strings.RotateRight, () => Transform(r: 90)));
        ((FontIcon)((Button)rotate.Children[0]).Content).RenderTransform = new Microsoft.UI.Xaml.Media.ScaleTransform { ScaleX = -1, CenterX = 7 };
        rotate.Children.Add(FileCard.IconButton("\uE8AB", Strings.FlipHorizontal, () => Transform(h: true)));
        var flipV = FileCard.IconButton("\uE8AB", Strings.FlipVertical, () => Transform(v: true));
        ((FontIcon)flipV.Content).RenderTransform = new Microsoft.UI.Xaml.Media.RotateTransform { Angle = 90, CenterX = 7, CenterY = 7 };
        rotate.Children.Add(flipV);
        side.Children.Add(rotate);

        side.Children.Add(Ui.SectionTitle(Strings.CropSection));
        _cropOn.OnContent = Strings.On;
        _cropOn.OffContent = Strings.Off;
        _cropOn.Toggled += (_, _) => { _canvas.SetEnabled(_cropOn.IsOn); _ratio.IsEnabled = _cropOn.IsOn; SyncSizeFromCrop(); };
        side.Children.Add(_cropOn);
        foreach (var r in Ratios) _ratio.Items.Add(r.Label);
        _ratio.SelectedIndex = 0;
        _ratio.IsEnabled = false;
        _ratio.Header = Strings.RatioLabel;
        _ratio.SelectionChanged += (_, _) => _canvas.SetRatio(Ratios[Math.Max(0, _ratio.SelectedIndex)].Ratio);
        side.Children.Add(_ratio);
        side.Children.Add(_cropSize);
        _canvas.CropChanged += SyncSizeFromCrop;

        side.Children.Add(Ui.SectionTitle(Strings.SizeSection));
        var dims = new Grid { ColumnSpacing = 8 };
        dims.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dims.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _width.Header = Strings.WidthPx;
        _height.Header = Strings.HeightPx;
        Grid.SetColumn(_height, 1);
        dims.Children.Add(_width);
        dims.Children.Add(_height);
        side.Children.Add(dims);
        _keepRatio.Content = Strings.KeepProportions;
        side.Children.Add(_keepRatio);
        _width.ValueChanged += (_, _) => OnSizeTyped(widthChanged: true);
        _height.ValueChanged += (_, _) => OnSizeTyped(widthChanged: false);
        var percents = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (int p in new[] { 25, 50, 75, 100 })
        {
            var b = new Button { Content = $"{p}%", Padding = new Thickness(10, 5, 10, 5) };
            b.Click += (_, _) => ScaleTo(p / 100.0);
            percents.Children.Add(b);
        }
        side.Children.Add(percents);

        side.Children.Add(Ui.SectionTitle(Strings.SaveAs));
        _format.Header = Strings.FormatLabel;
        side.Children.Add(_format);
        _quality.Header = Strings.QualityLabel;
        _quality.Items.Add(Strings.QualityHigh);
        _quality.Items.Add(Strings.QualityBalanced);
        _quality.Items.Add(Strings.QualitySmall);
        _quality.SelectedIndex = (int)Settings.Current.Quality;
        side.Children.Add(_quality);

        _save.Content = Strings.SaveCopy;
        _save.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        _save.Margin = new Thickness(0, 8, 0, 0);
        _save.Click += async (_, _) => await SaveAsync();
        side.Children.Add(_save);
        side.Children.Add(_progress);
        side.Children.Add(_status);
        _resultButtons.Children.Add(FileCard.IconButton("\uE8E5", Strings.Open, () => { if (_output is not null) Ui.Open(_output); }));
        _resultButtons.Children.Add(FileCard.IconButton("\uE838", Strings.ShowInFolder, () => { if (_output is not null) Ui.ShowInFolder(_output); }));
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
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = Strings.NotImageTitle, Content = Strings.NotImageBody(Path.GetFileName(path)), CloseButtonText = Strings.Ok };
            try { await dialog.ShowAsync(); } catch { }
            return;
        }
        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        _dropZone.Visibility = Visibility.Collapsed;
        _editor.Visibility = Visibility.Visible;
        _fileName.Text = Path.GetFileName(path);
        _fileInfo.Text = Strings.Loading;
        _status.Text = "";
        _resultButtons.Visibility = Visibility.Collapsed;
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
            _cropOn.IsOn = crop;
            _ratio.SelectedIndex = 0;
            bool animated = ImageEngine.IsAnimated(path);
            _fileInfo.Text = Strings.ImageInfo(preview.FullWidth, preview.FullHeight, Path.GetExtension(path).TrimStart('.').ToUpperInvariant(), animated);
            SetupFormats(path);
            await RenderAsync();
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

    private async Task RenderAsync()
    {
        if (_preview is null) return;
        (byte[] png, int fw, int fh) = _preview.Render(_rotation, _flipH, _flipV);
        var bmp = new BitmapImage();
        using (var ms = new MemoryStream(png))
            await bmp.SetSourceAsync(ms.AsRandomAccessStream());
        _canvas.SetImage(bmp, fw, fh, keepCrop: false);
        _canvas.SetEnabled(_cropOn.IsOn);
        SyncSizeFromCrop();
    }

    private async void Transform(int r = 0, bool h = false, bool v = false)
    {
        if (_preview is null) return;
        _rotation = ((_rotation + r) % 360 + 360) % 360;
        // A flip after a quarter turn flips the other axis of the original picture.
        if (h) _flipH = !_flipH;
        if (v) _flipV = !_flipV;
        _sizeTouched = false;
        await RenderAsync();
    }

    /// <summary>The size the edit starts from: the crop if cropping is on, the whole (rotated) image otherwise.</summary>
    private (double W, double H) BaseSize()
    {
        Rect c = _canvas.Crop;
        return _cropOn.IsOn ? (Math.Round(c.Width), Math.Round(c.Height)) : (_canvas.FullWidth, _canvas.FullHeight);
    }

    private void SyncSizeFromCrop()
    {
        (double w, double h) = BaseSize();
        _cropSize.Text = _cropOn.IsOn ? Strings.CropSize((int)w, (int)h) : "";
        if (_sizeTouched) return;
        _syncing = true;
        _width.Value = w;
        _height.Value = h;
        _syncing = false;
    }

    private void OnSizeTyped(bool widthChanged)
    {
        if (_syncing || double.IsNaN(_width.Value) || double.IsNaN(_height.Value)) return;
        _sizeTouched = true;
        if (_keepRatio.IsChecked != true) return;
        (double bw, double bh) = BaseSize();
        _syncing = true;
        if (widthChanged) _height.Value = Math.Max(1, Math.Round(_width.Value * bh / bw));
        else _width.Value = Math.Max(1, Math.Round(_height.Value * bw / bh));
        _syncing = false;
    }

    private void ScaleTo(double factor)
    {
        (double w, double h) = BaseSize();
        _syncing = true;
        _width.Value = Math.Max(1, Math.Round(w * factor));
        _height.Value = Math.Max(1, Math.Round(h * factor));
        _syncing = false;
        _sizeTouched = factor != 1;
    }

    private async Task SaveAsync()
    {
        if (_path is null || _preview is null) return;
        TargetFormat target = _format.SelectedIndex <= 0 ? _sameFormat : Formats[_format.SelectedIndex - 1];
        Rect c = _canvas.Crop;
        bool cropped = _cropOn.IsOn && (Math.Round(c.Width) < _canvas.FullWidth || Math.Round(c.Height) < _canvas.FullHeight);
        (double bw, double bh) = BaseSize();
        int w = double.IsNaN(_width.Value) ? (int)bw : (int)_width.Value;
        int h = double.IsNaN(_height.Value) ? (int)bh : (int)_height.Value;
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
        _progress.Visibility = Visibility.Visible;
        _resultButtons.Visibility = Visibility.Collapsed;
        _status.Text = Strings.Saving;
        try
        {
            await ImageEngine.EditAsync(_path, output, edit, target, (Quality)Math.Max(0, _quality.SelectedIndex), CancellationToken.None);
            _output = output;
            _status.Text = Strings.DoneSaved(Path.GetFileName(output));
            _resultButtons.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            OutputPaths.TryDelete(output);
            _status.Text = Ui.Describe(ex);
        }
        finally
        {
            _progress.Visibility = Visibility.Collapsed;
            _save.IsEnabled = true;
        }
    }
}
