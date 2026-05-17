namespace ChurchPresenter.Models.Media;

/// <summary>Source that produced a selection cursor change.</summary>
public enum SelectionSource
{
    /// <summary>Operator explicitly selected a slide via click or keyboard navigation.</summary>
    Operator,

    /// <summary>Selection was advanced by hold-to-seek navigation.</summary>
    Seek,

    /// <summary>Program output triggered selection sync (when not in user-override mode).</summary>
    Program,

    /// <summary>A slide hot-key was pressed.</summary>
    HotKey,
}

/// <summary>
/// Identifies a specific slide instance in the operator's selection model, including
/// arrangement-aware instance key for repeated slides.
/// </summary>
public sealed record SelectionCursor
{
    /// <summary>An empty cursor representing no selection.</summary>
    public static readonly SelectionCursor Empty = new();

    /// <summary>Absolute path of the presentation containing the selected slide.</summary>
    public string? PresentationPath { get; init; }

    /// <summary>The selected slide ID (base slide ID, shared across arrangement repetitions).</summary>
    public string? SlideId { get; init; }

    /// <summary>
    /// Stable deck-instance key disambiguating repeated slides in a named arrangement.
    /// Equals <see cref="SlideId"/> when no arrangement is active or the slide appears only once.
    /// </summary>
    public string? InstanceKey { get; init; }

    /// <summary>Which input produced this selection change.</summary>
    public SelectionSource Source { get; init; } = SelectionSource.Operator;

    /// <summary>True when this cursor points to a specific slide.</summary>
    public bool HasSelection => !string.IsNullOrWhiteSpace(SlideId);
}

/// <summary>
/// Immutable snapshot of the complete playback engine state at a point in time.
/// Consumers compare snapshots to detect relevant changes without inspecting each field of a live-mutating service.
/// </summary>
public sealed record PlaybackState
{
    // ── Session ───────────────────────────────────────────────────────────────

    /// <summary>Whether a presentation is currently loaded in the live session.</summary>
    public bool IsLive { get; init; }

    /// <summary>The active presentation document.</summary>
    public PresentationDocument? Presentation { get; init; }

    /// <summary>Absolute path of the active presentation file.</summary>
    public string? PresentationPath { get; init; }

    // ── Operator selection ────────────────────────────────────────────────────

    /// <summary>The operator's currently selected slide (not necessarily what's on the program output).</summary>
    public SelectionCursor OperatorCursor { get; init; } = SelectionCursor.Empty;

    /// <summary>
    /// When <c>true</c> the operator has explicitly chosen a slide and selection
    /// should not be overwritten by program output changes.
    /// </summary>
    public bool UserOverrideSelection { get; init; }

    // ── Program output ────────────────────────────────────────────────────────

    /// <summary>The slide currently showing in the program output.</summary>
    public string? CurrentSlideId { get; init; }

    /// <summary>
    /// Arrangement-aware instance key for the slide currently showing in the program output.
    /// Equals <see cref="CurrentSlideId"/> when no arrangement is active or the slide appears only once.
    /// </summary>
    public string? CurrentSlideInstanceKey { get; init; }

    /// <summary>Zero-based index of the current slide in the live document's slides list.</summary>
    public int CurrentSlideIndex { get; init; } = -1;

    /// <summary>Current build step index within the current slide (-1 = no build active).</summary>
    public int BuildIndex { get; init; } = -1;

    /// <summary>Whether the current slide has remaining build steps to advance through.</summary>
    public bool HasMoreBuilds { get; init; }

    /// <summary>Layer IDs that are currently visible, respecting build steps.</summary>
    public IReadOnlyList<string> VisibleLayerIds { get; init; } = Array.Empty<string>();

    /// <summary>Active media layers (underlay, overlay, audio) for the program output.</summary>
    public MediaLayersState MediaLayers { get; init; } = new();

    /// <summary>Presentation layer currently targeted by slide playback.</summary>
    public ChurchPresenter.Backend.Rendering.OutputLayerKind PresentationLayerKind { get; init; } =
        ChurchPresenter.Backend.Rendering.OutputLayerKind.Slide;

    // ── Operator controls ─────────────────────────────────────────────────────

    /// <summary>Blackout / dark-screen state.</summary>
    public bool IsBlackout { get; init; }

