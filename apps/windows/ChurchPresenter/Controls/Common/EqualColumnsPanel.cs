using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using Windows.Foundation;

namespace ChurchPresenter.Controls;

/// <summary>Lays out visible children as equal-width columns in a single row.</summary>
public sealed class EqualColumnsPanel : Panel
{
    /// <summary>Minimum unscaled width for each column before child content is uniformly scaled down to fit.</summary>
    public static readonly DependencyProperty MinimumColumnWidthProperty =
        DependencyProperty.Register(
            nameof(MinimumColumnWidth),
            typeof(double),
            typeof(EqualColumnsPanel),
            new PropertyMetadata(0d, OnMinimumColumnWidthChanged));

    /// <summary>Gets or sets the minimum unscaled width for each equal column.</summary>
    public double MinimumColumnWidth
    {
        get => (double)GetValue(MinimumColumnWidthProperty);
        set => SetValue(MinimumColumnWidthProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        int visibleChildren = CountVisibleChildren();
        if (visibleChildren == 0)
            return new Size(0, 0);

        if (double.IsInfinity(availableSize.Width))
            return MeasureUnboundedWidth(availableSize);

        double columnWidth = GetLogicalColumnWidth(availableSize.Width, visibleChildren);
        double availableHeight = double.IsInfinity(availableSize.Height)
            ? double.PositiveInfinity
            : availableSize.Height;
        double desiredHeight = 0;

        foreach (UIElement child in Children)
        {
            if (child.Visibility == Visibility.Collapsed)
                continue;

            child.Measure(new Size(columnWidth, availableHeight));
            desiredHeight = Math.Max(desiredHeight, child.DesiredSize.Height);
        }

        double height = double.IsInfinity(availableSize.Height)
            ? desiredHeight
            : availableSize.Height;

        return new Size(availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int visibleChildren = CountVisibleChildren();
        if (visibleChildren == 0)
            return finalSize;

        double logicalColumnWidth = GetLogicalColumnWidth(finalSize.Width, visibleChildren);
        double visualColumnWidth = finalSize.Width / visibleChildren;
        double scale = logicalColumnWidth > 0
            ? Math.Min(1, visualColumnWidth / logicalColumnWidth)
            : 1;
        double arrangeHeight = scale > 0 ? finalSize.Height / scale : finalSize.Height;
        double x = 0;

        foreach (UIElement child in Children)
        {
            if (child.Visibility == Visibility.Collapsed)
                continue;

            ApplyScale(child, scale);
            child.Arrange(new Rect(x, 0, logicalColumnWidth, arrangeHeight));
            x += visualColumnWidth;
        }

        return finalSize;
    }

    private Size MeasureUnboundedWidth(Size availableSize)
    {
        double desiredWidth = 0;
        double desiredHeight = 0;

        int visibleChildren = CountVisibleChildren();
        double columnWidth = GetLogicalColumnWidth(availableSize.Width, visibleChildren);

        foreach (UIElement child in Children)
        {
            if (child.Visibility == Visibility.Collapsed)
                continue;

            if (columnWidth > 0)
            {
                child.Measure(new Size(columnWidth, availableSize.Height));
                desiredWidth += columnWidth;
            }
            else
            {
                child.Measure(availableSize);
                desiredWidth += child.DesiredSize.Width;
            }

            desiredHeight = Math.Max(desiredHeight, child.DesiredSize.Height);
        }

        return new Size(desiredWidth, desiredHeight);
    }

    private double GetLogicalColumnWidth(double availableWidth, int visibleChildren)
    {
        if (visibleChildren <= 0)
            return 0;

        double measuredWidth = double.IsInfinity(availableWidth)
            ? 0
            : availableWidth / visibleChildren;

        return Math.Max(measuredWidth, MinimumColumnWidth);
    }

    private static void ApplyScale(UIElement child, double scale)
    {
        if (scale >= 0.999)
        {
            if (child.RenderTransform is ScaleTransform)
                child.RenderTransform = null;
            return;
        }

        child.RenderTransformOrigin = new Point(0, 0);
        child.RenderTransform = new ScaleTransform
        {
            ScaleX = scale,
            ScaleY = scale,
        };
    }

    private int CountVisibleChildren()
    {
        int count = 0;
        foreach (UIElement child in Children)
        {
            if (child.Visibility != Visibility.Collapsed)
                count++;
        }

        return count;
    }

    private static void OnMinimumColumnWidthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EqualColumnsPanel panel)
            panel.InvalidateMeasure();
    }
}
