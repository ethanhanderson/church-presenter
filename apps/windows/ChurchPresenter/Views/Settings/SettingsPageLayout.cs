using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChurchPresenter.Views;

/// <summary>
/// Centers settings hub/detail content in a column of ~70% window width (clamped to available space, with a minimum width floor).
/// </summary>
internal static class SettingsPageLayout
{
    private const double MinContentWidth = 440;
    private const double WidthFraction = 0.7;

    /// <summary>
    /// Sizes and centers <paramref name="widthTarget"/> so it fills about 70% of the page width (up to the inset-limited available width).
    /// </summary>
    /// <param name="page">The settings page (used for <see cref="FrameworkElement.ActualWidth"/>).</param>
    /// <param name="widthTarget">The column root (hub <see cref="Border"/> or detail <see cref="Border"/> wrapping header + body).</param>
    /// <param name="horizontalInset">Total horizontal inset already applied outside <paramref name="widthTarget"/> (e.g. page padding 32+32).</param>
    public static void BindSettingsColumnWidth(Page page, FrameworkElement widthTarget, double horizontalInset = 64)
    {
        void Update()
        {
            if (page.ActualWidth <= 0)
                return;

            double available = Math.Max(0, page.ActualWidth - horizontalInset);
            double ideal = Math.Max(MinContentWidth, page.ActualWidth * WidthFraction);
            double columnWidth = Math.Min(available, ideal);

            widthTarget.Width = columnWidth;
            widthTarget.HorizontalAlignment = HorizontalAlignment.Center;
        }

        page.SizeChanged += (_, _) => Update();
        page.Loaded += (_, _) => Update();
        // Must run once now: callers often invoke this from their own Loaded handler, after the page's Loaded has already fired.
        Update();
    }
}