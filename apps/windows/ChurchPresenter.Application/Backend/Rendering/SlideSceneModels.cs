
namespace ChurchPresenter.Backend.Rendering;

/// <summary>
/// Describes the host intent for a compiled slide scene.
/// </summary>
public enum RenderIntent
{
    /// <summary>A lightweight still thumbnail.</summary>
    Thumbnail,

    /// <summary>An editable vector canvas surface.</summary>
    Editor,

    /// <summary>An operator preview surface.</summary>
    Preview,

    /// <summary>An audience output frame.</summary>
    AudienceOutput,

    /// <summary>A stage preview or confidence display source.</summary>
    StagePreview,

    /// <summary>A static or motion export/capture render.</summary>
    Export,

    /// <summary>A benchmark-only render pass.</summary>
    Benchmark,
}

/// <summary>
/// Host-neutral slide scene node categories.
/// </summary>
public enum SlideSceneNodeKind
{
    /// <summary>Static or tokenized text.</summary>
    Text,

    /// <summary>Basic shape or custom shape node.</summary>
    Shape,

    /// <summary>Image, video, or audio-backed media node.</summary>
    Media,

    /// <summary>Live video input node.</summary>
    LiveVideo,

    /// <summary>Web or external content node.</summary>
    Web,

    /// <summary>Vector geometry node.</summary>
    Vector,

    /// <summary>Nested grouping node.</summary>
    Group,
}

/// <summary>
/// Host-neutral scene dependency categories.
/// </summary>
public enum SceneDependencyKind
{
    /// <summary>Media dependency.</summary>
    Media,

    /// <summary>Font dependency.</summary>
    Font,

    /// <summary>Runtime token dependency.</summary>
    Token,

    /// <summary>Live device dependency.</summary>
    Device,

    /// <summary>Presentation bundle dependency.</summary>
    Presentation,

    /// <summary>Theme dependency.</summary>
    Theme,
}

/// <summary>
/// Describes a host-neutral slide scene request.
/// </summary>
public sealed record SceneCompileRequest
{
    /// <summary>Presentation project that owns the slide.</summary>
    public PresentationProject? Project { get; init; }

    /// <summary>Slide to compile.</summary>
    public PresentationSlide? Slide { get; init; }

    /// <summary>Optional theme slide selected by presentation, slide, or Look variant.</summary>
    public ThemeTemplateSlide? ThemeSlide { get; init; }

    /// <summary>Optional arrangement instance key for repeated slide/group occurrences.</summary>
    public string? ArrangementInstanceKey { get; init; }

    /// <summary>Optional theme variant selected by an audience Look.</summary>
    public string? ThemeVariantId { get; init; }

    /// <summary>Current object build index; negative values mean all initially visible nodes are included.</summary>
    public int BuildIndex { get; init; } = -1;

    /// <summary>Optional set of visible layer ids supplied by current playback state.</summary>
    public IReadOnlySet<string>? VisibleLayerIds { get; init; }

    /// <summary>Host intent for the compiled scene.</summary>
    public RenderIntent Intent { get; init; } = RenderIntent.Preview;

