using ChurchPresenter.Backend.Output;
using ChurchPresenter.Backend.Overlays;
using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Backend.Stage;

using BackendOutputLayerKind = ChurchPresenter.Backend.Rendering.OutputLayerKind;

namespace ChurchPresenter.Services.Runtime;

/// <summary>
/// Read-model query surface for modular Show/output consumers that should not compose raw backend snapshots themselves.
/// </summary>
public interface ILiveProductionQueryService
{
    /// <summary>Raised whenever the projected query snapshot changes.</summary>
    event EventHandler<LiveProductionQueryChangedEventArgs>? Changed;

    /// <summary>Current projected query snapshot.</summary>
    LiveProductionQuerySnapshot Current { get; }
}

/// <summary>
/// Event data for <see cref="ILiveProductionQueryService.Changed"/>.
/// </summary>
public sealed class LiveProductionQueryChangedEventArgs : EventArgs
{
    /// <summary>Updated query snapshot.</summary>
    public LiveProductionQuerySnapshot Snapshot { get; init; } = LiveProductionQuerySnapshot.Empty;
}

/// <summary>
/// Projects the live-production backend state into smaller output and generated-system summaries for UI/query consumers.
/// </summary>
public sealed class LiveProductionQueryService : ILiveProductionQueryService
{
    private readonly ILiveProductionFacade _liveProduction;
    private readonly IOutputRoutingService _routing;

    /// <summary>
    /// Creates the query service and begins tracking live-production changes.
    /// </summary>
    public LiveProductionQueryService(
        ILiveProductionFacade liveProduction,
        IOutputRoutingService routing)
    {
        _liveProduction = liveProduction ?? throw new ArgumentNullException(nameof(liveProduction));
        _routing = routing ?? throw new ArgumentNullException(nameof(routing));

        _liveProduction.Changed += HandleChanged;
        _routing.Changed += HandleRoutingChanged;
        Current = BuildSnapshot(_liveProduction.Current, _routing);
    }

    /// <inheritdoc />
    public event EventHandler<LiveProductionQueryChangedEventArgs>? Changed;

    /// <inheritdoc />
    public LiveProductionQuerySnapshot Current { get; private set; }

    private void HandleChanged(object? sender, LiveProductionChangedEventArgs args)
    {
        Current = BuildSnapshot(args.Snapshot, _routing);
        Changed?.Invoke(this, new LiveProductionQueryChangedEventArgs { Snapshot = Current });
    }

    private void HandleRoutingChanged(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;
        Current = BuildSnapshot(_liveProduction.Current, _routing);
        Changed?.Invoke(this, new LiveProductionQueryChangedEventArgs { Snapshot = Current });
    }

    internal static LiveProductionQuerySnapshot BuildSnapshot(
        LiveProductionSnapshot liveProduction,
        IOutputRoutingService routing)
    {
        ArgumentNullException.ThrowIfNull(liveProduction);
        ArgumentNullException.ThrowIfNull(routing);

        return new LiveProductionQuerySnapshot
        {
            Version = liveProduction.SessionState.Version,
            ActiveLookId = routing.ActiveLookId,
            LivePresentationPath = liveProduction.PlaybackState.PresentationPath,
            LiveSlideId = liveProduction.PlaybackState.CurrentSlideId,
            Selection = BuildSelectionQuery(liveProduction.PlaybackState),
            Looks = routing.Looks
                .Select(look => new LiveLookQueryOption
                {
                    Id = look.Id,
                    Name = look.Name,
                    IsActive = string.Equals(look.Id, routing.ActiveLookId, StringComparison.OrdinalIgnoreCase),
                })
                .ToArray(),
            Screens = BuildScreenQueries(liveProduction).ToArray(),
            ActiveLayers = BuildActiveLayerQueries(liveProduction.SessionState).ToArray(),
            StageScreens = BuildStageScreenQueries(liveProduction).ToArray(),
            Endpoints = BuildEndpointQueries(liveProduction.Topology).ToArray(),
            FrameHealth = BuildFrameHealthQueries(liveProduction).ToArray(),
            MediaIssues = BuildMediaIssues(liveProduction).ToArray(),
            Generated = BuildGeneratedSystemsQuery(liveProduction.SessionState.GeneratedState, routing.ActiveLook),
        };
    }

