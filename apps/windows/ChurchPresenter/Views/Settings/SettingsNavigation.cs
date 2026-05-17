using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace ChurchPresenter.Views;

/// <summary>
/// Settings hub/detail <see cref="Frame"/> navigation using horizontal slide transitions (see Microsoft Learn: Page transitions).
/// </summary>
/// <remarks>
/// Forward navigation uses <see cref="SlideNavigationTransitionEffect.FromRight"/> so the detail page enters from the right.
/// Back navigation uses <see cref="Frame.GoBack(NavigationTransitionInfo)"/> with the same effect value: the platform
/// reverses the slide for a stack pop so the outgoing page moves off toward the trailing edge (right in LTR), instead of
/// the hub appearing to sweep in from the left as the dominant motion when using <c>FromLeft</c> with GoBack.
/// </remarks>
internal static class SettingsNavigation
{
    /// <summary>Hub → detail: incoming page enters from the right.</summary>
    public static void NavigateToDetail(Frame? frame, Type pageType)
    {
        if (frame is null)
            return;
        frame.Navigate(pageType, null, NavigateForwardSlide());
    }

    /// <summary>Detail → hub: pop stack; outgoing page moves toward the trailing edge (right in LTR).</summary>
    public static void GoBack(Frame? frame)
    {
        if (frame is { CanGoBack: true })
            frame.GoBack(GoBackSlide());
    }

    private static SlideNavigationTransitionInfo NavigateForwardSlide() =>
        new() { Effect = SlideNavigationTransitionEffect.FromRight };

    private static SlideNavigationTransitionInfo GoBackSlide() =>
        new() { Effect = SlideNavigationTransitionEffect.FromRight };
}