    /// <summary>Runtime token values available to dynamic text nodes.</summary>
    public IReadOnlyDictionary<string, string> RuntimeTokens { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Known dependency stamps keyed by dependency id.</summary>
    public IReadOnlyDictionary<string, ContentResourceStamp> DependencyStamps { get; init; } =
        new Dictionary<string, ContentResourceStamp>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Result of compiling a host-neutral slide scene.
/// </summary>
public sealed record SceneCompileResult
{
    /// <summary>Compiled scene snapshot.</summary>
    public SlideScene Scene { get; init; } = SlideScene.Empty;

    /// <summary>Compile diagnostics.</summary>
    public SceneCompileDiagnostics Diagnostics { get; init; } = new();
}

/// <summary>
/// Immutable host-neutral slide scene snapshot.
/// </summary>
public sealed record SlideScene
{
    /// <summary>Empty scene instance.</summary>
    public static SlideScene Empty { get; } = new()
    {
        Id = "scene:empty",
        Version = "empty",
        RenderSize = PixelSize.FullHd,
        Background = SceneBackground.OpaqueBlack,
        Nodes = Array.Empty<SlideSceneNode>(),
    };

    /// <summary>Stable scene id for diffing and diagnostics.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Stable version key derived from content and compile inputs.</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>Presentation id associated with this scene.</summary>
    public string? PresentationId { get; init; }

    /// <summary>Presentation source path associated with this scene.</summary>
    public string? PresentationPath { get; init; }

    /// <summary>Slide id associated with this scene.</summary>
    public string? SlideId { get; init; }

    /// <summary>Arrangement instance key for repeated slide instances.</summary>
    public string? ArrangementInstanceKey { get; init; }

    /// <summary>Render size in physical pixels.</summary>
    public PixelSize RenderSize { get; init; } = PixelSize.FullHd;

    /// <summary>Background descriptor for the scene surface.</summary>
    public SceneBackground Background { get; init; } = SceneBackground.OpaqueBlack;

    /// <summary>Ordered scene nodes in z-order.</summary>
    public IReadOnlyList<SlideSceneNode> Nodes { get; init; } = Array.Empty<SlideSceneNode>();

    /// <summary>Dependencies required by this scene.</summary>
    public IReadOnlyList<SceneDependency> Dependencies { get; init; } = Array.Empty<SceneDependency>();

    /// <summary>Compile diagnostics attached to the scene.</summary>
    public SceneCompileDiagnostics Diagnostics { get; init; } = new();
}

/// <summary>
/// Scene background descriptor.
/// </summary>
public sealed record SceneBackground
{
    /// <summary>Default opaque black background.</summary>
    public static SceneBackground OpaqueBlack { get; } = new() { Color = "#000000", AlphaMode = RenderAlphaMode.Opaque };

    /// <summary>Background color, when available.</summary>
    public string? Color { get; init; }

    /// <summary>Background media asset id, when available.</summary>
    public string? MediaId { get; init; }

    /// <summary>Background media type, when available.</summary>
    public string? MediaType { get; init; }

    /// <summary>Fit/crop/stretch policy.</summary>
    public string? Fit { get; init; }

    /// <summary>Background opacity.</summary>
    public double Opacity { get; init; } = 1;

    /// <summary>Frame alpha mode implied by the background.</summary>
    public RenderAlphaMode AlphaMode { get; init; } = RenderAlphaMode.Opaque;
}

/// <summary>
/// Base host-neutral scene node.
/// </summary>
public abstract record SlideSceneNode
{
    /// <summary>Stable node id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Node display name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Node category.</summary>
    public abstract SlideSceneNodeKind Kind { get; }

    /// <summary>Source layer or theme reference.</summary>
    public string? SourceReference { get; init; }

    /// <summary>Zero-based z-order.</summary>
    public int ZOrder { get; init; }

    /// <summary>Node bounds and transforms.</summary>
    public SceneNodeTransform Transform { get; init; } = new();

    /// <summary>Whether the node is visible for the compiled build state.</summary>
    public bool IsVisible { get; init; } = true;

    /// <summary>Whether the node is locked in editor surfaces.</summary>
    public bool IsLocked { get; init; }

    /// <summary>Optional blend mode.</summary>
    public string? BlendMode { get; init; }

    /// <summary>Node-level diagnostics.</summary>
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Node transform and bounds in scene coordinates.
/// </summary>
public sealed record SceneNodeTransform
{
    /// <summary>Left coordinate.</summary>
    public double X { get; init; }

    /// <summary>Top coordinate.</summary>
    public double Y { get; init; }

    /// <summary>Node width.</summary>
    public double Width { get; init; }

    /// <summary>Node height.</summary>
    public double Height { get; init; }

    /// <summary>Rotation in degrees.</summary>
    public double Rotation { get; init; }

    /// <summary>Opacity from 0 to 1.</summary>
    public double Opacity { get; init; } = 1;

    /// <summary>Whether the node is flipped horizontally.</summary>
    public bool FlipX { get; init; }

    /// <summary>Whether the node is flipped vertically.</summary>
    public bool FlipY { get; init; }

    /// <summary>Whether content is clipped to bounds.</summary>
    public bool ClipContent { get; init; }

    /// <summary>Optional uniform corner radius.</summary>
    public double? CornerRadius { get; init; }
}

/// <summary>
/// Host-neutral fill descriptor.
/// </summary>
public sealed record SceneFill
{
    /// <summary>Fill id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Fill color.</summary>
    public string Color { get; init; } = "#FFFFFF";

    /// <summary>Fill opacity.</summary>
    public double Opacity { get; init; } = 1;
}

/// <summary>
/// Host-neutral stroke descriptor.
/// </summary>
public sealed record SceneStroke
{
    /// <summary>Stroke id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Stroke color.</summary>
    public string Color { get; init; } = "#000000";

    /// <summary>Stroke opacity.</summary>
    public double Opacity { get; init; } = 1;

    /// <summary>Stroke width.</summary>
    public double Width { get; init; } = 1;
}

/// <summary>
/// Text scene node.
/// </summary>
public sealed record TextSceneNode : SlideSceneNode
{
    /// <inheritdoc />
    public override SlideSceneNodeKind Kind => SlideSceneNodeKind.Text;

    /// <summary>Resolved text content.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Font family.</summary>
    public string FontFamily { get; init; } = "Segoe UI";

    /// <summary>Font size.</summary>
    public double FontSize { get; init; } = 72;

    /// <summary>Font weight.</summary>
    public int FontWeight { get; init; } = 700;

    /// <summary>Whether the text is italic.</summary>
    public bool IsItalic { get; init; }

    /// <summary>Horizontal alignment.</summary>
    public string Alignment { get; init; } = "center";

    /// <summary>Vertical alignment.</summary>
    public string VerticalAlignment { get; init; } = "middle";

    /// <summary>Optional line-height multiplier.</summary>
    public double? LineHeight { get; init; }

    /// <summary>Letter spacing.</summary>
    public double LetterSpacing { get; init; }

    /// <summary>Text color.</summary>
    public string Color { get; init; } = "#FFFFFF";

    /// <summary>Text padding.</summary>
    public double Padding { get; init; }

    /// <summary>Token keys resolved inside this text.</summary>
    public IReadOnlyList<string> TokenKeys { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Shape scene node.
/// </summary>
public sealed record ShapeSceneNode : SlideSceneNode
{
    /// <inheritdoc />
    public override SlideSceneNodeKind Kind => SlideSceneNodeKind.Shape;

    /// <summary>Shape type.</summary>
    public string ShapeType { get; init; } = "rectangle";

    /// <summary>Fills in paint order.</summary>
    public IReadOnlyList<SceneFill> Fills { get; init; } = Array.Empty<SceneFill>();

    /// <summary>Strokes in paint order.</summary>
    public IReadOnlyList<SceneStroke> Strokes { get; init; } = Array.Empty<SceneStroke>();
}

/// <summary>
/// Media scene node.
/// </summary>
public sealed record MediaSceneNode : SlideSceneNode
{
    /// <inheritdoc />
    public override SlideSceneNodeKind Kind => SlideSceneNodeKind.Media;

    /// <summary>Media asset id.</summary>
    public string MediaId { get; init; } = string.Empty;

    /// <summary>Media type.</summary>
    public string MediaType { get; init; } = "image";

    /// <summary>Fit/crop/stretch policy.</summary>
    public string Fit { get; init; } = "contain";

    /// <summary>Whether playback loops.</summary>
    public bool Loop { get; init; }

    /// <summary>Whether playback is muted.</summary>
    public bool Muted { get; init; }

    /// <summary>Whether playback starts automatically.</summary>
    public bool Autoplay { get; init; }
}

/// <summary>
/// Live video scene node.
/// </summary>
public sealed record LiveVideoSceneNode : SlideSceneNode
{
    /// <inheritdoc />
    public override SlideSceneNodeKind Kind => SlideSceneNodeKind.LiveVideo;

    /// <summary>Live video source id.</summary>
    public string SourceId { get; init; } = string.Empty;

    /// <summary>Fit/crop/stretch policy.</summary>
    public string Fit { get; init; } = "cover";
}

/// <summary>
/// Web scene node.
/// </summary>
public sealed record WebSceneNode : SlideSceneNode
{
    /// <inheritdoc />
    public override SlideSceneNodeKind Kind => SlideSceneNodeKind.Web;

    /// <summary>Web URL.</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>Zoom factor.</summary>
    public double Zoom { get; init; } = 1;

    /// <summary>Whether the node is interactive in output mode.</summary>
    public bool Interactive { get; init; }

    /// <summary>Optional refresh interval in seconds.</summary>
    public int? RefreshInterval { get; init; }
}

/// <summary>
/// Vector scene node.
/// </summary>
public sealed record VectorSceneNode : SlideSceneNode
{
    /// <inheritdoc />
    public override SlideSceneNodeKind Kind => SlideSceneNodeKind.Vector;

    /// <summary>Path data.</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>Optional view box.</summary>
    public string? ViewBox { get; init; }

    /// <summary>Optional fill rule.</summary>
    public string? FillRule { get; init; }

    /// <summary>Fills in paint order.</summary>
    public IReadOnlyList<SceneFill> Fills { get; init; } = Array.Empty<SceneFill>();

    /// <summary>Strokes in paint order.</summary>
    public IReadOnlyList<SceneStroke> Strokes { get; init; } = Array.Empty<SceneStroke>();
}

/// <summary>
/// Group scene node.
/// </summary>
public sealed record GroupSceneNode : SlideSceneNode
{
    /// <inheritdoc />
    public override SlideSceneNodeKind Kind => SlideSceneNodeKind.Group;

    /// <summary>Child nodes.</summary>
    public IReadOnlyList<SlideSceneNode> Children { get; init; } = Array.Empty<SlideSceneNode>();
}

/// <summary>
/// Scene dependency descriptor.
/// </summary>
public sealed record SceneDependency
{
    /// <summary>Dependency kind.</summary>
    public SceneDependencyKind Kind { get; init; }

    /// <summary>Dependency id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Source node id, when applicable.</summary>
    public string? NodeId { get; init; }

    /// <summary>Whether the dependency was resolved by compile-time services.</summary>
    public bool IsResolved { get; init; } = true;

    /// <summary>Diagnostic message for missing or unhealthy dependencies.</summary>
    public string? Message { get; init; }

    /// <summary>Resource stamp captured for the dependency at compile time.</summary>
    public ContentResourceStamp? Stamp { get; init; }

    /// <summary>Classified dependency failure, when known.</summary>
    public ContentAccessFailureKind? FailureKind { get; init; }
}

/// <summary>
/// Compile diagnostics and metrics for a scene.
/// </summary>
public sealed record SceneCompileDiagnostics
{
    /// <summary>Compile duration.</summary>
    public TimeSpan CompileDuration { get; init; }

    /// <summary>Total node count.</summary>
    public int NodeCount { get; init; }

    /// <summary>Total visible node count.</summary>
    public int VisibleNodeCount { get; init; }

    /// <summary>Total dependency count.</summary>
    public int DependencyCount { get; init; }

    /// <summary>Warnings surfaced during compilation.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>Errors surfaced during compilation.</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    /// <summary>Performance metrics emitted by the compiler.</summary>
    public ScenePerformanceMetrics Performance { get; init; } = new();
}

/// <summary>
/// Performance metrics emitted by scene compilers and host adapters.
/// </summary>
public sealed record ScenePerformanceMetrics
{
    /// <summary>Elapsed compile time.</summary>
    public TimeSpan CompileTime { get; init; }

    /// <summary>Elapsed host apply time.</summary>
    public TimeSpan HostApplyTime { get; init; }

    /// <summary>Elapsed frame resolution time.</summary>
    public TimeSpan FrameResolveTime { get; init; }

    /// <summary>Approximate allocated bytes for the measured operation.</summary>
    public long AllocatedBytes { get; init; }

    /// <summary>Scene node count.</summary>
    public int NodeCount { get; init; }

    /// <summary>Rendered visible node count.</summary>
    public int VisibleNodeCount { get; init; }

    /// <summary>Media node count.</summary>
    public int MediaNodeCount { get; init; }
}

/// <summary>
/// Base payload detail record for layer-specific render payloads.
/// </summary>
public abstract record RenderPayloadDetail;

/// <summary>
/// Presentation or announcement scene payload detail.
/// </summary>
public sealed record PresentationRenderPayload : RenderPayloadDetail
{
    /// <summary>Source presentation id.</summary>
    public string? PresentationId { get; init; }

    /// <summary>Source presentation path.</summary>
    public string? PresentationPath { get; init; }

    /// <summary>Source slide id.</summary>
    public string? SlideId { get; init; }

    /// <summary>Arrangement instance key.</summary>
    public string? ArrangementInstanceKey { get; init; }

    /// <summary>Compiled scene snapshot.</summary>
    public SlideScene Scene { get; init; } = SlideScene.Empty;

    /// <summary>Precompiled screen-specific scenes keyed by Look-selected theme variant id.</summary>
    public IReadOnlyDictionary<string, SlideScene> VariantScenes { get; init; } =
        new Dictionary<string, SlideScene>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Applied theme id.</summary>
    public string? ThemeId { get; init; }

    /// <summary>Applied theme variant id.</summary>
    public string? ThemeVariantId { get; init; }

    /// <summary>Active object build index.</summary>
    public int BuildIndex { get; init; } = -1;
}

/// <summary>
/// Media layer payload detail.
/// </summary>
public sealed record MediaRenderPayload : RenderPayloadDetail
{
    /// <summary>Media layer state represented by this payload.</summary>
    public OutputLayerMedia Media { get; init; } = new();

    /// <summary>Target media slot.</summary>
    public string Target { get; init; } = "mediaUnderlay";
}

/// <summary>
/// Overlay layer payload detail.
/// </summary>
public sealed record OverlayRenderPayload : RenderPayloadDetail
{
    /// <summary>Overlay id.</summary>
    public string OverlayId { get; init; } = string.Empty;

    /// <summary>Overlay kind.</summary>
    public string OverlayKind { get; init; } = string.Empty;

    /// <summary>Optional scene snapshot for scene-based overlays.</summary>
    public SlideScene? Scene { get; init; }
}

/// <summary>
/// Live video payload detail.
/// </summary>
public sealed record LiveVideoRenderPayload : RenderPayloadDetail
{
    /// <summary>Live video source id.</summary>
    public string SourceId { get; init; } = string.Empty;

    /// <summary>Fit/crop/stretch policy.</summary>
    public string Fit { get; init; } = "cover";

    /// <summary>Whether the source includes audio.</summary>
    public bool HasAudio { get; init; }
}
