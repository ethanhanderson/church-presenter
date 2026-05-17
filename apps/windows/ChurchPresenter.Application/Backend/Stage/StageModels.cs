using ChurchPresenter.Backend.Rendering;

namespace ChurchPresenter.Backend.Stage;

/// <summary>
/// Delivery behavior for a stage-related command.
/// </summary>
public enum StageAudienceCommandMode
{
    /// <summary>Update stage screens without mutating audience output.</summary>
    StageOnly,

    /// <summary>Allow the caller to update both stage and audience output.</summary>
    StageAndAudience,
}

/// <summary>
/// Supported stage-layout element categories based on ProPresenter's stage screen model.
/// </summary>
public enum StageLayoutElementKind
{
    CurrentSlideText,
    CurrentSlidePreview,
    NextSlideText,
    NextSlidePreview,
    Notes,
    AudienceScreenPreview,
    StageScreenPreview,
    StageMessage,
    Timer,
    SystemClock,
    VideoCountdown,
    GroupName,
    GroupColor,
    CaptureStatus,
    CustomText,
    CustomShape,
}

/// <summary>
/// One element placed inside a stage layout.
/// </summary>
public sealed record StageLayoutElement
{
    /// <summary>Stable element id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Element kind.</summary>
    public StageLayoutElementKind Kind { get; init; }

    /// <summary>Optional source id such as timer id or preview screen id.</summary>
    public string? SourceId { get; init; }

    /// <summary>Optional operator-facing label.</summary>
    public string? Label { get; init; }

    /// <summary>Optional static text for custom-text elements.</summary>
    public string? StaticText { get; init; }

    /// <summary>Whether the element only appears while its source is active.</summary>
    public bool VisibleWhenActive { get; init; }

    /// <summary>Free-form properties reserved for later layout/renderer use.</summary>
    public IReadOnlyDictionary<string, string> Properties { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Named stage layout made of dashboard-style elements.
/// </summary>
public sealed record StageLayout
{
    /// <summary>Stable layout id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Operator-facing layout name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Layout elements in authoring order.</summary>
    public IReadOnlyList<StageLayoutElement> Elements { get; init; } = Array.Empty<StageLayoutElement>();
}

/// <summary>
/// Runtime state for one logical stage screen.
/// </summary>
public sealed record StageScreenState
{
    /// <summary>Logical stage screen id.</summary>
    public string ScreenId { get; init; } = string.Empty;

    /// <summary>Operator-facing stage screen name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Active layout id currently assigned to the screen.</summary>
    public string? ActiveLayoutId { get; init; }

    /// <summary>Last command mode that affected this stage screen.</summary>
    public StageAudienceCommandMode LastCommandMode { get; init; } = StageAudienceCommandMode.StageAndAudience;
}

/// <summary>
/// Generated stage-oriented content consumed by stage layouts.
/// </summary>
public sealed record StagePresentationSnapshot
{
    /// <summary>Current slide text, if known.</summary>
    public string? CurrentSlideText { get; init; }

    /// <summary>Next slide text, if known.</summary>
    public string? NextSlideText { get; init; }

    /// <summary>Operator notes for the current cue.</summary>
    public string? Notes { get; init; }

    /// <summary>Current slide preview, if available.</summary>
    public RenderPayloadDescriptor? CurrentSlidePreview { get; init; }

    /// <summary>Next slide preview, if available.</summary>
    public RenderPayloadDescriptor? NextSlidePreview { get; init; }

    /// <summary>Current group name, if available.</summary>
    public string? CurrentGroupName { get; init; }

    /// <summary>Current group color value, if available.</summary>
    public string? CurrentGroupColor { get; init; }
}

/// <summary>
/// Request contract for stage-data providers.
/// </summary>
public sealed record StageDataRequest
{
    /// <summary>Live render state used by providers to resolve dynamic stage content.</summary>
    public LiveRenderSessionState State { get; init; } = new();

    /// <summary>Stage screen being resolved.</summary>
    public string ScreenId { get; init; } = string.Empty;

    /// <summary>Element being resolved.</summary>
    public StageLayoutElement Element { get; init; } = new();
}

/// <summary>
/// Resolved stage payload plus optional diagnostics.
/// </summary>
public sealed record StageDataResult
{
    /// <summary>Payload for the stage renderer, if available.</summary>
    public RenderPayloadDescriptor? Payload { get; init; }

    /// <summary>Diagnostics emitted while resolving the element.</summary>
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Contract for later composition of stage-layout data providers.
/// </summary>
public interface IStageDataProvider
{
    /// <summary>Returns whether the provider can resolve the supplied element.</summary>
    bool CanResolve(StageLayoutElement element);

    /// <summary>Resolves a stage-layout element into a payload descriptor.</summary>
    StageDataResult Resolve(StageDataRequest request);
}