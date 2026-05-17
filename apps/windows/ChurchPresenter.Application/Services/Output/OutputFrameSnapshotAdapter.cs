using ChurchPresenter.Backend.Output;
using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Services.Runtime;

using BackendOutputLayerKind = ChurchPresenter.Backend.Rendering.OutputLayerKind;

namespace ChurchPresenter.Services.Output;

/// <summary>
/// Adapts a synchronized backend snapshot into the current WinUI output-surface contract.
/// </summary>
public static class OutputFrameSnapshotAdapter
{
    /// <summary>Builds one UI snapshot for the requested output surface.</summary>
    public static OutputFrameSnapshot Adapt(LiveProductionSnapshot liveProduction, string? screenId)
    {
        ArgumentNullException.ThrowIfNull(liveProduction);

        AudienceRenderFrame? audienceFrame = ResolveAudienceFrame(liveProduction.Frames, screenId);
        OutputScreenDiagnostics? screenDiagnostics = null;
        IReadOnlyList<OutputLayerRouteState> layerRoutes = OutputRoutingDefaults.CreateRouteStates(new OutputLookFeedRouting());
        bool routesPresentation = true;
        bool routesMedia = true;

        if (!string.IsNullOrWhiteSpace(screenId))
        {
            ScreenLayerRouting route = liveProduction.SessionState.ActiveLook.ResolveRoute(screenId);
            routesPresentation = route.Routes(BackendOutputLayerKind.Slide);
            routesMedia = route.Routes(BackendOutputLayerKind.Media);
            layerRoutes = BuildLayerRouteStates(route);
            screenDiagnostics = liveProduction.Topology.ResolveDiagnostics(screenId);
        }

        RenderFrame frame = BuildRenderFrame(
            liveProduction,
            audienceFrame,
            routesPresentation,
            routesMedia);

        return new OutputFrameSnapshot
        {
            ScreenId = screenId,
            Frame = frame,
            Scene = OutputSceneResolver.ResolveFromRenderFrame(frame),
            ProgramTitle = liveProduction.PlaybackState.Presentation?.Manifest.Title ?? string.Empty,
            AudienceFrame = audienceFrame,
            ScreenDiagnostics = screenDiagnostics,
            LayerRoutes = layerRoutes,
            RoutesPresentation = routesPresentation,
            RoutesMedia = routesMedia,
        };
    }

    private static IReadOnlyList<OutputLayerRouteState> BuildLayerRouteStates(ScreenLayerRouting route)
    {
        return OutputRoutingDefaults.Layers
            .Where(static layer => layer.IsRoutable)
            .Select(layer => new OutputLayerRouteState
            {
                Kind = layer.Kind,
                Id = layer.Id,
                DisplayName = layer.DisplayName,
                Category = layer.Category,
                IsEnabled = route.Routes(layer.Kind),
                ThemeVariantId = route.ResolveThemeVariant(layer.Kind),
                MaskId = layer.Kind == BackendOutputLayerKind.Mask ? route.ResolveMaskId() : null,
            })
            .ToArray();
    }

    private static AudienceRenderFrame? ResolveAudienceFrame(RenderFrameSet frames, string? screenId)
    {
        if (string.IsNullOrWhiteSpace(screenId))
            return null;

        return frames.AudienceFrames.TryGetValue(screenId, out AudienceRenderFrame? frame)
            ? frame
            : null;
    }

