using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Models.Documents;

namespace ChurchPresenter.Backend.Pipeline.Cues;

/// <summary>
/// Immutable prepared cue ready for layer routing and frame composition.
/// </summary>
public sealed record PreparedCueRecord
{
    /// <summary>Target output layer.</summary>
    public OutputLayerKind Layer { get; init; }

    /// <summary>Referenced content identity.</summary>
    public ContentRef ContentRef { get; init; } = new() { Kind = ContentRefKind.Slide, Id = string.Empty };

    /// <summary>Version stamp for cache and host diffing.</summary>
    public ResourceStamp ResourceStamp { get; init; } = new();

    /// <summary>Resolved render payload for the target layer.</summary>
    public RenderPayloadDescriptor Payload { get; init; } = new();

    /// <summary>Most-specific cue or presentation/asset transition resolved during preparation.</summary>
    public SlideTransition? Transition { get; init; }

    /// <summary>Hot resources retained while this cue is live or cached.</summary>
    public IReadOnlyList<IResourceHandle> RetainedResources { get; init; } = Array.Empty<IResourceHandle>();

    /// <summary>Estimated memory cost in bytes.</summary>
    public int SizeCostBytes { get; init; }

    /// <summary>UTC timestamp when preparation completed.</summary>
    public DateTimeOffset PreparedAt { get; init; } = DateTimeOffset.UtcNow;
}
