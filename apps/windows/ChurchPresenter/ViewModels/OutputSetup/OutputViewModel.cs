
using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.UI.Xaml.Media;

namespace ChurchPresenter.ViewModels;

/// <summary>
/// Audience / fullscreen output surface — mirrors the adapted backend output snapshot exposed by
/// <see cref="IOutputFrameFacade"/> while preserving the existing WinUI surface contract.
/// </summary>
public partial class OutputViewModel : ObservableObject
{
    private readonly IOutputFrameFacade _frames;
    private OutputScene _scene = OutputScene.Empty;
    private PresentationProject? _project;
    private PresentationSlide? _currentSlide;

    public OutputViewModel(IOutputFrameFacade frames)
    {
        _frames = frames ?? throw new ArgumentNullException(nameof(frames));
        _frames.Changed += (_, args) => RefreshFromSnapshot(args.Snapshot);
        RefreshFromSnapshot(_frames.Current);
    }

    // Stores the last fully resolved frame so all output properties come from one
    // consistent snapshot instead of mixing frame-resolved and raw engine-state data.
    private RenderFrame _currentFrame = RenderFrame.Empty;

    private string _programTitle = "";
    private bool _isBlackout;
    private bool _isClear;
    private bool _suppressPresentation;
    private bool _suppressMedia;
    private string? _screenId;
    private bool _routesPresentation = true;
    private bool _routesMedia = true;
    private IReadOnlyList<OutputLayerRouteState> _layerRoutes = Array.Empty<OutputLayerRouteState>();
    private OutputScreenDiagnostics? _screenDiagnostics;

    [ObservableProperty]
    private Stretch _outputStageStretch = Stretch.Uniform;

    [ObservableProperty]
    private string? _outputAspectRatioOverride;

    public string ProgramTitle
    {
        get => _programTitle;
        set => SetProperty(ref _programTitle, value);
    }

    /// <summary>The logical output screen id, or <c>null</c> for the shared program preview.</summary>
    public string? ScreenId
    {
        get => _screenId;
        private set => SetProperty(ref _screenId, value);
    }

    public PresentationProject? Project
    {
        get => _project;
        set => SetProperty(ref _project, value);
    }

    public PresentationSlide? CurrentSlide
    {
        get => _currentSlide;
        set => SetProperty(ref _currentSlide, value);
    }

    public bool IsBlackout
    {
        get => _isBlackout;
        set => SetProperty(ref _isBlackout, value);
    }

    public bool IsClear
    {
        get => _isClear;
        set => SetProperty(ref _isClear, value);
    }

    public bool SuppressPresentation
    {
        get => _suppressPresentation;
        set => SetProperty(ref _suppressPresentation, value);
    }

    public bool SuppressMedia
    {
        get => _suppressMedia;
        set => SetProperty(ref _suppressMedia, value);
    }

    /// <summary>Whether presentation content is routed to this output surface.</summary>
    public bool RoutesPresentation
    {
        get => _routesPresentation;
        private set => SetProperty(ref _routesPresentation, value);
    }

    /// <summary>Whether media content is routed to this output surface.</summary>
    public bool RoutesMedia
    {
        get => _routesMedia;
        private set => SetProperty(ref _routesMedia, value);
    }

    /// <summary>Backend layer routing states for this output surface.</summary>
    public IReadOnlyList<OutputLayerRouteState> LayerRoutes
    {
        get => _layerRoutes;
        private set => SetProperty(ref _layerRoutes, value);
    }

    /// <summary>Current diagnostics snapshot for this output surface, when available.</summary>
    public OutputScreenDiagnostics? ScreenDiagnostics
    {
        get => _screenDiagnostics;
        private set => SetProperty(ref _screenDiagnostics, value);
    }

    /// <summary>Whether diagnostics are available for this output surface.</summary>
    public bool HasScreenDiagnostics => ScreenDiagnostics != null;

    /// <summary>Current output health state for the screen.</summary>
    public ChurchPresenter.Backend.Output.EndpointHealth ScreenHealth =>
        ScreenDiagnostics?.Health ?? ChurchPresenter.Backend.Output.EndpointHealth.Unknown;

    /// <summary>Operator-facing output diagnostics text.</summary>
    public string ScreenDiagnosticsMessage => ScreenDiagnostics?.Message ?? string.Empty;

    /// <summary>Endpoint ids currently mapped to this screen.</summary>
    public IReadOnlyList<string> EndpointIds => ScreenDiagnostics?.EndpointIds ?? Array.Empty<string>();

    /// <summary>The fully resolved live output scene consumed by native WinUI output hosts.</summary>
    public OutputScene Scene
    {
        get => _scene;
        private set => SetProperty(ref _scene, value);
    }

    /// <summary>
    /// Layer IDs resolved from the last program frame; null/empty means render all layers.
    /// Always sourced from the resolved frame so it stays in sync with <see cref="CurrentSlide"/>.
    /// </summary>
    public IReadOnlyList<string>? VisibleLayerIds => _currentFrame.VisibleLayerIds;

