using System;
using System.IO;
using System.Linq;

using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Controls.Rendering;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;

using Windows.Foundation;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.UI;

namespace ChurchPresenter.Controls;

/// <summary>
/// Shared WinUI renderer for thumbnails, preview surfaces, editor canvases, and audience output.
/// </summary>
public sealed class SlideStageView : UserControl
{
    public static readonly DependencyProperty ProjectProperty =
        DependencyProperty.Register(
            nameof(Project),
            typeof(PresentationProject),
            typeof(SlideStageView),
            new PropertyMetadata(null, OnRenderPropertyChanged));

    public static readonly DependencyProperty SlideProperty =
        DependencyProperty.Register(
            nameof(Slide),
            typeof(PresentationSlide),
            typeof(SlideStageView),
            new PropertyMetadata(null, OnRenderPropertyChanged));

    public static readonly DependencyProperty VisibleLayerIdsProperty =
        DependencyProperty.Register(
            nameof(VisibleLayerIds),
            typeof(IEnumerable<string>),
            typeof(SlideStageView),
            new PropertyMetadata(null, OnRenderPropertyChanged));

    public static readonly DependencyProperty MediaLayersProperty =
        DependencyProperty.Register(
            nameof(MediaLayers),
            typeof(MediaLayersState),
            typeof(SlideStageView),
            new PropertyMetadata(null, OnRenderPropertyChanged));

    public static readonly DependencyProperty SuppressPresentationProperty =
        DependencyProperty.Register(
            nameof(SuppressPresentation),
            typeof(bool),
            typeof(SlideStageView),
            new PropertyMetadata(false, OnRenderPropertyChanged));

    public static readonly DependencyProperty SuppressMediaProperty =
        DependencyProperty.Register(
            nameof(SuppressMedia),
            typeof(bool),
            typeof(SlideStageView),
            new PropertyMetadata(false, OnRenderPropertyChanged));

    public static readonly DependencyProperty IsBlackoutProperty =
        DependencyProperty.Register(
            nameof(IsBlackout),
            typeof(bool),
            typeof(SlideStageView),
            new PropertyMetadata(false, OnRenderPropertyChanged));

    public static readonly DependencyProperty IsClearProperty =
        DependencyProperty.Register(
            nameof(IsClear),
            typeof(bool),
            typeof(SlideStageView),
            new PropertyMetadata(false, OnRenderPropertyChanged));

    public static readonly DependencyProperty RenderModeProperty =
        DependencyProperty.Register(
            nameof(RenderMode),
            typeof(SlideStageRenderMode),
            typeof(SlideStageView),
            new PropertyMetadata(SlideStageRenderMode.Preview, OnRenderPropertyChanged));

    public static readonly DependencyProperty ShowSafeAreaProperty =
        DependencyProperty.Register(
            nameof(ShowSafeArea),
            typeof(bool),
            typeof(SlideStageView),
            new PropertyMetadata(false, OnRenderPropertyChanged));

    public static readonly DependencyProperty TrackPlaybackCoordinatorProperty =
        DependencyProperty.Register(
            nameof(TrackPlaybackCoordinator),
            typeof(bool),
            typeof(SlideStageView),
            new PropertyMetadata(true));

    /// <summary>When <see cref="RenderMode"/> is <see cref="SlideStageRenderMode.Output"/>, letterbox vs crop.</summary>
    public static readonly DependencyProperty OutputStageStretchProperty =
        DependencyProperty.Register(
            nameof(OutputStageStretch),
            typeof(Stretch),
            typeof(SlideStageView),
            new PropertyMetadata(Stretch.Uniform, OnRenderPropertyChanged));

    /// <summary>Optional aspect ratio label (e.g. 16:9) for sizing the stage when rendering audience output.</summary>
    public static readonly DependencyProperty OutputAspectRatioOverrideProperty =
        DependencyProperty.Register(
            nameof(OutputAspectRatioOverride),
            typeof(string),
            typeof(SlideStageView),
            new PropertyMetadata(null, OnRenderPropertyChanged));

    /// <summary>
    /// Solid colour painted behind transparent-background slides in thumbnail mode.
    /// When alpha is 0 (<see cref="Colors.Transparent"/>) the checkerboard fallback is used instead.
    /// </summary>
    public static readonly DependencyProperty ThumbnailBackgroundColorProperty =
        DependencyProperty.Register(
            nameof(ThumbnailBackgroundColor),
            typeof(Color),
            typeof(SlideStageView),
            new PropertyMetadata(Colors.Transparent, OnThumbnailBgColorChanged));

