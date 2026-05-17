using ChurchPresenter.Backend.Rendering;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

namespace ChurchPresenter.App.Tests.Services.Output;

/// <summary>
/// Regression tests for <see cref="OutputRoutingService"/>.
/// </summary>
public sealed class OutputRoutingServiceTests
{
    [Fact]
    public void Default_look_routes_all_layers_to_all_known_feeds()
    {
        var machineState = new FakeMachineStateService();
        var (service, _) = Create(machineState);

        service.ActiveLookId.Should().Be(OutputLookIds.Default);
        service.Feeds.Select(feed => feed.Id).Should().Equal(OutputFeedIds.Main, OutputFeedIds.Stream, OutputFeedIds.Lobby);
        service.RoutesLayer(OutputFeedIds.Main, OutputLayerKind.Slide).Should().BeTrue();
        service.RoutesLayer(OutputFeedIds.Main, OutputLayerKind.Media).Should().BeTrue();
        service.RoutesLayer(OutputFeedIds.Main, OutputLayerKind.Audio).Should().BeTrue();
        service.RoutesLayer(OutputFeedIds.Main, OutputLayerKind.Messages).Should().BeTrue();
        service.RoutesLayer(OutputFeedIds.Main, OutputLayerKind.Props).Should().BeTrue();
        service.RoutesLayer(OutputFeedIds.Main, OutputLayerKind.Announcements).Should().BeTrue();
        service.RoutesLayer(OutputFeedIds.Main, OutputLayerKind.LiveVideo).Should().BeTrue();
        service.RoutesLayer(OutputFeedIds.Main, OutputLayerKind.Mask).Should().BeTrue();
        OutputRoutingDefaults.GetLayerDefinition(OutputLayerKind.Mask).IsRoutable.Should().BeTrue();
        OutputRoutingDefaults.GetLayerDefinition(OutputLayerKind.Mask).IsClearable.Should().BeFalse();
        service.RoutesLayer(OutputFeedIds.Stream, OutputLayerKind.Slide).Should().BeTrue();
        service.RoutesLayer(OutputFeedIds.Lobby, OutputLayerKind.Media).Should().BeTrue();
        service.ActiveLook.Routes.Should().AllSatisfy(route =>
            route.Layers.Select(layer => layer.Layer).Should().BeEquivalentTo(
                OutputRoutingDefaults.Layers.Select(layer => layer.Id)));
        service.ActiveLook.ClearGroups.Should().BeEmpty("custom clear groups should only exist after the operator creates them");
    }

    [Fact]
    public async Task SetRoutesAsync_persists_theme_variant_and_screen_mask_without_clearable_mask()
    {
        var machineState = new FakeMachineStateService();
        var (service, sharedConfig) = Create(machineState);

        await service.SetRoutesAsync(
        [
            new OutputLookFeedRouting
            {
                FeedId = OutputFeedIds.Main,
                Slide = true,
                Media = true,
                Layers =
                [
                    new OutputLayerRouteDefinition { Layer = "slide", Enabled = true },
                    new OutputLayerRouteDefinition { Layer = "mask", Enabled = true, MaskId = "main-mask" },
                ],
            },
            new OutputLookFeedRouting
            {
                FeedId = OutputFeedIds.Stream,
                Slide = true,
                Media = false,
                Layers =
                [
                    new OutputLayerRouteDefinition { Layer = "slide", Enabled = true, ThemeVariantId = "lower-third" },
                    new OutputLayerRouteDefinition { Layer = "mask", Enabled = false },
                ],
            },
        ]);

        OutputLookDefinition custom = sharedConfig.Output.Looks.Single(look => look.Id == OutputLookIds.Custom);
        custom.ResolveRouting(OutputFeedIds.Main).ResolveLayerRoute(OutputLayerKind.Mask)!.MaskId.Should().Be("main-mask");
        custom.ResolveRouting(OutputFeedIds.Stream).ResolveLayerRoute(OutputLayerKind.Slide)!.ThemeVariantId.Should().Be("lower-third");
        sharedConfig.Output.Masks.Should().ContainSingle(mask => mask.Id == "main-mask");
        OutputRoutingDefaults.ResolveClearGroupLayers(new OutputLookClearGroupDefinition { Layers = ["mask"] })
            .Should().BeEmpty();
    }

    [Fact]
    public async Task SetLayerRoutingAsync_promotes_default_to_custom_and_persists_feed_change()
    {
        var machineState = new FakeMachineStateService();
        var (service, sharedConfig) = Create(machineState);

        await service.SetLayerRoutingAsync(OutputFeedIds.Stream, OutputLayerKind.Media, enabled: false);

        service.ActiveLookId.Should().Be(OutputLookIds.Custom);
        service.RoutesLayer(OutputFeedIds.Stream, OutputLayerKind.Media).Should().BeFalse();
        service.RoutesLayer(OutputFeedIds.Stream, OutputLayerKind.Audio).Should().BeFalse();
        service.RoutesLayer(OutputFeedIds.Stream, OutputLayerKind.Slide).Should().BeTrue();
        sharedConfig.Output.Looks.Should().ContainSingle(look =>
            string.Equals(look.Id, OutputLookIds.Custom, StringComparison.OrdinalIgnoreCase));
        machineState.OutputBinding.Looks.Should().BeEmpty("custom Looks must be portable support settings, not machine-local bindings");
        sharedConfig.SaveCalls.Should().Be(1);
        machineState.SaveCalls.Should().Be(1);
    }

