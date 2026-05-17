using Microsoft.UI.Xaml.Media;

using Windows.UI;

namespace ChurchPresenter.Controls;

/// <summary>
/// Shared in-app <see cref="AcrylicBrush"/> settings for slide deck cards and slide-stage solid fills (thumbnail).
/// </summary>
public static class InAppAcrylicBrushFactory
{
    private const double GroupOrSolidTintOpacity = 0.52;
    private const double GroupOrSolidTintLuminosityOpacity = 0.84;

    /// <summary>Group-colored slide card chrome (Show deck).</summary>
    public static AcrylicBrush CreateGroupTint(Color tintColor) => CreateTint(tintColor, GroupOrSolidTintOpacity, GroupOrSolidTintLuminosityOpacity);

    /// <summary>Solid-color slide background in thumbnail mode (not image/video/gradient).</summary>
    public static AcrylicBrush CreateSolidSlideThumbnailTint(Color slideColor) =>
        CreateTint(slideColor, GroupOrSolidTintOpacity, GroupOrSolidTintLuminosityOpacity);

    private static AcrylicBrush CreateTint(Color tintColor, double tintOpacity, double luminosityOpacity) =>
        new()
        {
            TintColor = tintColor,
            TintOpacity = tintOpacity,
            TintLuminosityOpacity = luminosityOpacity,
            FallbackColor = tintColor,
        };
}