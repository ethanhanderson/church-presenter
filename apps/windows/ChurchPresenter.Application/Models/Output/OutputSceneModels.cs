using System.Collections.ObjectModel;

namespace ChurchPresenter.Models.Output;

/// <summary>
/// Fully resolved live output scene consumed by native WinUI output hosts.
/// Keeps presentation, media, and extracted web content explicit so hosts do not
/// have to reverse-engineer behavior from raw playback state.
/// </summary>
public sealed record OutputScene
{
    /// <summary>An empty scene representing no active output.</summary>
    public static readonly OutputScene Empty = new();

    /// <summary>The active presentation project.</summary>
    public PresentationProject? Project { get; init; }

    /// <summary>The current program slide id, preserved even when the slide fails to resolve.</summary>
    public string? ProgramSlideId { get; init; }

    /// <summary>Resolved presentation content for the scene.</summary>
    public PresentationScene Presentation { get; init; } = PresentationScene.Empty;

    /// <summary>Resolved persistent output media for the scene.</summary>
    public MediaScene Media { get; init; } = MediaScene.Empty;

    /// <summary>Convenience view of all web layers extracted from <see cref="Presentation"/>.</summary>
    public WebScene Web { get; init; } = WebScene.Empty;

    /// <summary>Resolved slide transition for presentation changes.</summary>
    public SlideTransition? Transition { get; init; }

    /// <summary>Resolved media-only transition for engine media slot changes.</summary>
    public SlideTransition? MediaTransition { get; init; }

    /// <summary>Whether the output is currently blacked out.</summary>
    public bool IsBlackout { get; init; }

    /// <summary>Whether the output is currently covered by the clear overlay.</summary>
    public bool IsClear { get; init; }

    /// <summary>Aspect-ratio override string for the output host.</summary>
    public string? OutputAspectRatioOverride { get; init; }

    /// <summary>Output scale mode for the output host.</summary>
    public string OutputScaleMode { get; init; } = "fit";
}

/// <summary>
/// Resolved presentation scene for a single slide surface, including ordered layer descriptors
/// and background metadata.
/// </summary>
public sealed record PresentationScene
{
    /// <summary>An empty presentation scene.</summary>
    public static readonly PresentationScene Empty = new();

    /// <summary>The resolved slide.</summary>
    public PresentationSlide? Slide { get; init; }

    /// <summary>Active build index for the resolved slide.</summary>
    public int BuildIndex { get; init; } = -1;

    /// <summary>Visible layer ids after build-step filtering.</summary>
    public IReadOnlyList<string>? VisibleLayerIds { get; init; }

    /// <summary>Resolved background descriptor.</summary>
    public PresentationBackgroundScene Background { get; init; } = PresentationBackgroundScene.Empty;

    /// <summary>Ordered presentation layers after visibility filtering.</summary>
    public IReadOnlyList<PresentationSceneLayer> Layers { get; init; } = Array.Empty<PresentationSceneLayer>();

    /// <summary>True when slide presentation content is suppressed.</summary>
    public bool Suppressed { get; init; }
}

/// <summary>Resolved scene for engine-owned media slots.</summary>
public sealed record MediaScene
{
    /// <summary>An empty media scene.</summary>
    public static readonly MediaScene Empty = new();

    /// <summary>The underlay media slot.</summary>
    public MediaSceneSlot Underlay { get; init; } = new("mediaUnderlay", null);

    /// <summary>The overlay media slot.</summary>
    public MediaSceneSlot Overlay { get; init; } = new("mediaOverlay", null);

    /// <summary>The audio-only media slot.</summary>
    public MediaSceneSlot Audio { get; init; } = new("audio", null);

    /// <summary>True when engine media output is suppressed.</summary>
    public bool Suppressed { get; init; }
}

/// <summary>Convenience grouping of extracted web layers from the presentation scene.</summary>
public sealed record WebScene
{
    /// <summary>An empty web scene.</summary>
    public static readonly WebScene Empty = new();

    /// <summary>Resolved web layers in slide order.</summary>
    public IReadOnlyList<WebSceneLayer> Layers { get; init; } = Array.Empty<WebSceneLayer>();
}