    [Fact]
    public async Task SetLayerRoutingAsync_persists_backend_overlay_layer_without_changing_slide_media_defaults()
    {
        var machineState = new FakeMachineStateService();
        var (service, sharedConfig) = Create(machineState);

        await service.SetLayerRoutingAsync(OutputFeedIds.Main, OutputLayerKind.Messages, enabled: false);

        service.ActiveLookId.Should().Be(OutputLookIds.Custom);
        service.RoutesLayer(OutputFeedIds.Main, OutputLayerKind.Messages).Should().BeFalse();
        service.RoutesLayer(OutputFeedIds.Main, OutputLayerKind.Slide).Should().BeTrue();
        service.RoutesLayer(OutputFeedIds.Main, OutputLayerKind.Media).Should().BeTrue();
        sharedConfig.Output.Looks
            .Single(look => look.Id == OutputLookIds.Custom)
            .ResolveRouting(OutputFeedIds.Main)
            .Layers.Should()
            .ContainSingle(layer => layer.Layer == "messages" && !layer.Enabled);
    }

    [Fact]
    public async Task ResetToDefaultAsync_restores_default_routing_without_removing_saved_custom_look()
    {
        var machineState = new FakeMachineStateService();
        var (service, sharedConfig) = Create(machineState);

        await service.SetLayerRoutingAsync(OutputFeedIds.Stream, OutputLayerKind.Media, enabled: false);
        await service.ResetToDefaultAsync();

        service.ActiveLookId.Should().Be(OutputLookIds.Default);
        service.RoutesLayer(OutputFeedIds.Stream, OutputLayerKind.Media).Should().BeTrue();
        sharedConfig.Output.Looks.Should().ContainSingle(look =>
            string.Equals(look.Id, OutputLookIds.Custom, StringComparison.OrdinalIgnoreCase));
    }

    private static (OutputRoutingService Service, FakeSharedConfigService SharedConfig) Create(FakeMachineStateService machineState)
    {
        var sharedConfig = new FakeSharedConfigService();
        var service = new OutputRoutingService(
            machineState,
            sharedConfig,
            new FakeOutputTopologyService(),
            NullLogger<OutputRoutingService>.Instance);
        return (service, sharedConfig);
    }

    private sealed class FakeOutputTopologyService : IOutputTopologyService
    {
        public IReadOnlyList<OutputFeedDefinition> AudienceScreens => OutputRoutingDefaults.BuiltInFeeds;

        public OutputTopologySnapshot GetSnapshot() => new();
    }

    private sealed class FakeMachineStateService : IMachineStateService
    {
        public OutputBinding OutputBinding { get; } = new();

        public RecentFilesState RecentFiles { get; } = new();

        public UpdatesState Updates { get; } = new();

        public DeviceBindingsState DeviceBindings { get; } = new();

        public CredentialsState Credentials { get; } = new();

        public CacheState Caches { get; } = new();

        public DiagnosticsState Diagnostics { get; } = new();

        public SettingsHealthSnapshot? SettingsHealth { get; private set; }

        public int SaveCalls { get; private set; }

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return Task.CompletedTask;
        }

        public void UpdateOutputBinding(Action<OutputBinding> mutator)
        {
            ArgumentNullException.ThrowIfNull(mutator);
            mutator(OutputBinding);
        }

        public void UpdateRecentFiles(Action<RecentFilesState> mutator)
        {
            ArgumentNullException.ThrowIfNull(mutator);
            mutator(RecentFiles);
        }

        public void UpdateUpdates(Action<UpdatesState> mutator)
        {
            ArgumentNullException.ThrowIfNull(mutator);
            mutator(Updates);
        }

        public void UpdateDeviceBindings(Action<DeviceBindingsState> mutator)
        {
            ArgumentNullException.ThrowIfNull(mutator);
            mutator(DeviceBindings);
        }

        public void UpdateCredentials(Action<CredentialsState> mutator)
        {
            ArgumentNullException.ThrowIfNull(mutator);
            mutator(Credentials);
        }

        public void UpdateCaches(Action<CacheState> mutator)
        {
            ArgumentNullException.ThrowIfNull(mutator);
            mutator(Caches);
        }

        public void UpdateDiagnostics(Action<DiagnosticsState> mutator)
        {
            ArgumentNullException.ThrowIfNull(mutator);
            mutator(Diagnostics);
        }

        public Task SaveHealthSnapshotAsync(SettingsHealthSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            SettingsHealth = snapshot;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSharedConfigService : ISharedConfigService
    {
        public OutputConfig Output { get; } = new();

        public ShowConfig Show { get; } = new();

        public StageConfig Stage { get; } = new();

        public EditorConfig Editor { get; } = new();

        public ReflowConfig Reflow { get; } = new();

        public IntegrationsConfig Integrations { get; } = new();

        public AppearanceConfig Appearance { get; } = new();

        public LibraryManagementConfig LibraryManagement { get; } = new();

        public SupportConfig Support { get; } = new();

        public int SaveCalls { get; private set; }

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return Task.CompletedTask;
        }

        public void UpdateOutput(Action<OutputConfig> mutator)
        {
            ArgumentNullException.ThrowIfNull(mutator);
            mutator(Output);
        }

        public void UpdateShow(Action<ShowConfig> mutator) => mutator(Show);

        public void UpdateStage(Action<StageConfig> mutator) => mutator(Stage);

        public void UpdateEditor(Action<EditorConfig> mutator) => mutator(Editor);

        public void UpdateReflow(Action<ReflowConfig> mutator) => mutator(Reflow);

        public void UpdateIntegrations(Action<IntegrationsConfig> mutator) => mutator(Integrations);

        public void UpdateAppearance(Action<AppearanceConfig> mutator) => mutator(Appearance);

        public void UpdateLibraryManagement(Action<LibraryManagementConfig> mutator) => mutator(LibraryManagement);

        public void UpdateSupport(Action<SupportConfig> mutator) => mutator(Support);
    }
}