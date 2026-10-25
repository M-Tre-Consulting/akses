using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Akses.App;

/// <summary>
/// Lays children out left to right, wrapping to a new row when the width runs out (the format
/// chips), with spacing between them; each child is aligned vertically within its row.
/// </summary>
public sealed class SpacedWrapPanel : Panel
{
    public double HorizontalSpacing { get; set; } = 8;
    public double VerticalSpacing { get; set; } = 8;

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = 0, height = 0;
        foreach (Control child in Children) child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        foreach ((List<Control> row, double rowWidth, double rowHeight) in Rows(availableSize.Width))
        {
            width = Math.Max(width, rowWidth);
            height += (height > 0 ? VerticalSpacing : 0) + rowHeight;
        }
        return new Size(double.IsInfinity(availableSize.Width) ? width : Math.Min(width, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double y = 0;
        foreach ((List<Control> row, _, double rowHeight) in Rows(finalSize.Width))
        {
            double x = 0;
            foreach (Control child in row)
            {
                Size s = child.DesiredSize;
                double top = child.VerticalAlignment switch
                {
                    VerticalAlignment.Center => (rowHeight - s.Height) / 2,
                    VerticalAlignment.Bottom => rowHeight - s.Height,
                    _ => 0,
                };
                child.Arrange(new Rect(x, y + top, s.Width, s.Height));
                x += s.Width + HorizontalSpacing;
            }
            y += rowHeight + VerticalSpacing;
        }
        return finalSize;
    }

    private IEnumerable<(List<Control> Row, double Width, double Height)> Rows(double available)
    {
        var row = new List<Control>();
        double x = 0, rowHeight = 0;
        foreach (Control child in Children)
        {
            if (!child.IsVisible) continue;
            Size s = child.DesiredSize;
            if (row.Count > 0 && x + s.Width > available)
            {
                yield return (row, x - HorizontalSpacing, rowHeight);
                row = [];
                x = 0;
                rowHeight = 0;
            }
            row.Add(child);
            x += s.Width + HorizontalSpacing;
            rowHeight = Math.Max(rowHeight, s.Height);
        }
        if (row.Count > 0) yield return (row, x - HorizontalSpacing, rowHeight);
    }
}
