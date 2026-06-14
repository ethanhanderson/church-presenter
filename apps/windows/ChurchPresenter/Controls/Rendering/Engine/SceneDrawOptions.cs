#nullable enable

using Windows.UI;

namespace ChurchPresenter.Controls.Rendering.Engine;

/// <summary>
/// Controls how transparent slide backgrounds are filled during rasterization.
/// Output surfaces keep real transparency; preview/thumbnail surfaces may show a
/// checkerboard or an optional solid preview fill.
/// </summary>
public readonly struct SceneDrawOptions
{
    /// <summary>Live audience/stage output and export: no checkerboard or preview fill.</summary>
    public static SceneDrawOptions Output { get; } = new(showTransparencyPattern: false, previewBackgroundFill: null);

    /// <summary>
    /// Initializes draw options.
    /// </summary>
    /// <param name="showTransparencyPattern">
    /// When true and the slide background is transparent, draw the design-tool checkerboard unless
    /// <paramref name="previewBackgroundFill"/> overrides it.
    /// </param>
    /// <param name="previewBackgroundFill">
    /// Optional solid fill behind transparent slide areas on preview surfaces.
    /// </param>
    /// <param name="isDarkTheme">
    /// When true, the checkerboard uses dark-mode tones; when false, light-mode tones are used.
    /// </param>
    /// <param name="checkerboardTileCount">
    /// Desired number of checkerboard tiles across the render width. Zero falls back to the
    /// <see cref="CheckerboardPatternBrush.DefaultCellSize"/> constant.
    /// </param>
    public SceneDrawOptions(
        bool showTransparencyPattern,
        Color? previewBackgroundFill,
        bool isDarkTheme = false,
        int checkerboardTileCount = 0)
    {
        ShowTransparencyPattern = showTransparencyPattern;
        PreviewBackgroundFill = previewBackgroundFill is { A: > 0 } fill ? fill : null;
        IsDarkTheme = isDarkTheme;
        CheckerboardTileCount = checkerboardTileCount > 0 ? checkerboardTileCount : 0;
    }

    /// <summary>Whether a checkerboard may be drawn for transparent backgrounds.</summary>
    public bool ShowTransparencyPattern { get; }

    /// <summary>Optional fill over the checkerboard; 100% opacity replaces the pattern entirely.</summary>
    public Color? PreviewBackgroundFill { get; }

    /// <summary>
    /// When true, the Win2D checkerboard raster path draws dark-mode tones rather than
    /// the default light-mode palette. Set this from the host element's actual theme.
    /// </summary>
    public bool IsDarkTheme { get; }

    /// <summary>
    /// Desired number of checkerboard tiles across the render width. The renderer converts
    /// this to a pixel cell size relative to the scene's native render dimensions, producing
    /// a proportional pattern regardless of the surface resolution. Zero means use the
    /// fixed <see cref="CheckerboardPatternBrush.DefaultCellSize"/> fallback.
    /// </summary>
    public int CheckerboardTileCount { get; }

    /// <summary>Preview/thumbnail/editor surfaces with optional custom fill behind transparency.</summary>
    /// <param name="previewBackgroundFill">Optional fill behind transparency; 100% opacity hides the checkerboard.</param>
    /// <param name="isDarkTheme">True when the host UI is in dark mode.</param>
    /// <param name="checkerboardTileCount">Desired tiles across the render width (0 = fixed fallback).</param>
    public static SceneDrawOptions ForPreview(
        Color? previewBackgroundFill = null,
        bool isDarkTheme = false,
        int checkerboardTileCount = 0) =>
        new(showTransparencyPattern: true,
            previewBackgroundFill: previewBackgroundFill,
            isDarkTheme: isDarkTheme,
            checkerboardTileCount: checkerboardTileCount);

    /// <summary>
    /// Show deck thumbnails: raster slide content only; checkerboard and deck tint are composed in XAML.
    /// </summary>
    public static SceneDrawOptions ForDeckThumbnail() =>
        new(showTransparencyPattern: false, previewBackgroundFill: null);
}
