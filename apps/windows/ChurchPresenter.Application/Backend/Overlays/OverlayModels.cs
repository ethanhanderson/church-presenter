using ChurchPresenter.Backend.Output;
using ChurchPresenter.Backend.Rendering;

namespace ChurchPresenter.Backend.Overlays;

/// <summary>
/// Overlay concepts that participate in generated live state.
/// </summary>
public enum OverlayContentKind
{
    Message,
    Prop,
    Announcement,
    Mask,
    StageMessage,
}

/// <summary>
/// Stable mapping from overlay concepts to backend layer identities.
/// </summary>
public static class OverlayLayerIdentity
{
    /// <summary>Returns the audience/output layer for an overlay kind, if one exists.</summary>
    public static bool TryGetAudienceLayer(OverlayContentKind kind, out OutputLayerKind layerKind)
    {
        switch (kind)
        {
            case OverlayContentKind.Message:
                layerKind = OutputLayerKind.Messages;
                return true;
            case OverlayContentKind.Prop:
                layerKind = OutputLayerKind.Props;
                return true;
            case OverlayContentKind.Announcement:
                layerKind = OutputLayerKind.Announcements;
                return true;
            case OverlayContentKind.Mask:
                layerKind = OutputLayerKind.Mask;
                return true;
            default:
                layerKind = default;
                return false;
        }
    }

    /// <summary>Returns whether the overlay kind is stage-only.</summary>
    public static bool IsStageOnly(OverlayContentKind kind)
    {
        return kind == OverlayContentKind.StageMessage;
    }
}

/// <summary>
/// Named overlay state such as a message, prop, announcement, or mask.
/// </summary>
public sealed record OverlayContentState
{
    /// <summary>Stable overlay id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Operator-facing name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Overlay concept kind.</summary>
    public OverlayContentKind Kind { get; init; }

    /// <summary>Whether the overlay is currently live.</summary>
    public bool IsVisible { get; init; }

    /// <summary>Optional text content for generated overlays.</summary>
    public string? Text { get; init; }

    /// <summary>Optional payload descriptor for media/graphic overlays.</summary>
    public RenderPayloadDescriptor? Payload { get; init; }

    /// <summary>Token values resolved for this overlay instance.</summary>
    public IReadOnlyDictionary<string, string> Tokens { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Supported timer types derived from ProPresenter workflows.
/// </summary>
public enum GeneratedTimerKind
{
    Countdown,
    CountdownToTime,
    ElapsedTime,
    SystemClock,
    VideoCountdown,
    PlanningCenterLive,
}

/// <summary>
/// Runtime timer status.
/// </summary>
public enum GeneratedTimerStatus
{
    Stopped,
    Running,
    Paused,
    Completed,
    Overrun,
}

/// <summary>
/// Configured timer definition.
/// </summary>
public sealed record TimerDefinition
{
    /// <summary>Stable timer id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Operator-facing timer name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Timer type.</summary>
    public GeneratedTimerKind Kind { get; init; }

    /// <summary>Configured duration for countdown-style timers.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>Configured target time for count-to-time timers.</summary>
    public DateTimeOffset? TargetTime { get; init; }

    /// <summary>Whether the timer should continue past zero/end.</summary>
    public bool AllowsOverrun { get; init; }
}

/// <summary>
/// Generated timer state consumable by layouts, overlays, and diagnostics.
/// </summary>
public sealed record TimerSnapshot
{
    /// <summary>Stable timer id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Operator-facing timer name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Timer type.</summary>
    public GeneratedTimerKind Kind { get; init; }

    /// <summary>Current runtime status.</summary>
    public GeneratedTimerStatus Status { get; init; }

    /// <summary>Elapsed duration.</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>Remaining duration when applicable.</summary>
    public TimeSpan? Remaining { get; init; }

    /// <summary>Display-ready value for quick consumers.</summary>
    public string DisplayValue { get; init; } = string.Empty;

    /// <summary>Whether the timer is past its nominal end.</summary>
    public bool IsOverrun { get; init; }

    /// <summary>Optional color cue emitted by timer rules.</summary>
    public string? ActiveColor { get; init; }
}

/// <summary>
/// One generated token/value pair.
/// </summary>
public sealed record GeneratedTokenValue(string Token, string Value);

/// <summary>
/// Shared token provider contract for generated live state.
/// </summary>
public interface IGeneratedTokenValueProvider
{
    /// <summary>Provider id for diagnostics/registration.</summary>
    string ProviderId { get; }

    /// <summary>Resolves token values for a named source object.</summary>
    IReadOnlyList<GeneratedTokenValue> Resolve(GeneratedStateSnapshot state, string sourceId);
}

/// <summary>
/// Timer-backed token provider.
/// </summary>
public sealed class TimerTokenValueProvider : IGeneratedTokenValueProvider
{
    /// <inheritdoc />
    public string ProviderId => "timer";

    /// <inheritdoc />
    public IReadOnlyList<GeneratedTokenValue> Resolve(GeneratedStateSnapshot state, string sourceId)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!state.Timers.TryGetValue(sourceId, out TimerSnapshot? timer))
        {
            return Array.Empty<GeneratedTokenValue>();
        }

        return
        [
            new GeneratedTokenValue("timer.id", timer.Id),
            new GeneratedTokenValue("timer.name", timer.Name),
            new GeneratedTokenValue("timer.display", timer.DisplayValue),
            new GeneratedTokenValue("timer.status", timer.Status.ToString()),
            new GeneratedTokenValue("timer.elapsed", timer.Elapsed.ToString()),
            new GeneratedTokenValue("timer.remaining", timer.Remaining?.ToString() ?? string.Empty),
            new GeneratedTokenValue("timer.color", timer.ActiveColor ?? string.Empty),
        ];
    }
}

