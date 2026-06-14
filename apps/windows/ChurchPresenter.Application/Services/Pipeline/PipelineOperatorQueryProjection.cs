using ChurchPresenter.Backend.Output;
using ChurchPresenter.Backend.Overlays;
using ChurchPresenter.Backend.Pipeline.Routing;
using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Backend.Stage;
using ChurchPresenter.Models.Media;
using ChurchPresenter.Models.Output;

using BackendOutputLayerKind = ChurchPresenter.Backend.Rendering.OutputLayerKind;

namespace ChurchPresenter.Services.Pipeline;

/// <summary>
/// Builds legacy operator query snapshots from the unified pipeline runtime.
/// </summary>
internal static class PipelineOperatorQueryProjection
{
    internal static LiveProductionQuerySnapshot BuildProductionSnapshot(
        LiveSessionQuerySnapshot query,
        LiveSessionSnapshot pipelineSnapshot,
        IOperatorSelectionService operatorSelection,
        IOutputRoutingService routing)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(pipelineSnapshot);
        ArgumentNullException.ThrowIfNull(operatorSelection);
        ArgumentNullException.ThrowIfNull(routing);

        LiveRenderSessionState sessionState = pipelineSnapshot.SessionState;
        GeneratedStateSnapshot generatedState = sessionState.GeneratedState;

