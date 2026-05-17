using ChurchPresenter.Backend.Media;
using ChurchPresenter.Backend.Output;
using ChurchPresenter.Backend.Stage;

namespace ChurchPresenter.Backend.Rendering;

/// <summary>
/// Stable ProPresenter-style live output layer identities used by the backend render engine.
/// </summary>
public enum OutputLayerKind
{
    /// <summary>Non-visual audio cue and playlist layer.</summary>
    Audio,

    /// <summary>Operator-triggered message overlay layer.</summary>
    Messages,

    /// <summary>Persistent prop overlay layer.</summary>
    Props,

    /// <summary>Secondary announcement presentation lane.</summary>
    Announcements,

    /// <summary>Primary slide or presentation layer.</summary>
    Slide,

    /// <summary>Media bin image/video playback layer.</summary>
    Media,

    /// <summary>Live camera or input layer.</summary>
    LiveVideo,

    /// <summary>Mask or alpha constraint layer.</summary>
    Mask,
}

/// <summary>
/// Describes whether a frame should be rendered as opaque or alpha-capable content.
/// </summary>
public enum RenderAlphaMode
{
    /// <summary>The frame is fully opaque.</summary>
    Opaque,

    /// <summary>The frame uses straight alpha.</summary>
    StraightAlpha,

    /// <summary>The frame uses premultiplied alpha.</summary>
    PremultipliedAlpha,
}

/// <summary>
/// Describes the type of payload a render layer carries.
/// </summary>
public enum RenderPayloadKind
{
    /// <summary>No active payload.</summary>
    None,

    /// <summary>Slide, announcement, or generated text payload.</summary>
    Presentation,

    /// <summary>Still image payload.</summary>
    Image,

    /// <summary>Video payload.</summary>
    Video,

    /// <summary>Audio-only payload.</summary>
    Audio,

    /// <summary>Live video input payload.</summary>
    LiveVideo,

    /// <summary>Generated overlay payload such as a message or prop.</summary>
    Overlay,
}

/// <summary>
/// Lifecycle state for a layer clear mutation.
/// </summary>
public enum LayerClearState
{
    /// <summary>The layer is not currently clear-targeted.</summary>
    None,

    /// <summary>The layer is in a clear transition or host-applied clear operation.</summary>
    Clearing,

    /// <summary>The layer was cleared by the latest relevant live command.</summary>
    Cleared,
}

/// <summary>
/// Lifecycle state for a layer transition intent.
/// </summary>
public enum LayerTransitionPhase
{
    /// <summary>No transition intent is associated with the layer.</summary>
    None,

    /// <summary>The transition should be applied by the output host.</summary>
    Pending,

    /// <summary>The transition has been reported as active by a host.</summary>
    Active,

    /// <summary>The transition completed successfully.</summary>
    Completed,

    /// <summary>The transition failed and should be visible in diagnostics.</summary>
    Failed,
}

/// <summary>
/// Command provenance copied onto live layers and resolved frames.
/// </summary>
public sealed record LiveCommandProvenance
{
    /// <summary>Command id that produced this state.</summary>
    public Guid? CommandId { get; init; }

    /// <summary>Correlation id supplied by the caller, macro, automation source, or future remote API.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Command source kind as a stable diagnostic string.</summary>
    public string? SourceKind { get; init; }

    /// <summary>Optional source id such as view, macro, slide, or remote client id.</summary>
    public string? SourceId { get; init; }

    /// <summary>Operator or automation actor that issued the command, when known.</summary>
    public string? Actor { get; init; }
}

/// <summary>
/// Transition intent attached to the layer whose payload changed.
/// </summary>
public sealed record LayerTransitionState
{
    /// <summary>Empty transition state.</summary>
    public static LayerTransitionState None { get; } = new();

    /// <summary>Transition type, such as cut, fade, or slide.</summary>
    public string? Type { get; init; }

    /// <summary>Transition duration requested by command or cue resolution.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Current transition lifecycle phase.</summary>
    public LayerTransitionPhase Phase { get; init; } = LayerTransitionPhase.None;

    /// <summary>Command provenance for the transition intent.</summary>
    public LiveCommandProvenance Provenance { get; init; } = new();
}

/// <summary>
/// Render or recovery error attached to a layer, screen, endpoint, or frame.
/// </summary>
public sealed record RenderErrorDescriptor
{
    /// <summary>Operator-facing error message.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Layer associated with the error, when applicable.</summary>
    public OutputLayerKind? LayerKind { get; init; }

    /// <summary>Screen associated with the error, when applicable.</summary>
    public string? ScreenId { get; init; }

    /// <summary>Endpoint associated with the error, when applicable.</summary>
    public string? EndpointId { get; init; }

    /// <summary>Command provenance associated with the error.</summary>
    public LiveCommandProvenance Provenance { get; init; } = new();
}

/// <summary>
/// Endpoint health projected into a resolved frame for host diagnostics.
/// </summary>
public sealed record EndpointRenderDiagnostics
{
    /// <summary>Endpoint id currently mapped to the screen.</summary>
    public string EndpointId { get; init; } = string.Empty;

