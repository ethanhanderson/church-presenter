using ChurchPresenter.Backend.Rendering;

namespace ChurchPresenter.Backend.Pipeline.Cues;

/// <summary>
/// High-level cue category requested by a command source.
/// </summary>
public enum CueKind
{
    Slide,
    Media,
    Audio,
    Announcement,
    Message,
    Prop,
    Mask,
    LiveVideo,
    Overlay,
}

/// <summary>
/// Normalized cue request entering the preparation stage.
/// </summary>
public sealed record CueRequest
{
    /// <summary>Cue category.</summary>
    public CueKind CueKind { get; init; }

    /// <summary>Target output layer.</summary>
    public OutputLayerKind Layer { get; init; }

    /// <summary>Referenced content identity.</summary>
    public ContentRef ContentRef { get; init; } = new() { Kind = ContentRefKind.Slide, Id = string.Empty };

    /// <summary>Hash of active routing context used while preparing the cue.</summary>
    public string? RoutingHash { get; init; }

    /// <summary>Operator or automation overrides applied during preparation.</summary>
    public IReadOnlyDictionary<string, string>? Overrides { get; init; }
}
