using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;

namespace Akses.App;

/// <summary>
/// The image editor's canvas: the picture fitted in the available space and, when cropping is on, a
/// crop frame with eight handles, the outside dimmed and a rule-of-thirds grid. The crop is kept in
/// pixels of the full-size image, so the preview can be a small copy.
/// </summary>
internal sealed class CropCanvas : Panel
{
    private const double MinCropPx = 8;
    private const double Grab = 14;

    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly Canvas _overlay = new();
    private readonly Rectangle[] _dim = new Rectangle[4];
    private readonly Rectangle _frame = new() { Stroke = Brushes.White, StrokeThickness = 1.5, IsHitTestVisible = false };
    private readonly Line[] _thirds = new Line[4];
    private readonly Rectangle[] _handles = new Rectangle[8];

    // Handle edges: which sides of the rectangle a handle moves (left, top, right, bottom).
    private static readonly (bool L, bool T, bool R, bool B)[] HandleEdges =
    [
        (true, true, false, false), (false, true, false, false), (false, true, true, false), (false, false, true, false),
        (false, false, true, true), (false, false, false, true), (true, false, false, true), (true, false, false, false),
    ];

    private int _fullW = 1, _fullH = 1;
    private Rect _crop;
    private bool _enabled;
    private double? _ratio;

    private (bool L, bool T, bool R, bool B)? _dragEdges;
    private bool _dragMove;
    private Point _dragStart;
    private Rect _dragRect;

    public event Action? CropChanged;

    public Rect Crop => _crop;
    public int FullWidth => _fullW;
    public int FullHeight => _fullH;

