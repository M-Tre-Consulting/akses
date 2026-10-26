using System.Runtime.InteropServices;
using Akses.Core;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Akses.App;

/// <summary>
/// One idea: give Akses a file, it shows everything you can do with it. So the window has no menu,
/// just one screen at a time: the drop area, the file's page (<see cref="FileView"/>), and the tool
/// a file's action opens (trim, image editor, PDF tools), each with "← Indietro".
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly Grid _home;
    private readonly FileView _file = new();
    private readonly TrimPage _trim = new();
    private readonly ImagePage _image = new();
    private readonly PdfPage _pdf = new();
    private readonly JoinPage _join = new();
    private readonly SettingsPage _settings = new();
    private UIElement? _beforeSettings;

    public MainWindow()
    {
        InitializeComponent();
        Title = "Akses";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarArea);
        SystemBackdrop = new MicaBackdrop();

        BrandLogo.Source = LegalDialog.LoadLogo();
        TitleIcon.Source = LegalDialog.LoadAppIcon();

        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        Ui.WindowHandle = hwnd;
        AppWindow appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(iconPath)) appWindow.SetIcon(iconPath);
        double scale = GetDpiForWindow(hwnd) / 96.0;
        appWindow.Resize(new Windows.Graphics.SizeInt32((int)(1060 * scale), (int)(800 * scale)));
        CenterOnScreen(appWindow);

        Converter.HtmlToPdf = new WebViewPdfPrinter(PdfWebView, DispatcherQueue);

        _home = BuildHome();
        _file.NewFileRequested += () => { _file.Clear(); Show(_home); };
        _file.SettingsRequested += ShowSettings;
        _file.ToolRequested += OpenTool;
        _trim.BackRequested += BackToFile;
        _image.BackRequested += BackToFile;
        _pdf.BackRequested += BackToFile;
        _join.BackRequested += BackToFile;
        _settings.BackRequested += () => Show(_beforeSettings ?? _home);
        _settings.OutputFolderChanged += _file.UpdateSaveTo;
        Show(_home);

        BuildUpdateBar();
        AutoUpdate.Ready += () => DispatcherQueue.TryEnqueue(ShowUpdateBar);
        AutoUpdate.Start();
        // A downloaded update is installed when Akses closes, silently: the next start is the new version.
        Closed += (_, _) => AutoUpdate.Launch(relaunch: false);
    }

    private readonly StackPanel _updateBar = new() { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, Visibility = Visibility.Collapsed };
    private readonly TextBlock _updateText = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>"Akses 1.2.0 è pronto · Aggiorna e riavvia", in the middle of the signature row.</summary>
    private void BuildUpdateBar()
    {
        _updateBar.Children.Add(new FontIcon { Glyph = "", FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Foreground = FileCard.Brush("AccentTextFillColorPrimaryBrush") });
        _updateBar.Children.Add(_updateText);
        var install = new HyperlinkButton { Content = Strings.UpdateInstall, FontSize = 12, Padding = new Thickness(6, 2, 6, 2) };
        install.Click += (_, _) => { if (AutoUpdate.Launch(relaunch: true)) Close(); };
        _updateBar.Children.Add(install);
        Grid.SetColumn(_updateBar, 1);
        SignatureGrid.Children.Add(_updateBar);
    }

    private void ShowUpdateBar()
    {
        if (AutoUpdate.Pending is not { } update) return;
        _updateText.Text = Strings.UpdateReady(update.Version.ToString(3));
        _updateBar.Visibility = Visibility.Visible;
    }

    private Grid BuildHome()
    {
        var root = new Grid { Padding = new Thickness(28, 4, 28, 20), RowSpacing = 16 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(Ui.Header(Strings.HomeTitle, Strings.HomeSubtitle));
        var gear = FileCard.IconButton("\uE713", Strings.SettingsTitle, ShowSettings);
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

    private void CenterOnScreen(AppWindow appWindow)
    {
        DisplayArea area = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);
        var work = area.WorkArea;
        appWindow.Move(new Windows.Graphics.PointInt32(
            work.X + Math.Max(0, (work.Width - appWindow.Size.Width) / 2),
            work.Y + Math.Max(0, (work.Height - appWindow.Size.Height) / 2)));
    }

    private void Show(UIElement page)
    {
        if (page != _trim) _trim.PausePlayback();
        PageHost.Children.Clear();
        PageHost.Children.Add(page);
    }

    private void ShowSettings()
    {
        UIElement? current = PageHost.Children.FirstOrDefault();
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
            case FileView.Tool.Join:
                _join.Open(files);
                Show(_join);
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
    /// ("Apri con Akses" in Explorer): <c>files…</c> opens their page; <c>--to &lt;format&gt;
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
            else if (!a.StartsWith("--", StringComparison.Ordinal)) files.Add(a);
        }
        IReadOnlyList<string> expanded = Ui.ExpandFolders(files);

        if (bringToFront) BringToFront();
        if (expanded.Count == 0) return;

        OpenFiles(expanded);
        if (target is not null) _file.ConvertNow(expanded, target);
        else if (tool == "--trim") OpenTool(FileView.Tool.Trim, expanded);
        else if (tool == "--edit") OpenTool(FileView.Tool.Image, expanded);
    }

    private void BringToFront()
    {
        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (IsIconic(hwnd)) ShowWindow(hwnd, 9 /* SW_RESTORE */);
        Activate();
        SetForegroundWindow(hwnd);
    }

    private void OnBrandSiteClicked(object sender, RoutedEventArgs e) => LegalDialog.OpenUrl(Strings.CompanySite);

    private async void OnBrandLegalClicked(object sender, RoutedEventArgs e) => await LegalDialog.ShowAsync(Content.XamlRoot);

    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
}
