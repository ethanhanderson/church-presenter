using ChurchPresenter.Backend.Output;
using ChurchPresenter.Backend.Overlays;
using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Backend.Stage;

using BackendOutputLayerKind = ChurchPresenter.Backend.Rendering.OutputLayerKind;

namespace ChurchPresenter.Models.Output;

/// <summary>
/// Operator-facing Look option for live output queries.
/// </summary>
public sealed record LiveLookQueryOption
{
    /// <summary>Stable Look id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Display name visible to the operator.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Whether this Look is currently active.</summary>
    public bool IsActive { get; init; }
}

/// <summary>
/// One logical screen summary projected from the live-production snapshot.
/// </summary>
public sealed record LiveOutputScreenQuery
{
    /// <summary>Logical screen id.</summary>
    public string ScreenId { get; init; } = string.Empty;

    /// <summary>Display name for the screen.</summary>
    public string ScreenName { get; init; } = string.Empty;

    /// <summary>Audience or stage screen kind.</summary>
    public OutputScreenKind Kind { get; init; }

    /// <summary>Current output health for the screen.</summary>
    public EndpointHealth Health { get; init; } = EndpointHealth.Unknown;

    /// <summary>Active Look id used while resolving this screen.</summary>
    public string ActiveLookId { get; init; } = string.Empty;

    /// <summary>Active Look display name used while resolving this screen.</summary>
    public string ActiveLookName { get; init; } = string.Empty;

    /// <summary>Operator-facing diagnostics message.</summary>
    public string DiagnosticsMessage { get; init; } = string.Empty;

    /// <summary>Endpoint ids currently mapped to the screen.</summary>
    public IReadOnlyList<string> EndpointIds { get; init; } = Array.Empty<string>();

    /// <summary>Whether the backend produced a resolved frame for this screen.</summary>
    public bool HasResolvedFrame { get; init; }

    /// <summary>Resolved frame sequence, when a frame exists.</summary>
    public long? FrameSequence { get; init; }

    /// <summary>Frame-level diagnostics, when the resolver emitted a message.</summary>
    public string? FrameDiagnosticsMessage { get; init; }

    /// <summary>Whether presentation content is routed to the screen.</summary>
    public bool RoutesPresentation { get; init; } = true;

    /// <summary>Whether media content is routed to the screen.</summary>
    public bool RoutesMedia { get; init; } = true;

    /// <summary>Visible backend layers for audience outputs.</summary>
    public IReadOnlyList<BackendOutputLayerKind> VisibleAudienceLayers { get; init; } = Array.Empty<BackendOutputLayerKind>();

    /// <summary>Resolved backend layer routing state for audience outputs.</summary>
    public IReadOnlyList<OutputLayerRouteState> LayerRoutes { get; init; } = Array.Empty<OutputLayerRouteState>();

    /// <summary>Resolved stage layout id, when this is a stage screen.</summary>
    public string? StageLayoutId { get; init; }

    /// <summary>Latest stage command mode, when this is a stage screen.</summary>
    public StageAudienceCommandMode? StageCommandMode { get; init; }

    /// <summary>Resolved stage-layout payloads, when this is a stage screen.</summary>
    public IReadOnlyList<RenderPayloadDescriptor> StagePayloads { get; init; } = Array.Empty<RenderPayloadDescriptor>();

    /// <summary>Human-readable endpoint summary for shell diagnostics.</summary>
    public string EndpointSummary => EndpointIds.Count == 0
        ? "No mapped endpoints"
        : string.Join(", ", EndpointIds);

    /// <summary>Human-readable routing summary for audience shell diagnostics.</summary>
    public string RoutingSummary => Kind == OutputScreenKind.Stage
        ? "Stage routing is independent from audience Looks."
        : $"Look {ActiveLookName}: {FormatRouteSummary()}";

    /// <summary>Human-readable visible-layer summary for shell diagnostics.</summary>
    public string VisibleLayerSummary => Kind == OutputScreenKind.Stage
        ? StageLayoutId is { Length: > 0 } ? $"Stage layout: {StageLayoutId}" : "No stage layout frame"
        : VisibleAudienceLayers.Count == 0 ? "No visible audience layers" : string.Join(", ", VisibleAudienceLayers);

    /// <summary>Human-readable frame health summary for shell diagnostics.</summary>
    public string FrameSummary
    {
        get
        {
            if (!HasResolvedFrame)
                return "Frame: not resolved";

            string sequence = FrameSequence.HasValue ? $"#{FrameSequence.Value}" : "resolved";
            return string.IsNullOrWhiteSpace(FrameDiagnosticsMessage)
                ? $"Frame: {sequence}"
                : $"Frame: {sequence} - {FrameDiagnosticsMessage}";
        }
    }