    public CropCanvas()
    {
        Children.Add(_image);
        var dimBrush = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0));
        for (int i = 0; i < 4; i++) _overlay.Children.Add(_dim[i] = new Rectangle { Fill = dimBrush, IsHitTestVisible = false });
        var thirdBrush = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255));
        for (int i = 0; i < 4; i++) _overlay.Children.Add(_thirds[i] = new Line { Stroke = thirdBrush, StrokeThickness = 1, IsHitTestVisible = false });
        _overlay.Children.Add(_frame);
        for (int i = 0; i < 8; i++)
            _overlay.Children.Add(_handles[i] = new Rectangle { Width = 12, Height = 12, Fill = Brushes.White, StrokeThickness = 2, RadiusX = 2, RadiusY = 2, IsHitTestVisible = false }
                .Res(Shape.StrokeProperty, Palette.Accent));
        Children.Add(_overlay);

        SizeChanged += (_, _) => Layout();
        _overlay.PointerPressed += OnPressed;
        _overlay.PointerMoved += OnMoved;
        _overlay.PointerReleased += (_, e) => { _dragEdges = null; _dragMove = false; e.Pointer.Capture(null); };
        SetEnabled(false);
    }

    public void SetImage(IImage source, int fullWidth, int fullHeight, bool keepCrop)
    {
        bool sameSize = fullWidth == _fullW && fullHeight == _fullH;
        _image.Source = source;
        _fullW = Math.Max(1, fullWidth);
        _fullH = Math.Max(1, fullHeight);
        if (!keepCrop || !sameSize) ResetCrop();
        else Layout();
    }

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        _overlay.IsVisible = enabled;
        _overlay.Background = enabled ? Brushes.Transparent : null;
        Layout();
    }

    /// <summary>Width/height ratio to keep (null = free). Re-centres the largest crop with that ratio.</summary>
    public void SetRatio(double? ratio)
    {
        _ratio = ratio;
        ResetCrop();
    }

    public void ResetCrop()
    {
        if (_ratio is double r)
        {
            double w = _fullW, h = w / r;
            if (h > _fullH) { h = _fullH; w = h * r; }
            _crop = new Rect((_fullW - w) / 2, (_fullH - h) / 2, w, h);
        }
        else
        {
            _crop = new Rect(0, 0, _fullW, _fullH);
        }
        Layout();
        CropChanged?.Invoke();
    }

    /// <summary>Where the image is drawn inside the control, and its scale (screen px per image px).</summary>
    private (double X, double Y, double Scale) Fit()
    {
        double scale = Math.Min(Bounds.Width / _fullW, Bounds.Height / _fullH);
        if (double.IsNaN(scale) || scale <= 0) scale = 1;
        return ((Bounds.Width - _fullW * scale) / 2, (Bounds.Height - _fullH * scale) / 2, scale);
    }

    private void Layout()
    {
        if (!_enabled || Bounds.Width <= 0) return;
        (double ox, double oy, double s) = Fit();
        double x = ox + _crop.X * s, y = oy + _crop.Y * s, w = _crop.Width * s, h = _crop.Height * s;
        double iw = _fullW * s, ih = _fullH * s;

        Place(_dim[0], ox, oy, iw, y - oy);                         // above
        Place(_dim[1], ox, y + h, iw, oy + ih - (y + h));           // below
        Place(_dim[2], ox, y, x - ox, h);                           // left
        Place(_dim[3], x + w, y, ox + iw - (x + w), h);             // right
        Place(_frame, x, y, w, h);

        for (int i = 0; i < 2; i++)
        {
            double tx = x + w * (i + 1) / 3, ty = y + h * (i + 1) / 3;
            _thirds[i].StartPoint = new Point(tx, y);
            _thirds[i].EndPoint = new Point(tx, y + h);
            _thirds[i + 2].StartPoint = new Point(x, ty);
            _thirds[i + 2].EndPoint = new Point(x + w, ty);
        }

        Point[] points = HandlePoints(x, y, w, h);
        for (int i = 0; i < 8; i++)
        {
            Canvas.SetLeft(_handles[i], points[i].X - 6);
            Canvas.SetTop(_handles[i], points[i].Y - 6);
        }
    }

    private static Point[] HandlePoints(double x, double y, double w, double h) =>
    [
        new(x, y), new(x + w / 2, y), new(x + w, y), new(x + w, y + h / 2),
        new(x + w, y + h), new(x + w / 2, y + h), new(x, y + h), new(x, y + h / 2),
    ];

    private static void Place(Control e, double x, double y, double w, double h)
    {
        Canvas.SetLeft(e, x);
        Canvas.SetTop(e, y);
        e.Width = Math.Max(0, w);
        e.Height = Math.Max(0, h);
    }

    private int HitHandle(Point p)
    {
        (double ox, double oy, double s) = Fit();
        Point[] points = HandlePoints(ox + _crop.X * s, oy + _crop.Y * s, _crop.Width * s, _crop.Height * s);
        for (int i = 0; i < 8; i++)
            if (Math.Abs(points[i].X - p.X) <= Grab && Math.Abs(points[i].Y - p.Y) <= Grab) return i;
        return -1;
    }

    private bool InsideCrop(Point p)
    {
        (double ox, double oy, double s) = Fit();
        return p.X >= ox + _crop.Left * s && p.X <= ox + _crop.Right * s && p.Y >= oy + _crop.Top * s && p.Y <= oy + _crop.Bottom * s;
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        Point p = e.GetPosition(_overlay);
        int handle = HitHandle(p);
        if (handle >= 0) _dragEdges = HandleEdges[handle];
        else if (InsideCrop(p)) _dragMove = true;
        else return;
        _dragStart = p;
        _dragRect = _crop;
        e.Pointer.Capture(_overlay);
        e.Handled = true;
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        Point p = e.GetPosition(_overlay);
        if (_dragEdges is null && !_dragMove)
        {
            int handle = HitHandle(p);
            StandardCursorType shape = handle switch
            {
                0 => StandardCursorType.TopLeftCorner,
                4 => StandardCursorType.BottomRightCorner,
                2 => StandardCursorType.TopRightCorner,
                6 => StandardCursorType.BottomLeftCorner,
                1 or 5 => StandardCursorType.SizeNorthSouth,
                3 or 7 => StandardCursorType.SizeWestEast,
                _ => InsideCrop(p) ? StandardCursorType.SizeAll : StandardCursorType.Arrow,
            };
            _overlay.Cursor = new Cursor(shape);
            return;
        }

        double s = Fit().Scale;
        double dx = (p.X - _dragStart.X) / s, dy = (p.Y - _dragStart.Y) / s;
        Rect r = _dragRect;
        if (_dragMove)
        {
            double x = Math.Clamp(r.X + dx, 0, _fullW - r.Width), y = Math.Clamp(r.Y + dy, 0, _fullH - r.Height);
            _crop = new Rect(x, y, r.Width, r.Height);
        }
        else
        {
            _crop = Resize(r, _dragEdges!.Value, dx, dy);
        }
        Layout();
        CropChanged?.Invoke();
    }

    private Rect Resize(Rect r, (bool L, bool T, bool R, bool B) edges, double dx, double dy)
    {
        double left = r.Left, top = r.Top, right = r.Right, bottom = r.Bottom;
        if (edges.L) left = Math.Clamp(left + dx, 0, right - MinCropPx);
        if (edges.R) right = Math.Clamp(right + dx, left + MinCropPx, _fullW);
        if (edges.T) top = Math.Clamp(top + dy, 0, bottom - MinCropPx);
        if (edges.B) bottom = Math.Clamp(bottom + dy, top + MinCropPx, _fullH);

        if (_ratio is not double ratio) return new Rect(left, top, right - left, bottom - top);

        bool horizontal = edges.L || edges.R, vertical = edges.T || edges.B;
        double w = right - left, h = bottom - top;
        if (horizontal && vertical)
        {
            // Corner: follow whichever side moved more, the other one follows the ratio.
            if (w / ratio > h) h = w / ratio; else w = h * ratio;
        }
        else if (horizontal) h = w / ratio;
        else w = h * ratio;

        // Anchor: the opposite side for a moved side, the centre for an untouched axis.
        double cx = (r.Left + r.Right) / 2, cy = (r.Top + r.Bottom) / 2;
        double maxW = edges.L ? r.Right : edges.R ? _fullW - r.Left : 2 * Math.Min(cx, _fullW - cx);
        double maxH = edges.T ? r.Bottom : edges.B ? _fullH - r.Top : 2 * Math.Min(cy, _fullH - cy);
        double fit = Math.Min(1, Math.Min(maxW / w, maxH / h));
        w *= fit;
        h *= fit;

        double x = edges.L ? r.Right - w : edges.R ? r.Left : cx - w / 2;
        double y = edges.T ? r.Bottom - h : edges.B ? r.Top : cy - h / 2;
        return new Rect(x, y, w, h);
    }
}
