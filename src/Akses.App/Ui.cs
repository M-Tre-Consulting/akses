using System.Diagnostics;
using System.Globalization;
using Akses.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Akses.App;

/// <summary>Small shared pieces of UI plumbing: pickers, drag and drop, opening results, glyphs, error text.</summary>
internal static class Ui
{
    /// <summary>Window handle for the file pickers (unpackaged WinUI 3 pickers need it).</summary>
    public static IntPtr WindowHandle { get; set; }

    public static string GlyphFor(FileKind kind) => kind switch
    {
        FileKind.Image => "\uEB9F",
        FileKind.Video => "\uE714",
        FileKind.Audio => "\uE8D6",
        FileKind.Pdf => "\uE8A5",
        FileKind.Document => "\uE8A5",
        FileKind.Data => "\uE943",
        FileKind.Subtitle => "\uE7F0",
        _ => "\uE9CE",
    };

    // ---- files in ----------------------------------------------------------------------------

    public static async Task<IReadOnlyList<string>> PickFilesAsync(bool multiple = true)
    {
        var picker = new FileOpenPicker { ViewMode = PickerViewMode.List, SuggestedStartLocation = PickerLocationId.Desktop };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHandle);
        if (multiple)
        {
            IReadOnlyList<StorageFile> files = await picker.PickMultipleFilesAsync();
            return files.Select(f => f.Path).ToList();
        }
        StorageFile? file = await picker.PickSingleFileAsync();
        return file is null ? [] : [file.Path];
    }

    public static async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.Desktop };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHandle);
        StorageFolder? folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    /// <summary>Accepts files dropped from Explorer on <paramref name="target"/>; folders are expanded (up to 500 files).</summary>
    public static void AcceptFileDrops(UIElement target, Action<IReadOnlyList<string>> onFiles)
    {
        target.AllowDrop = true;
        target.DragOver += (_, e) =>
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = Strings.DropCaption;
        };
        target.Drop += async (_, e) =>
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
            var deferral = e.GetDeferral();
            try
            {
                IReadOnlyList<IStorageItem> items = await e.DataView.GetStorageItemsAsync();
                onFiles(ExpandFolders(items.Select(i => i.Path)));
            }
            catch (Exception ex)
            {
                AppLog.Write($"Drop fallito: {ex.Message}");
            }
            finally
            {
                deferral.Complete();
            }
        };
    }

    public static IReadOnlyList<string> ExpandFolders(IEnumerable<string> paths)
    {
        var files = new List<string>();
        foreach (string p in paths)
        {
            if (File.Exists(p)) files.Add(p);
            else if (Directory.Exists(p))
            {
                try
                {
                    files.AddRange(Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories)
                        .Where(f => FormatCatalog.KindOf(f) != FileKind.Unknown).Take(500));
                }
                catch (Exception ex) { AppLog.Write($"Cartella {p} non leggibile: {ex.Message}"); }
            }
        }
        return files;
    }

    // ---- results out -------------------------------------------------------------------------

    public static void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { AppLog.Write($"Apertura di {path} fallita: {ex.Message}"); }
    }

    public static void ShowInFolder(string path)
    {
        try
        {
            if (Directory.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            else Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { AppLog.Write($"Esplora file su {path} fallito: {ex.Message}"); }
    }

    /// <summary>A short, user-facing explanation of why a job failed (details go to the log).</summary>
    public static string Describe(Exception ex)
    {
        AppLog.Write($"Operazione fallita: {ex}");
        return ex switch
        {
            ToolMissingException => Strings.ToolMissing,
            InvalidDataException { Message: "no audio track" } => Strings.NoAudioTrack,
            InvalidDataException { Message: "no video track" } => Strings.NoVideoTrack,
            UnauthorizedAccessException => Strings.AccessDenied,
            IOException io when io.HResult == unchecked((int)0x80070020) => Strings.FileInUse,
            ImageMagick.MagickException => Strings.CannotRead,
            PdfSharp.Pdf.IO.PdfReaderException => Strings.PdfProtected,
            ToolFailedException t => Strings.Failed(FirstLine(t.Message)),
            _ => Strings.Failed(FirstLine(ex.Message)),
        };
    }

    private static string FirstLine(string s)
    {
        string line = s.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? s;
        return line.Length > 160 ? line[..160] + "…" : line;
    }

    // ---- building blocks ---------------------------------------------------------------------

    /// <summary>Page title + one line of explanation, the same on every screen.</summary>
    public static StackPanel Header(string title, string subtitle)
    {
        var stack = new StackPanel { Spacing = 2, Margin = new Thickness(0, 4, 0, 18) };
        stack.Children.Add(new TextBlock { Text = title, FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        stack.Children.Add(new TextBlock { Text = subtitle, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
        return stack;
    }

    /// <summary>Header of a tool opened from a file's page: "← Indietro", then title and explanation.</summary>
    public static StackPanel EditorHeader(string title, string subtitle, Action onBack)
    {
        var stack = Header(title, subtitle);
        var back = new HyperlinkButton { Padding = new Thickness(4, 2, 6, 2), Margin = new Thickness(-4, 0, 0, 4) };
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(new FontIcon { Glyph = "\uE72B", FontSize = 12 });
        content.Children.Add(new TextBlock { Text = Strings.Back });
        back.Content = content;
        back.Click += (_, _) => onBack();
        stack.Children.Insert(0, back);
        return stack;
    }

    /// <summary>A section label inside a page ("Ruota e rifletti", "Dimensioni"…).</summary>
    public static TextBlock SectionTitle(string text) =>
        new() { Text = text, FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0) };

    /// <summary>The big dashed "drop your files here" area used by every tool's empty state.</summary>
    public static Grid DropZone(string title, string subtitle, string buttonText, Func<Task> onPick)
    {
        var grid = new Grid { MinHeight = 260 };
        grid.Children.Add(new Rectangle
        {
            RadiusX = 14,
            RadiusY = 14,
            StrokeThickness = 2,
            StrokeDashArray = [4, 3],
            Stroke = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
            Fill = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
        });
        var stack = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(24) };
        stack.Children.Add(new FontIcon { Glyph = "\uE898", FontSize = 40, Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"] });
        stack.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(new TextBlock { Text = subtitle, Opacity = 0.7, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 });
        var button = new Button { Content = buttonText, Style = (Style)Application.Current.Resources["AccentButtonStyle"], HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        button.Click += async (_, _) => await onPick();
        stack.Children.Add(button);
        grid.Children.Add(stack);
        return grid;
    }

    public static string FormatTime(TimeSpan t, bool withMillis = true)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        string main = t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes:00}:{t.Seconds:00}";
        return withMillis ? $"{main}.{t.Milliseconds / 10:00}" : main;
    }

    /// <summary>Parses "1:02.5", "62,5", "01:02:03.25" (comma or dot) into a time; null when it isn't one.</summary>
    public static TimeSpan? ParseTime(string text)
    {
        string[] parts = text.Trim().Replace(',', '.').Split(':');
        if (parts.Length is < 1 or > 3) return null;
        double seconds = 0;
        foreach (string part in parts)
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || v < 0) return null;
            seconds = seconds * 60 + v;
        }
        return TimeSpan.FromSeconds(seconds);
    }
}
