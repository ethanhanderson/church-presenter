using ChurchPresenter.Backend.Output;
using ChurchPresenter.Backend.Overlays;
using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Backend.Stage;
using ChurchPresenter.Models.Output;

using BackendOutputLayerKind = ChurchPresenter.Backend.Rendering.OutputLayerKind;

namespace ChurchPresenter.Services.Pipeline;

/// <summary>
/// Query DTOs projected from the unified output pipeline live session snapshot.
/// </summary>
public sealed record LiveSessionQuerySnapshot
{
    /// <summary>An empty snapshot.</summary>
    public static readonly LiveSessionQuerySnapshot Empty = new();

    /// <summary>Current pipeline session version.</summary>
    public long Version { get; init; }

    /// <summary>Active Look id.</summary>
    public string ActiveLookId { get; init; } = string.Empty;

    /// <summary>Live presentation path from the slide layer payload, when any.</summary>
    public string? LivePresentationPath { get; init; }

    /// <summary>Live slide id from the slide layer payload, when any.</summary>
    public string? LiveSlideId { get; init; }

    /// <summary>Available Looks with active-state decoration.</summary>
    public IReadOnlyList<LiveLookQueryOption> Looks { get; init; } = Array.Empty<LiveLookQueryOption>();

    /// <summary>Pipeline layer slots keyed by layer kind.</summary>
    public IReadOnlyDictionary<BackendOutputLayerKind, LiveLayerSlotQuery> LayerSlots { get; init; } =
        new Dictionary<BackendOutputLayerKind, LiveLayerSlotQuery>();

    /// <summary>Current active backend layers.</summary>
    public IReadOnlyList<LiveLayerStateQuery> ActiveLayers { get; init; } = Array.Empty<LiveLayerStateQuery>();

    /// <summary>Configured clear groups from the active Look.</summary>
    public IReadOnlyList<LiveClearGroupQuery> ClearGroups { get; init; } = Array.Empty<LiveClearGroupQuery>();
}

/// <summary>
/// One pipeline layer slot projected for operator queries.
/// </summary>
public sealed record LiveLayerSlotQuery
{
    /// <summary>Layer identity.</summary>
    public BackendOutputLayerKind Kind { get; init; }

    /// <summary>Whether a runtime clear override is active.</summary>
    public bool ClearOverride { get; init; }

    /// <summary>Prepared cue content reference id, when any.</summary>
    public string? PreparedContentId { get; init; }

    /// <summary>Active payload id from backend layer state.</summary>
    public string? PayloadId { get; init; }

    /// <summary>UTC timestamp when the slot last received a prepared cue.</summary>
    public DateTimeOffset EnterTimestamp { get; init; }
}
