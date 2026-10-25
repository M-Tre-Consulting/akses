using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Akses.App;

/// <summary>The card for one <see cref="FileItem"/>: icon, name, details, progress, and Apri / Mostra / Rimuovi.</summary>
internal sealed class FileCard : UserControl
{
    private readonly TextBlock _details, _status;
    private readonly SelectableTextBlock _error;
    private readonly ProgressBar _progress;
    private readonly StackPanel _resultButtons;
    private readonly PropertyChangedEventHandler _handler;

    public FileItem Item { get; }
    public event Action<FileCard>? RemoveRequested;

    public FileCard(FileItem item, bool canRemove = true, Control? extra = null)
    {
        Item = item;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };

        var badge = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(8),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 14, 0),
            Child = new Glyph(Icons.For(item.Kind), 18) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                .Res(Glyph.ForegroundProperty, Palette.OnAccent),
        }.Res(Border.BackgroundProperty, Palette.Accent);
        grid.Children.Add(badge);

        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = item.Name, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTip.SetTip(name, item.Path);
        text.Children.Add(name);
        _details = new TextBlock { FontSize = 12 }.Res(TextBlock.ForegroundProperty, Palette.TextSecondary);
        _status = new TextBlock { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis }.Res(TextBlock.ForegroundProperty, Palette.AccentText);
        _error = new SelectableTextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap }.Res(SelectableTextBlock.ForegroundProperty, Palette.Critical);
        _progress = new ProgressBar { Maximum = 100, Margin = new Thickness(0, 4, 0, 0), MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Stretch };
        text.Children.Add(_details);
        text.Children.Add(_status);
        text.Children.Add(_error);
        text.Children.Add(_progress);
        if (extra is not null) text.Children.Add(extra);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        _resultButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        _resultButtons.Children.Add(Ui.IconButton(Icons.Open, Strings.Open, () => { if (item.Output is { } o) Ui.Open(o); }));
        _resultButtons.Children.Add(Ui.IconButton(Icons.Folder, Strings.ShowInFolder, () => { if (item.Output is { } o) Ui.ShowInFolder(o); }));
        buttons.Children.Add(_resultButtons);
        if (canRemove)
            buttons.Children.Add(Ui.IconButton(Icons.Close, Strings.Remove, () => RemoveRequested?.Invoke(this)));
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);

        Content = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 10, 10),
            Child = grid,
        }.Res(Border.BackgroundProperty, Palette.Card).Res(Border.BorderBrushProperty, Palette.CardStroke);

        Refresh();
        _handler = (_, _) => Dispatcher.UIThread.Post(Refresh);
        // Fast jobs can finish before the card is on screen: catch up when it gets there.
        AttachedToVisualTree += (_, _) => { item.PropertyChanged += _handler; Refresh(); };
        DetachedFromVisualTree += (_, _) => item.PropertyChanged -= _handler;
    }

    private void Refresh()
    {
        _details.Text = Item.Details;
        _status.Text = Item.Status;
        _status.IsVisible = Item.Status.Length > 0;
        _error.Text = Item.Error;
        _error.IsVisible = Item.Error.Length > 0;
        _progress.IsVisible = Item.IsBusy;
        _progress.IsIndeterminate = Item.IsIndeterminate;
        _progress.Value = Item.Progress;
        _resultButtons.IsVisible = Item.HasResult;
    }
}