    /// <summary>Endpoint kind, when the endpoint exists in the topology snapshot.</summary>
    public OutputEndpointKind? Kind { get; init; }

    /// <summary>Current endpoint health.</summary>
    public EndpointHealth Health { get; init; } = EndpointHealth.Unknown;

    /// <summary>Short health or recovery message.</summary>
    public string? Message { get; init; }
}

/// <summary>
/// Nominal render size in physical pixels.
/// </summary>
public sealed record PixelSize(int Width, int Height)
{
    /// <summary>Default 16:9 HD render size.</summary>
    public static PixelSize FullHd { get; } = new(1920, 1080);
}

/// <summary>
/// Endpoint-independent payload descriptor consumed by output hosts.
/// </summary>
public sealed record RenderPayloadDescriptor
{
    /// <summary>Stable payload id for diffing and diagnostics.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Kind of payload represented by this descriptor.</summary>
    public RenderPayloadKind Kind { get; init; } = RenderPayloadKind.None;

    /// <summary>Operator-facing payload name.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Optional document, cue, asset, or generated-content reference.</summary>
    public string? SourceReference { get; init; }

    /// <summary>Optional theme or style variant selected by the active route.</summary>
    public string? ThemeVariantId { get; init; }

    /// <summary>Layer-specific payload detail, such as a compiled slide scene or media cue settings.</summary>
    public RenderPayloadDetail? Detail { get; init; }
}

/// <summary>
/// Live state for one fixed output layer.
/// </summary>
public sealed record LayerState
{
    /// <summary>Layer identity.</summary>
    public OutputLayerKind Kind { get; init; }

    /// <summary>Active payload, if any.</summary>
    public RenderPayloadDescriptor? Payload { get; init; }

    /// <summary>Whether this layer is currently visible before screen-specific routing.</summary>
    public bool IsVisible { get; init; }

    /// <summary>Whether this layer has been intentionally suppressed.</summary>
    public bool IsSuppressed { get; init; }

    /// <summary>Whether the last mutation cleared this layer.</summary>
    public bool IsCleared { get; init; }

    /// <summary>Clear lifecycle state for recovery and host diagnostics.</summary>
    public LayerClearState ClearState { get; init; } = LayerClearState.None;

    /// <summary>Command id responsible for the current state.</summary>
    public Guid? SourceCommandId { get; init; }

    /// <summary>Full command provenance responsible for the current state.</summary>
    public LiveCommandProvenance Provenance { get; init; } = new();

    /// <summary>Transition intent for the latest layer mutation.</summary>
    public LayerTransitionState Transition { get; init; } = LayerTransitionState.None;

    /// <summary>Media/audio/live-video playback state reported for this layer, when available.</summary>
    public MediaPlaybackCoordinationSnapshot? PlayerState { get; init; }

    /// <summary>Structured render or recovery errors associated with this layer.</summary>
    public IReadOnlyList<RenderErrorDescriptor> RenderErrors { get; init; } = Array.Empty<RenderErrorDescriptor>();

    /// <summary>Human-readable diagnostics for recovery UI.</summary>
    public string? Diagnostics { get; init; }
}

/// <summary>
/// Resolved layer descriptor inside an audience render frame.
/// </summary>
public sealed record RenderLayerDescriptor
{
    /// <summary>Layer identity.</summary>
    public OutputLayerKind Kind { get; init; }

    /// <summary>Resolved payload for this layer.</summary>
    public RenderPayloadDescriptor Payload { get; init; } = new();

    /// <summary>Whether the descriptor is visible after Look routing and layer state are applied.</summary>
    public bool IsVisible { get; init; }

    /// <summary>Whether the layer is suppressed or unavailable.</summary>
    public bool IsSuppressed { get; init; }

    /// <summary>Clear lifecycle state resolved for this frame layer.</summary>
    public LayerClearState ClearState { get; init; } = LayerClearState.None;

    /// <summary>Command id responsible for this layer descriptor.</summary>
    public Guid? SourceCommandId { get; init; }

    /// <summary>Full command provenance responsible for this layer descriptor.</summary>
    public LiveCommandProvenance Provenance { get; init; } = new();

    /// <summary>Transition intent for this layer descriptor.</summary>
    public LayerTransitionState Transition { get; init; } = LayerTransitionState.None;

    /// <summary>Media/audio/live-video playback state reported for this layer, when available.</summary>
    public MediaPlaybackCoordinationSnapshot? PlayerState { get; init; }

    /// <summary>Structured render or recovery errors associated with this descriptor.</summary>
    public IReadOnlyList<RenderErrorDescriptor> RenderErrors { get; init; } = Array.Empty<RenderErrorDescriptor>();

    /// <summary>Diagnostics snapshot for this layer.</summary>
    public string? Diagnostics { get; init; }
}

/// <summary>
/// Immutable render frame for one audience screen.
/// </summary>
public sealed record AudienceRenderFrame
{
    /// <summary>Monotonic frame sequence.</summary>
    public long Sequence { get; init; }

    /// <summary>Logical audience screen id.</summary>
    public string ScreenId { get; init; } = string.Empty;