/// <summary>
/// Static snapshot scene used by thumbnails, editor previews, and image export.
/// This scene intentionally contains only capturable/static content descriptors.
/// </summary>
public sealed record SnapshotScene
{
    /// <summary>An empty snapshot scene.</summary>
    public static readonly SnapshotScene Empty = new();

    /// <summary>The presentation project that owns the slide.</summary>
    public PresentationProject? Project { get; init; }

    /// <summary>The resolved presentation scene.</summary>
    public PresentationScene Presentation { get; init; } = PresentationScene.Empty;

    /// <summary>Resolved static media slots for the snapshot surface.</summary>
    public MediaScene Media { get; init; } = MediaScene.Empty;

    /// <summary>Convenience grouping of extracted web layers from the presentation scene.</summary>
    public WebScene Web { get; init; } = WebScene.Empty;

    /// <summary>Whether the snapshot is currently blacked out.</summary>
    public bool IsBlackout { get; init; }

    /// <summary>Whether the snapshot is currently covered by the clear overlay.</summary>
    public bool IsClear { get; init; }

    /// <summary>Aspect-ratio override string for the snapshot surface.</summary>
    public string? OutputAspectRatioOverride { get; init; }

    /// <summary>Output scale mode for the snapshot surface.</summary>
    public string OutputScaleMode { get; init; } = "fit";
}

/// <summary>Resolved background descriptor for a presentation scene.</summary>
public sealed record PresentationBackgroundScene
{
    /// <summary>An empty background scene.</summary>
    public static readonly PresentationBackgroundScene Empty = new();

    /// <summary>The underlying background model.</summary>
    public SlideBackground? Background { get; init; }

    /// <summary>Resolved image/video background media when the background uses external media.</summary>
    public BackgroundMediaScene? Media { get; init; }
}

/// <summary>Ordered descriptor for one presentation layer.</summary>
public sealed record PresentationSceneLayer
{
    /// <summary>The slide layer id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>The underlying layer model.</summary>
    public SlideLayer Layer { get; init; } = null!;

    /// <summary>The resolved presentation-layer kind.</summary>
    public PresentationSceneLayerKind Kind { get; init; }

    /// <summary>True when the layer needs an external host such as WebView2 or MediaPlayerElement.</summary>
    public bool UsesExternalContent { get; init; }
}

/// <summary>Ordered descriptor for a slide web layer.</summary>
public sealed record WebSceneLayer
{
    /// <summary>The slide layer id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>The underlying web layer model.</summary>
    public WebLayer Layer { get; init; } = new();
}

/// <summary>Resolved engine media slot descriptor.</summary>
/// <param name="SlotName">Logical slot name such as <c>mediaUnderlay</c>.</param>
/// <param name="Media">Current media payload for the slot, or <c>null</c>.</param>
public sealed record MediaSceneSlot(string SlotName, OutputLayerMedia? Media);

/// <summary>Resolved background image/video descriptor.</summary>
public sealed record BackgroundMediaScene
{
    /// <summary>The background media id.</summary>
    public string MediaId { get; init; } = string.Empty;

    /// <summary>The background media type.</summary>
    public string MediaType { get; init; } = "image";

    /// <summary>How the media should fit inside the slide.</summary>
    public string? Fit { get; init; }

    /// <summary>Loop behavior for video backgrounds.</summary>
    public bool Loop { get; init; }

    /// <summary>Muted behavior for video backgrounds.</summary>
    public bool Muted { get; init; }

    /// <summary>Autoplay behavior for the background media.</summary>
    public bool Autoplay { get; init; } = true;

    /// <summary>Resolved opacity for the background media.</summary>
    public double Opacity { get; init; } = 1;
}

/// <summary>Kinds of slide layers a native scene host can render.</summary>
public enum PresentationSceneLayerKind
{
    /// <summary>A text layer.</summary>
    Text,

    /// <summary>A shape layer.</summary>
    Shape,

    /// <summary>A media layer.</summary>
    Media,

    /// <summary>A web layer.</summary>
    Web,

    /// <summary>A vector path layer.</summary>
    Vector,
}