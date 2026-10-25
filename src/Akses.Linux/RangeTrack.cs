using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Akses.App;

/// <summary>
/// The trim timeline: a strip (video frames or the audio waveform) with two handles for the start
/// and end of the selection, the parts outside dimmed, and a playhead. Dragging a handle moves that
/// end; clicking or dragging anywhere else moves the playhead. All times in seconds.
/// </summary>
internal sealed class RangeTrack : Border
{
    private const double HandleWidth = 14;
    private const double MinSelection = 0.1;

    private readonly Grid _strip = new();
    private readonly Rectangle _dimLeft = Dim(), _dimRight = Dim();
    private readonly Border _selection;
    private readonly Border _startHandle, _endHandle;
    private readonly Rectangle _playhead;
    private readonly Canvas _overlay = new() { Background = Brushes.Transparent };

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
        ClipToBounds = true;
        this.Res(BackgroundProperty, Palette.Subtle);

        _selection = new Border { BorderThickness = new Thickness(0, 3, 0, 3), IsHitTestVisible = false }.Res(Border.BorderBrushProperty, Palette.Accent);
        _startHandle = Handle(Icons.Left);
        _endHandle = Handle(Icons.Right);
        _playhead = new Rectangle { Width = 2, Fill = Brushes.White, IsHitTestVisible = false };

        _overlay.Children.Add(_dimLeft);
        _overlay.Children.Add(_dimRight);
        _overlay.Children.Add(_selection);
        _overlay.Children.Add(_playhead);
        _overlay.Children.Add(_startHandle);
        _overlay.Children.Add(_endHandle);
        var layers = new Panel();
        layers.Children.Add(_strip);
        layers.Children.Add(_overlay);
        Child = layers;

        SizeChanged += (_, _) => Layout();
        _overlay.PointerPressed += OnPressed;
        _overlay.PointerMoved += OnMoved;
        _overlay.PointerReleased += (_, e) => { _drag = Drag.None; e.Pointer.Capture(null); };
        _overlay.PointerCaptureLost += (_, _) => _drag = Drag.None;
    }

    private static Rectangle Dim() => new() { Fill = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), IsHitTestVisible = false };

    private static Border Handle(string icon) => new Border
    {
        Width = HandleWidth,
        CornerRadius = new CornerRadius(4),
        IsHitTestVisible = false,
        Child = new Glyph(icon, 10) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }.Res(Glyph.ForegroundProperty, Palette.OnAccent),
    }.Res(Border.BackgroundProperty, Palette.Accent);

    /// <summary>What to show behind the handles: frame thumbnails or a waveform.</summary>
    public void SetStrip(Control? content)
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

    private double Usable => Math.Max(1, Bounds.Width - 2 * HandleWidth);
    private double X(double seconds) => HandleWidth + seconds / Duration * Usable;
    private double Time(double x) => Math.Clamp((x - HandleWidth) / Usable * Duration, 0, Duration);

    private void Layout()
    {
        double h = Bounds.Height, xs = X(Start), xe = X(End);
        _strip.Margin = new Thickness(HandleWidth, 0, HandleWidth, 0);
        Place(_dimLeft, 0, xs, h);
        Place(_dimRight, xe, Math.Max(0, Bounds.Width - xe), h);
        Place(_selection, xs, Math.Max(0, xe - xs), h);
        Place(_startHandle, xs - HandleWidth, HandleWidth, h);
        Place(_endHandle, xe, HandleWidth, h);
        Place(_playhead, X(Position) - 1, 2, h);
    }

    private static void Place(Control e, double x, double width, double height)
    {
        Canvas.SetLeft(e, x);
        e.Width = Math.Max(0, width);
        e.Height = Math.Max(0, height);
    }

    private bool OverHandle(double x)
    {
        double xs = X(Start), xe = X(End);
        return (x >= xs - HandleWidth - 4 && x <= xs + 4) || (x >= xe - 4 && x <= xe + HandleWidth + 4);
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        double x = e.GetPosition(this).X;
        double xs = X(Start), xe = X(End);
        // Handles sit outside the selection; give them a little extra grab area.
        if (x >= xs - HandleWidth - 4 && x <= xs + 4) _drag = Drag.Start;
        else if (x >= xe - 4 && x <= xe + HandleWidth + 4) _drag = Drag.End;
        else _drag = Drag.Seek;
        e.Pointer.Capture(_overlay);
        Apply(x);
        e.Handled = true;
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        double x = e.GetPosition(this).X;
        _overlay.Cursor = new Cursor(_drag is Drag.Start or Drag.End || (_drag == Drag.None && OverHandle(x))
            ? StandardCursorType.SizeWestEast : StandardCursorType.Arrow);
        if (_drag != Drag.None) Apply(x);
    }

    private void Apply(double x)
    {
        switch (_drag)
        {
            case Drag.Start:
                Start = Math.Max(0, Math.Min(Time(x + HandleWidth / 2), End - MinSelection));
                Layout();
                RangeChanged?.Invoke();
                SeekRequested?.Invoke(Start);
                break;
            case Drag.End:
                End = Math.Min(Duration, Math.Max(Time(x - HandleWidth / 2), Start + MinSelection));
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
