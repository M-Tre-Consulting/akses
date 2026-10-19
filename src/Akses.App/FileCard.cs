using System.ComponentModel;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Akses.App;

/// <summary>The card for one <see cref="FileItem"/>: icon, name, details, progress, and Apri / Mostra / Rimuovi.</summary>
internal sealed class FileCard : UserControl
{
    private readonly TextBlock _details, _status, _error;
    private readonly ProgressBar _progress;
    private readonly StackPanel _resultButtons;

    public FileItem Item { get; }
    public event Action<FileCard>? RemoveRequested;

    public FileCard(FileItem item, bool canRemove = true, UIElement? extra = null)
    {
        Item = item;
        var grid = new Grid { ColumnSpacing = 14 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var badge = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(8),
            Background = Brush("AccentFillColorDefaultBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new FontIcon { Glyph = item.Glyph, FontSize = 18, Foreground = Brush("TextOnAccentFillColorPrimaryBrush") },
        };
        grid.Children.Add(badge);

        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = item.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTipService.SetToolTip(name, item.Path);
        text.Children.Add(name);
        _details = new TextBlock { FontSize = 12, Foreground = Brush("TextFillColorSecondaryBrush") };
        _status = new TextBlock { FontSize = 12, Foreground = Brush("AccentTextFillColorPrimaryBrush"), TextTrimming = TextTrimming.CharacterEllipsis };
        _error = new TextBlock { FontSize = 12, Foreground = Brush("SystemFillColorCriticalBrush"), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        _progress = new ProgressBar { Maximum = 100, Margin = new Thickness(0, 4, 0, 0), MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Stretch };
        text.Children.Add(_details);
        text.Children.Add(_status);
        text.Children.Add(_error);
        text.Children.Add(_progress);
        if (extra is not null) text.Children.Add(extra);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        _resultButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        _resultButtons.Children.Add(IconButton("\uE8E5", Strings.Open, () => { if (item.Output is { } o) Ui.Open(o); }));
        _resultButtons.Children.Add(IconButton("\uE838", Strings.ShowInFolder, () => { if (item.Output is { } o) Ui.ShowInFolder(o); }));
        buttons.Children.Add(_resultButtons);
        if (canRemove)
            buttons.Children.Add(IconButton("\uE711", Strings.Remove, () => RemoveRequested?.Invoke(this)));
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);

        Content = new Border
        {
            Background = Brush("CardBackgroundFillColorDefaultBrush"),
            BorderBrush = Brush("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 10, 10),
            Child = grid,
        };

        Refresh();
        PropertyChangedEventHandler handler = (_, _) => DispatcherQueue?.TryEnqueue(Refresh);
        // Fast jobs can finish before the card is on screen: catch up when it gets there.
        Loaded += (_, _) => { item.PropertyChanged += handler; Refresh(); };
        Unloaded += (_, _) => item.PropertyChanged -= handler;
    }

    private void Refresh()
    {
        _details.Text = Item.Details;
        _status.Text = Item.Status;
        _status.Visibility = Item.StatusVisibility;
        _error.Text = Item.Error;
        _error.Visibility = Item.ErrorVisibility;
        _progress.Visibility = Item.BusyVisibility;
        _progress.IsIndeterminate = Item.IsIndeterminate;
        _progress.Value = Item.Progress;
        _resultButtons.Visibility = Item.ResultVisibility;
    }

    internal static Button IconButton(string glyph, string tooltip, Action onClick)
    {
        var b = new Button { Content = new FontIcon { Glyph = glyph, FontSize = 14 }, Padding = new Thickness(9, 7, 9, 7) };
        ToolTipService.SetToolTip(b, tooltip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, tooltip);
        b.Click += (_, _) => onClick();
        return b;
    }

    internal static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}
