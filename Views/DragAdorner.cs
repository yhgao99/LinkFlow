using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace LinkFlow.Views;

public class DragAdorner : Adorner
{
    private readonly UIElement _child;
    private readonly TranslateTransform _transform = new();

    public DragAdorner(UIElement adornedElement, UIElement adornerElement)
        : base(adornedElement)
    {
        _child = adornerElement;
        _child.RenderTransform = _transform;
        AddVisualChild(_child);
        IsHitTestVisible = false;
    }

    public void UpdatePosition(Point point)
    {
        _transform.X = point.X;
        _transform.Y = point.Y;
    }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) => _child;

    protected override Size MeasureOverride(Size constraint)
    {
        _child.Measure(constraint);
        return _child.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _child.Arrange(new Rect(new Point(0, 0), _child.DesiredSize));
        return finalSize;
    }
}
