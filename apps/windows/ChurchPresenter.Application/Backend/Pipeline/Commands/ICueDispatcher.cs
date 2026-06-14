using ChurchPresenter.Backend.Commands;
using ChurchPresenter.Backend.Overlays;
using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Backend.Stage;
using ChurchPresenter.Models.Media;

namespace ChurchPresenter.Backend.Pipeline.Commands;

/// <summary>
/// Public dispatch surface for live commands entering the unified output pipeline.
/// </summary>
public interface ICueDispatcher
{
    /// <summary>Dispatches a normalized live command.</summary>
    Task<DispatchReceipt> DispatchAsync(LiveCommand command, CancellationToken cancellationToken = default);

    /// <summary>Dispatches a prepared slide cue to the program slide layer.</summary>
    Task<DispatchReceipt> DispatchSlideCueAsync(
        PreparedSlideCue cue,
        LiveCommandSource? source = null,
        CancellationToken cancellationToken = default);

    /// <summary>Dispatches a prepared media cue to the requested target layer.</summary>
    Task<DispatchReceipt> DispatchMediaCueAsync(
        PreparedMediaCue cue,
        string? target = null,
        LiveCommandSource? source = null,
        CancellationToken cancellationToken = default);

    /// <summary>Clears the specified output layers.</summary>
    Task<DispatchReceipt> ClearLayersAsync(
        IEnumerable<OutputLayerKind> layers,
        CancellationToken cancellationToken = default);

    /// <summary>Clears a configured clear group.</summary>
    Task<DispatchReceipt> ClearGroupAsync(string clearGroupId, CancellationToken cancellationToken = default);

    /// <summary>Releases runtime clear overrides for the specified layers.</summary>
    Task<DispatchReceipt> ReleaseClearedLayersAsync(
        IEnumerable<OutputLayerKind> layers,
        CancellationToken cancellationToken = default);

    /// <summary>Activates a Look preset.</summary>
    Task<DispatchReceipt> SetLookAsync(string lookId, CancellationToken cancellationToken = default);

    /// <summary>Updates generated overlay state.</summary>
    Task<DispatchReceipt> SetOverlayAsync(OverlayContentState overlay, CancellationToken cancellationToken = default);

    /// <summary>Updates timer state.</summary>
    Task<DispatchReceipt> SetTimerAsync(TimerSnapshot timer, CancellationToken cancellationToken = default);

    /// <summary>Updates capture-session state.</summary>
    Task<DispatchReceipt> SetCaptureSessionAsync(CaptureSessionState captureSession, CancellationToken cancellationToken = default);

    /// <summary>Assigns a stage layout to a stage screen.</summary>
    Task<DispatchReceipt> SetStageLayoutAsync(
        string screenId,
        string stageLayoutId,
        StageAudienceCommandMode deliveryMode = StageAudienceCommandMode.StageAndAudience,
        CancellationToken cancellationToken = default);

    /// <summary>Executes a macro expansion through the pipeline.</summary>
    Task<DispatchReceipt> ExecuteMacroAsync(
        LiveMacroDefinition macro,
        LiveCommandSource? source = null,
        CancellationToken cancellationToken = default);

    /// <summary>Clears all primary live output layers and ends the current live session.</summary>
    Task<DispatchReceipt> EndLiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Re-composes the current live snapshot after global transition defaults change.</summary>
    Task<DispatchReceipt> InvalidateTransitionDefaultsAsync(CancellationToken cancellationToken = default);
}
