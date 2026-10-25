using Akses.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Akses.App;

/// <summary>
/// One idea: give Akses a file, it shows everything you can do with it. So the window has no menu,
/// just one screen at a time: the drop area, the file's page (<see cref="FileView"/>), and the tool
/// a file's action opens (trim, image editor, PDF tools), each with "← Indietro". Same layout as the
/// Windows app's MainWindow, with the system title bar.
/// </summary>
internal sealed class MainWindow : Window
{
    private readonly Panel _pageHost = new();
    private readonly Grid _home;
    private readonly FileView _file = new();
    private readonly TrimPage _trim = new();
    private readonly ImagePage _image = new();
    private readonly PdfPage _pdf = new();
    private readonly SettingsPage _settings = new();
    private Control? _beforeSettings;

    public MainWindow()
    {
        Title = "Akses";
        Width = 1060;
        Height = 800;
        MinWidth = 640;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        this.Res(BackgroundProperty, Palette.Window);
        string icon = Path.Combine(AppContext.BaseDirectory, "Assets", "akses.png");
        if (File.Exists(icon)) Icon = new WindowIcon(icon);
        Ui.Window = this;

        Converter.HtmlToPdf = new LibreOfficePdfPrinter();

        var root = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(0, 12, 0, 0) };
        root.Children.Add(_pageHost);
        var signature = BuildSignature();
        Grid.SetRow(signature, 1);
        root.Children.Add(signature);

        // Dialogs (Dialogs.cs) open on this layer, above everything.
        var dialogLayer = new Panel { IsHitTestVisible = false };
        Dialogs.Host = dialogLayer;
        var all = new Panel();
        all.Children.Add(root);
        all.Children.Add(dialogLayer);
        Content = all;

        _home = BuildHome();
        _file.NewFileRequested += () => { _file.Clear(); Show(_home); };
        _file.SettingsRequested += ShowSettings;
        _file.ToolRequested += OpenTool;
        _trim.BackRequested += BackToFile;
        _image.BackRequested += BackToFile;
        _pdf.BackRequested += BackToFile;
        _settings.BackRequested += () => Show(_beforeSettings ?? _home);
        _settings.OutputFolderChanged += _file.UpdateSaveTo;
        Show(_home);

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Space && _pageHost.Children.FirstOrDefault() == _trim && FocusManager?.GetFocusedElement() is not TextBox)
            {
                _trim.TogglePlayFromKeyboard();
                e.Handled = true;
            }
        };
        Closing += (_, _) => _trim.Shutdown();
    }

    private Grid BuildHome()
    {
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Margin = new Thickness(28, 4, 28, 20) };

        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        top.Children.Add(Ui.Header(Strings.HomeTitle, Strings.HomeSubtitle));
        var gear = Ui.IconButton(Icons.Settings, Strings.SettingsTitle, ShowSettings);
        gear.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(gear, 1);
        top.Children.Add(gear);
        root.Children.Add(top);

        var drop = Ui.DropZone(Strings.DropTitle, Strings.ConvertDropSubtitle(FormatCatalog.InputExtensions.Count), Strings.ChooseFiles,
            async () => OpenFiles(await Ui.PickFilesAsync()));
        Grid.SetRow(drop, 1);
        root.Children.Add(drop);
        Ui.AcceptFileDrops(root, OpenFiles);
        return root;
    }

    /// <summary>
    /// M-Tre signature (brand guide, m-tre-site/brand/README.md section 1): always visible. Logo +
    /// name open the company site; the year opens "Privacy, licenza e contatti". Not localized.
    /// </summary>
    private static Grid BuildSignature()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(14, 2, 14, 4) };

        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        string logo = Path.Combine(AppContext.BaseDirectory, "Assets", "MTreLogo.png");
        if (File.Exists(logo)) brand.Children.Add(new Image { Source = new Bitmap(logo), Width = 16, Height = 16 });
        brand.Children.Add(new TextBlock { Text = Strings.CompanyName, FontSize = 11, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center }
            .Res(TextBlock.ForegroundProperty, Palette.TextSecondary));
        grid.Children.Add(Plain(brand, Strings.CompanySite, () => Ui.OpenUrl(Strings.CompanySite)));

        var year = Plain(new TextBlock { Text = Strings.CopyrightYear, FontSize = 11, FontWeight = FontWeight.Medium }
            .Res(TextBlock.ForegroundProperty, Palette.TextSecondary), Strings.LegalTitle, () => _ = LegalDialog.ShowAsync());
        Grid.SetColumn(year, 2);
        grid.Children.Add(year);
        return grid;

        static Button Plain(Control content, string tip, Action onClick)
        {
            var b = new Button { Content = content, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(6, 4), Cursor = new Cursor(StandardCursorType.Hand) };
            ToolTip.SetTip(b, tip);
            b.Click += (_, _) => onClick();
            return b;
        }
    }

    private void Show(Control page)
    {
        if (page != _trim) _trim.PausePlayback();
        _pageHost.Children.Clear();
        _pageHost.Children.Add(page);
    }

    private void ShowSettings()
    {
        Control? current = _pageHost.Children.FirstOrDefault();
        if (current != _settings) _beforeSettings = current;
        Show(_settings);
    }

    private void BackToFile() => Show(_file.HasFiles ? _file : _home);

    private void OpenFiles(IReadOnlyList<string> files)
    {
        if (files.Count == 0) return;
        _file.AddFiles(files);
        Show(_file);
    }

    private void OpenTool(FileView.Tool tool, IReadOnlyList<string> files)
    {
        switch (tool)
        {
            case FileView.Tool.Trim:
                _trim.Load(files[0]);
                Show(_trim);
                break;
            case FileView.Tool.Crop or FileView.Tool.Image:
                _ = _image.LoadAsync(files[0], crop: tool == FileView.Tool.Crop);
                Show(_image);
                break;
            default:
                _pdf.Open(tool switch
                {
                    FileView.Tool.PdfExtract => PdfTool.Extract,
                    FileView.Tool.PdfRotate => PdfTool.Rotate,
                    _ => PdfTool.Merge,
                }, files);
                Show(_pdf);
                break;
        }
    }

    /// <summary>
    /// A command line, from this launch or from a later one forwarded by <see cref="Program"/>
    /// ("Apri con Akses" in the file manager): <c>files…</c> opens their page; <c>--to &lt;format&gt;
    /// files…</c> also converts them right away; <c>--trim</c> / <c>--edit</c> open the tool.
    /// </summary>
    public void HandleArgs(string[] args, bool bringToFront = false)
    {
        string? target = null, tool = null;
        var files = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if (a == "--to" && i + 1 < args.Length) target = args[++i];
            else if (a is "--trim" or "--edit") tool = a;
            // Tools run in their own folder: a relative path from a terminal must become absolute.
            else if (!a.StartsWith("--", StringComparison.Ordinal)) files.Add(Path.GetFullPath(a.StartsWith("file://", StringComparison.Ordinal) ? new Uri(a).LocalPath : a));
        }
        IReadOnlyList<string> expanded = Ui.ExpandFolders(files);

        if (bringToFront)
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
        }
        if (expanded.Count == 0) return;

        OpenFiles(expanded);
        if (target is not null) _file.ConvertNow(expanded, target);
        else if (tool == "--trim") OpenTool(FileView.Tool.Trim, expanded);
        else if (tool == "--edit") OpenTool(FileView.Tool.Image, expanded);
    }
}
