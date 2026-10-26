using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Akses.App;

/// <summary>Where results are saved, the Explorer menu, and "Privacy, licenza e contatti".</summary>
internal sealed class SettingsPage : UserControl
{
    private readonly RadioButtons _where = new();
    private readonly TextBlock _folder = new() { Opacity = 0.75, TextWrapping = TextWrapping.Wrap };
    private readonly ToggleSwitch _shell = new();

    public event Action? BackRequested;

    /// <summary>The output folder changed (the convert screen shows it).</summary>
    public event Action? OutputFolderChanged;

    public SettingsPage()
    {
        var stack = new StackPanel { Spacing = 10, Padding = new Thickness(28, 4, 28, 24), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
        stack.Children.Add(Ui.EditorHeader(Strings.SettingsTitle, Strings.SettingsSubtitle, () => BackRequested?.Invoke()));

        stack.Children.Add(Ui.SectionTitle(Strings.WhereToSave));
        _where.Items.Add(Strings.SaveNextToOriginal);
        _where.Items.Add(Strings.SaveInFolder);
        _where.SelectedIndex = Settings.Current.OutputFolder is null ? 0 : 1;
        _where.SelectionChanged += async (_, _) =>
        {
            if (_where.SelectedIndex == 0) SetFolder(null);
            else if (Settings.Current.OutputFolder is null) await ChooseFolderAsync();
        };
        stack.Children.Add(_where);
        var choose = new Button { Content = Strings.ChooseFolder };
        choose.Click += async (_, _) => await ChooseFolderAsync();
        var folderRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(28, 0, 0, 0) };
        folderRow.Children.Add(choose);
        folderRow.Children.Add(_folder);
        stack.Children.Add(folderRow);
        UpdateFolder();

        stack.Children.Add(Ui.SectionTitle(Strings.ExplorerMenu));
        stack.Children.Add(new TextBlock { Text = Strings.ExplorerMenuHint, TextWrapping = TextWrapping.Wrap, Opacity = 0.75 });
        _shell.OnContent = Strings.On;
        _shell.OffContent = Strings.Off;
        _shell.IsOn = ShellIntegration.IsRegistered();
        _shell.Toggled += async (_, _) =>
        {
            bool on = _shell.IsOn;
            _shell.IsEnabled = false;
            await Task.Run(() => { if (on) ShellIntegration.Register(); else ShellIntegration.Unregister(); });
            _shell.IsEnabled = true;
        };
        stack.Children.Add(_shell);

        stack.Children.Add(Ui.SectionTitle(Strings.UpdatesSection));
        stack.Children.Add(new TextBlock { Text = Strings.UpdatesHint, TextWrapping = TextWrapping.Wrap, Opacity = 0.75 });
        var updates = new ToggleSwitch { OnContent = Strings.On, OffContent = Strings.Off, IsOn = Settings.Current.AutoUpdate };
        updates.Toggled += (_, _) => { Settings.Current.AutoUpdate = updates.IsOn; Settings.Current.Save(); };
        stack.Children.Add(updates);
        var checkRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var check = new Button { Content = Strings.CheckNow };
        var checkStatus = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Opacity = 0.75 };
        check.Click += async (_, _) =>
        {
            check.IsEnabled = false;
            checkStatus.Text = Strings.UpdateChecking;
            try
            {
                checkStatus.Text = await AutoUpdate.CheckNowAsync() && AutoUpdate.Pending is { } u
                    ? Strings.UpdateReady(u.Version.ToString(3)) : Strings.UpToDate;
            }
            catch (Exception ex)
            {
                AppLog.Write($"Controllo aggiornamenti non riuscito: {ex}");
                checkStatus.Text = Strings.UpdateCheckFailed;
            }
            check.IsEnabled = true;
        };
        checkRow.Children.Add(check);
        checkRow.Children.Add(checkStatus);
        stack.Children.Add(checkRow);

        stack.Children.Add(Ui.SectionTitle(Strings.About));
        stack.Children.Add(new TextBlock { Text = $"{Strings.AppName} · {Strings.VersionLabel}", FontWeight = FontWeights.SemiBold });
        stack.Children.Add(new TextBlock { Text = Strings.AboutText, TextWrapping = TextWrapping.Wrap, Opacity = 0.75 });
        var links = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var legal = new Button { Content = Strings.LegalTitle };
        legal.Click += async (_, _) => await LegalDialog.ShowAsync(XamlRoot);
        var site = new HyperlinkButton { Content = Strings.ProductPageUrl.Replace("https://", "") };
        site.Click += (_, _) => LegalDialog.OpenUrl(Strings.ProductPageUrl);
        links.Children.Add(legal);
        links.Children.Add(site);
        stack.Children.Add(links);

        Content = new ScrollViewer { Content = stack };
        Loaded += (_, _) =>
        {
            _shell.IsOn = ShellIntegration.IsRegistered();
            _where.SelectedIndex = Settings.Current.OutputFolder is null ? 0 : 1;
            UpdateFolder();
        };
    }

    private async Task ChooseFolderAsync()
    {
        string? folder = await Ui.PickFolderAsync();
        if (folder is not null) SetFolder(folder);
        else if (Settings.Current.OutputFolder is null) _where.SelectedIndex = 0;
    }

    private void SetFolder(string? folder)
    {
        Settings.Current.OutputFolder = folder;
        Settings.Current.Save();
        _where.SelectedIndex = folder is null ? 0 : 1;
        UpdateFolder();
        OutputFolderChanged?.Invoke();
    }

    private void UpdateFolder() => _folder.Text = Settings.Current.OutputFolder ?? Strings.NextToOriginalHint;
}