/// <summary>
/// Capture output destination category.
/// </summary>
public enum CaptureDestinationKind
{
    File,
    Rtmp,
}

/// <summary>
/// Operator-facing capture health.
/// </summary>
public enum CaptureSessionHealth
{
    Idle,
    Starting,
    Healthy,
    Degraded,
    Failed,
    Stopped,
}

/// <summary>
/// Capture session configuration metadata.
/// </summary>
public sealed record CaptureSessionMetadata
{
    /// <summary>Stable capture session id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Operator-facing session name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Logical source screen id.</summary>
    public string SourceScreenId { get; init; } = string.Empty;

    /// <summary>Destination category.</summary>
    public CaptureDestinationKind DestinationKind { get; init; }

    /// <summary>File path or stream URL.</summary>
    public string Destination { get; init; } = string.Empty;

    /// <summary>Configured codec or profile.</summary>
    public string? Codec { get; init; }

    /// <summary>Configured resolution label or value.</summary>
    public string? Resolution { get; init; }

    /// <summary>Configured nominal frame rate.</summary>
    public double? FrameRate { get; init; }
}

/// <summary>
/// Runtime capture session state.
/// </summary>
public sealed record CaptureSessionState
{
    /// <summary>Static capture metadata.</summary>
    public CaptureSessionMetadata Metadata { get; init; } = new();

    /// <summary>Whether the capture session is actively consuming frames.</summary>
    public bool IsActive { get; init; }

    /// <summary>Current health/state reported to the operator.</summary>
    public CaptureSessionHealth Health { get; init; } = CaptureSessionHealth.Idle;

    /// <summary>Elapsed runtime while active.</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>Optional status detail.</summary>
    public string? Detail { get; init; }
}

/// <summary>
/// Output-host frame application feedback reported by displays, previews, or capture consumers.
/// </summary>
public sealed record OutputHostFrameFeedbackState
{
    /// <summary>Logical screen id that produced the resolved frame.</summary>
    public string ScreenId { get; init; } = string.Empty;

    /// <summary>Endpoint or consumer id that applied the frame.</summary>
    public string EndpointId { get; init; } = string.Empty;

    /// <summary>Last backend frame sequence resolved for this screen.</summary>
    public long? LastResolvedFrameSequence { get; init; }

    /// <summary>Last frame sequence the host reports as applied.</summary>
    public long? LastAppliedFrameSequence { get; init; }

    /// <summary>UTC time when the host last applied a frame.</summary>
    public DateTimeOffset? LastAppliedAt { get; init; }

    /// <summary>Most recent backend render duration observed by the host, when available.</summary>
    public TimeSpan? LastRenderDuration { get; init; }

    /// <summary>Most recent UI-thread frame apply duration reported by the host.</summary>
    public TimeSpan? LastApplyDuration { get; init; }

    /// <summary>Total stale or dropped frames reported by the host.</summary>
    public int DroppedFrameCount { get; init; }

    /// <summary>Whether the host considers the endpoint visible or active.</summary>
    public bool IsVisible { get; init; }

    /// <summary>Endpoint health observed by the host.</summary>
    public EndpointHealth EndpointHealth { get; init; } = EndpointHealth.Unknown;

    /// <summary>Current local monitor index for local-display hosts, when known.</summary>
    public int? MonitorIndex { get; init; }

    /// <summary>WinUI WindowId/AppWindow identity associated with the host, when known.</summary>
    public string? WindowId { get; init; }

    /// <summary>Whether the host remapped after a monitor reconnect or topology change.</summary>
    public bool WasRemapped { get; init; }

    /// <summary>Latest render or apply exception text reported by the host.</summary>
    public string? RenderError { get; init; }

    /// <summary>Optional host-level status or error text.</summary>
    public string? Detail { get; init; }
}

/// <summary>
/// Media/player failure surfaced to operator diagnostics without depending on WinUI player types.
/// </summary>
public sealed record MediaPlayerFailureState
{
    /// <summary>Stable player or host id.</summary>
    public string PlayerId { get; init; } = string.Empty;

    /// <summary>Backend layer associated with the failure.</summary>
    public OutputLayerKind LayerKind { get; init; } = OutputLayerKind.Media;

    /// <summary>Payload id being played when the failure was observed.</summary>
    public string? PayloadId { get; init; }

