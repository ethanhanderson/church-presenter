
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using Windows.Media.Playback;

namespace ChurchPresenter.Controls;

/// <summary>
/// Native live output compositor for audience, stage, and Show-page program preview.
/// Engine media slots remain persistent outside presentation transitions so slide changes do not interrupt them.
/// </summary>
public sealed class LiveOutputSurface : UserControl
{
    public static readonly DependencyProperty SceneProperty =
        DependencyProperty.Register(nameof(Scene), typeof(OutputScene), typeof(LiveOutputSurface),
            new PropertyMetadata(OutputScene.Empty, OnSceneChanged));

    public static readonly DependencyProperty PlaybackRegistrationModeProperty =
        DependencyProperty.Register(nameof(PlaybackRegistrationMode), typeof(MediaPlaybackRegistrationMode), typeof(LiveOutputSurface),
            new PropertyMetadata(MediaPlaybackRegistrationMode.Authority));

    private readonly Grid _root;
    private readonly MediaTransitionHost _underlaySlot;
    private readonly PresentationTransitionHost _presentationHost;
    private readonly MediaTransitionHost _overlaySlot;
    private readonly MediaTransitionHost _audioSlot;
    private readonly Border _clearOverlay;
    private readonly Border _blackoutOverlay;
    private readonly IMediaPlayerRegistration? _playbackRegistration;
    private readonly IMediaPrewarmService? _preWarmService;

    /// <summary>Initializes a new instance of the <see cref="LiveOutputSurface"/> control.</summary>
    public LiveOutputSurface()
        : this(App.Services)
    {
    }

    private LiveOutputSurface(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        _playbackRegistration = services.GetService<IMediaPlayerRegistration>();
        _preWarmService = services.GetService<IMediaPrewarmService>();
        _underlaySlot = new MediaTransitionHost
        {
            PlaceholderBrush = new SolidColorBrush(Microsoft.UI.Colors.Black),
        };
        _presentationHost = new PresentationTransitionHost();
        _overlaySlot = new MediaTransitionHost
        {
            PlaceholderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        _audioSlot = new MediaTransitionHost
        {
            PlaceholderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Opacity = 0,
            IsHitTestVisible = false,
        };
        _clearOverlay = CreateClearOverlay();
        _blackoutOverlay = new Border
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Black),
            Visibility = Visibility.Collapsed,
        };

