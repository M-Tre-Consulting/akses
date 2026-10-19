using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Akses.App;

/// <summary>
/// The trim timeline: a strip (video frames or the audio waveform) with two handles for the start
/// and end of the selection, the parts outside dimmed, and a playhead. Dragging a handle moves that
/// end; clicking or dragging anywhere else moves the playhead. All times in seconds.
/// </summary>
internal sealed class RangeTrack : Grid
{
    private const double HandleWidth = 14;
    private const double MinSelection = 0.1;

    private readonly Grid _strip = new();
    private readonly Rectangle _dimLeft = Dim(), _dimRight = Dim();
    private readonly Border _selection;
    private readonly Border _startHandle, _endHandle;
    private readonly Rectangle _playhead;
    private readonly Canvas _overlay = new();

    private enum Drag { None, Start, End, Seek }
    private Drag _drag;

    public double Duration { get; private set; }
    public double Start { get; private set; }
    public double End { get; private set; }
    public double Position { get; private set; }

    /// <summary>The selection changed by dragging (not by <see cref="SetRange"/>).</summary>
    public event Action? RangeChanged;
    /// <summary>The user clicked or dragged to a time.</summary>
    public event Action<double>? SeekRequested;

    public RangeTrack()
    {
        Height = 76;
        CornerRadius = new CornerRadius(8);
        Background = FileCard.Brush("ControlFillColorSecondaryBrush");

        Brush accent = FileCard.Brush("AccentFillColorDefaultBrush");
        _selection = new Border { BorderBrush = accent, BorderThickness = new Thickness(0, 3, 0, 3), IsHitTestVisible = false };
        _startHandle = Handle(accent, "\uE76B");
        _endHandle = Handle(accent, "\uE76C");
        _playhead = new Rectangle { Width = 2, Fill = new SolidColorBrush(Colors.White), IsHitTestVisible = false };

        _overlay.Children.Add(_dimLeft);
        _overlay.Children.Add(_dimRight);
        _overlay.Children.Add(_selection);
        _overlay.Children.Add(_playhead);
        _overlay.Children.Add(_startHandle);
        _overlay.Children.Add(_endHandle);
        Children.Add(_strip);
        Children.Add(_overlay);

        SizeChanged += (_, _) => Layout();
        PointerPressed += OnPressed;
        PointerMoved += OnMoved;
        PointerReleased += (_, e) => { _drag = Drag.None; ReleasePointerCapture(e.Pointer); };
        PointerCaptureLost += (_, _) => _drag = Drag.None;
    }

    private static Rectangle Dim() => new() { Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(150, 0, 0, 0)), IsHitTestVisible = false };

    private static Border Handle(Brush accent, string glyph) => new()
    {
        Width = HandleWidth,
        Background = accent,
        CornerRadius = new CornerRadius(4),
        IsHitTestVisible = false,
        Child = new FontIcon { Glyph = glyph, FontSize = 10, Foreground = FileCard.Brush("TextOnAccentFillColorPrimaryBrush") },
    };

    /// <summary>What to show behind the handles: frame thumbnails or a waveform.</summary>
    public void SetStrip(UIElement? content)
    {
        _strip.Children.Clear();
        if (content is not null) _strip.Children.Add(content);
    }

    public void SetRange(double duration, double start, double end)
    {
        Duration = Math.Max(duration, 0.001);
        Start = Math.Clamp(start, 0, Duration);
        End = Math.Clamp(end, Start, Duration);
        Layout();
    }

    public void SetPosition(double seconds)
    {
        Position = Math.Clamp(seconds, 0, Duration);
        Layout();
    }

    private double Usable => Math.Max(1, ActualWidth - 2 * HandleWidth);
    private double X(double seconds) => HandleWidth + seconds / Duration * Usable;
    private double Time(double x) => Math.Clamp((x - HandleWidth) / Usable * Duration, 0, Duration);

    private void Layout()
    {
        double h = ActualHeight, xs = X(Start), xe = X(End);
        _strip.Margin = new Thickness(HandleWidth, 0, HandleWidth, 0);
        Place(_dimLeft, 0, xs, h);
        Place(_dimRight, xe, Math.Max(0, ActualWidth - xe), h);
        Place(_selection, xs, Math.Max(0, xe - xs), h);
        Place(_startHandle, xs - HandleWidth, HandleWidth, h);
        Place(_endHandle, xe, HandleWidth, h);
        Place(_playhead, X(Position) - 1, 2, h);
    }

    private static void Place(FrameworkElement e, double x, double width, double height)
    {
        Canvas.SetLeft(e, x);
        e.Width = Math.Max(0, width);
        e.Height = Math.Max(0, height);
    }

    private void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        double x = e.GetCurrentPoint(this).Position.X;
        double xs = X(Start), xe = X(End);
        // Handles sit outside the selection; give them a little extra grab area.
        if (x >= xs - HandleWidth - 4 && x <= xs + 4) _drag = Drag.Start;
        else if (x >= xe - 4 && x <= xe + HandleWidth + 4) _drag = Drag.End;
        else _drag = Drag.Seek;
        CapturePointer(e.Pointer);
        Apply(x);
        e.Handled = true;
    }

    private void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        double x = e.GetCurrentPoint(this).Position.X;
        double xs = X(Start), xe = X(End);
        bool overHandle = (x >= xs - HandleWidth - 4 && x <= xs + 4) || (x >= xe - 4 && x <= xe + HandleWidth + 4);
        ProtectedCursor = InputSystemCursor.Create(_drag is Drag.Start or Drag.End || (_drag == Drag.None && overHandle)
            ? InputSystemCursorShape.SizeWestEast : InputSystemCursorShape.Arrow);
        if (_drag != Drag.None) Apply(x);
    }

    private void Apply(double x)
    {
        switch (_drag)
        {
            case Drag.Start:
                Start = Math.Min(Time(x + HandleWidth / 2), End - MinSelection);
                Start = Math.Max(0, Start);
                Layout();
                RangeChanged?.Invoke();
                SeekRequested?.Invoke(Start);
                break;
            case Drag.End:
                End = Math.Max(Time(x - HandleWidth / 2), Start + MinSelection);
                End = Math.Min(Duration, End);
                Layout();
                RangeChanged?.Invoke();
                SeekRequested?.Invoke(End);
                break;
            case Drag.Seek:
                SeekRequested?.Invoke(Time(x));
                break;
        }
    }
}
