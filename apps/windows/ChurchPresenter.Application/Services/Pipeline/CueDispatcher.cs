using ChurchPresenter.Backend.Commands;
using ChurchPresenter.Backend.Overlays;
using ChurchPresenter.Backend.Pipeline.Commands;
using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Backend.Stage;
using ChurchPresenter.Models.Media;

namespace ChurchPresenter.Services.Pipeline;

/// <summary>
/// Pipeline-backed implementation of <see cref="ICueDispatcher"/>.
/// </summary>
public sealed class CueDispatcher(CuePipelineRuntime runtime) : ICueDispatcher
{
    private readonly CuePipelineRuntime _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

    /// <inheritdoc />
    public Task<DispatchReceipt> DispatchAsync(LiveCommand command, CancellationToken cancellationToken = default) =>
        _runtime.DispatchAsync(command, cancellationToken);

    /// <inheritdoc />
    public Task<DispatchReceipt> DispatchSlideCueAsync(
        PreparedSlideCue cue,
        LiveCommandSource? source = null,
        CancellationToken cancellationToken = default) =>
        _runtime.DispatchSlideCueAsync(cue, source, cancellationToken);

    /// <inheritdoc />
    public Task<DispatchReceipt> DispatchMediaCueAsync(
        PreparedMediaCue cue,
        string? target = null,
        LiveCommandSource? source = null,
        CancellationToken cancellationToken = default) =>
        _runtime.DispatchMediaCueAsync(cue, target, source, cancellationToken);

    /// <inheritdoc />
    public Task<DispatchReceipt> ClearLayersAsync(IEnumerable<OutputLayerKind> layers, CancellationToken cancellationToken = default) =>
        _runtime.ClearLayersAsync(layers, cancellationToken);

    /// <inheritdoc />
    public Task<DispatchReceipt> ClearGroupAsync(string clearGroupId, CancellationToken cancellationToken = default) =>
        _runtime.ClearGroupAsync(clearGroupId, cancellationToken);

    /// <inheritdoc />
    public Task<DispatchReceipt> ReleaseClearedLayersAsync(IEnumerable<OutputLayerKind> layers, CancellationToken cancellationToken = default) =>
        _runtime.ReleaseClearedLayersAsync(layers, cancellationToken);

    /// <inheritdoc />
    public Task<DispatchReceipt> SetLookAsync(string lookId, CancellationToken cancellationToken = default) =>
        _runtime.SetLookAsync(lookId, cancellationToken);

    /// <inheritdoc />
    public Task<DispatchReceipt> SetOverlayAsync(OverlayContentState overlay, CancellationToken cancellationToken = default) =>
        _runtime.SetOverlayAsync(overlay, cancellationToken);

    /// <inheritdoc />
    public Task<DispatchReceipt> SetTimerAsync(TimerSnapshot timer, CancellationToken cancellationToken = default) =>
        _runtime.SetTimerAsync(timer, cancellationToken);

    /// <inheritdoc />
    public Task<DispatchReceipt> SetCaptureSessionAsync(CaptureSessionState captureSession, CancellationToken cancellationToken = default) =>
        _runtime.SetCaptureSessionAsync(captureSession, cancellationToken);

    /// <inheritdoc />
    public Task<DispatchReceipt> SetStageLayoutAsync(
        string screenId,
        string stageLayoutId,
        StageAudienceCommandMode deliveryMode = StageAudienceCommandMode.StageAndAudience,
        CancellationToken cancellationToken = default) =>
        _runtime.SetStageLayoutAsync(screenId, stageLayoutId, deliveryMode, cancellationToken);

    /// <inheritdoc />
    public Task<DispatchReceipt> ExecuteMacroAsync(LiveMacroDefinition macro, LiveCommandSource? source = null, CancellationToken cancellationToken = default) =>
        _runtime.ExecuteMacroAsync(macro, source, cancellationToken);

    /// <inheritdoc />
    public Task<DispatchReceipt> EndLiveAsync(CancellationToken cancellationToken = default) =>
        _runtime.EndLiveAsync(cancellationToken);

    /// <inheritdoc />
    public Task<DispatchReceipt> InvalidateTransitionDefaultsAsync(CancellationToken cancellationToken = default) =>
        _runtime.InvalidateTransitionDefaultsAsync(cancellationToken);
}