    /// <summary>
    /// Media layers resolved from the last program frame.
    /// Always sourced from the resolved frame so underlay/overlay/audio stay consistent.
    /// </summary>
    public MediaLayersState MediaLayers => _currentFrame.MediaLayers;

    /// <summary>Presentation-wide default transition from the active bundle's arrangement settings.</summary>
    public SlideTransition? DefaultTransition => Project?.Arrangement?.DefaultTransition;

    /// <summary>
    /// Engine-ready transition for the current program slide (includes the global Show fallback).
    /// </summary>
    public SlideTransition? EffectiveTransition => _currentFrame.Transition;

    /// <summary>Resolved global media transition for program output (media-layer-only crossfades).</summary>
    public SlideTransition? EffectiveMediaTransition => _currentFrame.MediaTransition;

    /// <summary>Logical output width derived from the current aspect-ratio override.</summary>
    public double FrameWidth =>
        PresentationModelUtilities.GetBaseSlideSize(OutputAspectRatioOverride, null).Width;

    /// <summary>Logical output height derived from the current aspect-ratio override.</summary>
    public double FrameHeight =>
        PresentationModelUtilities.GetBaseSlideSize(OutputAspectRatioOverride, null).Height;

    /// <summary>True when program presentation content is currently visible on this output surface.</summary>
    public bool HasPresentationContent =>
        CurrentSlide != null
        && !SuppressPresentation
        && !IsBlackout
        && !IsClear;

    /// <summary>True when program media content is currently visible on this output surface.</summary>
    public bool HasMediaContent =>
        !SuppressMedia
        && !IsBlackout
        && !IsClear
        && HasMediaLayers(MediaLayers);

    /// <summary>Re-reads output scaling / aspect ratio from the active presentation and notifies bound output surfaces.</summary>
    public void RefreshOutputLayoutFromPresentation()
    {
        ApplyFrameOutputLayout(_frames.Current.Frame);
    }

    private void ApplyFrameOutputLayout(RenderFrame frame)
    {
        OutputStageStretch = string.Equals(frame.OutputScaleMode, "fill", StringComparison.OrdinalIgnoreCase)
            ? Stretch.UniformToFill
            : Stretch.Uniform;
        OutputAspectRatioOverride = frame.OutputAspectRatioOverride;
        OnPropertyChanged(nameof(FrameWidth));
        OnPropertyChanged(nameof(FrameHeight));
    }

    private void RefreshFromSnapshot(OutputFrameSnapshot snapshot)
    {
        var frame = snapshot.Frame;
        ApplySnapshotMetadata(snapshot);
        if (RenderFrameContentComparer.AreEquivalent(_currentFrame, frame))
            return;

        // Store the resolved frame first so all property getters read from the same snapshot.
        _currentFrame = frame;
        Scene = snapshot.Scene;

        ApplyFrameOutputLayout(frame);

        // Resolve the incoming slide into the backing field WITHOUT firing PropertyChanged yet.
        // This ensures the transition property is ready before output hosts react to the slide change.
        _currentSlide = frame.Slide;
        _project = frame.Project;

        IsBlackout = frame.IsBlackout;
        IsClear = frame.IsClear;
        SuppressPresentation = frame.SuppressPresentation;
        SuppressMedia = frame.SuppressMedia;

        OnPropertyChanged(nameof(Project));
        OnPropertyChanged(nameof(VisibleLayerIds));
        OnPropertyChanged(nameof(MediaLayers));
        OnPropertyChanged(nameof(DefaultTransition));
        OnPropertyChanged(nameof(HasScreenDiagnostics));
        OnPropertyChanged(nameof(ScreenHealth));
        OnPropertyChanged(nameof(ScreenDiagnosticsMessage));
        OnPropertyChanged(nameof(EndpointIds));

        // Notify transitions before the current slide so bound hosts see a fully prepared scene.
        OnPropertyChanged(nameof(EffectiveTransition));
        OnPropertyChanged(nameof(EffectiveMediaTransition));
        OnPropertyChanged(nameof(CurrentSlide));
        OnPropertyChanged(nameof(HasPresentationContent));
        OnPropertyChanged(nameof(HasMediaContent));
    }

    private void ApplySnapshotMetadata(OutputFrameSnapshot snapshot)
    {
        ScreenId = snapshot.ScreenId;
        ProgramTitle = snapshot.ProgramTitle;
        RoutesPresentation = snapshot.RoutesPresentation;
        RoutesMedia = snapshot.RoutesMedia;
        LayerRoutes = snapshot.LayerRoutes;
        ScreenDiagnostics = snapshot.ScreenDiagnostics;

        OnPropertyChanged(nameof(HasScreenDiagnostics));
        OnPropertyChanged(nameof(ScreenHealth));
        OnPropertyChanged(nameof(ScreenDiagnosticsMessage));
        OnPropertyChanged(nameof(EndpointIds));
    }

    private static bool HasMediaLayers(MediaLayersState mediaLayers) =>
        mediaLayers.MediaUnderlay != null || mediaLayers.MediaOverlay != null || mediaLayers.Audio != null;
}