        return new LiveProductionQuerySnapshot
        {
            Version = query.Version,
            ActiveLookId = query.ActiveLookId,
            LivePresentationPath = query.LivePresentationPath,
            LiveSlideId = query.LiveSlideId,
            Selection = BuildSelectionQuery(operatorSelection, query),
            Looks = query.Looks,
            Screens = BuildScreenQueries(sessionState, routing).ToArray(),
            ActiveLayers = query.ActiveLayers,
            StageScreens = BuildStageScreenQueries(sessionState, routing).ToArray(),
            Endpoints = BuildEndpointQueries(sessionState).ToArray(),
            FrameHealth = BuildFrameHealthQueries(sessionState).ToArray(),
            MediaIssues = BuildMediaIssues(generatedState).ToArray(),
            Generated = BuildGeneratedSystemsQuery(generatedState, routing.ActiveLook),
        };
    }

    private static LiveSelectionStateQuery BuildSelectionQuery(
        IOperatorSelectionService operatorSelection,
        LiveSessionQuerySnapshot query)
    {
        SelectionCursor cursor = operatorSelection.SelectionCursor;
        bool slideMatches = string.Equals(cursor.SlideId, query.LiveSlideId, StringComparison.OrdinalIgnoreCase);
        bool presentationMatches = string.Equals(cursor.PresentationPath, query.LivePresentationPath, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(cursor.PresentationPath)
            || string.IsNullOrWhiteSpace(query.LivePresentationPath);
        LiveLayerStateQuery? slideLayer = query.ActiveLayers.FirstOrDefault(static layer => layer.Kind == BackendOutputLayerKind.Slide);
        bool instanceMatches = string.Equals(cursor.InstanceKey, slideLayer?.PayloadInstanceKey, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(cursor.InstanceKey)
            || string.IsNullOrWhiteSpace(slideLayer?.PayloadInstanceKey);

        return new LiveSelectionStateQuery
        {
            SelectedPresentationPath = cursor.PresentationPath,
            SelectedSlideId = cursor.SlideId,
            SelectedInstanceKey = cursor.InstanceKey,
            LivePresentationPath = query.LivePresentationPath,
            LiveSlideId = query.LiveSlideId,
            LiveInstanceKey = slideLayer?.PayloadInstanceKey,
            IsSelectionLive = cursor.HasSelection && slideMatches && presentationMatches && instanceMatches,
            UserOverrideSelection = operatorSelection.UserOverrideSelection,
        };
    }

    private static IEnumerable<LiveOutputScreenQuery> BuildScreenQueries(LiveRenderSessionState sessionState, IOutputRoutingService routing)
    {
        foreach (OutputScreen screen in sessionState.Screens.Values.OrderBy(static screen => screen.Name, StringComparer.OrdinalIgnoreCase))
        {
            ScreenLayerRouting route = sessionState.ActiveLook.ResolveRoute(screen.Id);
            yield return new LiveOutputScreenQuery
            {
                ScreenId = screen.Id,
                ScreenName = screen.Name,
                Kind = screen.Kind,
                Health = EndpointHealth.Unknown,
                ActiveLookId = routing.ActiveLookId,
                ActiveLookName = sessionState.ActiveLook.Name,
                DiagnosticsMessage = string.Empty,
                EndpointIds = Array.Empty<string>(),
                HasResolvedFrame = false,
                RoutesPresentation = route.Routes(BackendOutputLayerKind.Slide),
                RoutesMedia = route.Routes(BackendOutputLayerKind.Media),
            };
        }
    }

    private static IEnumerable<LiveStageScreenQuery> BuildStageScreenQueries(LiveRenderSessionState sessionState, IOutputRoutingService routing)
    {
        foreach (LiveOutputScreenQuery screen in BuildScreenQueries(sessionState, routing)
                     .Where(static screen => screen.Kind == OutputScreenKind.Stage))
        {
            sessionState.StageLayoutIdsByScreenId.TryGetValue(screen.ScreenId, out string? layoutId);
            yield return new LiveStageScreenQuery
            {
                ScreenId = screen.ScreenId,
                ScreenName = screen.ScreenName,
                StageLayoutId = layoutId,
                CommandMode = StageAudienceCommandMode.StageAndAudience,
                Payloads = Array.Empty<RenderPayloadDescriptor>(),
            };
        }
    }

    private static IEnumerable<LiveEndpointHealthQuery> BuildEndpointQueries(LiveRenderSessionState sessionState)
    {
        foreach (OutputEndpoint endpoint in sessionState.Endpoints.Values.OrderBy(static endpoint => endpoint.Name, StringComparer.OrdinalIgnoreCase))
        {
            yield return new LiveEndpointHealthQuery
            {
                EndpointId = endpoint.Id,
                EndpointName = endpoint.Name,
                Kind = endpoint.Kind,
                Health = endpoint.Health,
                ScreenIds = sessionState.ScreenMappings
                    .Where(mapping => mapping.EndpointIds.Any(endpointId =>
                        string.Equals(endpointId, endpoint.Id, StringComparison.OrdinalIgnoreCase)))
                    .Select(static mapping => mapping.ScreenId)
                    .OrderBy(static screenId => screenId, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                Capabilities = endpoint.Capabilities,
                NativeId = endpoint.NativeId,
            };
        }
    }

    private static IEnumerable<LiveFrameHealthQuery> BuildFrameHealthQueries(LiveRenderSessionState sessionState)
    {
        foreach (OutputHostFrameFeedbackState feedback in sessionState.GeneratedState.HostFeedback.Values
                     .OrderBy(static item => item.ScreenId, StringComparer.OrdinalIgnoreCase))
        {
            yield return new LiveFrameHealthQuery
            {
                ScreenId = feedback.ScreenId,
                ResolvedSequence = feedback.LastResolvedFrameSequence,
                AppliedSequence = feedback.LastAppliedFrameSequence,
                IsStale = feedback.LastAppliedFrameSequence.HasValue
                    && feedback.LastResolvedFrameSequence.HasValue
                    && feedback.LastAppliedFrameSequence.Value < feedback.LastResolvedFrameSequence.Value,
                DroppedFrameCount = feedback.DroppedFrameCount,
                LastAppliedAt = feedback.LastAppliedAt,
                LastRenderDuration = feedback.LastRenderDuration,
                LastApplyDuration = feedback.LastApplyDuration,
                EndpointHealth = feedback.EndpointHealth,
                WindowId = feedback.WindowId,
                Diagnostics = feedback.RenderError ?? feedback.Detail,
            };
        }
    }

    private static IEnumerable<LiveMediaIssueQuery> BuildMediaIssues(GeneratedStateSnapshot generatedState)
    {
        foreach (MissingMediaReferenceState missing in generatedState.MissingMedia.Values
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

        foreach (MediaPlayerFailureState failure in generatedState.MediaPlayerFailures.Values
                     .Where(static failure => failure.IsActive)
                     .OrderBy(static failure => failure.PlayerId, StringComparer.OrdinalIgnoreCase))
        {
            yield return new LiveMediaIssueQuery
            {
                Id = $"player-failure:{failure.PlayerId}",
                Kind = "player-failure",
                SubjectId = failure.PlayerId,
                Message = failure.Message,
                RecoveryActionType = "reset-player",
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

    private static IReadOnlyList<OverlayContentState> GetVisibleOverlays(
        IReadOnlyDictionary<string, OverlayContentState> overlays) =>
        overlays.Values.Where(static overlay => overlay.IsVisible).ToArray();
}
