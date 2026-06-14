using ChurchPresenter.App.Tests.TestSupport;
using ChurchPresenter.Backend.Overlays;

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
        var service = new ShowTimerService(settings, NullLogger<ShowTimerService>.Instance, new FakeCueDispatcher());

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
    public async Task ActivateTimer_publishes_generated_timer_snapshot_when_cue_dispatcher_is_available()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var settings = new SettingsService(paths.Object, new SharedConfigService(paths.Object, NullLogger<SharedConfigService>.Instance), new MachineStateService(paths.Object, NullLogger<MachineStateService>.Instance), NullLogger<SettingsService>.Instance);
        FakeCueDispatcher cueDispatcher = new();
        var service = new ShowTimerService(settings, NullLogger<ShowTimerService>.Instance, cueDispatcher);
        var timer = await service.SaveTimerAsync(new ShowTimerDefinition
        {
            Name = "Welcome Countdown",
            DurationSeconds = 90,
        });

        service.ActivateTimer(timer.Id);

        cueDispatcher.LastTimer.Should().NotBeNull();
        cueDispatcher.LastTimer!.Id.Should().Be(timer.Id);
        cueDispatcher.LastTimer.Name.Should().Be("Welcome Countdown");
        cueDispatcher.LastTimer.Status.Should().Be(GeneratedTimerStatus.Running);
        cueDispatcher.LastTimer.Remaining.Should().Be(TimeSpan.FromSeconds(90));
        cueDispatcher.LastTimer.DisplayValue.Should().Be("01:30");
    }

    [Fact]
    public async Task ActivateTimer_null_publishes_stopped_timer_snapshot_for_previous_timer()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var settings = new SettingsService(paths.Object, new SharedConfigService(paths.Object, NullLogger<SharedConfigService>.Instance), new MachineStateService(paths.Object, NullLogger<MachineStateService>.Instance), NullLogger<SettingsService>.Instance);
        FakeCueDispatcher cueDispatcher = new();
        var service = new ShowTimerService(settings, NullLogger<ShowTimerService>.Instance, cueDispatcher);
        var timer = await service.SaveTimerAsync(new ShowTimerDefinition
        {
            Name = "Welcome Countdown",
            DurationSeconds = 90,
        });

        service.ActivateTimer(timer.Id);
        service.ActivateTimer(null);

        cueDispatcher.LastTimer.Should().NotBeNull();
        cueDispatcher.LastTimer!.Id.Should().Be(timer.Id);
        cueDispatcher.LastTimer.Status.Should().Be(GeneratedTimerStatus.Stopped);
        cueDispatcher.LastTimer.Remaining.Should().Be(TimeSpan.Zero);
        cueDispatcher.LastTimer.DisplayValue.Should().Be("00:00");
    }
}
