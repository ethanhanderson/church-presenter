using ChurchPresenter.Backend.Output;
using ChurchPresenter.Backend.Overlays;
using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Backend.Stage;

namespace ChurchPresenter.Backend.Commands;

/// <summary>
/// Live command categories accepted by the backend executor.
/// </summary>
public enum LiveCommandKind
{
    /// <summary>Set or replace a layer payload.</summary>
    SetLayerPayload,

    /// <summary>Clear one or more layers.</summary>
    Clear,

    /// <summary>Activate a different audience Look.</summary>
    SetLook,

    /// <summary>Assign a stage layout to a stage screen.</summary>
    SetStageLayout,

    /// <summary>Set or replace generated overlay state.</summary>
    SetOverlayState,

    /// <summary>Set or replace timer state.</summary>
    SetTimerState,

    /// <summary>Set or replace capture-session state.</summary>
    SetCaptureSessionState,
}

/// <summary>
/// Source category for live commands.
/// </summary>
public enum LiveCommandSourceKind
{
    /// <summary>Local operator UI source.</summary>
    Operator,

    /// <summary>Slide action source.</summary>
    SlideAction,

    /// <summary>Macro source.</summary>
    Macro,

    /// <summary>Timer or playback marker source.</summary>
    Automation,

    /// <summary>Future remote/control source.</summary>
    Remote,
}

/// <summary>
/// Target category for a live command.
/// </summary>
public enum LiveCommandTargetKind
{
    /// <summary>Global live session target.</summary>
    Global,

    /// <summary>Specific logical screen target.</summary>
    Screen,

    /// <summary>Specific output layer target.</summary>
    Layer,

    /// <summary>Specific endpoint target.</summary>
    Endpoint,
}

/// <summary>
/// Normalized action categories applied to live state.
/// </summary>
public enum LiveActionKind
{
    /// <summary>Set or replace a layer payload.</summary>
    SetLayerPayload,

    /// <summary>Clear layers.</summary>
    ClearLayers,

    /// <summary>Set the active audience Look.</summary>
    SetLook,

    /// <summary>Set a stage layout for a stage screen.</summary>
    SetStageLayout,

    /// <summary>Set or replace generated overlay state.</summary>
    SetOverlayState,

    /// <summary>Set or replace timer state.</summary>
    SetTimerState,

    /// <summary>Set or replace capture-session state.</summary>
    SetCaptureSessionState,
}

/// <summary>
/// Metadata describing who or what issued a command.
/// </summary>
public sealed record LiveCommandSource
{
    /// <summary>Source kind.</summary>
    public LiveCommandSourceKind Kind { get; init; }

    /// <summary>Optional source id such as view, macro, slide, or remote client id.</summary>
    public string? Id { get; init; }

    /// <summary>Operator or user identity for audit-ready commands.</summary>
    public string? Actor { get; init; }
}

/// <summary>
/// Target descriptor for command routing and audit.
/// </summary>
public sealed record LiveCommandTarget
{
    /// <summary>Target kind.</summary>
    public LiveCommandTargetKind Kind { get; init; } = LiveCommandTargetKind.Global;

    /// <summary>Optional target id such as screen or endpoint id.</summary>
    public string? Id { get; init; }

    /// <summary>Optional target layer.</summary>
    public OutputLayerKind? LayerKind { get; init; }

    /// <summary>Creates a layer target.</summary>
    public static LiveCommandTarget Layer(OutputLayerKind layerKind)
    {
        return new LiveCommandTarget
        {
            Kind = LiveCommandTargetKind.Layer,
            LayerKind = layerKind,
        };
    }

    /// <summary>Creates a screen target.</summary>
    public static LiveCommandTarget Screen(string screenId)
    {
        return new LiveCommandTarget
        {
            Kind = LiveCommandTargetKind.Screen,
            Id = screenId,
        };
    }
}

/// <summary>
/// Public/internal boundary for a requested live output operation.
/// </summary>
public sealed record LiveCommand
{
    /// <summary>Stable command id.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Command kind.</summary>
    public LiveCommandKind Kind { get; init; }

    /// <summary>Command source metadata.</summary>
    public LiveCommandSource Source { get; init; } = new();

    /// <summary>Command target metadata.</summary>
    public LiveCommandTarget Target { get; init; } = new();

