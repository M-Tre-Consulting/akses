using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Akses.App;

/// <summary>
/// The "Privacy, licenza e contatti" screen required by the M-Tre brand guide
/// (m-tre-site/brand/README.md, section 3): logo, app name, version and copyright; the app's own
/// privacy policy; owner; license and credits; contacts; last-updated date. Same content as the
/// Windows app's LegalDialog (the texts in Strings.cs have the Linux variants).
/// </summary>
internal static class LegalDialog
{
    public static Task ShowAsync()
    {
        var scroller = new ScrollViewer { Content = BuildContent(), MaxHeight = 520, Padding = new Thickness(0, 0, 16, 0) };
        return Dialogs.ShowAsync(null, scroller, Strings.Close);
    }

    private static StackPanel BuildContent()
    {
        var root = new StackPanel { Spacing = 18, MaxWidth = 520 };

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        string logo = Path.Combine(AppContext.BaseDirectory, "Assets", "MTreLogo.png");
        if (File.Exists(logo)) header.Children.Add(new Image { Source = new Bitmap(logo), Width = 44, Height = 44, VerticalAlignment = VerticalAlignment.Top });
        var titles = new StackPanel { Spacing = 2 };
        titles.Children.Add(new TextBlock { Text = Strings.AppName, FontSize = 20, FontWeight = FontWeight.SemiBold });
        titles.Children.Add(Secondary(Strings.VersionLabel));
        titles.Children.Add(Secondary(Strings.CopyrightLine));
        header.Children.Add(titles);
        root.Children.Add(header);

        var privacy = Strings.PrivacyParagraphs.Select(Paragraph).ToList();
        privacy.Add(Paragraph(Strings.PrivacyOnSite));
        privacy.Add(Link(Strings.PrivacyPageUrl.Replace("https://", ""), Strings.PrivacyPageUrl));
        root.Children.Add(Section(Strings.PrivacyHeading, privacy));

        var owners = new List<Control> { Paragraph(Strings.OwnerLine) };
        owners.AddRange(Strings.CompanyOwners.Select(Secondary));
        root.Children.Add(Section(Strings.OwnerHeading, owners));

        root.Children.Add(Section(Strings.LicenseHeading, Strings.LicenseParagraphs.Select(Paragraph)));

        root.Children.Add(Section(Strings.ContactsHeading,
        [
            Link(Strings.CompanySite, Strings.CompanySite),
            Link(Strings.CompanyEmail, "mailto:" + Strings.CompanyEmail),
        ]));

        root.Children.Add(new TextBlock { Text = Strings.LastUpdated, FontSize = 12, Opacity = 0.6 });
        return root;
    }

    private static StackPanel Section(string heading, IEnumerable<Control> children)
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = heading, FontSize = 16, FontWeight = FontWeight.SemiBold });
        foreach (Control child in children) panel.Children.Add(child);
        return panel;
    }

    private static Control Paragraph(string text) => new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap };

    private static Control Secondary(string text) =>
        new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }.Res(TextBlock.ForegroundProperty, Palette.TextSecondary);

    private static Control Link(string text, string url)
    {
        var link = Ui.Link(text, () => Ui.OpenUrl(url));
        link.Padding = new Thickness(0);
        link.HorizontalAlignment = HorizontalAlignment.Left;
        return link;
    }
}
