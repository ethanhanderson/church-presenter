using ChurchPresenter.Backend.Rendering;

namespace ChurchPresenter.Models.Media;

/// <summary>
/// Base record for prepared content cues that are ready to enter a routed output layer immediately.
/// </summary>
public abstract record PreparedCue
{
    /// <summary>The routed layer this cue targets.</summary>
    public OutputLayerKind LayerKind { get; init; }

    /// <summary>When the cue was prepared.</summary>
    public DateTimeOffset PreparedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Prepared slide cue for entering a presentation layer without resolving the document again at take-live time.
/// </summary>
public sealed record PreparedSlideCue : PreparedCue
{
    public PreparedSlideCue()
    {
        LayerKind = OutputLayerKind.Slide;
    }

    /// <summary>The owning presentation document.</summary>
    public PresentationDocument Presentation { get; init; } = null!;

    /// <summary>Canonical presentation path used by the cue cache and UI selection model.</summary>
    public string PresentationPath { get; init; } = string.Empty;

    /// <summary>The slide id to enter.</summary>
    public string SlideId { get; init; } = string.Empty;

    /// <summary>Arrangement-aware instance key for repeated slides when available.</summary>
    public string? InstanceKey { get; init; }

    /// <summary>Resolved slide index inside <see cref="Presentation"/>.</summary>
    public int SlideIndex { get; init; } = -1;

    /// <summary>Resolved DTO slide used for build visibility and cue composition.</summary>
    public SlideDto? SlideDocument { get; init; }

    /// <summary>Resolved typed project slide used by rendering and transition resolution.</summary>
    public PresentationSlide? Slide { get; init; }

    /// <summary>Compiled host-neutral scene for the slide at prepare time.</summary>
    public ChurchPresenter.Backend.Rendering.SlideScene? Scene { get; init; }

    /// <summary>Dependency diagnostics captured when the scene was prepared.</summary>
    public IReadOnlyList<ChurchPresenter.Backend.Rendering.SceneDependency> DependencyDiagnostics { get; init; } =
        Array.Empty<ChurchPresenter.Backend.Rendering.SceneDependency>();

    /// <summary>Slide-authored media cues prepared as layer updates for immediate application.</summary>
    public MediaLayersState MediaLayers { get; init; } = new();
}

/// <summary>
/// Prepared media cue for entering the global media layer without resolving the source path again at take time.
/// </summary>
public sealed record PreparedMediaCue : PreparedCue
{
    public PreparedMediaCue()
    {
        LayerKind = OutputLayerKind.Media;
    }

    /// <summary>The target slot for this prepared media layer.</summary>
    public string Target { get; init; } = "mediaUnderlay";

    /// <summary>Fully resolved media payload ready for assignment to the program media layer.</summary>
    public OutputLayerMedia Media { get; init; } = new();
}