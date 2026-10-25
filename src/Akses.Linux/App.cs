using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Akses.App;

/// <summary>
/// The Avalonia application: Fluent theme in the Akses coral, light or dark like the desktop, and
/// the few colours the pages share (<see cref="Palette"/>). No XAML, like the Windows app's pages.
/// </summary>
internal sealed class App : Application
{
    private MainWindow? _window;

    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Default;
        Styles.Add(new FluentTheme
        {
            Palettes =
            {
                [ThemeVariant.Light] = new ColorPaletteResources { Accent = Color.Parse("#E8552A") },
                [ThemeVariant.Dark] = new ColorPaletteResources { Accent = Color.Parse("#FF6A3D") },
            },
        });
        Palette.AddResources(Resources);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Previews and work files of the previous session (the privacy policy promises this).
            _ = Task.Run(() => Akses.Core.OutputPaths.TryDelete(Path.Combine(Path.GetTempPath(), "Akses")));

            _window = new MainWindow();
            desktop.MainWindow = _window;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            _window.HandleArgs(Program.InitialArgs);
            Program.ListenForOtherLaunches(more =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() => _window.HandleArgs(more, bringToFront: true)));
            _ = Task.Run(ShellIntegration.RegisterOnFirstRun);
        }
        base.OnFrameworkInitializationCompleted();
    }
}

/// <summary>
/// The colours the Windows app takes from WinUI's theme resources (card, stroke, secondary text…),
/// as resources with a light and a dark value. <see cref="Ui.Res{T}"/> binds a property to one.
/// </summary>
internal static class Palette
{
    public const string Window = "AksesWindow";
    public const string Card = "AksesCard";
    public const string CardStroke = "AksesCardStroke";
    public const string Subtle = "AksesSubtle";
    public const string TextSecondary = "AksesTextSecondary";
    public const string Critical = "AksesCritical";
    public const string Accent = "AksesAccent";
    public const string AccentText = "AksesAccentText";
    public const string OnAccent = "AksesOnAccent";

    public static void AddResources(IResourceDictionary resources)
    {
        ResourceDictionary light = Dictionary(
            window: "#F6F3F1", card: "#FFFFFF", stroke: "#E4DDD9", subtle: "#EDE8E5", secondary: "#6B625E",
            critical: "#C42B1C", accent: "#E8552A", accentText: "#C2410C");
        AddButtonColors(light, background: "#FFFFFF", hover: "#F8F4F2", pressed: "#EFE9E6", border: "#DCD3CE", hoverBorder: "#CFC5BF");
        ResourceDictionary dark = Dictionary(
            window: "#1B1715", card: "#26211F", stroke: "#3A3330", subtle: "#2F2926", secondary: "#A8A09B",
            critical: "#FF99A4", accent: "#FF6A3D", accentText: "#FF8A5C");
        AddButtonColors(dark, background: "#2C2623", hover: "#352E2A", pressed: "#25201D", border: "#443B37", hoverBorder: "#524843");
        resources.ThemeDictionaries[ThemeVariant.Light] = light;
        resources.ThemeDictionaries[ThemeVariant.Dark] = dark;
        resources["ButtonBorderThemeThickness"] = new Thickness(1);
        resources["ToggleButtonBorderThemeThickness"] = new Thickness(1);
    }

    private static ResourceDictionary Dictionary(string window, string card, string stroke, string subtle, string secondary,
        string critical, string accent, string accentText) => new()
    {
        [Window] = Brush(window),
        [Card] = Brush(card),
        [CardStroke] = Brush(stroke),
        [Subtle] = Brush(subtle),
        [TextSecondary] = Brush(secondary),
        [Critical] = Brush(critical),
        [Accent] = Brush(accent),
        [AccentText] = Brush(accentText),
        [OnAccent] = Brushes.White,
    };

    /// <summary>
    /// Fluent's own button colours, closer to WinUI's: a card-coloured button with a thin border
    /// instead of Avalonia's grey fill.
    /// </summary>
    private static void AddButtonColors(ResourceDictionary d, string background, string hover, string pressed, string border, string hoverBorder)
    {
        foreach (string prefix in new[] { "Button", "ToggleButton", "RepeatButton" })
        {
            d[prefix + "Background"] = Brush(background);
            d[prefix + "BackgroundPointerOver"] = Brush(hover);
            d[prefix + "BackgroundPressed"] = Brush(pressed);
            d[prefix + "BorderBrush"] = Brush(border);
            d[prefix + "BorderBrushPointerOver"] = Brush(hoverBorder);
            d[prefix + "BorderBrushPressed"] = Brush(border);
        }
    }

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));
}