    private static void OnThumbnailBgColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SlideStageView view)
        {
            view._lastCheckerW = view._lastCheckerH = -1;
            view.TryApplyThumbnailCheckerboardBackground();
        }
    }

    private readonly Grid _root;
    private readonly IBundleAssetCacheService _assetCache;
    private readonly IMediaPlayerRegistration? _playbackCoordinator;
    private readonly ISlideSceneCompiler _sceneCompiler;
    private readonly IThemeResolutionService _themeResolution;
    private readonly IWinUiSceneHost _sceneHost = new WinUiSceneHost();
    private readonly ILogger<SlideStageView>? _logger;
    private readonly List<MediaPlayer> _activePlayers = new();
    private bool _isRefreshing;
    private bool _refreshRequested;
    private int _lastCheckerW = -1;
    private int _lastCheckerH = -1;
    private bool _lastCheckerDark;

    /// <summary>When true, engine (program) media players are tracked separately so slide changes do not dispose them.</summary>
    private bool _layeredOutputMediaActive;

    private bool _refreshCoalescePending;
    private Grid? _outputUnderlayHost;
    private Grid? _outputBackgroundHost;
    private Grid? _outputOverlayHost;
    private Grid? _outputSlideChromeHost;
    private string? _cachedOutputEngineMediaSignature;
    private string? _cachedOutputPresentationSignature;
    private readonly List<MediaPlayer> _engineMediaPlayers = new();
    private readonly List<MediaPlayer> _engineAudioPlayers = new();
    private readonly List<MediaPlayer> _presentationMediaPlayers = new();
    /// <summary>XAML default ctor; resolves services from the app container (single composition-root touchpoint).</summary>
    public SlideStageView()
        : this(App.Services)
    {
    }

    private SlideStageView(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _assetCache = services.GetRequiredService<IBundleAssetCacheService>();
        _playbackCoordinator = services.GetService<IMediaPlayerRegistration>();
        _sceneCompiler = services.GetService<ISlideSceneCompiler>() ?? new SlideSceneCompiler();
        _themeResolution = services.GetService<IThemeResolutionService>() ?? new ThemeResolutionService();
        _logger = services.GetService<ILogger<SlideStageView>>();
        _root = new Grid
        {
            Background = new SolidColorBrush(Colors.Black),
        };
        Content = _root;
        Loaded += (_, _) => ScheduleRefreshCoalesced();
        Unloaded += (_, _) => DisposePlayers();
        SizeChanged += OnSlideStageSizeChanged;
        ActualThemeChanged += (_, _) =>
        {
            _lastCheckerW = _lastCheckerH = -1;
            TryApplyThumbnailCheckerboardBackground();
        };
    }

    public PresentationProject? Project
    {
        get => (PresentationProject?)GetValue(ProjectProperty);
        set => SetValue(ProjectProperty, value);
    }

    public PresentationSlide? Slide
    {
        get => (PresentationSlide?)GetValue(SlideProperty);
        set => SetValue(SlideProperty, value);
    }

    public IEnumerable<string>? VisibleLayerIds
    {
        get => (IEnumerable<string>?)GetValue(VisibleLayerIdsProperty);
        set
        {
            if (ReferenceEquals(VisibleLayerIds, value))
                return;

            SetValue(VisibleLayerIdsProperty, value);
        }
    }

    public MediaLayersState? MediaLayers
    {
        get => (MediaLayersState?)GetValue(MediaLayersProperty);
        set
        {
            if (ReferenceEquals(MediaLayers, value))
                return;

            SetValue(MediaLayersProperty, value);
        }
    }

    public bool SuppressPresentation
    {
        get => (bool)GetValue(SuppressPresentationProperty);
        set
        {
            if (SuppressPresentation == value)
                return;

            SetValue(SuppressPresentationProperty, value);
        }
    }

    public bool SuppressMedia
    {
        get => (bool)GetValue(SuppressMediaProperty);
        set
        {
            if (SuppressMedia == value)
                return;

            SetValue(SuppressMediaProperty, value);
        }
    }

    public bool IsBlackout
    {
        get => (bool)GetValue(IsBlackoutProperty);
        set
        {
            if (IsBlackout == value)
                return;

            SetValue(IsBlackoutProperty, value);
        }
    }

    public bool IsClear
    {
        get => (bool)GetValue(IsClearProperty);
        set
        {
            if (IsClear == value)
                return;

            SetValue(IsClearProperty, value);
        }
    }

    public SlideStageRenderMode RenderMode
    {
        get => (SlideStageRenderMode)GetValue(RenderModeProperty);
        set
        {
            if (RenderMode == value)
                return;

            SetValue(RenderModeProperty, value);
        }
    }

    public bool ShowSafeArea
    {
        get => (bool)GetValue(ShowSafeAreaProperty);
        set
        {
            if (ShowSafeArea == value)
                return;

            SetValue(ShowSafeAreaProperty, value);
        }
    }

    /// <summary>
    /// When <c>true</c>, output-mode players are published to the shared media playback coordinator.
    /// Hosts that suppress their own media should disable this to avoid overriding engine media playback.
    /// </summary>
    public bool TrackPlaybackCoordinator
    {
        get => (bool)GetValue(TrackPlaybackCoordinatorProperty);
        set => SetValue(TrackPlaybackCoordinatorProperty, value);
    }

    public Stretch OutputStageStretch
    {
        get => (Stretch)GetValue(OutputStageStretchProperty);
        set => SetValue(OutputStageStretchProperty, value);
    }

    public string? OutputAspectRatioOverride
    {
        get => (string?)GetValue(OutputAspectRatioOverrideProperty);
        set => SetValue(OutputAspectRatioOverrideProperty, value);
    }

    public Color ThumbnailBackgroundColor
    {
        get => (Color)GetValue(ThumbnailBackgroundColorProperty);
        set => SetValue(ThumbnailBackgroundColorProperty, value);
    }

    private static void OnRenderPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs _)
    {
        if (dependencyObject is SlideStageView view)
            view.ScheduleRefreshCoalesced();
    }

    private void ScheduleRefreshCoalesced()
    {
        if (!IsLoaded)
            return;

        if (_refreshCoalescePending)
            return;

        _refreshCoalescePending = true;
        var dq = DispatcherQueue.GetForCurrentThread();
        if (dq == null)
        {
            _refreshCoalescePending = false;
            RequestRefresh();
            return;
        }

        dq.TryEnqueue(DispatcherQueuePriority.Normal, () =>
        {
            _refreshCoalescePending = false;
            RequestRefresh();
        });
    }

    private void OnSlideStageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        TryApplyThumbnailCheckerboardBackground();
    }

    /// <summary>
    /// Fills the stage root with either the configured solid background colour or a checkerboard bitmap for transparent-background slides in thumbnail mode.
    /// </summary>
    private void TryApplyThumbnailCheckerboardBackground()
    {
        if (IsBlackout)
            return;
        if (RenderMode != SlideStageRenderMode.Thumbnail || Slide?.Background is not TransparentSlideBackground)
            return;
        if (ActualWidth <= 0 || ActualHeight <= 0)
            return;

        var solidColor = ThumbnailBackgroundColor;
        if (solidColor.A > 0)
        {
            // Use the configured solid background colour instead of the checkerboard.
            _root.Background = new SolidColorBrush(solidColor);
            _lastCheckerW = _lastCheckerH = -1;
            return;
        }

        var w = (int)Math.Max(1, Math.Round(ActualWidth));
        var h = (int)Math.Max(1, Math.Round(ActualHeight));
        var dark = ActualTheme == ElementTheme.Dark;
        if (w == _lastCheckerW && h == _lastCheckerH && dark == _lastCheckerDark)
            return;

        _lastCheckerW = w;
        _lastCheckerH = h;
        _lastCheckerDark = dark;
        _root.Background = CheckerboardPatternBrush.CreateBrush(w, h, CheckerboardPatternBrush.DefaultCellSize, dark);
    }

    private void ScheduleThumbnailCheckerboardAfterLayout()
    {
        if (IsBlackout)
            return;
        if (RenderMode != SlideStageRenderMode.Thumbnail || Slide?.Background is not TransparentSlideBackground)
            return;

        var dq = DispatcherQueue.GetForCurrentThread();
        if (dq == null)
            return;

        dq.TryEnqueue(DispatcherQueuePriority.Normal, TryApplyThumbnailCheckerboardBackground);
    }

    private void RequestRefresh()
    {
        if (!IsLoaded)
            return;

        if (_isRefreshing)
        {
            _refreshRequested = true;
            return;
        }

        try
        {
            _isRefreshing = true;
            RefreshCore();
        }
        finally
        {
            _isRefreshing = false;
        }

        if (_refreshRequested)
        {
            _refreshRequested = false;
            RequestRefresh();
        }
    }

    private void RefreshCore()
    {
        try
        {
            if (RenderMode == SlideStageRenderMode.Output)
            {
                RefreshCoreOutputLayered();
                return;
            }

            _layeredOutputMediaActive = false;
            ResetOutputLayerHosts();
            DisposePlayers();
            _root.Children.Clear();
            RefreshCoreUnifiedStack();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Slide stage refresh failed.");
            try
            {
                DisposePlayers();
            }
            catch
            {
                // Best effort cleanup.
            }

            ResetOutputLayerHosts();
            _root.Children.Clear();
            _root.Background = new SolidColorBrush(Colors.Black);
            _root.Children.Add(CreateCenteredMessage("Unable to display media", 18, Colors.White, 0.85));
        }
    }

    private void ResetOutputLayerHosts()
    {
        _outputUnderlayHost = null;
        _outputBackgroundHost = null;
        _outputOverlayHost = null;
        _outputSlideChromeHost = null;
        _cachedOutputEngineMediaSignature = null;
        _cachedOutputPresentationSignature = null;
    }

    private static string BuildOutputEngineMediaSignature(
        PresentationProject? project,
        MediaLayersState? layers,
        bool suppressMedia)
    {
        static string Normalize(string? value) => value?.Trim() ?? "";

        static string LayerSig(OutputLayerMedia? layer)
        {
            if (layer == null)
                return "";

            return string.Join(":",
                Normalize(layer.MediaId),
                Normalize(layer.MediaType),
                Normalize(layer.Fit),
                layer.Loop,
                layer.Muted,
                layer.Autoplay,
                Normalize(layer.ResolvedSourcePath));
        }

        var projectKey = PresentationModelUtilities.StablePresentationKey(project);
        layers ??= new MediaLayersState();
        return string.Join("|",
            projectKey,
            suppressMedia,
            LayerSig(layers.MediaUnderlay),
            LayerSig(layers.MediaOverlay),
            LayerSig(layers.Audio));
    }

    private static string BuildOutputPresentationSignature(
        PresentationProject? project,
        PresentationSlide? slide,
        IEnumerable<string>? visibleLayerIds,
        bool suppressPresentation,
        bool isBlackout,
        bool isClear,
        bool showSafeArea,
        string? outputAspectOverride,
        Stretch outputStretch)
    {
        var vis = visibleLayerIds == null
            ? ""
            : string.Join(",", visibleLayerIds.OrderBy(static id => id, StringComparer.OrdinalIgnoreCase));
        var projectKey = PresentationModelUtilities.StablePresentationKey(project);
        return string.Join("|",
            projectKey,
            slide?.Id ?? "",
            vis,
            suppressPresentation,
            isBlackout,
            isClear,
            showSafeArea,
            outputAspectOverride ?? "",
            outputStretch.ToString());
    }

    private void RefreshCoreOutputLayered()
    {
        _layeredOutputMediaActive = true;
        _lastCheckerW = _lastCheckerH = -1;
        _lastCheckerDark = false;
        _root.Background = CreateStageSurfaceBackground();

        string? aspectForSize = Project?.Manifest.AspectRatio;
        if (!string.IsNullOrWhiteSpace(OutputAspectRatioOverride))
            aspectForSize = OutputAspectRatioOverride;

        var size = PresentationModelUtilities.GetBaseSlideSize(aspectForSize, Project?.Manifest.SlideSize);

        var engineSig = BuildOutputEngineMediaSignature(Project, MediaLayers, SuppressMedia);
        var presSig = BuildOutputPresentationSignature(
            Project,
            Slide,
            VisibleLayerIds,
            SuppressPresentation,
            IsBlackout,
            IsClear,
            ShowSafeArea,
            OutputAspectRatioOverride,
            OutputStageStretch);

        if (IsBlackout)
        {
            DisposePlayers();
            ResetOutputLayerHosts();
            _root.Children.Clear();
            var blackoutGrid = new Grid
            {
                Width = size.Width,
                Height = size.Height,
                Background = new SolidColorBrush(Colors.Black),
            };
            blackoutGrid.Children.Add(new Border { Background = new SolidColorBrush(Colors.Black) });
            _root.Children.Add(new Viewbox
            {
                Stretch = OutputStageStretch,
                Child = blackoutGrid,
            });
            SyncOutputPlaybackCoordinator();
            return;
        }

        if (_outputUnderlayHost != null
            && string.Equals(engineSig, _cachedOutputEngineMediaSignature, StringComparison.Ordinal)
            && string.Equals(presSig, _cachedOutputPresentationSignature, StringComparison.Ordinal))
        {
            return;
        }

        var engineChanged = !string.Equals(engineSig, _cachedOutputEngineMediaSignature, StringComparison.Ordinal);
        var presChanged = !string.Equals(presSig, _cachedOutputPresentationSignature, StringComparison.Ordinal);
        var branch = _outputUnderlayHost == null || (engineChanged && presChanged)
            ? "rebuild-tree"
            : presChanged
                ? "presentation-only"
                : engineChanged
                    ? "engine-only"
                    : "skip-unchanged";

        if (_outputUnderlayHost == null || (engineChanged && presChanged))
        {
            DisposePlayers();
            _root.Children.Clear();
            BuildOutputLayeredTree(size);
        }
        else if (presChanged)
        {
            DisposePresentationPlayersOnly();
            FillOutputPresentationLayers(size);
        }
        else
        {
            DisposeEnginePlayersOnly();
            FillOutputEngineMediaLayers();
        }

        _cachedOutputEngineMediaSignature = engineSig;
        _cachedOutputPresentationSignature = presSig;

        ScheduleThumbnailCheckerboardAfterLayout();
        SyncOutputPlaybackCoordinator();
    }

    private void BuildOutputLayeredTree(SlideSizeDto size)
    {
        ResetOutputLayerHosts();

        var stageGrid = new Grid
        {
            Width = size.Width,
            Height = size.Height,
            Background = CreateStageSurfaceBackground(),
        };

        _outputUnderlayHost = CreateStretchHostGrid();
        _outputBackgroundHost = CreateStretchHostGrid();
        _outputOverlayHost = CreateStretchHostGrid();
        _outputSlideChromeHost = CreateStretchHostGrid();

        stageGrid.Children.Add(_outputUnderlayHost);
        stageGrid.Children.Add(_outputBackgroundHost);
        stageGrid.Children.Add(_outputOverlayHost);
        stageGrid.Children.Add(_outputSlideChromeHost);

        var viewbox = new Viewbox
        {
            Stretch = OutputStageStretch,
            Child = stageGrid,
        };
        _root.Children.Add(viewbox);

        FillOutputEngineMediaLayers();
        FillOutputPresentationLayers(size);
    }

    private static Grid CreateStretchHostGrid() =>
        new()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = new SolidColorBrush(Colors.Transparent),
        };

    private void FillOutputEngineMediaLayers()
    {
        if (_outputUnderlayHost == null || _outputOverlayHost == null)
            return;

        _outputUnderlayHost.Children.Clear();
        _outputOverlayHost.Children.Clear();

        if (SuppressMedia)
            return;

        AddFullscreenMedia(_outputUnderlayHost, MediaLayers?.MediaUnderlay, trackAsEngineMedia: true);
        AddFullscreenMedia(_outputOverlayHost, MediaLayers?.MediaOverlay, trackAsEngineMedia: true);
        AddAudioOnlyOutputLayer(MediaLayers?.Audio);
    }

    private void FillOutputPresentationLayers(SlideSizeDto size)
    {
        if (_outputBackgroundHost == null || _outputSlideChromeHost == null)
            return;

        _outputBackgroundHost.Children.Clear();
        _outputSlideChromeHost.Children.Clear();

        if (!SuppressPresentation)
            RenderBackground(_outputBackgroundHost, Slide?.Background);
        else
            _outputBackgroundHost.Background = new SolidColorBrush(Colors.Transparent);

        if (!SuppressPresentation)
        {
            if (Slide != null)
                RenderSlideLayers(_outputSlideChromeHost, Slide, size);
            else if (RenderMode == SlideStageRenderMode.Editor)
                _outputSlideChromeHost.Children.Add(CreateCenteredMessage("No slide selected"));
        }

        if (ShowSafeArea && !SuppressPresentation)
            _outputSlideChromeHost.Children.Add(CreateSafeAreaOverlay());

        if (IsClear)
        {
            _outputSlideChromeHost.Children.Add(new Border
            {
                Background = new SolidColorBrush(Colors.Black),
                Child = CreateCenteredMessage("Church Presenter", 48, Colors.White, 0.2),
            });
        }
    }

    private void RefreshCoreUnifiedStack()
    {
        _lastCheckerW = _lastCheckerH = -1;
        _lastCheckerDark = false;
        _root.Background = CreateStageSurfaceBackground();

        string? aspectForSize = Project?.Manifest.AspectRatio;
        if (!string.IsNullOrWhiteSpace(OutputAspectRatioOverride) &&
            (RenderMode == SlideStageRenderMode.Output || RenderMode == SlideStageRenderMode.Preview))
            aspectForSize = OutputAspectRatioOverride;

        var size = PresentationModelUtilities.GetBaseSlideSize(aspectForSize, Project?.Manifest.SlideSize);
        var stageGrid = new Grid
        {
            Width = size.Width,
            Height = size.Height,
            Background = CreateStageSurfaceBackground(),
        };

        var viewbox = new Viewbox
        {
            Stretch = RenderMode == SlideStageRenderMode.Output ? OutputStageStretch : Stretch.Uniform,
            Child = stageGrid,
        };
        _root.Children.Add(viewbox);

        if (IsBlackout)
        {
            stageGrid.Children.Add(new Border { Background = new SolidColorBrush(Colors.Black) });
            return;
        }

        // Output layer stack: bottom -> top
        // 1) mediaUnderlay  2) slide background (+ bg image/video)  3) mediaOverlay  4) slide elements (canvas)
        if (!SuppressMedia)
            AddFullscreenMedia(stageGrid, MediaLayers?.MediaUnderlay, trackAsEngineMedia: false);

        if (!SuppressPresentation)
            RenderBackground(stageGrid, Slide?.Background);

        if (!SuppressMedia)
        {
            AddFullscreenMedia(stageGrid, MediaLayers?.MediaOverlay, trackAsEngineMedia: false);
            AddAudioOnlyOutputLayer(MediaLayers?.Audio);
        }

        if (!SuppressPresentation)
        {
            if (Slide != null)
                RenderSlideLayers(stageGrid, Slide, size);
            else if (RenderMode == SlideStageRenderMode.Editor)
                stageGrid.Children.Add(CreateCenteredMessage("No slide selected"));
        }

        if (ShowSafeArea && !SuppressPresentation)
            stageGrid.Children.Add(CreateSafeAreaOverlay());

        if (IsClear)
        {
            stageGrid.Children.Add(new Border
            {
                Background = new SolidColorBrush(Colors.Black),
                Child = CreateCenteredMessage("Church Presenter", 48, Colors.White, 0.2),
            });
        }

        ScheduleThumbnailCheckerboardAfterLayout();

        SyncOutputPlaybackCoordinator();
    }

    private void SyncOutputPlaybackCoordinator()
    {
        if (RenderMode != SlideStageRenderMode.Output || !TrackPlaybackCoordinator)
            return;

        if (_layeredOutputMediaActive)
        {
            var mediaPlayers = _engineMediaPlayers
                .Concat(_presentationMediaPlayers)
                .ToArray();
            _playbackCoordinator?.RegisterActivePlayers(
                mediaPlayers,
                ResolveActiveMediaName(),
                MediaPlaybackRegistrationMode.Authority,
                MediaPlaybackTarget.MediaFiles);
            _playbackCoordinator?.RegisterActivePlayers(
                _engineAudioPlayers,
                ResolveActiveAudioName(),
                MediaPlaybackRegistrationMode.Authority,
                MediaPlaybackTarget.AudioFiles);
            return;
        }

        if (_activePlayers.Count > 0)
            NotifyCoordinatorOfNewPlayers(ResolveActiveMediaName());
        else
            _playbackCoordinator?.RegisterActivePlayers(Array.Empty<MediaPlayer>(), null, MediaPlaybackRegistrationMode.Authority);
    }

    private void RenderBackground(Grid stageGrid, SlideBackground? background)
    {
        var effectiveBackground = background ?? new SolidSlideBackground { Color = "#000000" };
        stageGrid.Background = CreateBackgroundBrush(effectiveBackground);

        switch (effectiveBackground)
        {
            case ImageSlideBackground imageBackground:
                AddBackgroundMedia(stageGrid, imageBackground.MediaId, imageBackground.Fit, imageBackground.Opacity, isVideo: false);
                break;

            case VideoSlideBackground videoBackground:
                AddBackgroundMedia(stageGrid, videoBackground.MediaId, videoBackground.Fit, videoBackground.Opacity, isVideo: true, loop: videoBackground.Loop, muted: videoBackground.Muted);
                break;
        }
    }

    private void AddBackgroundMedia(
        Grid stageGrid,
        string mediaId,
        string? fit,
        double opacity,
        bool isVideo,
        bool? loop = null,
        bool? muted = null)
    {
        var path = _assetCache.ResolveMediaPath(Project, mediaId);
        if (string.IsNullOrWhiteSpace(path))
            return;

        var element = isVideo
            ? RenderMode == SlideStageRenderMode.Thumbnail
                ? CreateVideoThumbnailElement(path, fit, opacity)
                : CreateVideoElement(path, fit, loop ?? true, muted ?? true, autoplay: true, opacity, trackAsEngineMedia: false)
            : CreateImageElement(path, fit, opacity);

        stageGrid.Children.Add(element);
    }

    private string? ResolveOutputLayerMediaPath(OutputLayerMedia media)
    {
        if (!string.IsNullOrWhiteSpace(media.ResolvedSourcePath) && File.Exists(media.ResolvedSourcePath))
            return media.ResolvedSourcePath;
        return _assetCache.ResolveMediaPath(Project, media.MediaId);
    }

    private void AddFullscreenMedia(Grid stageGrid, OutputLayerMedia? media, bool trackAsEngineMedia)
    {
        if (media == null || string.Equals(media.MediaType, "audio", StringComparison.OrdinalIgnoreCase))
            return;

        var path = ResolveOutputLayerMediaPath(media);
        if (string.IsNullOrWhiteSpace(path))
            return;

        var element = string.Equals(media.MediaType, "video", StringComparison.OrdinalIgnoreCase)
            ? RenderMode == SlideStageRenderMode.Thumbnail
                ? CreateVideoThumbnailElement(path, media.Fit, 1)
                : CreateVideoElement(path, media.Fit, media.Loop, media.Muted, media.Autoplay, 1, trackAsEngineMedia)
            : CreateImageElement(path, media.Fit, 1);

        stageGrid.Children.Add(element);
    }

    private void RenderSlideLayers(Grid stageGrid, PresentationSlide slide, SlideSizeDto size)
    {
        ThemeResolutionResult theme = _themeResolution.ResolveThemeSlide(Project, slide);
        SceneCompileResult result = _sceneCompiler.Compile(new SceneCompileRequest
        {
            Project = Project,
            Slide = slide,
            ThemeSlide = theme.ThemeSlide,
            BuildIndex = RenderMode == SlideStageRenderMode.Output ? -1 : 0,
            VisibleLayerIds = VisibleLayerIds?.ToHashSet(StringComparer.OrdinalIgnoreCase),
            Intent = RenderMode switch
            {
                SlideStageRenderMode.Thumbnail => RenderIntent.Thumbnail,
                SlideStageRenderMode.Editor => RenderIntent.Editor,
                SlideStageRenderMode.Output => RenderIntent.AudienceOutput,
                _ => RenderIntent.Preview,
            },
        });

        _sceneHost.Apply(stageGrid, result.Scene, new WinUiSceneHostOptions
        {
            CreateMediaElement = CreateMediaElementFromSceneNode,
            CreateWebElement = CreateWebElementFromSceneNode,
        });
    }

    private FrameworkElement? CreateMediaElementFromSceneNode(MediaSceneNode node)
    {
        return CreateMediaLayerElement(new MediaLayer
        {
            Id = node.Id,
            Name = node.Name,
            MediaId = node.MediaId,
            MediaType = node.MediaType,
            Fit = node.Fit,
            Loop = node.Loop,
            Muted = node.Muted,
            Autoplay = node.Autoplay,
            Transform = new LayerTransformModel
            {
                X = node.Transform.X,
                Y = node.Transform.Y,
                Width = node.Transform.Width,
                Height = node.Transform.Height,
                Rotation = node.Transform.Rotation,
                Opacity = node.Transform.Opacity,
                FlipX = node.Transform.FlipX,
                FlipY = node.Transform.FlipY,
                ClipContent = node.Transform.ClipContent,
            },
        });
    }

    private FrameworkElement? CreateWebElementFromSceneNode(WebSceneNode node)
    {
        return CreateWebLayerElement(new WebLayer
        {
            Id = node.Id,
            Name = node.Name,
            Url = node.Url,
            Zoom = node.Zoom,
            Interactive = node.Interactive,
            RefreshInterval = node.RefreshInterval,
            Transform = new LayerTransformModel
            {
                X = node.Transform.X,
                Y = node.Transform.Y,
                Width = node.Transform.Width,
                Height = node.Transform.Height,
                Rotation = node.Transform.Rotation,
                Opacity = node.Transform.Opacity,
                FlipX = node.Transform.FlipX,
                FlipY = node.Transform.FlipY,
                ClipContent = node.Transform.ClipContent,
            },
        });
    }

    private FrameworkElement? CreateLayerElement(SlideLayer layer)
    {
        return layer switch
        {
            TextLayer textLayer => CreateTextLayerElement(textLayer),
            ShapeLayer shapeLayer => CreateShapeLayerElement(shapeLayer),
            MediaLayer mediaLayer => CreateMediaLayerElement(mediaLayer),
            WebLayer webLayer => CreateWebLayerElement(webLayer),
            VectorLayer vectorLayer => CreateVectorLayerElement(vectorLayer),
            _ => null,
        };
    }

    /// <summary>
    /// Waits for snapshot-only external content (for example WebView2 captures) to finish loading
    /// before callers attempt bitmap export.
    /// </summary>
    public async Task WaitForExternalContentAsync()
    {
        var webLayers = EnumerateVisualDescendants(_root)
            .OfType<WebLayerView>()
            .ToArray();

        if (webLayers.Length == 0)
            return;

        await Task.WhenAll(webLayers.Select(static view => view.WaitUntilReadyAsync())).ConfigureAwait(true);
    }

    private FrameworkElement CreateTextLayerElement(TextLayer layer)
    {
        var style = layer.Style ?? PresentationModelUtilities.CreateDefaultTextStyle();
        var fillColor = layer.Fills?.FirstOrDefault(fill => fill.Enabled is not false)?.Color ?? style.Color;
        var foreground = new SolidColorBrush(ParseColor(fillColor));
        var textBlock = new TextBlock
        {
            Text = layer.Content,
            TextWrapping = TextWrapping.WrapWholeWords,
            TextAlignment = ParseTextAlignment(style.Alignment),
            Foreground = foreground,
            FontSize = style.Font.Size,
            FontStyle = style.Font.Italic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal,
            FontWeight = new Windows.UI.Text.FontWeight { Weight = (ushort)Math.Clamp(style.Font.Weight, 100, 900) },
            CharacterSpacing = (int)Math.Round(style.Font.LetterSpacing * 100),
            LineHeight = style.Font.LineHeight is > 0 ? style.Font.Size * style.Font.LineHeight.Value : double.NaN,
            HorizontalAlignment = ParseHorizontalAlignment(style.Alignment),
            VerticalAlignment = ParseVerticalAlignment(style.VerticalAlignment),
            FontFamily = _assetCache.ResolveFontFamily(Project, style.Font.Family),
        };

        var paddingValue = layer.Padding ?? 2;
        return new Grid
        {
            Padding = new Thickness(paddingValue / 100d * layer.Transform.Width),
            Clip = layer.Transform.ClipContent == true
                ? new RectangleGeometry { Rect = new Rect(0, 0, Math.Max(1, layer.Transform.Width), Math.Max(1, layer.Transform.Height)) }
                : null,
            Children = { textBlock },
        };
    }

    private FrameworkElement CreateShapeLayerElement(ShapeLayer layer)
    {
        var style = layer.Style ?? new ShapeStyleModel();
        var fill = layer.Fills?.FirstOrDefault(candidate => candidate.Enabled is not false);
        var stroke = layer.Strokes?.FirstOrDefault(candidate => candidate.Enabled is not false);
        var fillBrush = new SolidColorBrush(ParseColor(fill?.Color ?? style.Fill))
        {
            Opacity = fill?.Opacity ?? style.FillOpacity,
        };
        var strokeBrush = new SolidColorBrush(ParseColor(stroke?.Color ?? style.Stroke))
        {
            Opacity = stroke?.Opacity ?? style.StrokeOpacity,
        };
        var strokeThickness = stroke?.Width ?? style.StrokeWidth;

        return layer.ShapeType switch
        {
            "ellipse" => new Ellipse
            {
                Fill = fillBrush,
                Stroke = strokeBrush,
                StrokeThickness = strokeThickness,
            },
            "line" => new Line
            {
                X1 = 0,
                Y1 = layer.Transform.Height / 2d,
                X2 = layer.Transform.Width,
                Y2 = layer.Transform.Height / 2d,
                Stroke = strokeBrush,
                StrokeThickness = strokeThickness,
                Stretch = Stretch.Fill,
            },
            "triangle" => new Polygon
            {
                Fill = fillBrush,
                Stroke = strokeBrush,
                StrokeThickness = strokeThickness,
                Points = new PointCollection
                {
                    new Point(layer.Transform.Width / 2d, 0),
                    new Point(layer.Transform.Width, layer.Transform.Height),
                    new Point(0, layer.Transform.Height),
                },
            },
            _ => new Rectangle
            {
                Fill = fillBrush,
                Stroke = strokeBrush,
                StrokeThickness = strokeThickness,
                RadiusX = style.CornerRadius,
                RadiusY = style.CornerRadius,
            },
        };
    }

    /// <summary>
    /// Parses path mini-language into a <see cref="Geometry"/>. WinUI does not expose <c>Geometry.Parse</c> like WPF.
    /// </summary>
    private static Geometry ParsePathGeometry(string pathData)
    {
        var escaped = pathData
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
        var xaml =
            $"<PathGeometry xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Figures=\"{escaped}\" />";
        return (Geometry)XamlReader.Load(xaml);
    }

    private FrameworkElement CreateVectorLayerElement(VectorLayer layer)
    {
        try
        {
            var geometry = ParsePathGeometry(layer.Path);
            var fill = layer.Fills?.FirstOrDefault(candidate => candidate.Enabled is not false);
            var stroke = layer.Strokes?.FirstOrDefault(candidate => candidate.Enabled is not false);

            return new Microsoft.UI.Xaml.Shapes.Path
            {
                Data = geometry,
                Fill = new SolidColorBrush(ParseColor(fill?.Color ?? "#FFFFFF"))
                {
                    Opacity = fill?.Opacity ?? 1,
                },
                Stroke = stroke == null ? null : new SolidColorBrush(ParseColor(stroke.Color))
                {
                    Opacity = stroke.Opacity,
                },
                StrokeThickness = stroke?.Width ?? 0,
                Stretch = Stretch.Fill,
            };
        }
        catch
        {
            return CreatePlaceholderLayer("Vector", "#1F2937");
        }
    }

    private FrameworkElement CreateMediaLayerElement(MediaLayer layer)
    {
        var path = _assetCache.ResolveMediaPath(Project, layer.MediaId);
        if (string.IsNullOrWhiteSpace(path))
            return CreatePlaceholderLayer(layer.MediaType == "video" ? "Video" : "Image", "#111827");

        if (string.Equals(layer.MediaType, "video", StringComparison.OrdinalIgnoreCase))
        {
            if (RenderMode == SlideStageRenderMode.Thumbnail)
                return CreatePlaceholderLayer("Video", "#111827");

            return CreateVideoElement(path, layer.Fit, layer.Loop ?? true, layer.Muted ?? true, layer.Autoplay ?? true, 1, trackAsEngineMedia: false);
        }

        return CreateImageElement(path, layer.Fit, 1);
    }

    private FrameworkElement CreateWebLayerElement(WebLayer layer)
    {
        return new WebLayerView
        {
            Url = layer.Url,
            Zoom = layer.Zoom,
            Interactive = RenderMode == SlideStageRenderMode.Output && layer.Interactive,
            RefreshInterval = layer.RefreshInterval,
            UseLiveContent = RenderMode == SlideStageRenderMode.Output,
        };
    }

    private FrameworkElement CreateImageElement(string path, string? fit, double opacity)
    {
        try
        {
            var bitmap = new BitmapImage(new Uri(path));
            return new Image
            {
                Source = bitmap,
                Stretch = ParseStretch(fit),
                Opacity = opacity,
            };
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "BitmapImage failed for path {Path}.", path);
            return CreatePlaceholderLayer("Image", "#111827");
        }
    }

    /// <summary>
    /// Looping uses <see cref="MediaPlaybackList"/> with <see cref="MediaPlaybackList.AutoRepeatEnabled"/> so repeat
    /// boundaries use the list playback path (gapless-oriented) instead of only toggling <see cref="MediaPlayer.IsLoopingEnabled"/>.
    /// </summary>
    private static IMediaPlaybackSource CreatePlaybackSourceForUri(Uri uri, bool loop)
    {
        if (!loop)
            return MediaSource.CreateFromUri(uri);

        var coreSource = MediaSource.CreateFromUri(uri);
        var item = new MediaPlaybackItem(coreSource);
        // MaxPlayedItemsToKeepOpen = 1: keep the item decoded across the loop boundary so the next
        // repeat starts from the already-open pipeline instead of re-initialising from scratch.
        var list = new MediaPlaybackList { AutoRepeatEnabled = true, MaxPlayedItemsToKeepOpen = 1 };
        list.Items.Add(item);
        return list;
    }

    private FrameworkElement CreateVideoElement(
        string path,
        string? fit,
        bool loop,
        bool muted,
        bool autoplay,
        double opacity,
        bool trackAsEngineMedia)
    {
        try
        {
            var player = new MediaPlayer
            {
                IsMuted = muted,
                AutoPlay = autoplay,
                Source = CreatePlaybackSourceForUri(new Uri(path), loop),
            };
            _activePlayers.Add(player);
            if (_layeredOutputMediaActive)
            {
                if (trackAsEngineMedia)
                    _engineMediaPlayers.Add(player);
                else
                    _presentationMediaPlayers.Add(player);
            }

            var element = new MediaPlayerElement
            {
                AreTransportControlsEnabled = false,
                AutoPlay = autoplay,
                Stretch = ParseStretch(fit),
                Opacity = opacity,
            };
            element.SetMediaPlayer(player);
            return element;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "MediaPlayer failed for path {Path}.", path);
            return CreatePlaceholderLayer("Video", "#111827");
        }
    }

    /// <summary>
    /// Plays the dedicated audio output layer (no visual surface). Video continues to use fullscreen layers.
    /// </summary>
    private void AddAudioOnlyOutputLayer(OutputLayerMedia? media)
    {
        if (media == null || RenderMode == SlideStageRenderMode.Thumbnail)
            return;

        var path = ResolveOutputLayerMediaPath(media);
        if (string.IsNullOrWhiteSpace(path))
            return;

        var effective = MediaInference.ResolveEffectiveMediaType(media.MediaType, path);
        if (!string.Equals(effective, "audio", StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            var player = new MediaPlayer
            {
                IsMuted = media.Muted,
                AutoPlay = media.Autoplay,
                Source = CreatePlaybackSourceForUri(new Uri(path), media.Loop),
            };
            _activePlayers.Add(player);
            if (_layeredOutputMediaActive)
                _engineAudioPlayers.Add(player);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Audio-only MediaPlayer failed for path {Path}.", path);
        }
    }

    private FrameworkElement CreateVideoThumbnailElement(string path, string? fit, double opacity)
    {
        var host = new Grid
        {
            Background = new SolidColorBrush(ParseColor("#111827")),
            Opacity = opacity,
        };

        var image = new Image
        {
            Stretch = ParseStretch(fit),
        };

        host.Children.Add(image);
        host.Children.Add(new FontIcon
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Glyph = "\uE768",
            FontSize = 22,
            Foreground = new SolidColorBrush(Colors.White) { Opacity = 0.75 },
            IsHitTestVisible = false,
        });

        _ = LoadThumbnailIntoImageAsync(image, path, "video");
        return host;
    }

    private FrameworkElement CreatePlaceholderLayer(string text, string backgroundColor)
    {
        return new Border
        {
            Background = new SolidColorBrush(ParseColor(backgroundColor)),
            BorderBrush = new SolidColorBrush(Colors.White) { Opacity = 0.15 },
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = CreateCenteredMessage(text, 16, Colors.White, 0.75),
        };
    }

    private static FrameworkElement CreateCenteredMessage(string text, double fontSize = 18, Color? color = null, double opacity = 0.6)
    {
        return new Grid
        {
            Children =
            {
                new TextBlock
                {
                    Text = text,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(color ?? Colors.White) { Opacity = opacity },
                    FontSize = fontSize,
                    FontWeight = FontWeights.SemiBold,
                    TextAlignment = TextAlignment.Center,
                },
            },
        };
    }

    private static async Task LoadThumbnailIntoImageAsync(Image image, string path, string mediaType)
    {
        var source = await MediaThumbnailLoader.TryLoadAsync(path, mediaType, 320).ConfigureAwait(true);
        if (source != null)
            image.Source = source;
    }

    private static FrameworkElement CreateSafeAreaOverlay()
    {
        var outer = new Rectangle
        {
            Width = double.NaN,
            Height = double.NaN,
            Margin = new Thickness(96, 54, 96, 54),
            Stroke = new SolidColorBrush(Colors.White) { Opacity = 0.25 },
            StrokeDashArray = new DoubleCollection { 8, 8 },
            StrokeThickness = 2,
        };
        var inner = new Rectangle
        {
            Width = double.NaN,
            Height = double.NaN,
            Margin = new Thickness(192, 108, 192, 108),
            Stroke = new SolidColorBrush(Colors.White) { Opacity = 0.15 },
            StrokeDashArray = new DoubleCollection { 8, 8 },
            StrokeThickness = 2,
        };

        return new Grid
        {
            IsHitTestVisible = false,
            Children = { outer, inner },
        };
    }

    private static void ApplyTransforms(FrameworkElement element, LayerTransformModel transform)
    {
        element.RenderTransform = new CompositeTransform
        {
            Rotation = transform.Rotation,
            ScaleX = transform.FlipX == true ? -1 : 1,
            ScaleY = transform.FlipY == true ? -1 : 1,
            CenterX = transform.Width / 2d,
            CenterY = transform.Height / 2d,
        };
    }

    private Brush CreateStageSurfaceBackground()
    {
        var shouldBeTransparent =
            RenderMode is SlideStageRenderMode.Output or SlideStageRenderMode.Preview
            && (SuppressPresentation || Slide?.Background is TransparentSlideBackground)
            && !IsBlackout
            && !IsClear;

        return new SolidColorBrush(shouldBeTransparent ? Colors.Transparent : Colors.Black);
    }

    private Brush CreateBackgroundBrush(SlideBackground background)
    {
        switch (background)
        {
            case SolidSlideBackground solid:
                {
                    var c = ParseColor(solid.Color);
                    return RenderMode == SlideStageRenderMode.Thumbnail
                        ? InAppAcrylicBrushFactory.CreateSolidSlideThumbnailTint(c)
                        : new SolidColorBrush(c);
                }

            case GradientSlideBackground gradient:
                var brush = new LinearGradientBrush();
                var angleRadians = gradient.Angle * Math.PI / 180d;
                var x = Math.Cos(angleRadians) * 0.5 + 0.5;
                var y = Math.Sin(angleRadians) * 0.5 + 0.5;
                brush.StartPoint = new Point(1 - x, 1 - y);
                brush.EndPoint = new Point(x, y);
                foreach (var stop in gradient.Stops.OrderBy(stop => stop.Position))
                {
                    brush.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop
                    {
                        Color = ParseColor(stop.Color),
                        Offset = stop.Position / 100d,
                    });
                }
                return brush;

            case TransparentSlideBackground:
                return new SolidColorBrush(Colors.Transparent);

            default:
                return new SolidColorBrush(Colors.Black);
        }
    }

    private static Stretch ParseStretch(string? fit)
    {
        return fit?.ToLowerInvariant() switch
        {
            "contain" => Stretch.Uniform,
            "fill" => Stretch.Fill,
            "none" => Stretch.None,
            _ => Stretch.UniformToFill,
        };
    }

    private static HorizontalAlignment ParseHorizontalAlignment(string? alignment)
    {
        return alignment?.ToLowerInvariant() switch
        {
            "left" => HorizontalAlignment.Left,
            "right" => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Center,
        };
    }

    private static VerticalAlignment ParseVerticalAlignment(string? alignment)
    {
        return alignment?.ToLowerInvariant() switch
        {
            "top" => VerticalAlignment.Top,
            "bottom" => VerticalAlignment.Bottom,
            _ => VerticalAlignment.Center,
        };
    }

    private static TextAlignment ParseTextAlignment(string? alignment)
    {
        return alignment?.ToLowerInvariant() switch
        {
            "left" => TextAlignment.Left,
            "right" => TextAlignment.Right,
            _ => TextAlignment.Center,
        };
    }

    private static Color ParseColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Colors.Transparent;

        var trimmed = value.Trim();
        if (trimmed.StartsWith('#'))
        {
            var hex = trimmed[1..];
            if (hex.Length == 3)
                hex = string.Concat(hex.Select(character => $"{character}{character}"));
            if (hex.Length == 6)
                hex = $"FF{hex}";

            if (hex.Length == 8 &&
                byte.TryParse(hex[..2], System.Globalization.NumberStyles.HexNumber, null, out var a) &&
                byte.TryParse(hex[2..4], System.Globalization.NumberStyles.HexNumber, null, out var r) &&
                byte.TryParse(hex[4..6], System.Globalization.NumberStyles.HexNumber, null, out var g) &&
                byte.TryParse(hex[6..8], System.Globalization.NumberStyles.HexNumber, null, out var b))
            {
                return Color.FromArgb(a, r, g, b);
            }
        }

        if (trimmed.StartsWith("rgba", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            var values = trimmed[(trimmed.IndexOf('(') + 1)..trimmed.LastIndexOf(')')]
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (values.Length >= 3 &&
                byte.TryParse(values[0], out var red) &&
                byte.TryParse(values[1], out var green) &&
                byte.TryParse(values[2], out var blue))
            {
                var alpha = (byte)255;
                if (values.Length == 4 &&
                    double.TryParse(values[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var opacity))
                {
                    alpha = (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255d);
                }

                return Color.FromArgb(alpha, red, green, blue);
            }
        }

        return trimmed.ToLowerInvariant() switch
        {
            "white" => Colors.White,
            "black" => Colors.Black,
            "transparent" => Colors.Transparent,
            _ => Colors.White,
        };
    }

    private static IEnumerable<DependencyObject> EnumerateVisualDescendants(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;

            foreach (var descendant in EnumerateVisualDescendants(child))
                yield return descendant;
        }
    }

    private void DisposePlayers()
    {
        // Unregister from the output transport coordinator before disposing players so timer/event handlers
        // never touch a disposed MediaPlayer (clear → play again could otherwise crash).
        if (RenderMode == SlideStageRenderMode.Output && TrackPlaybackCoordinator)
        {
            _playbackCoordinator?.RegisterActivePlayers(Array.Empty<MediaPlayer>(), null, MediaPlaybackRegistrationMode.Authority, MediaPlaybackTarget.MediaFiles);
            _playbackCoordinator?.RegisterActivePlayers(Array.Empty<MediaPlayer>(), null, MediaPlaybackRegistrationMode.Authority, MediaPlaybackTarget.AudioFiles);
        }

        // MediaPlayerElement must release the player before MediaPlayer.Dispose(); otherwise WinUI can AV
        // when the tree is cleared or when a new player is bound (clear layer → play again).
        DetachMediaPlayerElementsFromRoot();

        foreach (var player in _activePlayers)
        {
            try
            {
                player.Pause();
                player.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "MediaPlayer.Dispose failed during stage teardown.");
            }
        }

        _activePlayers.Clear();
        _engineMediaPlayers.Clear();
        _engineAudioPlayers.Clear();
        _presentationMediaPlayers.Clear();
    }

    private void DetachMediaPlayerElementsUnder(DependencyObject? subtreeRoot)
    {
        if (subtreeRoot == null)
            return;

        try
        {
            DetachMediaPlayerElementsRecursive(subtreeRoot);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed while detaching MediaPlayerElement instances under a subtree.");
        }
    }

    private void DisposeEnginePlayersOnly()
    {
        DetachMediaPlayerElementsUnder(_outputUnderlayHost);
        DetachMediaPlayerElementsUnder(_outputOverlayHost);
        foreach (var player in _engineMediaPlayers)
        {
            try
            {
                player.Pause();
                player.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "MediaPlayer.Dispose failed during engine media teardown.");
            }

            _activePlayers.Remove(player);
        }

        _engineMediaPlayers.Clear();
        foreach (var player in _engineAudioPlayers)
        {
            try
            {
                player.Pause();
                player.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "MediaPlayer.Dispose failed during engine audio teardown.");
            }

            _activePlayers.Remove(player);
        }

        _engineAudioPlayers.Clear();
    }

    private void DisposePresentationPlayersOnly()
    {
        DetachMediaPlayerElementsUnder(_outputBackgroundHost);
        DetachMediaPlayerElementsUnder(_outputSlideChromeHost);
        foreach (var player in _presentationMediaPlayers)
        {
            try
            {
                player.Pause();
                player.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "MediaPlayer.Dispose failed during presentation media teardown.");
            }

            _activePlayers.Remove(player);
        }

        _presentationMediaPlayers.Clear();
    }

    /// <summary>
    /// Walks the live visual tree and clears <see cref="MediaPlayerElement.MediaPlayer"/> so native teardown
    /// happens before managed <see cref="MediaPlayer.Dispose"/>.
    /// </summary>
    private void DetachMediaPlayerElementsFromRoot()
    {
        try
        {
            DetachMediaPlayerElementsRecursive(_root);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed while detaching MediaPlayerElement instances from the stage tree.");
        }
    }

    private void DetachMediaPlayerElementsRecursive(DependencyObject node)
    {
        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            DetachMediaPlayerElementsRecursive(child);
        }

        if (node is MediaPlayerElement mpe)
        {
            try
            {
                mpe.SetMediaPlayer(null);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "SetMediaPlayer(null) failed during stage teardown.");
            }
        }
    }

    private void NotifyCoordinatorOfNewPlayers(string? cueName = null)
    {
        if (RenderMode != SlideStageRenderMode.Output || !TrackPlaybackCoordinator)
            return;
        _playbackCoordinator?.RegisterActivePlayers(_activePlayers, cueName, MediaPlaybackRegistrationMode.Authority);
    }

    private string ResolveActiveMediaName()
    {
        if (MediaLayers?.MediaUnderlay != null)
            return MediaCueDisplayNameResolver.Resolve(MediaLayers.MediaUnderlay, Project);

        if (MediaLayers?.MediaOverlay != null)
            return MediaCueDisplayNameResolver.Resolve(MediaLayers.MediaOverlay, Project);

        return MediaCueDisplayNameResolver.Resolve(Slide?.MediaCues?.FirstOrDefault(), Project);
    }

    private string? ResolveActiveAudioName()
    {
        if (MediaLayers?.Audio != null)
            return MediaCueDisplayNameResolver.Resolve(MediaLayers.Audio, Project);

        return null;
    }
}

/// <summary>
/// Rendering modes used to vary expensive visuals across thumbnail, preview, output, and editor surfaces.
/// </summary>
public enum SlideStageRenderMode
{
    Thumbnail,
    Preview,
    Output,
    Editor,
}