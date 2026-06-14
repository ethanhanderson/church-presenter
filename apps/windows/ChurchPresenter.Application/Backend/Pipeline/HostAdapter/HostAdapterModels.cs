#nullable enable

using ChurchPresenter.Backend.Rendering;

namespace ChurchPresenter.Backend.Pipeline.HostAdapter;

/// <summary>
/// Stable identity for one persistent layer surface on one logical screen.
/// </summary>
public readonly record struct LayerSlotId(string ScreenId, OutputLayerKind LayerKind)
{
    /// <summary>Creates a layer slot id.</summary>
    public static LayerSlotId For(string screenId, OutputLayerKind layerKind) => new(screenId, layerKind);

    /// <inheritdoc />
    public override string ToString() => $"{ScreenId}:{LayerKind}";
}

/// <summary>
/// Host surface configuration for one logical screen.
/// </summary>
public sealed record ScreenConfiguration
{
    /// <summary>Logical screen id.</summary>
    public string ScreenId { get; init; } = string.Empty;

    /// <summary>Nominal render size for the screen.</summary>
    public PixelSize RenderSize { get; init; } = PixelSize.FullHd;

    /// <summary>Whether the screen is a stage screen rather than audience output.</summary>
    public bool IsStage { get; init; }
}

/// <summary>
/// Host health severity reported by native adapters.
/// </summary>
public enum HostHealthSeverity
{
    /// <summary>Healthy and ready.</summary>
    Healthy,

    /// <summary>Degraded but still operating.</summary>
    Degraded,

    /// <summary>Failed and requires recovery.</summary>
    Failed,

    /// <summary>Recovering from a prior failure.</summary>
    Recovering,
}

/// <summary>
/// Host health feedback raised by native adapters.
/// </summary>
public sealed record HostHealthEvent
{
    /// <summary>Optional layer slot associated with the event.</summary>
    public LayerSlotId? LayerSlot { get; init; }

    /// <summary>Health severity.</summary>
    public HostHealthSeverity Severity { get; init; } = HostHealthSeverity.Healthy;

    /// <summary>Operator-facing message.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Optional exception detail.</summary>
    public Exception? Exception { get; init; }

    /// <summary>Optional timing diagnostics in milliseconds.</summary>
    public IReadOnlyDictionary<string, double>? TimingMs { get; init; }
}

/// <summary>
/// Runtime-owned media player handle passed to host adapters.
/// </summary>
public readonly record struct PlayerHandle(Guid Id, LayerSlotId SlotId);

/// <summary>
/// Host apply timing snapshot for one frame apply.
/// </summary>
public sealed record HostApplyTimingRecord
{
    /// <summary>Total apply duration in milliseconds.</summary>
    public double ApplyTimeMs { get; init; }

    /// <summary>Per-layer apply durations in milliseconds.</summary>
    public IReadOnlyDictionary<string, double> LayerApplyMs { get; init; } =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Editor-specific host surface options.
/// </summary>
public sealed record EditorHostOptions
{
    /// <summary>Whether selection adorners should be visible.</summary>
    public bool ShowAdorners { get; init; } = true;
}
