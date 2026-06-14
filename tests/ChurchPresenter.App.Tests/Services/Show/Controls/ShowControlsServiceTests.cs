using ChurchPresenter.App.Tests.TestSupport;
using ChurchPresenter.Backend.Commands;
using ChurchPresenter.Backend.Media;
using ChurchPresenter.Backend.Overlays;
using ChurchPresenter.Backend.Stage;
using ChurchPresenter.Services.Pipeline;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

namespace ChurchPresenter.App.Tests.Services.Show.Controls;

public sealed class ShowControlsServiceTests
{
    [Fact]
    public async Task SaveAudioPlaylistAsync_persists_portable_audio_bin_definition()
    {
        TestContext context = await TestContext.CreateAsync();
        ShowControlsService service = context.CreateService();

        ShowAudioPlaylistDefinition saved = await service.SaveAudioPlaylistAsync(new ShowAudioPlaylistDefinition
        {
            Name = "Walk In",
            ItemIds = ["track-1"],
            Shuffle = true,
            TransitionSeconds = 1.25,
        });

        await context.SharedConfig.LoadAsync();
        ShowAudioPlaylistDefinition playlist = context.SharedConfig.Show.AudioPlaylists.Should().ContainSingle().Subject;
        playlist.Id.Should().Be(saved.Id);
        playlist.Name.Should().Be("Walk In");
        playlist.ItemIds.Should().Equal("track-1");
        playlist.Shuffle.Should().BeTrue();
        playlist.TransitionSeconds.Should().Be(1.25);
    }

    [Fact]
    public async Task ShowMessageAsync_resolves_runtime_text_and_publishes_message_overlay()
    {
        TestContext context = await TestContext.CreateAsync();
        ShowControlsService service = context.CreateService();
        ShowMessageDefinition message = await service.SaveMessageAsync(new ShowMessageDefinition
        {
            Name = "Welcome",
            Template = "Welcome [name]",
            Tokens =
            [
                new ShowMessageTokenDefinition { Id = "name", Name = "name", DefaultValue = "guest" },
            ],
        });

        bool shown = await service.ShowMessageAsync(message.Id, [new ShowMessageRuntimeTokenValue("name", "NCBF")]);

        shown.Should().BeTrue();
        context.CueDispatcher.LastOverlay.Should().NotBeNull();
        context.CueDispatcher.LastOverlay!.Kind.Should().Be(OverlayContentKind.Message);
        context.CueDispatcher.LastOverlay.IsVisible.Should().BeTrue();
        context.CueDispatcher.LastOverlay.Text.Should().Be("Welcome NCBF");
    }

    [Fact]
    public async Task TogglePropAsync_publishes_prop_overlay_and_macro_can_reuse_it()
    {
        TestContext context = await TestContext.CreateAsync();
        ShowControlsService service = context.CreateService();
        ShowPropDefinition prop = await service.SavePropAsync(new ShowPropDefinition
        {
            Name = "Lower Third",
            Text = "Prayer Team",
        });
        ShowMacroDefinition macro = await service.SaveMacroAsync(new ShowMacroDefinition
        {
            Name = "Prayer Moment",
            Commands =
            [
                new ShowMacroCommandDefinition
                {
                    Id = "show-prop",
                    Kind = "prop",
                    TargetId = prop.Id,
                },
            ],
        });

        bool toggled = await service.TogglePropAsync(prop.Id);
        bool executed = await service.ExecuteMacroAsync(macro.Id);

        toggled.Should().BeTrue();
        context.CueDispatcher.LastOverlay.Should().NotBeNull();
        context.CueDispatcher.LastOverlay!.Kind.Should().Be(OverlayContentKind.Prop);
        context.CueDispatcher.LastOverlay.IsVisible.Should().BeTrue();
        executed.Should().BeTrue();
        context.CueDispatcher.LastMacro.Should().NotBeNull();
        LiveCommand command = context.CueDispatcher.LastMacro!.Commands.Should().ContainSingle().Subject;
        command.Kind.Should().Be(LiveCommandKind.SetOverlayState);
        command.Overlay.Should().NotBeNull();
        command.Overlay!.Kind.Should().Be(OverlayContentKind.Prop);
        command.Overlay.Id.Should().Be(prop.Id);
    }

    private sealed class TestContext
    {
        public required SharedConfigService SharedConfig { get; init; }

        public required SettingsService Settings { get; init; }

        public required FakeCueDispatcher CueDispatcher { get; init; }

        public required CuePipelineRuntime Runtime { get; init; }

        public static async Task<TestContext> CreateAsync()
        {
            string root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
            var paths = TestContentPaths.Create(root);
            await paths.Object.EnsureDocumentsLayoutAsync();
            SharedConfigService sharedConfig = new(paths.Object, NullLogger<SharedConfigService>.Instance);
            SettingsService settings = new(paths.Object, sharedConfig, new MachineStateService(paths.Object, NullLogger<MachineStateService>.Instance), NullLogger<SettingsService>.Instance);
            await sharedConfig.LoadAsync();
            await settings.LoadAsync();

            FakeCueDispatcher cueDispatcher = new();
            CuePipelineRuntime runtime = PipelineTestRuntime.Create();

            return new TestContext
            {
                SharedConfig = sharedConfig,
                Settings = settings,
                CueDispatcher = cueDispatcher,
                Runtime = runtime,
            };
        }