    /// <summary>Clear / white-screen state.</summary>
    public bool IsClear { get; init; }

    /// <summary>Which output groups are being suppressed (presentation or media).</summary>
    public SuppressState Suppress { get; init; } = new();

    /// <summary>Which output groups are currently mid-clear animation.</summary>
    public ClearingState IsClearing { get; init; } = new();

    /// <summary>True when a presentation clear can be undone.</summary>
    public bool CanUndoClearPresentation { get; init; }

    /// <summary>True when a media clear can be undone.</summary>
    public bool CanUndoClearMedia { get; init; }

    // ── Output destinations ───────────────────────────────────────────────────

    /// <summary>Whether the audience output is enabled.</summary>
    public bool IsAudienceEnabled { get; init; }

    /// <summary>Whether the stage output is enabled.</summary>
    public bool IsStageEnabled { get; init; }

    /// <summary>
    /// Global slide transition fallback from Show settings (used when slide and arrangement omit a transition).
    /// </summary>
    public SlideTransition? GlobalSlideFallback { get; init; }

    /// <summary>
    /// Global media transition from Show settings (used when only program media layers change).
    /// </summary>
    public SlideTransition? GlobalMediaFallback { get; init; }
}

/// <summary>
/// A fully resolved frame descriptor for one output surface (operator preview, audience, or stage).
/// Contains everything a render surface needs; consuming UI does not re-resolve transitions or media layers.
/// </summary>
public sealed record RenderFrame
{
    /// <summary>An empty frame representing no content (no presentation loaded).</summary>
    public static readonly RenderFrame Empty = new();

    /// <summary>The active presentation project providing slide data and theme settings.</summary>
    public PresentationProject? Project { get; init; }

    /// <summary>The resolved slide to display, or <c>null</c> when suppressed or unset.</summary>
    public PresentationSlide? Slide { get; init; }

    /// <summary>
    /// Program slide id from <see cref="PlaybackState.CurrentSlideId"/>. Kept when <see cref="Slide"/> fails to
    /// resolve so output diffing still tracks navigation.
    /// </summary>
    public string? ProgramSlideId { get; init; }

    /// <summary>Current build step from <see cref="PlaybackState.BuildIndex"/>.</summary>
    public int BuildIndex { get; init; } = -1;

    /// <summary>
    /// Layer IDs to render; <c>null</c> or empty means render all layers (operator preview without build steps).
    /// </summary>
    public IReadOnlyList<string>? VisibleLayerIds { get; init; }

    /// <summary>Media layers resolved for this surface and slide.</summary>
    public MediaLayersState MediaLayers { get; init; } = new();

    /// <summary>Pre-resolved transition for this surface, or <c>null</c> for a cut.</summary>
    public SlideTransition? Transition { get; init; }

    /// <summary>Global default for media-only crossfades on the output TransitionHost.</summary>
    public SlideTransition? MediaTransition { get; init; }

    /// <summary>Whether the presentation layer is suppressed.</summary>
    public bool SuppressPresentation { get; init; }

    /// <summary>Whether the media layer is suppressed.</summary>
    public bool SuppressMedia { get; init; }

    /// <summary>Whether the output is blacked out.</summary>
    public bool IsBlackout { get; init; }

    /// <summary>Whether the output is cleared to white/blank.</summary>
    public bool IsClear { get; init; }

    /// <summary>Aspect-ratio override string (e.g. "16:9") or <c>null</c> to use the display's native ratio.</summary>
    public string? OutputAspectRatioOverride { get; init; }

    /// <summary>Scaling mode string: "fit" (letterbox) or "fill" (crop).</summary>
    public string OutputScaleMode { get; init; } = "fit";
}

/// <summary>
/// Result of a single seek-navigation step, carrying whether a move occurred and the
/// delay to wait before the next step.
/// </summary>
public readonly record struct SlideSeekStepResult(bool Moved, TimeSpan Delay)
{
    /// <summary>A no-move result with zero delay.</summary>
    public static readonly SlideSeekStepResult None = new(false, TimeSpan.Zero);

    /// <summary>A successful move result with a given delay before the next step.</summary>
    public static SlideSeekStepResult FromDelay(TimeSpan delay) => new(true, delay);
}