    /// <summary>Active Look preset id.</summary>
    public string LookPresetId { get; init; } = string.Empty;

    /// <summary>Command provenance associated with the latest resolved session mutation.</summary>
    public LiveCommandProvenance Provenance { get; init; } = new();

    /// <summary>Nominal render size.</summary>
    public PixelSize RenderSize { get; init; } = PixelSize.FullHd;

    /// <summary>Frame alpha mode.</summary>
    public RenderAlphaMode AlphaMode { get; init; }

    /// <summary>Resolved layers in backend stack order.</summary>
    public IReadOnlyList<RenderLayerDescriptor> Layers { get; init; } = Array.Empty<RenderLayerDescriptor>();

    /// <summary>Frame-level diagnostics snapshot.</summary>
    public RenderDiagnostics Diagnostics { get; init; } = new();
}

/// <summary>
/// Immutable render frame for one stage screen.
/// </summary>
public sealed record StageRenderFrame
{
    /// <summary>Monotonic frame sequence.</summary>
    public long Sequence { get; init; }

    /// <summary>Logical stage screen id.</summary>
    public string ScreenId { get; init; } = string.Empty;

    /// <summary>Active stage layout id for this screen.</summary>
    public string StageLayoutId { get; init; } = string.Empty;

    /// <summary>Last stage delivery mode associated with this screen.</summary>
    public StageAudienceCommandMode CommandMode { get; init; } = StageAudienceCommandMode.StageAndAudience;

    /// <summary>Command provenance associated with the latest resolved session mutation.</summary>
    public LiveCommandProvenance Provenance { get; init; } = new();

    /// <summary>Nominal render size.</summary>
    public PixelSize RenderSize { get; init; } = PixelSize.FullHd;

    /// <summary>Stage-only content payloads resolved independently from audience Looks.</summary>
    public IReadOnlyList<RenderPayloadDescriptor> Payloads { get; init; } = Array.Empty<RenderPayloadDescriptor>();

    /// <summary>Frame-level diagnostics snapshot.</summary>
    public RenderDiagnostics Diagnostics { get; init; } = new();
}

/// <summary>
/// Diagnostics attached to a resolved frame.
/// </summary>
public sealed record RenderDiagnostics
{
    /// <summary>Endpoint ids currently mapped to the screen.</summary>
    public IReadOnlyList<string> EndpointIds { get; init; } = Array.Empty<string>();

    /// <summary>Endpoint health details for each mapped endpoint.</summary>
    public IReadOnlyList<EndpointRenderDiagnostics> Endpoints { get; init; } = Array.Empty<EndpointRenderDiagnostics>();

    /// <summary>Structured render or recovery errors attached to this frame.</summary>
    public IReadOnlyList<RenderErrorDescriptor> RenderErrors { get; init; } = Array.Empty<RenderErrorDescriptor>();

    /// <summary>Optional render or recovery message.</summary>
    public string? Message { get; init; }
}

/// <summary>
/// Stores the latest immutable frames by logical screen id.
/// </summary>
public interface IRenderFrameStore
{
    /// <summary>Stores audience and stage frame snapshots.</summary>
    void Save(RenderFrameSet frames);

    /// <summary>Gets the latest audience frame for a screen.</summary>
    AudienceRenderFrame? GetAudienceFrame(string screenId);

    /// <summary>Gets the latest stage frame for a screen.</summary>
    StageRenderFrame? GetStageFrame(string screenId);
}

/// <summary>
/// Latest audience and stage frame snapshots produced by the backend engine.
/// </summary>
public sealed record RenderFrameSet
{
    /// <summary>Audience frames keyed by screen id.</summary>
    public IReadOnlyDictionary<string, AudienceRenderFrame> AudienceFrames { get; init; } =
        new Dictionary<string, AudienceRenderFrame>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Stage frames keyed by screen id.</summary>
    public IReadOnlyDictionary<string, StageRenderFrame> StageFrames { get; init; } =
        new Dictionary<string, StageRenderFrame>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// In-memory frame store for tests and initial application services.
/// </summary>
public sealed class InMemoryRenderFrameStore : IRenderFrameStore
{
    private readonly Dictionary<string, AudienceRenderFrame> _audienceFrames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, StageRenderFrame> _stageFrames = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public void Save(RenderFrameSet frames)
    {
        ArgumentNullException.ThrowIfNull(frames);

        _audienceFrames.Clear();
        _stageFrames.Clear();

        foreach ((string screenId, AudienceRenderFrame frame) in frames.AudienceFrames)
        {
            _audienceFrames[screenId] = frame;
        }

        foreach ((string screenId, StageRenderFrame frame) in frames.StageFrames)
        {
            _stageFrames[screenId] = frame;
        }
    }

    /// <inheritdoc />
    public AudienceRenderFrame? GetAudienceFrame(string screenId)
    {
        return _audienceFrames.TryGetValue(screenId, out AudienceRenderFrame? frame)
            ? frame
            : null;
    }

    /// <inheritdoc />
    public StageRenderFrame? GetStageFrame(string screenId)
    {
        return _stageFrames.TryGetValue(screenId, out StageRenderFrame? frame)
            ? frame
            : null;
    }
}