using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Akses.App;

/// <summary>
/// WinUI's ContentDialog for Avalonia: a card over the dimmed window (<see cref="Host"/> is a layer of
/// the main window), one dialog at a time. <see cref="ShowAsync"/> answers true for the primary button.
/// </summary>
internal static class Dialogs
{
    /// <summary>The overlay layer of the main window, set by <see cref="MainWindow"/>.</summary>
    public static Panel? Host { get; set; }

    public sealed class Handle
    {
        internal TaskCompletionSource<bool> Result { get; } = new();
        internal Button? CloseButton { get; set; }

        /// <summary>Closes the dialog as if the close button had been pressed.</summary>
        public void Hide() => Result.TrySetResult(false);

        /// <summary>Changes the text of the close button ("Annulla" → "Chiudi").</summary>
        public void SetCloseText(string text) { if (CloseButton is not null) CloseButton.Content = text; }

        public Task<bool> Task => Result.Task;
    }

    public static Task<bool> ShowAsync(string? title, object content, string closeText, string? primaryText = null) =>
        Show(title, content, closeText, primaryText).Task;

    public static Handle Show(string? title, object content, string closeText, string? primaryText = null)
    {
        var handle = new Handle();
        if (Host is null || Host.Children.Count > 0)
        {
            handle.Result.TrySetResult(false);
            return handle;
        }

        var body = new StackPanel { Spacing = 16 };
        if (title is not null)
            body.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(content is string text
            ? new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap }
            : (Control)content);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        if (primaryText is not null)
        {
            Button primary = Ui.AccentButton(primaryText);
            primary.MinWidth = 120;
            primary.Click += (_, _) => handle.Result.TrySetResult(true);
            buttons.Children.Add(primary);
        }
        var close = new Button { Content = closeText, MinWidth = 120, HorizontalContentAlignment = HorizontalAlignment.Center };
        if (primaryText is null) close.Classes.Add("accent");
        close.Click += (_, _) => handle.Result.TrySetResult(false);
        handle.CloseButton = close;
        buttons.Children.Add(close);
        body.Children.Add(buttons);

        var card = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(24),
            MaxWidth = 580,
            Margin = new Thickness(24),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            BoxShadow = BoxShadows.Parse("0 12 40 0 #50000000"),
            Child = body,
        }.Res(Border.BackgroundProperty, Palette.Card).Res(Border.BorderBrushProperty, Palette.CardStroke);

        var layer = new Grid { Background = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)) };
        layer.Children.Add(card);
        layer.KeyDown += (_, e) => { if (e.Key == Key.Escape) { handle.Result.TrySetResult(false); e.Handled = true; } };
        Host.Children.Add(layer);
        Host.IsHitTestVisible = true;
        close.Focus();

        _ = handle.Task.ContinueWith(_ =>
        {
            Host.Children.Remove(layer);
            Host.IsHitTestVisible = false;
        }, TaskScheduler.FromCurrentSynchronizationContext());
        return handle;
    }
}
