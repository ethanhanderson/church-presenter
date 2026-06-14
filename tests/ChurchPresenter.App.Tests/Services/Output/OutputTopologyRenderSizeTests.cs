using ChurchPresenter.Backend.Output;
using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Models.Settings;
using ChurchPresenter.Services.Output;
using ChurchPresenter.Services.Settings;

using FluentAssertions;

namespace ChurchPresenter.App.Tests.Services.Output;

/// <summary>
/// Render-size resolution for logical output screens.
/// </summary>
public sealed class OutputTopologyRenderSizeTests
{
    [Fact]
    public void Main_screen_uses_primary_display_native_resolution_when_no_monitors_are_mapped()
    {
        FakeSettingsService settings = new();
        OutputTopologyService service = new(
            settings,
            new FakeLocalDisplayCatalogService(
            [
                CreateMonitor(0, "Built-in", 3840, 2160, isPrimary: true),
            ]));

        OutputTopologySnapshot snapshot = service.GetSnapshot();

        snapshot.Screens[OutputScreenIds.Main].RenderSize.Should().Be(new PixelSize(3840, 2160));
        snapshot.Screens[OutputScreenIds.Stream].RenderSize.Should().Be(PixelSize.FullHd);
    }

    [Fact]
    public void Main_screen_prefers_configured_monitor_resolution_over_primary_fallback()
    {
        FakeSettingsService settings = new();
        settings.Settings.Output.AudienceMonitorIds = ["1"];

        OutputTopologyService service = new(
            settings,
            new FakeLocalDisplayCatalogService(
            [
                CreateMonitor(0, "Built-in", 3840, 2160, isPrimary: true),
                CreateMonitor(1, "Projector", 1920, 1080, isPrimary: false),
            ]));

        OutputTopologySnapshot snapshot = service.GetSnapshot();

        snapshot.Screens[OutputScreenIds.Main].RenderSize.Should().Be(new PixelSize(1920, 1080));
    }

    private static MonitorInfoDto CreateMonitor(
        int index,
        string name,
        int width,
        int height,
        bool isPrimary)
    {
        return new MonitorInfoDto(index, name, width, height, index * width, 0, isPrimary, 60);
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettingsDto Settings { get; } = new();

        public Task LoadAsync() => Task.CompletedTask;

        public Task SaveAsync() => Task.CompletedTask;

        public void Update(Action<AppSettingsDto> mutator)
        {
            ArgumentNullException.ThrowIfNull(mutator);
            mutator(Settings);
        }
    }

    private sealed class FakeLocalDisplayCatalogService(IReadOnlyList<MonitorInfoDto> displays) : ILocalDisplayCatalogService
    {
        private readonly IReadOnlyList<MonitorInfoDto> _displays = displays;

        public IReadOnlyList<MonitorInfoDto> GetDisplays() => _displays;
    }
}
