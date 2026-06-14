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

    /// <summary>Horizontal gap between equal columns.</summary>
    public static readonly DependencyProperty ColumnSpacingProperty =
        DependencyProperty.Register(
            nameof(ColumnSpacing),
            typeof(double),
            typeof(EqualColumnsPanel),
            new PropertyMetadata(0d, OnLayoutPropertyChanged));

    /// <summary>Gets or sets the horizontal gap between equal columns.</summary>
    public double ColumnSpacing
    {
        get => (double)GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    /// <summary>When true, each column is measured and arranged as a square (height equals column width).</summary>
    public static readonly DependencyProperty SquareCellsProperty =
        DependencyProperty.Register(
            nameof(SquareCells),
            typeof(bool),
            typeof(EqualColumnsPanel),
            new PropertyMetadata(false, OnLayoutPropertyChanged));

    /// <summary>Gets or sets whether each child is laid out in a square cell.</summary>
    public bool SquareCells
    {
        get => (bool)GetValue(SquareCellsProperty);
        set => SetValue(SquareCellsProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        int visibleChildren = CountVisibleChildren();
        if (visibleChildren == 0)
            return new Size(0, 0);

        if (double.IsInfinity(availableSize.Width))
            return MeasureUnboundedWidth(availableSize);

        double columnWidth = GetLogicalColumnWidth(availableSize.Width, visibleChildren);
        double visualColumnWidth = GetVisualColumnWidth(availableSize.Width, visibleChildren);
        double availableHeight = double.IsInfinity(availableSize.Height)
            ? double.PositiveInfinity
            : availableSize.Height;
        double desiredHeight = 0;

        foreach (UIElement child in Children)
        {
            if (child.Visibility == Visibility.Collapsed)
                continue;

            double measureHeight = SquareCells ? columnWidth : availableHeight;
            child.Measure(new Size(columnWidth, measureHeight));
            desiredHeight = Math.Max(desiredHeight, child.DesiredSize.Height);
        }

        if (SquareCells)
            return new Size(availableSize.Width, visualColumnWidth);

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
        double visualColumnWidth = GetVisualColumnWidth(finalSize.Width, visibleChildren);
        double scale = logicalColumnWidth > 0
            ? Math.Min(1, visualColumnWidth / logicalColumnWidth)
            : 1;
        double arrangeHeight = SquareCells
            ? logicalColumnWidth
            : scale > 0 ? finalSize.Height / scale : finalSize.Height;
        double x = 0;

        foreach (UIElement child in Children)
        {
            if (child.Visibility == Visibility.Collapsed)
                continue;

            ApplyScale(child, scale);
            child.Arrange(new Rect(x, 0, logicalColumnWidth, arrangeHeight));
            x += visualColumnWidth + ColumnSpacing;
        }

        return SquareCells
            ? new Size(finalSize.Width, visualColumnWidth)
            : finalSize;
    }

    private Size MeasureUnboundedWidth(Size availableSize)
    {
        double desiredWidth = 0;
        double desiredHeight = 0;

        int visibleChildren = CountVisibleChildren();
        double columnWidth = GetLogicalColumnWidth(availableSize.Width, visibleChildren);
        int placedChildren = 0;

        foreach (UIElement child in Children)
        {
            if (child.Visibility == Visibility.Collapsed)
                continue;

            if (columnWidth > 0)
            {
                double measureHeight = SquareCells ? columnWidth : availableSize.Height;
                child.Measure(new Size(columnWidth, measureHeight));
                desiredWidth += columnWidth;
            }
            else
            {
                child.Measure(availableSize);
                desiredWidth += child.DesiredSize.Width;
            }

            placedChildren++;
            if (placedChildren < visibleChildren)
                desiredWidth += ColumnSpacing;

            desiredHeight = Math.Max(
                desiredHeight,
                SquareCells ? columnWidth : child.DesiredSize.Height);
        }

        return new Size(desiredWidth, desiredHeight);
    }

    private double GetContentWidth(double availableWidth, int visibleChildren)
    {
        if (visibleChildren <= 0 || double.IsInfinity(availableWidth))
            return 0;

        double spacingTotal = Math.Max(0, visibleChildren - 1) * ColumnSpacing;
        return Math.Max(0, availableWidth - spacingTotal);
    }

    private double GetVisualColumnWidth(double availableWidth, int visibleChildren)
    {
        if (visibleChildren <= 0)
            return 0;

        double contentWidth = GetContentWidth(availableWidth, visibleChildren);
        return double.IsInfinity(availableWidth) ? 0 : contentWidth / visibleChildren;
    }

    private double GetLogicalColumnWidth(double availableWidth, int visibleChildren)
    {
        if (visibleChildren <= 0)
            return 0;

        double measuredWidth = GetVisualColumnWidth(availableWidth, visibleChildren);
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

    private static void OnMinimumColumnWidthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        OnLayoutPropertyChanged(d, e);

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EqualColumnsPanel panel)
            panel.InvalidateMeasure();
    }
}