    private static LiveSelectionStateQuery BuildSelectionQuery(PlaybackState playbackState)
    {
        SelectionCursor cursor = playbackState.OperatorCursor;
        bool slideMatches = string.Equals(cursor.SlideId, playbackState.CurrentSlideId, StringComparison.OrdinalIgnoreCase);
        bool presentationMatches = string.Equals(cursor.PresentationPath, playbackState.PresentationPath, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(cursor.PresentationPath)
            || string.IsNullOrWhiteSpace(playbackState.PresentationPath);
        bool instanceMatches = string.Equals(cursor.InstanceKey, playbackState.CurrentSlideInstanceKey, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(cursor.InstanceKey)
            || string.IsNullOrWhiteSpace(playbackState.CurrentSlideInstanceKey);

        return new LiveSelectionStateQuery
        {
            SelectedPresentationPath = cursor.PresentationPath,
            SelectedSlideId = cursor.SlideId,
            SelectedInstanceKey = cursor.InstanceKey,
            LivePresentationPath = playbackState.PresentationPath,
            LiveSlideId = playbackState.CurrentSlideId,
            LiveInstanceKey = playbackState.CurrentSlideInstanceKey,
            IsSelectionLive = cursor.HasSelection && slideMatches && presentationMatches && instanceMatches,
            UserOverrideSelection = playbackState.UserOverrideSelection,
        };
    }

    private static IEnumerable<LiveOutputScreenQuery> BuildScreenQueries(LiveProductionSnapshot liveProduction)
    {
        foreach (OutputScreen screen in liveProduction.Topology.Screens.Values.OrderBy(static screen => screen.Name, StringComparer.OrdinalIgnoreCase))
        {
            OutputScreenDiagnostics diagnostics = liveProduction.Topology.ResolveDiagnostics(screen.Id);
            ScreenLayerRouting route = liveProduction.SessionState.ActiveLook.ResolveRoute(screen.Id);
            IReadOnlyList<OutputLayerRouteState> layerRoutes = BuildLayerRouteStates(route);
            string activeLookId = liveProduction.SessionState.ActiveLook.Id;
            string activeLookName = liveProduction.SessionState.ActiveLook.Name;

            if (screen.Kind == OutputScreenKind.Stage)
            {
                liveProduction.Frames.StageFrames.TryGetValue(screen.Id, out StageRenderFrame? stageFrame);
                yield return new LiveOutputScreenQuery
                {
                    ScreenId = screen.Id,
                    ScreenName = screen.Name,
                    Kind = screen.Kind,
                    Health = diagnostics.Health,
                    ActiveLookId = activeLookId,
                    ActiveLookName = activeLookName,
                    DiagnosticsMessage = diagnostics.Message,
                    EndpointIds = diagnostics.EndpointIds,
                    HasResolvedFrame = stageFrame != null,
                    FrameSequence = stageFrame?.Sequence,
                    FrameDiagnosticsMessage = stageFrame?.Diagnostics.Message,
                    StageLayoutId = stageFrame?.StageLayoutId,
                    StageCommandMode = stageFrame?.CommandMode,
                    StagePayloads = stageFrame?.Payloads ?? Array.Empty<RenderPayloadDescriptor>(),
                };

                continue;
            }

            liveProduction.Frames.AudienceFrames.TryGetValue(screen.Id, out AudienceRenderFrame? audienceFrame);
            yield return new LiveOutputScreenQuery
            {
                ScreenId = screen.Id,
                ScreenName = screen.Name,
                Kind = screen.Kind,
                Health = diagnostics.Health,
                ActiveLookId = activeLookId,
                ActiveLookName = activeLookName,
                DiagnosticsMessage = diagnostics.Message,
                EndpointIds = diagnostics.EndpointIds,
                HasResolvedFrame = audienceFrame != null,
                FrameSequence = audienceFrame?.Sequence,
                FrameDiagnosticsMessage = audienceFrame?.Diagnostics.Message,
                RoutesPresentation = route.Routes(BackendOutputLayerKind.Slide),
                RoutesMedia = route.Routes(BackendOutputLayerKind.Media),
                LayerRoutes = layerRoutes,
                VisibleAudienceLayers = audienceFrame?.Layers
                    .Where(static layer => layer.IsVisible)
                    .Select(static layer => layer.Kind)
                    .ToArray()
                    ?? Array.Empty<BackendOutputLayerKind>(),
            };
        }
    }

    private static IEnumerable<LiveLayerStateQuery> BuildActiveLayerQueries(LiveRenderSessionState state)
    {
        foreach (LayerState layerState in state.Layers.Values
                     .OrderBy(static layer => layer.Kind)
                     .Where(static layer => layer.Payload != null || layer.IsSuppressed || layer.IsCleared || !string.IsNullOrWhiteSpace(layer.Diagnostics)))
        {
            OutputLayerDefinition definition = OutputRoutingDefaults.GetLayerDefinition(layerState.Kind);
            PresentationRenderPayload? presentationPayload = layerState.Payload?.Detail as PresentationRenderPayload;
            yield return new LiveLayerStateQuery
            {
                Kind = layerState.Kind,
                Id = definition.Id,
                DisplayName = definition.DisplayName,
                IsLive = layerState.Payload != null && layerState.IsVisible && !layerState.IsSuppressed && !layerState.IsCleared,
                IsSuppressed = layerState.IsSuppressed,
                IsCleared = layerState.IsCleared,
                PayloadId = layerState.Payload?.Id,
                PayloadName = layerState.Payload?.DisplayName,
                SourceReference = layerState.Payload?.SourceReference,
                PayloadPresentationPath = presentationPayload?.PresentationPath,
                PayloadSlideId = presentationPayload?.SlideId,
                PayloadInstanceKey = presentationPayload?.ArrangementInstanceKey,
                SourceCommandId = layerState.SourceCommandId,
                Diagnostics = layerState.Diagnostics,
            };
        }
    }

    private static IEnumerable<LiveStageScreenQuery> BuildStageScreenQueries(LiveProductionSnapshot liveProduction)
    {
        foreach (LiveOutputScreenQuery screen in BuildScreenQueries(liveProduction)
                     .Where(static screen => screen.Kind == OutputScreenKind.Stage))
        {
            yield return new LiveStageScreenQuery
            {
                ScreenId = screen.ScreenId,
                ScreenName = screen.ScreenName,
                StageLayoutId = screen.StageLayoutId,
                CommandMode = screen.StageCommandMode,
                Payloads = screen.StagePayloads,
                FrameSequence = screen.FrameSequence,
            };
        }
    }

    private static IEnumerable<LiveEndpointHealthQuery> BuildEndpointQueries(OutputTopologySnapshot topology)
    {
        foreach (OutputEndpoint endpoint in topology.Endpoints.Values.OrderBy(static endpoint => endpoint.Name, StringComparer.OrdinalIgnoreCase))
        {
            string[] screenIds = topology.ScreenMappings
                .Where(mapping => mapping.EndpointIds.Any(endpointId =>
                    string.Equals(endpointId, endpoint.Id, StringComparison.OrdinalIgnoreCase)))
                .Select(static mapping => mapping.ScreenId)
                .OrderBy(static screenId => screenId, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            yield return new LiveEndpointHealthQuery
            {
                EndpointId = endpoint.Id,
                EndpointName = endpoint.Name,
                Kind = endpoint.Kind,
                Health = endpoint.Health,
                ScreenIds = screenIds,
                Capabilities = endpoint.Capabilities,
                NativeId = endpoint.NativeId,
            };
        }
    }

    private static IEnumerable<LiveFrameHealthQuery> BuildFrameHealthQueries(LiveProductionSnapshot liveProduction)
    {
        foreach (OutputScreen screen in liveProduction.Topology.Screens.Values.OrderBy(static screen => screen.Name, StringComparer.OrdinalIgnoreCase))
        {
            long? resolvedSequence = screen.Kind == OutputScreenKind.Stage
                ? liveProduction.Frames.StageFrames.TryGetValue(screen.Id, out StageRenderFrame? stageFrame) ? stageFrame.Sequence : null
                : liveProduction.Frames.AudienceFrames.TryGetValue(screen.Id, out AudienceRenderFrame? audienceFrame) ? audienceFrame.Sequence : null;
            OutputHostFrameFeedbackState[] feedback = liveProduction.SessionState.GeneratedState.HostFeedback.Values
                .Where(item => string.Equals(item.ScreenId, screen.Id, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            long? appliedSequence = feedback
                .Select(static item => item.LastAppliedFrameSequence)
                .Where(static sequence => sequence.HasValue)
                .DefaultIfEmpty()
                .Max();
            int droppedFrames = feedback.Sum(static item => item.DroppedFrameCount);
            bool isStale = resolvedSequence.HasValue
                && appliedSequence.HasValue
                && appliedSequence.Value < resolvedSequence.Value;
            string? hostDetail = feedback
                .Select(static item => item.Detail)
                .FirstOrDefault(static detail => !string.IsNullOrWhiteSpace(detail));
            OutputHostFrameFeedbackState? latestFeedback = feedback
                .Where(static item => item.LastAppliedAt.HasValue)
                .OrderByDescending(static item => item.LastAppliedAt)
                .FirstOrDefault()
                ?? feedback.FirstOrDefault();
            string? renderError = feedback
                .Select(static item => item.RenderError)
                .FirstOrDefault(static error => !string.IsNullOrWhiteSpace(error));

            yield return new LiveFrameHealthQuery
            {
                ScreenId = screen.Id,
                ResolvedSequence = resolvedSequence,
                AppliedSequence = appliedSequence,
                IsStale = isStale,
                DroppedFrameCount = droppedFrames,
                LastAppliedAt = latestFeedback?.LastAppliedAt,
                LastRenderDuration = latestFeedback?.LastRenderDuration,
                LastApplyDuration = latestFeedback?.LastApplyDuration,
                EndpointHealth = latestFeedback?.EndpointHealth ?? EndpointHealth.Unknown,
                WindowId = latestFeedback?.WindowId,
                Diagnostics = renderError ?? hostDetail,
            };
        }
    }

    private static IEnumerable<LiveMediaIssueQuery> BuildMediaIssues(LiveProductionSnapshot liveProduction)
    {
        foreach (MissingMediaReferenceState missing in liveProduction.SessionState.GeneratedState.MissingMedia.Values
                     .OrderBy(static missing => missing.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            yield return new LiveMediaIssueQuery
            {
                Id = $"missing-media:{missing.AssetId}",
                Kind = "missing-media",
                SubjectId = missing.AssetId,
                Message = string.IsNullOrWhiteSpace(missing.DiagnosticMessage)
                    ? $"{missing.DisplayName} is missing."
                    : missing.DiagnosticMessage!,
                RecoveryActionType = "relink-media",
            };
        }

        foreach (MediaPlayerFailureState failure in liveProduction.SessionState.GeneratedState.MediaPlayerFailures.Values
                     .Where(static failure => failure.IsActive)
                     .OrderBy(static failure => failure.PlayerId, StringComparer.OrdinalIgnoreCase))
        {
            yield return new LiveMediaIssueQuery
            {
                Id = $"player:{failure.PlayerId}",
                Kind = "player-failure",
                LayerKind = failure.LayerKind,
                SubjectId = failure.PayloadId ?? failure.PlayerId,
                Message = failure.Message,
                RecoveryActionType = "reset-player",
            };
        }

        foreach (LayerState layerState in liveProduction.SessionState.Layers.Values
                     .Where(static layer => (layer.Kind is BackendOutputLayerKind.Media or BackendOutputLayerKind.Audio or BackendOutputLayerKind.LiveVideo)
                         && !string.IsNullOrWhiteSpace(layer.Diagnostics)))
        {
            yield return new LiveMediaIssueQuery
            {
                Id = $"layer:{layerState.Kind}",
                Kind = "layer-diagnostic",
                LayerKind = layerState.Kind,
                SubjectId = layerState.Payload?.Id,
                Message = layerState.Diagnostics!,
                RecoveryActionType = "clear-layer",
            };
        }

        foreach (CaptureSessionState capture in liveProduction.SessionState.GeneratedState.CaptureSessions.Values
                     .Where(static capture => capture.Health is CaptureSessionHealth.Degraded or CaptureSessionHealth.Failed)
                     .OrderBy(static capture => capture.Metadata.Name, StringComparer.OrdinalIgnoreCase))
        {
            yield return new LiveMediaIssueQuery
            {
                Id = $"capture:{capture.Metadata.Id}",
                Kind = "capture",
                SubjectId = capture.Metadata.Id,
                Message = string.IsNullOrWhiteSpace(capture.Detail)
                    ? $"{capture.Metadata.Name} is {capture.Health}."
                    : capture.Detail!,
                RecoveryActionType = "restart-capture",
            };
        }
    }

    private static LiveGeneratedSystemsQuery BuildGeneratedSystemsQuery(
        GeneratedStateSnapshot generatedState,
        OutputLookDefinition activeLook)
    {
        return new LiveGeneratedSystemsQuery
        {
            StageMessageText = generatedState.StageMessageText,
            Timers = generatedState.Timers.Values
                .OrderBy(static timer => timer.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            VisibleMessages = GetVisibleOverlays(generatedState.Messages),
            VisibleProps = GetVisibleOverlays(generatedState.Props),
            VisibleAnnouncements = GetVisibleOverlays(generatedState.Announcements),
            VisibleMasks = GetVisibleOverlays(generatedState.Masks),
            ActiveCaptureSessions = generatedState.CaptureSessions.Values
                .Where(static session => session.IsActive)
                .OrderBy(static session => session.Metadata.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            ClearGroupIds = generatedState.RecoveryDiagnostics.AvailableClearGroupIds
                .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            ClearGroups = activeLook.ClearGroups
                .Select(clearGroup => new LiveClearGroupQuery
                {
                    Id = clearGroup.Id,
                    Name = clearGroup.Name,
                    Icon = clearGroup.Icon,
                    TintEnabled = clearGroup.TintEnabled,
                    TintColor = clearGroup.TintColor,
                    Scopes = clearGroup.Scopes.ToArray(),
                    Layers = OutputRoutingDefaults.ResolveClearGroupLayers(clearGroup)
                        .OrderBy(static layer => layer)
                        .ToArray(),
                    StopPresentationTimeline = clearGroup.StopPresentationTimeline,
                    StopAnnouncementTimeline = clearGroup.StopAnnouncementTimeline,
                })
                .Where(static clearGroup => clearGroup.Layers.Count > 0)
                .OrderBy(static clearGroup => clearGroup.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            StageLayoutsByScreenId = new Dictionary<string, string>(
                generatedState.RecoveryDiagnostics.StageLayoutsByScreenId,
                StringComparer.OrdinalIgnoreCase),
            HostFeedback = generatedState.HostFeedback.Values
                .OrderBy(static feedback => feedback.ScreenId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static feedback => feedback.EndpointId, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            MediaPlayerFailures = generatedState.MediaPlayerFailures.Values
                .Where(static failure => failure.IsActive)
                .OrderBy(static failure => failure.PlayerId, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            MissingMedia = generatedState.MissingMedia.Values
                .OrderBy(static missing => missing.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
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

    private static IReadOnlyList<OverlayContentState> GetVisibleOverlays(
        IReadOnlyDictionary<string, OverlayContentState> overlays)
    {
        return overlays.Values
            .Where(static overlay => overlay.IsVisible)
            .OrderBy(static overlay => overlay.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}