        public ShowControlsService CreateService() =>
            new(
                SharedConfig,
                Settings,
                new FakeMediaLibraryService(),
                new FakeCuePreparationService(),
                CueDispatcher,
                new FakeLiveSessionQueryService(),
                Runtime,
                new FakeOutputTopologyService(),
                new FakeStageLayoutRegistryService());
    }

    private sealed class FakeLiveSessionQueryService : ILiveSessionQueryService
    {
        public event EventHandler<LiveSessionQueryChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public LiveSessionQuerySnapshot Current { get; } = LiveSessionQuerySnapshot.Empty;
    }

    private sealed class FakeOutputTopologyService : IOutputTopologyService
    {
        public IReadOnlyList<OutputFeedDefinition> AudienceScreens { get; } = [];

        public OutputTopologySnapshot GetSnapshot() => new();
    }

    private sealed class FakeStageLayoutRegistryService : IStageLayoutRegistryService
    {
        public IReadOnlyDictionary<string, StageLayout> GetLayouts() => new Dictionary<string, StageLayout>();

        public IReadOnlyDictionary<string, string> GetDefaultLayoutIdsByScreenId() => new Dictionary<string, string>();
    }

    private sealed class FakeMediaLibraryService : IMediaLibraryService
    {
        public Task<IReadOnlyList<MediaPlaylistManifest>> GetPlaylistsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<MediaPlaylistManifest>>([]);

        public Task<IReadOnlyList<MediaLibraryItem>> GetRootItemsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<MediaLibraryItem>>([]);

        public Task<IReadOnlyList<MediaAsset>> GetAssetsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<MediaAsset>>([]);

        public Task<MediaAsset?> ResolveAssetAsync(string assetIdOrPath, CancellationToken ct = default) =>
            Task.FromResult<MediaAsset?>(null);

        public Task<MediaPlaybackRequest?> ResolvePlaybackRequestAsync(SlideMediaCue cue, string? ownerReferenceId = null, CancellationToken ct = default) =>
            Task.FromResult<MediaPlaybackRequest?>(null);

        public Task<MediaCleanupReferenceGraph> BuildCleanupReferenceGraphAsync(IEnumerable<PresentationProject> presentations, IEnumerable<MediaReferenceNode>? additionalNodes = null, CancellationToken ct = default) =>
            Task.FromResult(new MediaCleanupReferenceGraph());

        public string ResolveStoredMediaPath(string? storedPath) => storedPath ?? string.Empty;

        public Task<MediaMigrationResult> MigrateExternalMediaToManagedStorageAsync(CancellationToken ct = default) =>
            Task.FromResult(new MediaMigrationResult());

        public Task<MediaLinkStatistics> GetMediaLinkStatisticsAsync(CancellationToken ct = default) =>
            Task.FromResult(new MediaLinkStatistics());

        public Task<MediaPlaylistManifest?> GetPlaylistAsync(string playlistId, CancellationToken ct = default) =>
            Task.FromResult<MediaPlaylistManifest?>(null);

        public Task<MediaPlaylistManifest> CreatePlaylistAsync(string name, CancellationToken ct = default) =>
            Task.FromResult(new MediaPlaylistManifest { Id = Guid.NewGuid().ToString("N"), Name = name });

        public Task<bool> RenamePlaylistAsync(string playlistId, string newName, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<bool> DeletePlaylistAsync(string playlistId, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<MediaLibraryItem> AddItemAsync(string playlistId, string filePath, CancellationToken ct = default) =>
            Task.FromResult(new MediaLibraryItem());

        public Task<MediaLibraryItem> AddRootItemAsync(string filePath, CancellationToken ct = default) =>
            Task.FromResult(new MediaLibraryItem());

        public Task<bool> RemoveItemAsync(string playlistId, string itemId, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<bool> RemoveRootItemAsync(string itemId, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<bool> RenameItemAsync(string? playlistId, string itemId, string newName, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<MediaLibraryItem?> DuplicateItemAsync(string? playlistId, string itemId, CancellationToken ct = default) =>
            Task.FromResult<MediaLibraryItem?>(null);

        public Task<bool> UpdateItemCueDefaultsAsync(string? playlistId, string itemId, MediaCueDefaults defaults, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<bool> UpdateMediaItemFileMetadataAsync(string itemId, double? durationSeconds, int? width, int? height, CancellationToken ct = default) =>
            Task.FromResult(false);
    }

    private sealed class FakeCuePreparationService : ICuePreparationService
    {
        public Task<PreparedSlideCue?> PrepareSlideCueAsync(string? presentationPath, string slideId, string? instanceKey = null, PresentationDocument? fallbackDocument = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<PreparedSlideCue?>(null);

        public PreparedSlideCue? GetPreparedSlideCue(string? presentationPath, string slideId, string? instanceKey = null) => null;

        public void InvalidatePresentationCues(string? presentationPath)
        {
        }

        public PreparedMediaCue? PrepareMediaCue(MediaLibraryItem item) => null;

        public Task<PreparedSlideCue?> PreparePreviewSlideCueAsync(
            PresentationDocument document,
            PresentationSlide slide,
            string? instanceKey = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<PreparedSlideCue?>(null);
    }
}