        _root = new Grid
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Black),
        };
        _root.Children.Add(_underlaySlot);
        _root.Children.Add(_presentationHost);
        _root.Children.Add(_overlaySlot);
        _root.Children.Add(_audioSlot);
        _root.Children.Add(_clearOverlay);
        _root.Children.Add(_blackoutOverlay);
        Content = _root;

        _underlaySlot.ActivePlayersChanged += OnMediaSlotActivePlayersChanged;
        _overlaySlot.ActivePlayersChanged += OnMediaSlotActivePlayersChanged;
        _audioSlot.ActivePlayersChanged += OnMediaSlotActivePlayersChanged;

        if (_preWarmService != null)
            _preWarmService.PreWarmRequested += OnPreWarmRequested;

        Unloaded += (_, _) =>
        {
            _playbackRegistration?.RegisterActivePlayers(Array.Empty<MediaPlayer>(), null, PlaybackRegistrationMode, MediaPlaybackTarget.MediaFiles);
            _playbackRegistration?.RegisterActivePlayers(Array.Empty<MediaPlayer>(), null, PlaybackRegistrationMode, MediaPlaybackTarget.AudioFiles);
            if (_preWarmService != null)
                _preWarmService.PreWarmRequested -= OnPreWarmRequested;
        };
    }

    /// <summary>The currently resolved live output scene.</summary>
    public OutputScene Scene
    {
        get => (OutputScene)GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    public MediaPlaybackRegistrationMode PlaybackRegistrationMode
    {
        get => (MediaPlaybackRegistrationMode)GetValue(PlaybackRegistrationModeProperty);
        set => SetValue(PlaybackRegistrationModeProperty, value);
    }

    private static void OnSceneChanged(DependencyObject d, DependencyPropertyChangedEventArgs _)
    {
        if (d is LiveOutputSurface surface)
            surface.ApplyScene();
    }

    private void ApplyScene()
    {
        var scene = Scene ?? OutputScene.Empty;
        var hasPresentationContent = scene.Presentation.Slide != null;

        _underlaySlot.Media = scene.Media.Suppressed ? null : scene.Media.Underlay.Media;
        _overlaySlot.Media = scene.Media.Suppressed ? null : scene.Media.Overlay.Media;
        _audioSlot.Media = scene.Media.Suppressed ? null : scene.Media.Audio.Media;
        _underlaySlot.Transition = scene.MediaTransition;
        _overlaySlot.Transition = scene.MediaTransition;
        _audioSlot.Transition = scene.MediaTransition;

        _presentationHost.Project = scene.Project;
        _presentationHost.Slide = scene.Presentation.Slide;
        _presentationHost.VisibleLayerIds = scene.Presentation.VisibleLayerIds;
        _presentationHost.SuppressPresentation = scene.Presentation.Suppressed;
        _presentationHost.DefaultTransition = scene.Transition;
        _presentationHost.OutputAspectRatioOverride = scene.OutputAspectRatioOverride;
        _presentationHost.OutputStageStretch = string.Equals(scene.OutputScaleMode, "fill", StringComparison.OrdinalIgnoreCase)
            ? Stretch.UniformToFill
            : Stretch.Uniform;
        _presentationHost.Visibility = hasPresentationContent ? Visibility.Visible : Visibility.Collapsed;

        _clearOverlay.Visibility = scene.IsClear ? Visibility.Visible : Visibility.Collapsed;
        _blackoutOverlay.Visibility = scene.IsBlackout ? Visibility.Visible : Visibility.Collapsed;

        DispatcherQueue.TryEnqueue(() => RegisterActivePlayers(Scene ?? OutputScene.Empty));
    }

    private void OnMediaSlotActivePlayersChanged(object? sender, EventArgs e) =>
        RegisterActivePlayers(Scene ?? OutputScene.Empty);

    private void OnPreWarmRequested(object? sender, MediaPrewarmRequestedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() => RoutePreWarm(e.Media, e.LayerTarget));
    }

    private void RoutePreWarm(OutputLayerMedia media, string layerTarget)
    {
        var slot = layerTarget switch
        {
            "mediaOverlay" => _overlaySlot,
            "audio" => _audioSlot,
            _ => _underlaySlot,
        };
        slot.PreWarmMedia(media);
    }

    private void RegisterActivePlayers(OutputScene scene)
    {
        if (_playbackRegistration == null)
            return;

        var mediaPlayers = new List<MediaPlayer>();
        mediaPlayers.AddRange(_underlaySlot.ActivePlayers);
        mediaPlayers.AddRange(_overlaySlot.ActivePlayers);

        var mediaCueName = scene.Media.Underlay.Media != null
            ? MediaCueDisplayNameResolver.Resolve(scene.Media.Underlay.Media, scene.Project)
            : scene.Media.Overlay.Media != null
                ? MediaCueDisplayNameResolver.Resolve(scene.Media.Overlay.Media, scene.Project)
                : null;
        var audioCueName = scene.Media.Audio.Media != null
            ? MediaCueDisplayNameResolver.Resolve(scene.Media.Audio.Media, scene.Project)
            : null;

        _playbackRegistration.RegisterActivePlayers(mediaPlayers, mediaCueName, PlaybackRegistrationMode, MediaPlaybackTarget.MediaFiles);
        _playbackRegistration.RegisterActivePlayers(_audioSlot.ActivePlayers, audioCueName, PlaybackRegistrationMode, MediaPlaybackTarget.AudioFiles);
    }

    private static Border CreateClearOverlay()
    {
        return new Border
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Black),
            Visibility = Visibility.Collapsed,
            Child = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) { Opacity = 0.2 },
                FontSize = 48,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiLight,
                Text = "Church Presenter",
            },
        };
    }
}
