using Microsoft.UI.Xaml;

using Windows.UI.ViewManagement;

namespace ChurchPresenter;

/// <summary>
/// Applies theme using <see cref="FrameworkElement.RequestedTheme"/> (<see cref="ElementTheme"/>).
/// WinUI does not allow setting <see cref="Application.RequestedTheme"/> after startup; use the window root instead.
/// </summary>
public static class AppThemeHelper
{
    public static ElementTheme MapElementTheme(string? theme) =>
        theme?.Trim().ToLowerInvariant() switch
        {
            "light" => ElementTheme.Light,
            "dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

    /// <summary>Whether Windows is using a dark background (for theme-brush fallbacks).</summary>
    public static bool IsSystemDarkTheme()
    {
        try
        {
            var settings = new UISettings();
            Windows.UI.Color bg = settings.GetColorValue(UIColorType.Background);
            return (bg.R + bg.G + bg.B) / 3.0 < 128;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Sets theme on the window content tree (call after the window exists).</summary>
    public static void ApplyToWindow(Window? window, string? theme)
    {
        if (window?.Content is FrameworkElement root)
        {
            root.RequestedTheme = MapElementTheme(theme);
            return;
        }

        window?.DispatcherQueue?.TryEnqueue(() =>
        {
            if (window.Content is FrameworkElement late)
                late.RequestedTheme = MapElementTheme(theme);
        });
    }
}