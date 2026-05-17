using ChurchPresenter.Backend.Output;

using FluentAssertions;

namespace ChurchPresenter.App.Tests.Services.Output;

/// <summary>
/// Covers the compatibility output-topology bridge used by backend output state and local window hosts.
/// </summary>
public sealed class OutputTopologyServiceTests
{
    [Fact]
    public void GetSnapshot_builds_logical_screen_registry_and_placeholder_routes()
    {
        FakeSettingsService settings = new();
        settings.Settings.Output.AudienceMonitorIds = ["1"];
        settings.Settings.Output.StageMonitorIds = ["2"];

        OutputTopologyService service = new(
            settings,
            new FakeLocalDisplayCatalogService(
            [
                CreateMonitor(1, "Projector"),
                CreateMonitor(2, "Confidence"),
            ]));

        OutputTopologySnapshot snapshot = service.GetSnapshot();

        snapshot.Screens.Keys.Should().Equal(OutputScreenIds.Main, OutputScreenIds.Stream, OutputScreenIds.Lobby, OutputScreenIds.Stage);
        snapshot.ResolveMapping(OutputScreenIds.Main).EndpointIds.Should().Equal("local-display:1");
        snapshot.ResolveMapping(OutputScreenIds.Stage).EndpointIds.Should().Equal("local-display:2");
        snapshot.ResolveMapping(OutputScreenIds.Stream).EndpointIds.Should().Equal("placeholder:stream");
        snapshot.ResolveMapping(OutputScreenIds.Lobby).EndpointIds.Should().Equal("placeholder:lobby");
        snapshot.Endpoints["placeholder:stream"].Health.Should().Be(EndpointHealth.Placeholder);
        snapshot.Endpoints["placeholder:lobby"].Health.Should().Be(EndpointHealth.Placeholder);
    }

    [Fact]
    public void GetSnapshot_preserves_missing_local_endpoints_for_reconnect_diagnostics()
    {
        FakeSettingsService settings = new();
        settings.Settings.Output.AudienceMonitorIds = ["0", "3"];

        OutputTopologyService service = new(
            settings,
            new FakeLocalDisplayCatalogService(
            [
                CreateMonitor(0, "Main Display"),
            ]));

        OutputTopologySnapshot snapshot = service.GetSnapshot();

        snapshot.ResolveMapping(OutputScreenIds.Main).EndpointIds.Should().Equal("local-display:0", "local-display:3");
        snapshot.Endpoints["local-display:0"].Health.Should().Be(EndpointHealth.Connected);
        snapshot.Endpoints["local-display:3"].Health.Should().Be(EndpointHealth.Missing);

        IReadOnlyList<LocalDisplayOutputTarget> targets = snapshot.GetLocalDisplayTargets(OutputScreenIds.Main);
        targets.Should().HaveCount(2);
        targets.Should().ContainSingle(target => target.EndpointId == "local-display:0" && target.IsConnected);
        targets.Should().ContainSingle(target => target.EndpointId == "local-display:3" && !target.IsConnected && target.MonitorIndex == 3);

        OutputScreenDiagnostics diagnostics = snapshot.ResolveDiagnostics(OutputScreenIds.Main);
        diagnostics.Health.Should().Be(EndpointHealth.Missing);
        diagnostics.CanReconnect.Should().BeTrue();
        diagnostics.Message.Should().Contain("reconnect automatically");
    }

    [Fact]
    public void GetSnapshot_supports_zero_one_and_many_local_display_targets()
    {
        FakeSettingsService settings = new();
        settings.Settings.Output.AudienceMonitorIds = ["0", "2", "2"];
        settings.Settings.Output.StageMonitorIds = [];

        OutputTopologyService service = new(
            settings,
            new FakeLocalDisplayCatalogService(
            [
                CreateMonitor(0, "Main"),
                CreateMonitor(2, "Lobby"),
            ]));

        OutputTopologySnapshot snapshot = service.GetSnapshot();

        snapshot.ResolveMapping(OutputScreenIds.Stage).EndpointIds.Should().BeEmpty();
        snapshot.GetLocalDisplayTargets(OutputScreenIds.Stage).Should().BeEmpty();
        snapshot.GetLocalDisplayTargets(OutputScreenIds.Main)
            .Select(target => target.EndpointId)
            .Should()
            .Equal("local-display:0", "local-display:2");
    }

    [Fact]
    public void GetSnapshot_marks_placeholder_endpoints_with_operator_capabilities()
    {
        OutputTopologyService service = new(
            new FakeSettingsService(),
            new FakeLocalDisplayCatalogService([]));

        OutputTopologySnapshot snapshot = service.GetSnapshot();

        snapshot.Endpoints["placeholder:stream"].Capabilities.Should().HaveFlag(EndpointCapability.Placeholder);
        snapshot.Endpoints["placeholder:stream"].Capabilities.Should().HaveFlag(EndpointCapability.Capture);
        snapshot.Endpoints["placeholder:lobby"].Capabilities.Should().HaveFlag(EndpointCapability.Mirror);
        snapshot.ResolveDiagnostics(OutputScreenIds.Stream).Health.Should().Be(EndpointHealth.Placeholder);
        snapshot.ResolveDiagnostics(OutputScreenIds.Lobby).Health.Should().Be(EndpointHealth.Placeholder);
    }

    private static MonitorInfoDto CreateMonitor(int index, string name)
    {
        return new MonitorInfoDto(
            index,
            name,
            Width: 1920,
            Height: 1080,
            X: index * 1920,
            Y: 0,
            IsPrimary: index == 0,
            RefreshRate: 60);
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