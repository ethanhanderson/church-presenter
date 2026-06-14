namespace ChurchPresenter.Backend.Pipeline.Cues;

/// <summary>
/// Identifies the kind of content referenced by a cue or cache invalidation event.
/// </summary>
public enum ContentRefKind
{
    Presentation,
    Slide,
    MediaAsset,
    MediaCue,
    AudioCue,
    Message,
    Prop,
    Mask,
    Theme,
}

/// <summary>
/// Stable content identity used by cue preparation and invalidation.
/// </summary>
public sealed record ContentRef
{
    /// <summary>Content category.</summary>
    public ContentRefKind Kind { get; init; }

    /// <summary>Primary content id or path.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Optional content version stamp when known.</summary>
    public string? Version { get; init; }
}