    /// <summary>Operator-facing failure message.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Whether this failure is still active.</summary>
    public bool IsActive { get; init; } = true;
}

/// <summary>
/// Missing-media reference visible to recovery surfaces.
/// </summary>
public sealed record MissingMediaReferenceState
{
    /// <summary>Stable asset id, when known.</summary>
    public string AssetId { get; init; } = string.Empty;

    /// <summary>Display name for the missing media item.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Content, cue, layer, or playlist reference that needs the asset.</summary>
    public string? SourceReference { get; init; }

    /// <summary>Last path known for the media item.</summary>
    public string? LastKnownPath { get; init; }

    /// <summary>Operator-facing diagnostic text.</summary>
    public string? DiagnosticMessage { get; init; }
}

/// <summary>
/// Layer-level operator recovery snapshot.
/// </summary>
public sealed record LayerRecoveryState
{
    /// <summary>Layer identity.</summary>
    public OutputLayerKind LayerKind { get; init; }

    /// <summary>Whether the layer is currently live.</summary>
    public bool IsLive { get; init; }

    /// <summary>Current payload id, if any.</summary>
    public string? PayloadId { get; init; }

    /// <summary>Optional source reference for the live payload.</summary>
    public string? SourceReference { get; init; }

    /// <summary>Command id that last touched the layer.</summary>
    public Guid? SourceCommandId { get; init; }

    /// <summary>Correlation id for the command that last touched the layer.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Source kind for the command that last touched the layer.</summary>
    public string? SourceKind { get; init; }

    /// <summary>Clear lifecycle state for the layer.</summary>
    public LayerClearState ClearState { get; init; } = LayerClearState.None;

    /// <summary>Transition intent for the current layer state.</summary>
    public LayerTransitionState Transition { get; init; } = LayerTransitionState.None;

    /// <summary>Whether the backend has player-state feedback for this layer.</summary>
    public bool HasPlayerState { get; init; }

    /// <summary>Structured render or recovery errors associated with this layer.</summary>
    public IReadOnlyList<RenderErrorDescriptor> RenderErrors { get; init; } = Array.Empty<RenderErrorDescriptor>();

    /// <summary>Diagnostics visible to recovery tooling.</summary>
    public string? Diagnostics { get; init; }
}

/// <summary>
/// Observable recovery state answering "what is live and why?".
/// </summary>
public sealed record OperatorRecoveryDiagnosticsState
{
    /// <summary>Recovery snapshot per reserved output layer.</summary>
    public IReadOnlyList<LayerRecoveryState> Layers { get; init; } = Array.Empty<LayerRecoveryState>();

    /// <summary>Stage-layout ids active per stage screen.</summary>
    public IReadOnlyDictionary<string, string> StageLayoutsByScreenId { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Configured clear groups visible to the operator.</summary>
    public IReadOnlySet<string> AvailableClearGroupIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Active capture-session ids.</summary>
    public IReadOnlyList<string> ActiveCaptureSessionIds { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Shared generated/live state consumed by overlays, stage layouts, and operator recovery.
/// </summary>
public sealed record GeneratedStateSnapshot
{
    /// <summary>Timer states keyed by timer id.</summary>
    public IReadOnlyDictionary<string, TimerSnapshot> Timers { get; init; } =
        new Dictionary<string, TimerSnapshot>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Audience message states keyed by message id.</summary>
    public IReadOnlyDictionary<string, OverlayContentState> Messages { get; init; } =
        new Dictionary<string, OverlayContentState>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Prop states keyed by prop id.</summary>
    public IReadOnlyDictionary<string, OverlayContentState> Props { get; init; } =
        new Dictionary<string, OverlayContentState>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Announcement states keyed by announcement id.</summary>
    public IReadOnlyDictionary<string, OverlayContentState> Announcements { get; init; } =
        new Dictionary<string, OverlayContentState>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Mask states keyed by mask id.</summary>
    public IReadOnlyDictionary<string, OverlayContentState> Masks { get; init; } =
        new Dictionary<string, OverlayContentState>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Stage-only message text.</summary>
    public string? StageMessageText { get; init; }

    /// <summary>Capture session states keyed by session id.</summary>
    public IReadOnlyDictionary<string, CaptureSessionState> CaptureSessions { get; init; } =
        new Dictionary<string, CaptureSessionState>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Output-host frame application feedback keyed by host or endpoint id.</summary>
    public IReadOnlyDictionary<string, OutputHostFrameFeedbackState> HostFeedback { get; init; } =
        new Dictionary<string, OutputHostFrameFeedbackState>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Active media/player failures keyed by player or failure id.</summary>
    public IReadOnlyDictionary<string, MediaPlayerFailureState> MediaPlayerFailures { get; init; } =
        new Dictionary<string, MediaPlayerFailureState>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Missing media references keyed by asset or reference id.</summary>
    public IReadOnlyDictionary<string, MissingMediaReferenceState> MissingMedia { get; init; } =
        new Dictionary<string, MissingMediaReferenceState>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Operator recovery state.</summary>
    public OperatorRecoveryDiagnosticsState RecoveryDiagnostics { get; init; } = new();
}