    /// <summary>Payload used by layer mutation commands.</summary>
    public RenderPayloadDescriptor? Payload { get; init; }

    /// <summary>Transition intent for the target layer when the command changes layer content.</summary>
    public LayerTransitionState? Transition { get; init; }

    /// <summary>Look used by Look activation commands.</summary>
    public LookPreset? Look { get; init; }

    /// <summary>Clear payload used by clear commands.</summary>
    public ClearCommand? Clear { get; init; }

    /// <summary>Stage layout id used by stage-layout commands.</summary>
    public string? StageLayoutId { get; init; }

    /// <summary>Stage delivery mode for stage-related commands.</summary>
    public StageAudienceCommandMode DeliveryMode { get; init; } = StageAudienceCommandMode.StageAndAudience;

    /// <summary>Generated overlay state used by overlay commands.</summary>
    public OverlayContentState? Overlay { get; init; }

    /// <summary>Timer snapshot used by generated timer commands.</summary>
    public TimerSnapshot? Timer { get; init; }

    /// <summary>Capture session state used by capture commands.</summary>
    public CaptureSessionState? CaptureSession { get; init; }

    /// <summary>Correlation id reserved for diagnostics and future remote APIs.</summary>
    public string? CorrelationId { get; init; }
}

/// <summary>
/// Normalized state mutation derived from a live command.
/// </summary>
public sealed record LiveAction
{
    /// <summary>Action kind.</summary>
    public LiveActionKind Kind { get; init; }

    /// <summary>Action target.</summary>
    public LiveCommandTarget Target { get; init; } = new();

    /// <summary>Command id that produced this action.</summary>
    public Guid? SourceCommandId { get; init; }

    /// <summary>Correlation id copied from the source command for diagnostics.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Command source metadata copied from the source command.</summary>
    public LiveCommandSource Source { get; init; } = new();

    /// <summary>Payload for layer mutation actions.</summary>
    public RenderPayloadDescriptor? Payload { get; init; }

    /// <summary>Transition intent for the target layer when the action changes layer content.</summary>
    public LayerTransitionState? Transition { get; init; }

    /// <summary>Look for Look activation actions.</summary>
    public LookPreset? Look { get; init; }

    /// <summary>Clear details for layer clear actions.</summary>
    public ClearCommand? Clear { get; init; }

    /// <summary>Stage layout id for stage-layout actions.</summary>
    public string? StageLayoutId { get; init; }

    /// <summary>Stage delivery mode associated with the action.</summary>
    public StageAudienceCommandMode DeliveryMode { get; init; } = StageAudienceCommandMode.StageAndAudience;

    /// <summary>Generated overlay state associated with the action.</summary>
    public OverlayContentState? Overlay { get; init; }

    /// <summary>Timer snapshot associated with the action.</summary>
    public TimerSnapshot? Timer { get; init; }

    /// <summary>Capture session state associated with the action.</summary>
    public CaptureSessionState? CaptureSession { get; init; }
}

/// <summary>
/// Atomic set of actions derived from one live command or macro expansion.
/// </summary>
public sealed record ActionBatch
{
    /// <summary>Batch id.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Source command id.</summary>
    public Guid SourceCommandId { get; init; }

    /// <summary>Correlation id associated with this batch, when the caller supplied one.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Command or macro source metadata.</summary>
    public LiveCommandSource Source { get; init; } = new();

    /// <summary>Optional macro id when the batch comes from macro expansion.</summary>
    public string? MacroId { get; init; }

    /// <summary>Normalized actions.</summary>
    public IReadOnlyList<LiveAction> Actions { get; init; } = Array.Empty<LiveAction>();
}

/// <summary>
/// Result produced by executing a command or action batch.
/// </summary>
public sealed record ActionResult
{
    /// <summary>Whether all actions were applied.</summary>
    public bool Succeeded { get; init; }

    /// <summary>Expanded action batch.</summary>
    public ActionBatch Batch { get; init; } = new();

    /// <summary>Updated live session state after applying the batch.</summary>
    public LiveRenderSessionState State { get; init; } = new();

    /// <summary>Frames produced after mutation.</summary>
    public RenderFrameSet Frames { get; init; } = new();

    /// <summary>Diagnostics emitted during expansion or application.</summary>
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
}