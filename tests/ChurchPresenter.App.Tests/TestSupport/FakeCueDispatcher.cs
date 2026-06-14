using ChurchPresenter.Backend.Commands;
using ChurchPresenter.Backend.Overlays;
using ChurchPresenter.Backend.Pipeline.Commands;
using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Backend.Stage;
using ChurchPresenter.Models.Media;

namespace ChurchPresenter.App.Tests.TestSupport;

internal sealed class FakeCueDispatcher : ICueDispatcher
{
    public OverlayContentState? LastOverlay { get; private set; }

    public LiveMacroDefinition? LastMacro { get; private set; }

    public TimerSnapshot? LastTimer { get; private set; }

    public List<IReadOnlyList<LiveCommand>> CommandBatches { get; } = [];

    public int DispatchSlideCueCount { get; private set; }

    public int DispatchMediaCueCount { get; private set; }

    public string? LastMediaCueTarget { get; private set; }

    public PreparedMediaCue? LastMediaCue { get; private set; }

    public Task<DispatchReceipt> DispatchAsync(LiveCommand command, CancellationToken cancellationToken = default)
    {
        if (command.Overlay != null)
            LastOverlay = command.Overlay;

        CommandBatches.Add([command]);
        return Task.FromResult(Success());
    }

    public Task<DispatchReceipt> DispatchSlideCueAsync(
        PreparedSlideCue cue,
        LiveCommandSource? source = null,
        CancellationToken cancellationToken = default)
    {
        DispatchSlideCueCount++;
        return Task.FromResult(Success());
    }

    public Task<DispatchReceipt> DispatchMediaCueAsync(
        PreparedMediaCue cue,
        string? target = null,
        LiveCommandSource? source = null,
        CancellationToken cancellationToken = default)
    {
        DispatchMediaCueCount++;
        LastMediaCue = cue;
        LastMediaCueTarget = target;
        return Task.FromResult(Success());
    }

    public Task<DispatchReceipt> ClearLayersAsync(IEnumerable<OutputLayerKind> layers, CancellationToken cancellationToken = default) =>
        Task.FromResult(Success());

    public Task<DispatchReceipt> ClearGroupAsync(string clearGroupId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Success());

    public Task<DispatchReceipt> ReleaseClearedLayersAsync(IEnumerable<OutputLayerKind> layers, CancellationToken cancellationToken = default) =>
        Task.FromResult(Success());

    public Task<DispatchReceipt> SetLookAsync(string lookId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Success());

    public Task<DispatchReceipt> SetOverlayAsync(OverlayContentState overlay, CancellationToken cancellationToken = default)
    {
        LastOverlay = overlay;
        return Task.FromResult(Success());
    }

    public Task<DispatchReceipt> SetTimerAsync(TimerSnapshot timer, CancellationToken cancellationToken = default)
    {
        LastTimer = timer;
        return Task.FromResult(Success());
    }

    public Task<DispatchReceipt> SetCaptureSessionAsync(CaptureSessionState captureSession, CancellationToken cancellationToken = default) =>
        Task.FromResult(Success());

    public Task<DispatchReceipt> SetStageLayoutAsync(
        string screenId,
        string stageLayoutId,
        StageAudienceCommandMode deliveryMode = StageAudienceCommandMode.StageAndAudience,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Success());

    public Task<DispatchReceipt> ExecuteMacroAsync(LiveMacroDefinition macro, LiveCommandSource? source = null, CancellationToken cancellationToken = default)
    {
        LastMacro = macro;
        LastOverlay = macro.Commands.LastOrDefault(command => command.Overlay != null)?.Overlay;
        return Task.FromResult(Success());
    }

    public Task<DispatchReceipt> EndLiveAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Success());

    public Task<DispatchReceipt> InvalidateTransitionDefaultsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Success());

    private static DispatchReceipt Success() => new() { Succeeded = true, CommandId = Guid.NewGuid() };
}
