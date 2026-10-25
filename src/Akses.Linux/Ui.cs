using System.Diagnostics;
using System.Globalization;
using Akses.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace Akses.App;

/// <summary>Small shared pieces of UI plumbing: pickers, drag and drop, opening results, error text, building blocks.</summary>
internal static class Ui
{
    /// <summary>The main window, for the file pickers.</summary>
    public static TopLevel? Window { get; set; }

    /// <summary>Binds <paramref name="property"/> to a light/dark resource of <see cref="Palette"/>.</summary>
    public static T Res<T>(this T control, AvaloniaProperty property, string key) where T : Control
    {
        control.Bind(property, control.GetResourceObservable(key));
        return control;
    }

    // ---- files in ----------------------------------------------------------------------------

    public static async Task<IReadOnlyList<string>> PickFilesAsync(bool multiple = true)
    {
        if (Window is null) return [];
        IReadOnlyList<IStorageFile> files = await Window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = multiple,
            Title = multiple ? Strings.ChooseFiles : Strings.ChooseFile,
        });
        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }

    public static async Task<string?> PickFolderAsync()
    {
        if (Window is null) return null;
        IReadOnlyList<IStorageFolder> folders = await Window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            Title = Strings.ChooseFolder,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    /// <summary>Accepts files dropped from the file manager on <paramref name="target"/>; folders are expanded (up to 500 files).</summary>
#pragma warning disable CS0618 // DragEventArgs.Data: still the simplest way to get the dropped files in Avalonia 11.3
    public static void AcceptFileDrops(Control target, Action<IReadOnlyList<string>> onFiles)
    {
        DragDrop.SetAllowDrop(target, true);
        target.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        });
        target.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            e.Handled = true;
            try
            {
                IEnumerable<IStorageItem> items = e.Data.GetFiles() ?? [];
                onFiles(ExpandFolders(items.Select(i => i.TryGetLocalPath()).OfType<string>()));
            }
            catch (Exception ex)
            {
                AppLog.Write($"Drop fallito: {ex.Message}");
            }
        });
    }
