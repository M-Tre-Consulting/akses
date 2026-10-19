using System.Diagnostics;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Akses.App;

/// <summary>
/// The "Privacy, licenza e contatti" screen required by the M-Tre brand guide
/// (m-tre-site/brand/README.md, section 3): logo, app name, version and copyright;
/// the app's own privacy policy; owner; license and credits; contacts; last-updated date.
/// Built in code rather than XAML so it adds no .xbf the publish step would have to
/// copy by hand (see installer/build-installer.ps1).
/// </summary>
internal static class LegalDialog
{
    private static bool _isOpen;

    public static async Task ShowAsync(XamlRoot xamlRoot)
    {
        if (_isOpen) return;
        _isOpen = true;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = xamlRoot,
                Content = new ScrollViewer
                {
                    Content = BuildContent(),
                    MaxHeight = 520,
                    Padding = new Thickness(0, 0, 16, 0),
                },
                CloseButtonText = Strings.Close,
                DefaultButton = ContentDialogButton.Close,
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            // ShowAsync throws if another ContentDialog is already open.
            AppLog.Write($"  LegalDialog non mostrato: {ex.Message}");
        }
        finally
        {
            _isOpen = false;
        }
    }

    public static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { AppLog.Write($"  Apertura di {url} fallita: {ex.Message}"); }
    }

    /// <summary>The app icon (the same .ico as the exe, tray and taskbar), for the title bar.</summary>
    public static ImageSource? LoadAppIcon()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        return File.Exists(path) ? new BitmapImage(new Uri(path)) : null;
    }

    public static ImageSource? LoadLogo()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "MTreLogo.png");
        return File.Exists(path) ? new BitmapImage(new Uri(path)) : null;
    }

    private static StackPanel BuildContent()
    {
        var root = new StackPanel { Spacing = 18, MaxWidth = 520 };

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        header.Children.Add(new Image { Source = LoadLogo(), Width = 44, Height = 44, VerticalAlignment = VerticalAlignment.Top });
        var titles = new StackPanel { Spacing = 2 };
        titles.Children.Add(new TextBlock { Text = Strings.AppName, FontSize = 20, FontWeight = FontWeights.SemiBold });
        titles.Children.Add(Secondary(Strings.VersionLabel));
        titles.Children.Add(Secondary(Strings.CopyrightLine));
        header.Children.Add(titles);
        root.Children.Add(header);

        var privacy = Strings.PrivacyParagraphs.Select(Paragraph).ToList();
        privacy.Add(Paragraph(Strings.PrivacyOnSite));
        privacy.Add(Link(Strings.PrivacyPageUrl.Replace("https://", ""), Strings.PrivacyPageUrl));
        root.Children.Add(Section(Strings.PrivacyHeading, privacy));

        var owners = new List<UIElement> { Paragraph(Strings.OwnerLine) };
        owners.AddRange(Strings.CompanyOwners.Select(Secondary));
        root.Children.Add(Section(Strings.OwnerHeading, owners));

        root.Children.Add(Section(Strings.LicenseHeading, Strings.LicenseParagraphs.Select(Paragraph)));

        root.Children.Add(Section(Strings.ContactsHeading,
        [
            Link(Strings.CompanySite, Strings.CompanySite),
            Link(Strings.CompanyEmail, "mailto:" + Strings.CompanyEmail),
        ]));

        root.Children.Add(new TextBlock
        {
            Text = Strings.LastUpdated,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
        });
        return root;
    }

    private static StackPanel Section(string heading, IEnumerable<UIElement> children)
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = heading, FontSize = 16, FontWeight = FontWeights.SemiBold });
        foreach (UIElement child in children) panel.Children.Add(child);
        return panel;
    }

    private static UIElement Paragraph(string text) =>
        new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };

    private static UIElement Secondary(string text) => new TextBlock
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
    };

    private static UIElement Link(string text, string url)
    {
        var link = new HyperlinkButton { Content = text, Padding = new Thickness(0) };
        link.Click += (_, _) => OpenUrl(url);
        return link;
    }
}