    private string FormatRouteSummary()
    {
        if (LayerRoutes.Count == 0)
            return $"slide {(RoutesPresentation ? "on" : "off")}, media {(RoutesMedia ? "on" : "off")}";

        return string.Join(
            ", ",
            LayerRoutes.Select(static route => $"{route.DisplayName.ToLowerInvariant()} {(route.IsEnabled ? "on" : "off")}"));
    }
}

/// <summary>
/// Selected-vs-live cursor state for operator surfaces.
/// </summary>
public sealed record LiveSelectionStateQuery
{
    /// <summary>Presentation path currently selected by the operator.</summary>
    public string? SelectedPresentationPath { get; init; }

    /// <summary>Slide id currently selected by the operator.</summary>
    public string? SelectedSlideId { get; init; }

    /// <summary>Arrangement-aware selected slide instance key.</summary>
    public string? SelectedInstanceKey { get; init; }

    /// <summary>Presentation path currently live in program output.</summary>
    public string? LivePresentationPath { get; init; }

    /// <summary>Slide id currently live in program output.</summary>
    public string? LiveSlideId { get; init; }

    /// <summary>Arrangement-aware live slide instance key.</summary>
    public string? LiveInstanceKey { get; init; }

    /// <summary>Whether the current operator selection matches the live slide.</summary>
    public bool IsSelectionLive { get; init; }

    /// <summary>Whether the operator is intentionally holding selection away from program output.</summary>
    public bool UserOverrideSelection { get; init; }
}

/// <summary>
/// Operator-facing state for one backend live layer.
/// </summary>
public sealed record LiveLayerStateQuery
{
    /// <summary>Backend layer identity.</summary>
    public BackendOutputLayerKind Kind { get; init; }

    /// <summary>Portable layer id used by routing and clear groups.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Layer display name.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Whether this layer currently contributes live output before screen-specific routing.</summary>
    public bool IsLive { get; init; }

    /// <summary>Whether this layer is currently suppressed.</summary>
    public bool IsSuppressed { get; init; }

    /// <summary>Whether this layer was explicitly cleared.</summary>
    public bool IsCleared { get; init; }

    /// <summary>Active payload id, when any.</summary>
    public string? PayloadId { get; init; }

    /// <summary>Active payload display name, when any.</summary>
    public string? PayloadName { get; init; }

    /// <summary>Active payload source reference, when any.</summary>
    public string? SourceReference { get; init; }

    /// <summary>Presentation path carried by the layer payload, when the payload is presentation-based.</summary>
    public string? PayloadPresentationPath { get; init; }

    /// <summary>Slide id carried by the layer payload, when the payload is presentation-based.</summary>
    public string? PayloadSlideId { get; init; }

    /// <summary>Arrangement instance key carried by the layer payload, when the payload is presentation-based.</summary>
    public string? PayloadInstanceKey { get; init; }

    /// <summary>Command id that last touched the layer.</summary>
    public Guid? SourceCommandId { get; init; }

    /// <summary>Operator-facing diagnostics for this layer.</summary>
    public string? Diagnostics { get; init; }
}

/// <summary>
/// Operator-facing metadata for one configured clear group.
/// </summary>
public sealed record LiveClearGroupQuery
{
    /// <summary>Stable clear group id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Display name visible to the operator.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Icon glyph or identifier for the clear action.</summary>
    public string Icon { get; init; } = string.Empty;

    /// <summary>Whether the icon should use the configured tint.</summary>
    public bool TintEnabled { get; init; }

    /// <summary>Optional tint color string.</summary>
    public string? TintColor { get; init; }

    /// <summary>Configured operator-facing clear scopes.</summary>
    public IReadOnlyList<OutputClearScope> Scopes { get; init; } = Array.Empty<OutputClearScope>();

    /// <summary>Resolved backend layers affected by this group.</summary>
    public IReadOnlyList<BackendOutputLayerKind> Layers { get; init; } = Array.Empty<BackendOutputLayerKind>();

    /// <summary>Whether the group should stop presentation timelines when clearing.</summary>
    public bool StopPresentationTimeline { get; init; }

    /// <summary>Whether the group should stop announcement timelines when clearing.</summary>
    public bool StopAnnouncementTimeline { get; init; }
}

/// <summary>
/// Operator-facing metadata for one configured clear action.
/// </summary>
public sealed record LiveClearActionQuery
{
    /// <summary>Stable action id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Display name visible to the operator.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Icon glyph or identifier for the action.</summary>
    public string Icon { get; init; } = string.Empty;

    /// <summary>Whether the action represents a named clear group.</summary>
    public bool IsGroup { get; init; }

