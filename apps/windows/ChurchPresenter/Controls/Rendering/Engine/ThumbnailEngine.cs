#nullable enable

using ChurchPresenter.Adapters.FileSystem;
using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Controls;
using ChurchPresenter.Hosting;
using ChurchPresenter.Models.Documents;
using ChurchPresenter.Services.Themes;

using Microsoft.Extensions.DependencyInjection;

using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml.Media.Imaging;

using System.Runtime.InteropServices.WindowsRuntime;

using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace ChurchPresenter.Controls.Rendering.Engine;

/// <summary>
/// Offscreen rasterizer for slide thumbnails and image export. Compiles a presentation slide to a
/// <see cref="SlideScene"/> and draws it onto a headless Win2D <see cref="CanvasRenderTarget"/> via
/// the shared <see cref="SceneSurfaceRenderer"/>, so thumbnails and exports use the exact same
/// rasterizer as live output instead of a parallel XAML rendering path. Video, live-video, and web
/// layers are not rasterized here; callers that need those composite them on top (parity with the
/// legacy thumbnail path).
/// </summary>
public sealed class ThumbnailEngine(
    ISlideSceneCompiler sceneCompiler,
    IThemeResolutionService themeResolution,
    IBundleAssetCacheService assetCache)
{
    private readonly ISlideSceneCompiler _sceneCompiler = sceneCompiler ?? throw new ArgumentNullException(nameof(sceneCompiler));
    private readonly IThemeResolutionService _themeResolution = themeResolution ?? throw new ArgumentNullException(nameof(themeResolution));
    private readonly IBundleAssetCacheService _assetCache = assetCache ?? throw new ArgumentNullException(nameof(assetCache));

    /// <summary>
    /// Compiles a slide, preloads its background/media images (video first-frames included), and
    /// renders it to a XAML image source. The one-call path deck, editor, and theme-picker
    /// thumbnails use. For <see cref="RenderIntent.Thumbnail"/>, pass content-derived checkerboard tone
    /// from <see cref="SlideCheckerboardTone.UseDarkCheckerboard"/>; editor/preview intents may still
    /// follow host UI theme. Must run on the UI thread.
    /// </summary>
    public async Task<SoftwareBitmapSource> RenderSlideAsync(
        PresentationProject? project,
        PresentationSlide slide,
        RenderIntent intent,
        IReadOnlyCollection<string>? visibleLayerIds = null,
        Windows.UI.Color? previewBackgroundFill = null,
        bool isDarkTheme = false,
        CancellationToken cancellationToken = default)
    {
        ThrowIfShowDeckThumbnailRasterBlocked(cancellationToken);
        if (intent == RenderIntent.Thumbnail)
        {
            ShowNavigationDiagnostics.Log(
                "ThumbnailEngine.RenderSlide",
                $"slide={slide.Id} phase={App.Services?.GetService<IShowRenderSession>()?.ThumbnailRasterPhase}");
        }
        SlideScene scene = CompileScene(project, slide, intent, visibleLayerIds);
        using PreloadedSceneImageSource images = await SceneImageSourceFactory
            .CreateAsync(scene, _assetCache, project, cancellationToken: cancellationToken)
            .ConfigureAwait(true);
        ThrowIfShowDeckThumbnailRasterBlocked(cancellationToken);
        SceneDrawOptions options = CreateDrawOptions(intent, previewBackgroundFill, isDarkTheme);
        return await RenderToImageSourceAsync(scene, images, options, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Deck-thumbnail path that also returns a pixel buffer for navigation display copies.</summary>
    public async Task<DeckThumbnailRasterBundle> RenderSlideBundleAsync(
        PresentationProject? project,
        PresentationSlide slide,
        RenderIntent intent,
        IReadOnlyCollection<string>? visibleLayerIds = null,
        Windows.UI.Color? previewBackgroundFill = null,
        bool isDarkTheme = false,
        CancellationToken cancellationToken = default)
    {
        ThrowIfShowDeckThumbnailRasterBlocked(cancellationToken);
        if (intent == RenderIntent.Thumbnail)
        {
            ShowNavigationDiagnostics.Log(
                "ThumbnailEngine.RenderSlide",
                $"slide={slide.Id} phase={App.Services?.GetService<IShowRenderSession>()?.ThumbnailRasterPhase}");
        }

        SlideScene scene = CompileScene(project, slide, intent, visibleLayerIds);
        using PreloadedSceneImageSource images = await SceneImageSourceFactory
            .CreateAsync(scene, _assetCache, project, cancellationToken: cancellationToken)
            .ConfigureAwait(true);
        ThrowIfShowDeckThumbnailRasterBlocked(cancellationToken);
        SceneDrawOptions options = CreateDrawOptions(intent, previewBackgroundFill, isDarkTheme);
        return await RenderToImageSourceBundleAsync(scene, images, options, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Compiles and renders a slide to an image source and reports its native render size, so
    /// preview surfaces can letterbox the result to the slide aspect ratio. Pass
    /// <paramref name="isDarkTheme"/> true when the host UI is in dark mode so the transparency
    /// checkerboard uses dark tones. Must run on the UI thread.
    /// </summary>
    public async Task<(SoftwareBitmapSource Image, PixelSize Size)> RenderSlideSizedAsync(
        PresentationProject? project,
        PresentationSlide slide,
        RenderIntent intent,
        IReadOnlyCollection<string>? visibleLayerIds = null,
        Windows.UI.Color? previewBackgroundFill = null,
        bool isDarkTheme = false,
        CancellationToken cancellationToken = default)
    {
        ThrowIfBlockedDuringCachedShowRevisit(cancellationToken);
        SlideScene scene = CompileScene(project, slide, intent, visibleLayerIds);
        using PreloadedSceneImageSource images = await SceneImageSourceFactory
            .CreateAsync(scene, _assetCache, project, cancellationToken: cancellationToken)
            .ConfigureAwait(true);
        ThrowIfBlockedDuringCachedShowRevisit(cancellationToken);
        SceneDrawOptions options = CreateDrawOptions(intent, previewBackgroundFill, isDarkTheme);
        SoftwareBitmapSource image = await RenderToImageSourceAsync(scene, images, options).ConfigureAwait(true);
        return (image, scene.RenderSize);
    }

    /// <summary>Resolves the theme and compiles a slide into a render-ready scene.</summary>
    public SlideScene CompileScene(
        PresentationProject? project,
        PresentationSlide slide,
        RenderIntent intent,
        IReadOnlyCollection<string>? visibleLayerIds = null)
    {
        ArgumentNullException.ThrowIfNull(slide);

        ThemeResolutionResult theme = _themeResolution.ResolveThemeSlide(project, slide);
        SceneCompileResult result = _sceneCompiler.Compile(new SceneCompileRequest
        {
            Project = project,
            Slide = slide,
            ThemeSlide = theme.ThemeSlide,
            BuildIndex = 0,
            VisibleLayerIds = visibleLayerIds?.ToHashSet(StringComparer.OrdinalIgnoreCase),
            Intent = intent,
        });

        return result.Scene;
    }

    /// <summary>Draws a scene onto a new offscreen render target at its native render size.</summary>
    public CanvasRenderTarget RenderToTarget(
        SlideScene scene,
        ISceneImageSource? images = null,
        SceneDrawOptions options = default,
        float dpi = 96f,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scene);
        cancellationToken.ThrowIfCancellationRequested();

        CanvasDevice device = CanvasDevice.GetSharedDevice();
        var target = new CanvasRenderTarget(
            device,
            Math.Max(1, scene.RenderSize.Width),
            Math.Max(1, scene.RenderSize.Height),
            dpi);

        using (CanvasDrawingSession session = target.CreateDrawingSession())
            SceneSurfaceRenderer.Draw(session, scene, images, options);

        return target;
    }

    /// <summary>Renders a scene to a XAML <see cref="SoftwareBitmapSource"/> for an Image control. Must run on the UI thread.</summary>
    public async Task<SoftwareBitmapSource> RenderToImageSourceAsync(
        SlideScene scene,
        ISceneImageSource? images = null,
        SceneDrawOptions options = default,
        CancellationToken cancellationToken = default)
    {
        DeckThumbnailRasterBundle bundle =
            await RenderToImageSourceBundleAsync(scene, images, options, cancellationToken).ConfigureAwait(true);
        return (SoftwareBitmapSource)bundle.Source;
    }

    private async Task<DeckThumbnailRasterBundle> RenderToImageSourceBundleAsync(
        SlideScene scene,
        ISceneImageSource? images,
        SceneDrawOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using CanvasRenderTarget target = RenderToTarget(scene, images, options, cancellationToken: cancellationToken);
        int width = (int)target.SizeInPixels.Width;
        int height = (int)target.SizeInPixels.Height;
        byte[] pixels = target.GetPixelBytes();

        SoftwareBitmap bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            pixels.AsBuffer(),
            BitmapPixelFormat.Bgra8,
            width,
            height,
            BitmapAlphaMode.Premultiplied);

        try
        {
            var source = new SoftwareBitmapSource();
            await source.SetBitmapAsync(bitmap);
            return new DeckThumbnailRasterBundle(source, pixels, width, height);
        }
        finally
        {
            bitmap.Dispose();
        }
    }

    /// <summary>Renders a scene to a PNG stream for image export.</summary>
    public async Task SavePngAsync(
        SlideScene scene,
        IRandomAccessStream stream,
        ISceneImageSource? images = null,
        SceneDrawOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using CanvasRenderTarget target = RenderToTarget(scene, images, options);
        await target.SaveAsync(stream, CanvasBitmapFileFormat.Png);
    }

    /// <summary>
    /// Compiles a slide, preloads its background/media images (video first-frames included), and
    /// writes it to a PNG stream. The one-call path slide image export uses, sharing the live
    /// rasterizer instead of a parallel <c>SlideStageView</c> + <c>RenderTargetBitmap</c> capture.
    /// </summary>
    public async Task SaveSlidePngAsync(
        PresentationProject? project,
        PresentationSlide slide,
        RenderIntent intent,
        IRandomAccessStream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        SlideScene scene = CompileScene(project, slide, intent);
        using PreloadedSceneImageSource images = await SceneImageSourceFactory
            .CreateAsync(scene, _assetCache, project, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await SavePngAsync(scene, stream, images, SceneDrawOptions.Output).ConfigureAwait(false);
    }

    private static SceneDrawOptions CreateDrawOptions(RenderIntent intent, Windows.UI.Color? previewBackgroundFill, bool isDarkTheme)
    {
        return intent switch
        {
            RenderIntent.Export or RenderIntent.AudienceOutput or RenderIntent.StagePreview or RenderIntent.Benchmark
                => SceneDrawOptions.Output,
            RenderIntent.Thumbnail => SceneDrawOptions.ForDeckThumbnail(),
            RenderIntent.Editor
                => SceneDrawOptions.ForPreview(
                    previewBackgroundFill,
                    isDarkTheme,
                    checkerboardTileCount: CheckerboardPatternBrush.EditorTileCount),
            _
                => SceneDrawOptions.ForPreview(
                    previewBackgroundFill,
                    isDarkTheme,
                    checkerboardTileCount: CheckerboardPatternBrush.PreviewTileCount),
        };
    }

    /// <summary>Deck slide-list thumbnails must not raster while Show is mid navigation transition.</summary>
    private static void ThrowIfShowDeckThumbnailRasterBlocked(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (App.Services?.GetService<IShowRenderSession>() is
            { ThumbnailRasterPhase: DeckThumbnailRasterPhase.Blocked or DeckThumbnailRasterPhase.Returning })
            throw new OperationCanceledException();
    }

    /// <summary>Editor/theme GPU previews must not continue while cached Show is sliding in.</summary>
    private static void ThrowIfBlockedDuringCachedShowRevisit(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (App.Services?.GetService<IShowRenderSession>()?.ThumbnailRasterPhase
            == DeckThumbnailRasterPhase.Returning)
            throw new OperationCanceledException();
    }
}
