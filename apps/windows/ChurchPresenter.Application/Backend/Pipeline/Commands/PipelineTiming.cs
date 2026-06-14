using ChurchPresenter.Backend.Pipeline.Cues;

namespace ChurchPresenter.Backend.Pipeline.Commands;

/// <summary>
/// Per-stage timing diagnostics emitted by the unified output pipeline.
/// </summary>
public sealed record PipelineTiming
{
    /// <summary>Time spent preparing the cue in milliseconds.</summary>
    public double PrepareTimeMs { get; init; }

    /// <summary>Time spent routing the cue in milliseconds.</summary>
    public double RouteTimeMs { get; init; }

    /// <summary>Time spent composing frames in milliseconds.</summary>
    public double ComposeTimeMs { get; init; }

    /// <summary>Time spent applying frames to host adapters in milliseconds.</summary>
    public double ApplyTimeMs { get; init; }

    /// <summary>Time until media reported first-frame readiness in milliseconds.</summary>
    public double MediaReadyTimeMs { get; init; }

    /// <summary>Observed loop gap in milliseconds for media slots.</summary>
    public double LoopGapMs { get; init; }

    /// <summary>Cache tier that satisfied preparation.</summary>
    public CacheHitTier CacheHitTier { get; init; } = CacheHitTier.Miss;
}