    /// <summary>Clear group id when <see cref="IsGroup"/> is true.</summary>
    public string? ClearGroupId { get; init; }

    /// <summary>Explicit backend layers for single-layer or built-in clear actions.</summary>
    public IReadOnlyList<BackendOutputLayerKind> Layers { get; init; } = Array.Empty<BackendOutputLayerKind>();

    /// <summary>Whether the action uses a custom tint.</summary>
    public bool TintEnabled { get; init; }

    /// <summary>Optional tint color string.</summary>
    public string? TintColor { get; init; }
}

/// <summary>
/// Stage-screen layout and payload query state.
/// </summary>
public sealed record LiveStageScreenQuery
{
    /// <summary>Logical stage screen id.</summary>
    public string ScreenId { get; init; } = string.Empty;

    /// <summary>Operator-facing screen name.</summary>
    public string ScreenName { get; init; } = string.Empty;

    /// <summary>Active stage layout id.</summary>
    public string? StageLayoutId { get; init; }

    /// <summary>Last stage command mode that affected this screen.</summary>
    public StageAudienceCommandMode? CommandMode { get; init; }

    /// <summary>Resolved stage payloads for this screen.</summary>
    public IReadOnlyList<RenderPayloadDescriptor> Payloads { get; init; } = Array.Empty<RenderPayloadDescriptor>();

    /// <summary>Resolved frame sequence, when known.</summary>
    public long? FrameSequence { get; init; }
}

/// <summary>
/// Endpoint health exposed independently from screen routing summaries.
/// </summary>
public sealed record LiveEndpointHealthQuery
{
    /// <summary>Endpoint id.</summary>
    public string EndpointId { get; init; } = string.Empty;

    /// <summary>Endpoint display name.</summary>
    public string EndpointName { get; init; } = string.Empty;

    /// <summary>Endpoint kind.</summary>
    public OutputEndpointKind Kind { get; init; }

    /// <summary>Current endpoint health.</summary>
    public EndpointHealth Health { get; init; } = EndpointHealth.Unknown;

    /// <summary>Logical screens mapped to this endpoint.</summary>
    public IReadOnlyList<string> ScreenIds { get; init; } = Array.Empty<string>();

    /// <summary>Endpoint capabilities.</summary>
    public EndpointCapability Capabilities { get; init; }

    /// <summary>Optional native monitor/window/device identity.</summary>
    public string? NativeId { get; init; }
}

/// <summary>
/// Frame resolution and host-application feedback for one screen.
/// </summary>
public sealed record LiveFrameHealthQuery
{
    /// <summary>Logical screen id.</summary>
    public string ScreenId { get; init; } = string.Empty;

    /// <summary>Resolved backend frame sequence.</summary>
    public long? ResolvedSequence { get; init; }

    /// <summary>Last sequence reported as applied by a host, when known.</summary>
    public long? AppliedSequence { get; init; }

    /// <summary>Whether the host is behind the resolver.</summary>
    public bool IsStale { get; init; }

    /// <summary>Dropped/stale frame count reported by hosts for this screen.</summary>
    public int DroppedFrameCount { get; init; }

    /// <summary>Latest host-reported frame application time.</summary>
    public DateTimeOffset? LastAppliedAt { get; init; }

    /// <summary>Latest backend render duration observed by a host, when reported.</summary>
    public TimeSpan? LastRenderDuration { get; init; }

    /// <summary>Latest UI-thread frame apply duration reported by a host.</summary>
    public TimeSpan? LastApplyDuration { get; init; }

    /// <summary>Host-reported endpoint health for the most recent frame consumer.</summary>
    public EndpointHealth EndpointHealth { get; init; } = EndpointHealth.Unknown;

    /// <summary>WinUI WindowId/AppWindow identity for a local display host, when known.</summary>
    public string? WindowId { get; init; }

    /// <summary>Frame or host diagnostic text.</summary>
    public string? Diagnostics { get; init; }
}

/// <summary>
/// Media, player, capture, or missing-file issue surfaced to diagnostics and recovery.
/// </summary>
public sealed record LiveMediaIssueQuery
{
    /// <summary>Stable issue id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Issue category such as missing-media, player-failure, capture, or layer.</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>Related backend layer, when any.</summary>
    public BackendOutputLayerKind? LayerKind { get; init; }

    /// <summary>Related payload, asset, capture, or player id.</summary>
    public string? SubjectId { get; init; }

    /// <summary>Operator-facing issue text.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Recommended recovery action type, when obvious.</summary>
    public string? RecoveryActionType { get; init; }
}

/// <summary>
/// Query projection for generated live systems.
/// </summary>
public sealed record LiveGeneratedSystemsQuery
{
    /// <summary>Stage-only message text.</summary>
    public string? StageMessageText { get; init; }

