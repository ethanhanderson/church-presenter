using ChurchPresenter.Backend.Commands;
using ChurchPresenter.Backend.Overlays;
using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Backend.Stage;

using BackendOutputLayerKind = ChurchPresenter.Backend.Rendering.OutputLayerKind;

namespace ChurchPresenter.Services.Runtime;

/// <summary>
/// UI-facing access point for the backend live-production snapshot.
/// Keeps the current playback state, backend session state, and resolved backend frames in sync.
/// </summary>
public interface ILiveProductionFacade
{
    /// <summary>Raised when the backend live-production snapshot changes.</summary>
    event EventHandler<LiveProductionChangedEventArgs>? Changed;

    /// <summary>The latest synchronized live-production snapshot.</summary>
    LiveProductionSnapshot Current { get; }

    /// <summary>Updates the active Look through the durable routing service.</summary>
    Task SetLookAsync(string lookId, CancellationToken cancellationToken = default);

    /// <summary>Sets generated overlay state through the shared backend command pipeline.</summary>
    ActionResult SetOverlay(OverlayContentState overlay);

    /// <summary>Sets generated timer state through the shared backend command pipeline.</summary>
    ActionResult SetTimer(TimerSnapshot timer);

    /// <summary>Sets capture-session state through the shared backend command pipeline.</summary>
    ActionResult SetCaptureSession(CaptureSessionState captureSession);

    /// <summary>Records output host frame-application and endpoint feedback.</summary>
    void ReportOutputHostFeedback(OutputHostFrameFeedbackState feedback);

    /// <summary>Records an output media/player failure reported by a host.</summary>
    void ReportMediaPlayerFailure(MediaPlayerFailureState failure);

    /// <summary>Executes multiple generated/live commands through one shared action batch.</summary>
    ActionResult ExecuteCommands(
        IEnumerable<LiveCommand> commands,
        LiveCommandSource? source = null,
        string? macroId = null);

    /// <summary>Expands and executes a macro through the shared backend command pipeline.</summary>
    ActionResult ExecuteMacro(LiveMacroDefinition macro, LiveCommandSource? source = null);

    /// <summary>Clears one configured clear group through the shared backend command pipeline.</summary>
    ActionResult ClearGroup(string clearGroupId);

    /// <summary>Clears the supplied backend layers through the shared backend command pipeline.</summary>
    ActionResult ClearLayers(IEnumerable<BackendOutputLayerKind> layers);

    /// <summary>Releases clear overrides for layers whose content has been intentionally taken live again.</summary>
    ActionResult ReleaseClearedLayers(IEnumerable<BackendOutputLayerKind> layers);

    /// <summary>Assigns a stage layout through the shared backend command pipeline.</summary>
    ActionResult SetStageLayout(
        string screenId,
        string stageLayoutId,
        StageAudienceCommandMode deliveryMode = StageAudienceCommandMode.StageAndAudience);
}

/// <summary>Immutable snapshot shared by UI facades that bridge legacy playback and backend frames.</summary>
public sealed record LiveProductionSnapshot
{
    /// <summary>An empty snapshot.</summary>
    public static readonly LiveProductionSnapshot Empty = new();

    /// <summary>Latest legacy playback snapshot.</summary>
    public PlaybackState PlaybackState { get; init; } = new();

    /// <summary>Latest backend live-render session state.</summary>
    public LiveRenderSessionState SessionState { get; init; } = new();

    /// <summary>Latest backend-resolved frames.</summary>
    public RenderFrameSet Frames { get; init; } = new();

    /// <summary>Latest logical output topology and diagnostics snapshot.</summary>
    public OutputTopologySnapshot Topology { get; init; } = new();
}

/// <summary>Event args for <see cref="ILiveProductionFacade.Changed"/>.</summary>
public sealed class LiveProductionChangedEventArgs : EventArgs
{
    /// <summary>The updated live-production snapshot.</summary>
    public LiveProductionSnapshot Snapshot { get; init; } = LiveProductionSnapshot.Empty;
}