using ChurchPresenter.Backend.Pipeline.Cues;
using ChurchPresenter.Backend.Rendering;

namespace ChurchPresenter.Backend.Pipeline.Routing;

/// <summary>
/// Pipeline layer slot tracking prepared cue metadata alongside backend layer state.
/// </summary>
public sealed record LayerSlot
{
    /// <summary>Layer identity.</summary>
    public OutputLayerKind Kind { get; init; }

    /// <summary>Prepared cue currently assigned to the slot, when any.</summary>
    public PreparedCueRecord? PreparedCue { get; init; }

    /// <summary>Backend layer state mirrored from the render session.</summary>
    public LayerState State { get; init; } = new() { Kind = OutputLayerKind.Slide };

    /// <summary>UTC timestamp when the slot last received a prepared cue.</summary>
    public DateTimeOffset EnterTimestamp { get; init; }

    /// <summary>Transition intent for the latest slot mutation.</summary>
    public LayerTransitionState Transition { get; init; } = LayerTransitionState.None;

    /// <summary>Whether a runtime clear override is suppressing this slot.</summary>
    public bool ClearOverride { get; init; }

    /// <summary>Prior payload retained for optional recovery.</summary>
    public RenderPayloadDescriptor? PriorPayload { get; init; }
}