    /// <summary>Timer snapshots currently known to the runtime.</summary>
    public IReadOnlyList<TimerSnapshot> Timers { get; init; } = Array.Empty<TimerSnapshot>();

    /// <summary>Visible audience messages.</summary>
    public IReadOnlyList<OverlayContentState> VisibleMessages { get; init; } = Array.Empty<OverlayContentState>();

    /// <summary>Visible props.</summary>
    public IReadOnlyList<OverlayContentState> VisibleProps { get; init; } = Array.Empty<OverlayContentState>();

    /// <summary>Visible announcements.</summary>
    public IReadOnlyList<OverlayContentState> VisibleAnnouncements { get; init; } = Array.Empty<OverlayContentState>();

    /// <summary>Visible masks.</summary>
    public IReadOnlyList<OverlayContentState> VisibleMasks { get; init; } = Array.Empty<OverlayContentState>();

    /// <summary>Capture sessions currently active.</summary>
    public IReadOnlyList<CaptureSessionState> ActiveCaptureSessions { get; init; } = Array.Empty<CaptureSessionState>();

    /// <summary>Active clear group ids exposed by recovery diagnostics.</summary>
    public IReadOnlyList<string> ClearGroupIds { get; init; } = Array.Empty<string>();

    /// <summary>Configured clear group metadata for operator recovery surfaces.</summary>
    public IReadOnlyList<LiveClearGroupQuery> ClearGroups { get; init; } = Array.Empty<LiveClearGroupQuery>();

    /// <summary>Stage layouts currently assigned by screen id.</summary>
    public IReadOnlyDictionary<string, string> StageLayoutsByScreenId { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Host frame application feedback.</summary>
    public IReadOnlyList<OutputHostFrameFeedbackState> HostFeedback { get; init; } = Array.Empty<OutputHostFrameFeedbackState>();

    /// <summary>Active media/player failures.</summary>
    public IReadOnlyList<MediaPlayerFailureState> MediaPlayerFailures { get; init; } = Array.Empty<MediaPlayerFailureState>();

    /// <summary>Missing-media references that need operator recovery.</summary>
    public IReadOnlyList<MissingMediaReferenceState> MissingMedia { get; init; } = Array.Empty<MissingMediaReferenceState>();
}

/// <summary>
/// Read-model snapshot used by modular Show/output view models and query consumers.
/// </summary>
public sealed record LiveProductionQuerySnapshot
{
    /// <summary>An empty snapshot.</summary>
    public static readonly LiveProductionQuerySnapshot Empty = new();

    /// <summary>Current live session version.</summary>
    public long Version { get; init; }

    /// <summary>Current active Look id.</summary>
    public string ActiveLookId { get; init; } = string.Empty;

    /// <summary>Presentation path currently resolved by the legacy playback engine, when any.</summary>
    public string? LivePresentationPath { get; init; }

    /// <summary>Program slide id currently resolved by the legacy playback engine, when any.</summary>
    public string? LiveSlideId { get; init; }

    /// <summary>Selected-vs-live operator state.</summary>
    public LiveSelectionStateQuery Selection { get; init; } = new();

    /// <summary>Available Looks with active-state decoration.</summary>
    public IReadOnlyList<LiveLookQueryOption> Looks { get; init; } = Array.Empty<LiveLookQueryOption>();

    /// <summary>Current output screen summaries.</summary>
    public IReadOnlyList<LiveOutputScreenQuery> Screens { get; init; } = Array.Empty<LiveOutputScreenQuery>();

    /// <summary>Current active backend layers.</summary>
    public IReadOnlyList<LiveLayerStateQuery> ActiveLayers { get; init; } = Array.Empty<LiveLayerStateQuery>();

    /// <summary>Stage screen layout assignments and payloads.</summary>
    public IReadOnlyList<LiveStageScreenQuery> StageScreens { get; init; } = Array.Empty<LiveStageScreenQuery>();

    /// <summary>Endpoint health independent from screen summaries.</summary>
    public IReadOnlyList<LiveEndpointHealthQuery> Endpoints { get; init; } = Array.Empty<LiveEndpointHealthQuery>();

    /// <summary>Resolved/applied frame health per screen.</summary>
    public IReadOnlyList<LiveFrameHealthQuery> FrameHealth { get; init; } = Array.Empty<LiveFrameHealthQuery>();

    /// <summary>Media/player/capture/missing-file issues visible to recovery surfaces.</summary>
    public IReadOnlyList<LiveMediaIssueQuery> MediaIssues { get; init; } = Array.Empty<LiveMediaIssueQuery>();

    /// <summary>Current generated/live-system summary.</summary>
    public LiveGeneratedSystemsQuery Generated { get; init; } = new();
}