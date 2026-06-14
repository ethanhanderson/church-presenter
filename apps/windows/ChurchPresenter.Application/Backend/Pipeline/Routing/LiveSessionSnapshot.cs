using ChurchPresenter.Backend.Output;
using ChurchPresenter.Backend.Rendering;

namespace ChurchPresenter.Backend.Pipeline.Routing;

/// <summary>
/// Pipeline live session snapshot wrapping backend render state and per-layer slots.
/// </summary>
public sealed record LiveSessionSnapshot
{
    /// <summary>Underlying backend render session state.</summary>
    public LiveRenderSessionState SessionState { get; init; } = new();

    /// <summary>Prepared cue slots keyed by layer kind.</summary>
    public IReadOnlyDictionary<OutputLayerKind, LayerSlot> LayerSlots { get; init; } =
        CreateEmptyLayerSlots();

    /// <summary>Monotonic pipeline version.</summary>
    public long Version { get; init; }

    /// <summary>Active audience Look id.</summary>
    public string ActiveLookId { get; init; } = "default";

    /// <summary>Creates an empty snapshot with default layer slots.</summary>
    public static LiveSessionSnapshot CreateEmpty()
    {
        return new LiveSessionSnapshot
        {
            SessionState = new LiveRenderSessionState
            {
                Layers = LiveRenderSessionState.CreateEmptyLayers(),
            },
            LayerSlots = CreateEmptyLayerSlots(),
            Version = 0,
            ActiveLookId = "default",
        };
    }

    private static IReadOnlyDictionary<OutputLayerKind, LayerSlot> CreateEmptyLayerSlots()
    {
        return Enum.GetValues<OutputLayerKind>()
            .ToDictionary(
                kind => kind,
                kind => new LayerSlot { Kind = kind, State = new LayerState { Kind = kind } },
                EqualityComparer<OutputLayerKind>.Default);
    }
}