#pragma warning restore CS0618

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

    public static void Open(string path) => Run("xdg-open", path);

    /// <summary>
    /// The file selected in the file manager (Nemo, Nautilus, Dolphin… all answer the FreeDesktop
    /// FileManager1 call); just its folder when that isn't available.
    /// </summary>
    public static void ShowInFolder(string path)
    {
        if (Directory.Exists(path)) { Run("xdg-open", path); return; }
        try
        {
            var psi = new ProcessStartInfo("dbus-send") { UseShellExecute = false };
            foreach (string a in new[] { "--session", "--print-reply", "--dest=org.freedesktop.FileManager1", "--type=method_call",
                         "/org/freedesktop/FileManager1", "org.freedesktop.FileManager1.ShowItems",
                         "array:string:" + new Uri(path).AbsoluteUri, "string:" })
                psi.ArgumentList.Add(a);
            psi.RedirectStandardOutput = psi.RedirectStandardError = true;
            using Process p = Process.Start(psi)!;
            if (p.WaitForExit(3000) && p.ExitCode == 0) return;
        }
        catch (Exception ex)
        {
            AppLog.Write($"FileManager1 non disponibile: {ex.Message}");
        }
        Run("xdg-open", System.IO.Path.GetDirectoryName(path) ?? path);
    }

    public static void OpenUrl(string url) => Run("xdg-open", url);

    private static void Run(string exe, string arg)
    {
        try
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
            psi.ArgumentList.Add(arg);
            using Process? _ = Process.Start(psi);
        }
        catch (Exception ex) { AppLog.Write($"{exe} {arg} fallito: {ex.Message}"); }
    }

    /// <summary>A short, user-facing explanation of why a job failed (details go to the log).</summary>
    public static string Describe(Exception ex)
    {
        AppLog.Write($"Operazione fallita: {ex}");
        return ex switch
        {
            ToolMissingException { Exe: "soffice" } => Strings.LibreOfficeMissing,
            ToolMissingException => Strings.ToolMissing,
            InvalidDataException { Message: "no audio track" } => Strings.NoAudioTrack,
            InvalidDataException { Message: "no video track" } => Strings.NoVideoTrack,
            UnauthorizedAccessException => Strings.AccessDenied,
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
        stack.Children.Add(new TextBlock { Text = title, FontSize = 28, FontWeight = FontWeight.SemiBold });
        stack.Children.Add(new TextBlock { Text = subtitle, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
        return stack;
    }

    /// <summary>Header of a tool opened from a file's page: "← Indietro", then title and explanation.</summary>
    public static StackPanel EditorHeader(string title, string subtitle, Action onBack)
    {
        var stack = Header(title, subtitle);
        var back = Link(Strings.Back, onBack, Icons.Back);
        back.Margin = new Thickness(-6, 0, 0, 4);
        stack.Children.Insert(0, back);
        return stack;
    }

    /// <summary>A text link in the accent colour (WinUI's HyperlinkButton), optionally with an icon before it.</summary>
    public static Button Link(string text, Action onClick, string? icon = null)
    {
        object content = text;
        if (icon is not null)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            panel.Children.Add(new Glyph(icon, 13) { VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            content = panel;
        }
        var b = new Button
        {
            Content = content,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 4),
            Cursor = new Cursor(StandardCursorType.Hand),
        }.Res(Button.ForegroundProperty, Palette.AccentText);
        b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>A square button with just an icon and a tooltip (open, show in folder, remove…).</summary>
    public static Button IconButton(string icon, string tooltip, Action onClick)
    {
        var b = new Button { Content = new Glyph(icon, 16), Padding = new Thickness(9, 7) };
        ToolTip.SetTip(b, tooltip);
        Avalonia.Automation.AutomationProperties.SetName(b, tooltip);
        b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>A button with an icon and a text.</summary>
    public static Button TextButton(string text, string icon, Action onClick)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(new Glyph(icon, 14) { VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        var b = new Button { Content = content };
        b.Click += (_, _) => onClick();
        return b;
    }

    public static Button AccentButton(string text)
    {
        var b = new Button { Content = text, Padding = new Thickness(22, 7) };
        b.Classes.Add("accent");
        return b;
    }

    /// <summary>A label above a control (WinUI's Header on ComboBox / TextBox / NumberBox).</summary>
    public static StackPanel Labeled(string label, Control control)
    {
        var s = new StackPanel { Spacing = 6 };
        s.Children.Add(new TextBlock { Text = label });
        s.Children.Add(control);
        return s;
    }

    /// <summary>A section label inside a page ("Ruota e rifletti", "Dimensioni"…).</summary>
    public static TextBlock SectionTitle(string text) =>
        new() { Text = text, FontSize = 15, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0) };

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
        }.Res(Shape.StrokeProperty, Palette.Accent).Res(Shape.FillProperty, Palette.Card));
        var stack = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24) };
        stack.Children.Add(new Glyph(Icons.Upload, 40) { HorizontalAlignment = HorizontalAlignment.Center }.Res(Glyph.ForegroundProperty, Palette.AccentText));
        stack.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(new TextBlock { Text = subtitle, Opacity = 0.7, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 });
        var button = AccentButton(buttonText);
        button.HorizontalAlignment = HorizontalAlignment.Center;
        button.Margin = new Thickness(0, 8, 0, 0);
        button.Click += async (_, _) => await onPick();
        stack.Children.Add(button);
        grid.Children.Add(stack);
        return grid;
    }

    /// <summary>A rounded panel in the "subtle" fill (previews, timeline, thumbnail).</summary>
    public static Border SubtlePanel(Control? child = null) =>
        new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = child }.Res(Border.BackgroundProperty, Palette.Subtle);

    public static string FormatTime(TimeSpan t, bool withMillis = true)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        string main = t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes:00}:{t.Seconds:00}";
        return withMillis ? $"{main}.{t.Milliseconds / 10:00}" : main;
    }

    /// <summary>Parses "1:02.5", "62,5", "01:02:03.25" (comma or dot) into a time; null when it isn't one.</summary>
    public static TimeSpan? ParseTime(string? text)
    {
        string[] parts = (text ?? "").Trim().Replace(',', '.').Split(':');
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