    private static RenderFrame BuildRenderFrame(
        LiveProductionSnapshot liveProduction,
        AudienceRenderFrame? audienceFrame,
        bool routesPresentation,
        bool routesMedia)
    {
        PlaybackState playbackState = liveProduction.PlaybackState;
        PresentationProject? project = playbackState.Presentation?.Project;
        PresentationSlide? slide = ResolveCurrentSlide(project, playbackState.CurrentSlideId);
        SlideTransition? transition = TransitionResolver.Resolve(slide, project?.Arrangement, playbackState.GlobalSlideFallback);
        SlideTransition? mediaTransition = playbackState.GlobalMediaFallback == null
            ? null
            : TransitionResolver.Normalize(playbackState.GlobalMediaFallback);
        string? aspectRatio = project?.Manifest.AspectRatio?.Trim();

        MediaLayersState mediaLayers = IsBackendLayerSuppressed(
                liveProduction.SessionState,
                audienceFrame: null,
                BackendOutputLayerKind.Media,
                isRouted: true)
            ? SlideMediaLayerBuilder.Clone(playbackState.MediaLayers)
            : SlideMediaLayerBuilder.Overlay(SlideMediaLayerBuilder.Build(slide), playbackState.MediaLayers);

        return new RenderFrame
        {
            Project = project,
            Slide = slide,
            ProgramSlideId = playbackState.CurrentSlideId,
            BuildIndex = playbackState.BuildIndex,
            VisibleLayerIds = playbackState.VisibleLayerIds,
            MediaLayers = mediaLayers,
            Transition = transition,
            MediaTransition = mediaTransition,
            SuppressPresentation = IsBackendLayerSuppressed(
                liveProduction.SessionState,
                audienceFrame,
                BackendOutputLayerKind.Slide,
                routesPresentation),
            SuppressMedia = IsBackendLayerSuppressed(
                liveProduction.SessionState,
                audienceFrame,
                BackendOutputLayerKind.Media,
                routesMedia),
            IsBlackout = playbackState.IsBlackout,
            IsClear = playbackState.IsClear,
            OutputAspectRatioOverride = string.IsNullOrEmpty(aspectRatio) ? null : aspectRatio,
            OutputScaleMode = PresentationModelUtilities.NormalizeOutputScaleMode(project?.Manifest.OutputScaleMode),
        };
    }

    private static bool IsBackendLayerSuppressed(
        LiveRenderSessionState sessionState,
        AudienceRenderFrame? audienceFrame,
        BackendOutputLayerKind layerKind,
        bool isRouted)
    {
        if (!isRouted)
            return true;

        if (sessionState.Layers.TryGetValue(layerKind, out LayerState? layerState)
            && (layerState.IsSuppressed || layerState.IsCleared))
            return true;

        RenderLayerDescriptor? layer = audienceFrame?.Layers.FirstOrDefault(candidate => candidate.Kind == layerKind);
        return layer != null && (!layer.IsVisible || layer.IsSuppressed);
    }

    private static PresentationSlide? ResolveCurrentSlide(PresentationProject? project, string? slideId)
    {
        if (project == null || string.IsNullOrWhiteSpace(slideId))
            return null;

        return project.Slides.FirstOrDefault(slide =>
            string.Equals(slide.Id, slideId, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Shared base class for feed-specific output frame facades.</summary>
public abstract class OutputFrameFacadeBase : IOutputFrameFacade
{
    private readonly ILiveProductionFacade _liveProduction;
    private readonly string? _screenId;

    protected OutputFrameFacadeBase(ILiveProductionFacade liveProduction, string? screenId)
    {
        _liveProduction = liveProduction ?? throw new ArgumentNullException(nameof(liveProduction));
        _screenId = screenId;
        _liveProduction.Changed += HandleChanged;
        Current = OutputFrameSnapshotAdapter.Adapt(_liveProduction.Current, _screenId);
    }

    /// <inheritdoc />
    public event EventHandler<OutputFrameChangedEventArgs>? Changed;

    /// <inheritdoc />
    public OutputFrameSnapshot Current { get; private set; }

    private void HandleChanged(object? sender, LiveProductionChangedEventArgs args)
    {
        Current = OutputFrameSnapshotAdapter.Adapt(args.Snapshot, _screenId);
        Changed?.Invoke(this, new OutputFrameChangedEventArgs { Snapshot = Current });
    }
}

/// <summary>Shared program-output snapshot used inside the main Show shell.</summary>
public sealed class ProgramOutputFrameFacade(ILiveProductionFacade liveProduction)
    : OutputFrameFacadeBase(liveProduction, screenId: null);

/// <summary>Audience-output snapshot filtered through the backend Look routing.</summary>
public sealed class AudienceOutputFrameFacade(ILiveProductionFacade liveProduction)
    : OutputFrameFacadeBase(liveProduction, OutputFeedIds.Audience);

/// <summary>Stage-output snapshot filtered through the backend Look routing.</summary>
public sealed class StageOutputFrameFacade(ILiveProductionFacade liveProduction)
    : OutputFrameFacadeBase(liveProduction, OutputFeedIds.Stage);