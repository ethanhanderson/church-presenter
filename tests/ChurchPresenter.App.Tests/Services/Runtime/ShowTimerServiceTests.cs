using ChurchPresenter.App.Tests.TestSupport;
using ChurchPresenter.Backend.Commands;
using ChurchPresenter.Backend.Overlays;
using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Backend.Stage;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

namespace ChurchPresenter.App.Tests.Services.Runtime;

public sealed class ShowTimerServiceTests
{
    [Fact]
    public async Task SaveTimerAsync_persists_timer_and_activate_timer_tracks_runtime_state()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var settings = new SettingsService(paths.Object, new SharedConfigService(paths.Object, NullLogger<SharedConfigService>.Instance), new MachineStateService(paths.Object, NullLogger<MachineStateService>.Instance), NullLogger<SettingsService>.Instance);
        var service = new ShowTimerService(settings, NullLogger<ShowTimerService>.Instance);

        var timer = await service.SaveTimerAsync(new ShowTimerDefinition
        {
            Name = "Offering",
            DurationSeconds = 90,
        });
        var loaded = await service.GetTimersAsync();

        service.ActivateTimer(timer.Id);

        loaded.Should().ContainSingle(item => item.Id == timer.Id && item.Name == "Offering" && item.DurationSeconds == 90);
        service.CurrentTimerId.Should().Be(timer.Id);
        service.CurrentTimer.Should().NotBeNull();
        service.CurrentTimer!.Name.Should().Be("Offering");
        service.CurrentTimerStartedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ActivateTimer_publishes_generated_timer_snapshot_when_live_facade_is_available()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var settings = new SettingsService(paths.Object, new SharedConfigService(paths.Object, NullLogger<SharedConfigService>.Instance), new MachineStateService(paths.Object, NullLogger<MachineStateService>.Instance), NullLogger<SettingsService>.Instance);
        FakeLiveProductionFacade liveProduction = new();
        var service = new ShowTimerService(settings, NullLogger<ShowTimerService>.Instance, liveProduction);
        var timer = await service.SaveTimerAsync(new ShowTimerDefinition
        {
            Name = "Welcome Countdown",
            DurationSeconds = 90,
        });

        service.ActivateTimer(timer.Id);

        liveProduction.LastTimer.Should().NotBeNull();
        liveProduction.LastTimer!.Id.Should().Be(timer.Id);
        liveProduction.LastTimer.Name.Should().Be("Welcome Countdown");
        liveProduction.LastTimer.Status.Should().Be(GeneratedTimerStatus.Running);
        liveProduction.LastTimer.Remaining.Should().Be(TimeSpan.FromSeconds(90));
        liveProduction.LastTimer.DisplayValue.Should().Be("01:30");
    }

    [Fact]
    public async Task ActivateTimer_null_publishes_stopped_timer_snapshot_for_previous_timer()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var settings = new SettingsService(paths.Object, new SharedConfigService(paths.Object, NullLogger<SharedConfigService>.Instance), new MachineStateService(paths.Object, NullLogger<MachineStateService>.Instance), NullLogger<SettingsService>.Instance);
        FakeLiveProductionFacade liveProduction = new();
        var service = new ShowTimerService(settings, NullLogger<ShowTimerService>.Instance, liveProduction);
        var timer = await service.SaveTimerAsync(new ShowTimerDefinition
        {
            Name = "Welcome Countdown",
            DurationSeconds = 90,
        });

        service.ActivateTimer(timer.Id);
        service.ActivateTimer(null);

        liveProduction.LastTimer.Should().NotBeNull();
        liveProduction.LastTimer!.Id.Should().Be(timer.Id);
        liveProduction.LastTimer.Status.Should().Be(GeneratedTimerStatus.Stopped);
        liveProduction.LastTimer.Remaining.Should().Be(TimeSpan.Zero);
        liveProduction.LastTimer.DisplayValue.Should().Be("00:00");
    }

    private sealed class FakeLiveProductionFacade : ILiveProductionFacade
    {
        public event EventHandler<LiveProductionChangedEventArgs>? Changed;

        public LiveProductionSnapshot Current { get; } = LiveProductionSnapshot.Empty;

        public TimerSnapshot? LastTimer { get; private set; }

        public Task SetLookAsync(string lookId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ActionResult SetOverlay(OverlayContentState overlay) => new() { Succeeded = true };

        public ActionResult SetTimer(TimerSnapshot timer)
        {
            LastTimer = timer;
            Changed?.Invoke(this, new LiveProductionChangedEventArgs { Snapshot = Current });
            return new ActionResult { Succeeded = true };
        }

        public ActionResult SetCaptureSession(CaptureSessionState captureSession) => new() { Succeeded = true };

        public void ReportOutputHostFeedback(OutputHostFrameFeedbackState feedback)
        {
        }

        public void ReportMediaPlayerFailure(MediaPlayerFailureState failure)
        {
        }

        public ActionResult ExecuteCommands(
            IEnumerable<LiveCommand> commands,
            LiveCommandSource? source = null,
            string? macroId = null) => new() { Succeeded = true };

        public ActionResult ExecuteMacro(LiveMacroDefinition macro, LiveCommandSource? source = null) =>
            new() { Succeeded = true };

        public ActionResult ClearGroup(string clearGroupId) => new() { Succeeded = true };

        public ActionResult ClearLayers(IEnumerable<ChurchPresenter.Backend.Rendering.OutputLayerKind> layers) =>
            new() { Succeeded = true };

        public ActionResult ReleaseClearedLayers(IEnumerable<ChurchPresenter.Backend.Rendering.OutputLayerKind> layers) =>
            new() { Succeeded = true };

        public ActionResult SetStageLayout(
            string screenId,
            string stageLayoutId,
            StageAudienceCommandMode deliveryMode = StageAudienceCommandMode.StageAndAudience)
        {
            return new ActionResult { Succeeded = true };
        }
    }
}