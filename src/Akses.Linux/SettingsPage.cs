using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Akses.App;

/// <summary>Where results are saved, the file manager menu, and "Privacy, licenza e contatti".</summary>
internal sealed class SettingsPage : UserControl
{
    private readonly RadioButton _nextTo = new() { GroupName = "where" };
    private readonly RadioButton _inFolder = new() { GroupName = "where" };
    private readonly TextBlock _folder = new() { Opacity = 0.75, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly ToggleSwitch _shell = new();
    private bool _syncing;

    public event Action? BackRequested;

    /// <summary>The output folder changed (the file's page shows it).</summary>
    public event Action? OutputFolderChanged;

    public SettingsPage()
    {
        var stack = new StackPanel { Spacing = 10, Margin = new Thickness(28, 4, 28, 24), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
        stack.Children.Add(Ui.EditorHeader(Strings.SettingsTitle, Strings.SettingsSubtitle, () => BackRequested?.Invoke()));

        stack.Children.Add(Ui.SectionTitle(Strings.WhereToSave));
        _nextTo.Content = Strings.SaveNextToOriginal;
        _inFolder.Content = Strings.SaveInFolder;
        _nextTo.IsCheckedChanged += (_, _) => { if (!_syncing && _nextTo.IsChecked == true) SetFolder(null); };
        _inFolder.IsCheckedChanged += async (_, _) =>
        {
            if (!_syncing && _inFolder.IsChecked == true && Settings.Current.OutputFolder is null) await ChooseFolderAsync();
        };
        stack.Children.Add(_nextTo);
        stack.Children.Add(_inFolder);
        var choose = new Button { Content = Strings.ChooseFolder };
        choose.Click += async (_, _) => await ChooseFolderAsync();
        var folderRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(28, 0, 0, 0) };
        folderRow.Children.Add(choose);
        folderRow.Children.Add(_folder);
        stack.Children.Add(folderRow);

        stack.Children.Add(Ui.SectionTitle(Strings.ExplorerMenu));
        stack.Children.Add(new TextBlock { Text = Strings.ExplorerMenuHint, TextWrapping = TextWrapping.Wrap, Opacity = 0.75 });
        _shell.OnContent = Strings.On;
        _shell.OffContent = Strings.Off;
        _shell.IsCheckedChanged += async (_, _) =>
        {
            if (_syncing) return;
            bool on = _shell.IsChecked == true;
            _shell.IsEnabled = false;
            await Task.Run(() => { if (on) ShellIntegration.Register(); else ShellIntegration.Unregister(); });
            Settings.Current.ShellMenuOff = !on;
            Settings.Current.Save();
            _shell.IsEnabled = true;
        };
        stack.Children.Add(_shell);

        stack.Children.Add(Ui.SectionTitle(Strings.About));
        stack.Children.Add(new TextBlock { Text = $"{Strings.AppName} · {Strings.VersionLabel}", FontWeight = FontWeight.SemiBold });
        stack.Children.Add(new TextBlock { Text = Strings.AboutText, TextWrapping = TextWrapping.Wrap, Opacity = 0.75 });
        var links = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var legal = new Button { Content = Strings.LegalTitle };
        legal.Click += async (_, _) => await LegalDialog.ShowAsync();
        links.Children.Add(legal);
        links.Children.Add(Ui.Link(Strings.ProductPageUrl.Replace("https://", ""), () => Ui.OpenUrl(Strings.ProductPageUrl)));
        stack.Children.Add(links);

        Content = new ScrollViewer { Content = stack };
        AttachedToVisualTree += (_, _) => Sync();
    }

    private void Sync()
    {
        _syncing = true;
        _shell.IsChecked = ShellIntegration.IsRegistered();
        _nextTo.IsChecked = Settings.Current.OutputFolder is null;
        _inFolder.IsChecked = Settings.Current.OutputFolder is not null;
        _folder.Text = Settings.Current.OutputFolder ?? Strings.NextToOriginalHint;
        _syncing = false;
    }

    private async Task ChooseFolderAsync()
    {
        string? folder = await Ui.PickFolderAsync();
        if (folder is not null) SetFolder(folder);
        else Sync();
    }

    private void SetFolder(string? folder)
    {
        Settings.Current.OutputFolder = folder;
        Settings.Current.Save();
        Sync();
        OutputFolderChanged?.Invoke();
    }
}
