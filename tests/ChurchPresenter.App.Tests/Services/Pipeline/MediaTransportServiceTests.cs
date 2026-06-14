using ChurchPresenter.App.Tests.TestSupport;
using ChurchPresenter.Backend.Media;
using ChurchPresenter.Backend.Output;
using ChurchPresenter.Backend.Pipeline.Cues;
using ChurchPresenter.Backend.Pipeline.HostAdapter;
using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Models.Media;
using ChurchPresenter.Models.Output;
using ChurchPresenter.Services.Media;
using ChurchPresenter.Services.Pipeline;

using FluentAssertions;

using Moq;

using BackendOutputLayerKind = ChurchPresenter.Backend.Rendering.OutputLayerKind;

namespace ChurchPresenter.App.Tests.Services.Pipeline;

public sealed class MediaTransportServiceTests
{
    [Fact]
    public async Task GetDisplay_filters_state_by_selected_playback_target()
    {
        Mock<ICuePreparationCoordinator> preparation = new();
        preparation
            .Setup(service => service.PrepareMediaAsync(It.IsAny<PreparedMediaCue>(), It.IsAny<CancellationToken>()))
            .Returns<PreparedMediaCue, CancellationToken>((cue, _) =>
                Task.FromResult<PreparedCueRecord?>(new PreparedCueRecord
                {
                    Layer = cue.LayerKind,
                    ContentRef = new ContentRef { Kind = ContentRefKind.MediaAsset, Id = cue.Media.MediaId },
                    Payload = new RenderPayloadDescriptor
                    {
                        Id = cue.Media.MediaId,
                        Kind = RenderPayloadKind.Video,
                        DisplayName = cue.Media.DisplayName ?? string.Empty,
                        SourceReference = cue.Target,
                        Detail = new MediaRenderPayload
                        {
                            Media = cue.Media,
                            Target = cue.Target,
                        },
                    },
                }));
        preparation
            .Setup(service => service.PrepareSlideAsync(It.IsAny<PreparedSlideCue>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PreparedCueRecord
            {
                Layer = BackendOutputLayerKind.Announcements,
                ContentRef = new ContentRef { Kind = ContentRefKind.Slide, Id = "announcement-1" },
                Payload = new RenderPayloadDescriptor
                {
                    Id = "announcement-1",
                    Kind = RenderPayloadKind.Presentation,
                    DisplayName = "Announcement Slide",
                    Detail = new PresentationRenderPayload
                    {
                        PresentationPath = @"C:\shows\Pre Service Loop.cpres",
                        SlideId = "announcement-1",
                    },
                },
            });

        FakeAudienceHostAdapter adapter = new();
        CuePipelineRuntime runtime = PipelineTestRuntime.Create(
            preparation: preparation,
            topology: CreateAudienceTopology(),
            audienceAdapters: [adapter]);
        MediaTransportService service = new(runtime);
        await runtime.DispatchMediaCueAsync(new PreparedMediaCue
        {
            Media = new OutputLayerMedia
            {
                MediaId = @"C:\media\walk-in.mp4",
                DisplayName = string.Empty,
                MediaType = "video",
                ResolvedSourcePath = @"C:\media\walk-in.mp4",
            },
        }, MediaPlaybackLayerTargetNames.MediaUnderlay);
        await runtime.DispatchSlideCueAsync(new PreparedSlideCue
        {
            LayerKind = BackendOutputLayerKind.Announcements,
            PresentationPath = @"C:\shows\Pre Service Loop.cpres",
            SlideId = "announcement-1",
        });

        TransportDisplaySnapshot media = service.GetDisplay(MediaPlaybackTarget.MediaFiles);
        TransportDisplaySnapshot audio = service.GetDisplay(MediaPlaybackTarget.AudioFiles);
        TransportDisplaySnapshot announcement = service.GetDisplay(MediaPlaybackTarget.Announcements);

        media.HasActiveCue.Should().BeTrue();
        media.IsEnabled.Should().BeTrue();
        media.Name.Should().Be("walk-in");
        audio.HasActiveCue.Should().BeFalse();
        announcement.HasActiveCue.Should().BeTrue();
        announcement.Name.Should().Be("Pre Service Loop");
    }

    [Fact]
    public async Task Transport_commands_control_bound_media_layer_players()
    {
        Mock<ICuePreparationCoordinator> preparation = new();
        preparation
            .Setup(service => service.PrepareMediaAsync(It.IsAny<PreparedMediaCue>(), It.IsAny<CancellationToken>()))
            .Returns<PreparedMediaCue, CancellationToken>((cue, _) =>
                Task.FromResult<PreparedCueRecord?>(new PreparedCueRecord
                {
                    Layer = cue.LayerKind,
                    ContentRef = new ContentRef { Kind = ContentRefKind.MediaAsset, Id = cue.Media.MediaId },
                    Payload = new RenderPayloadDescriptor
                    {
                        Id = cue.Media.MediaId,
                        Kind = RenderPayloadKind.Video,
                        DisplayName = "Walk In",
                        SourceReference = cue.Target,
                        Detail = new MediaRenderPayload
                        {
                            Media = cue.Media,
                            Target = cue.Target,
                        },
                    },
                }));
        FakeAudienceHostAdapter adapter = new();
        CuePipelineRuntime runtime = PipelineTestRuntime.Create(
            preparation: preparation,
            topology: CreateAudienceTopology(),
            audienceAdapters: [adapter]);
        MediaTransportService service = new(runtime);

        await runtime.DispatchMediaCueAsync(new PreparedMediaCue
        {
            Media = new OutputLayerMedia
            {
                MediaId = @"C:\media\walk-in.mp4",
                DisplayName = "Walk In",
                MediaType = "video",
                ResolvedSourcePath = @"C:\media\walk-in.mp4",
            },
        }, MediaPlaybackLayerTargetNames.MediaUnderlay);

        FakeMediaSlotBinding binding = adapter.Bindings.Values.Should().ContainSingle().Subject;
        binding.IsPlaying.Should().BeTrue();
        binding.Position = TimeSpan.FromSeconds(10);

        await service.PauseAsync(MediaPlaybackTarget.MediaFiles);
        binding.IsPlaying.Should().BeFalse();

        await service.PlayAsync(MediaPlaybackTarget.MediaFiles);
        binding.IsPlaying.Should().BeTrue();

        await service.SeekForwardAsync(MediaPlaybackTarget.MediaFiles);
        binding.Position.Should().Be(TimeSpan.FromSeconds(15));

        await service.RestartAsync(MediaPlaybackTarget.MediaFiles);
        binding.Position.Should().Be(TimeSpan.Zero);
        binding.IsPlaying.Should().BeTrue();
    }

    [Fact]
    public async Task Retaking_same_media_layer_restarts_paused_binding()
    {
        Mock<ICuePreparationCoordinator> preparation = new();
        preparation
            .Setup(service => service.PrepareMediaAsync(It.IsAny<PreparedMediaCue>(), It.IsAny<CancellationToken>()))
            .Returns<PreparedMediaCue, CancellationToken>((cue, _) =>
                Task.FromResult<PreparedCueRecord?>(new PreparedCueRecord
                {
                    Layer = cue.LayerKind,
                    ContentRef = new ContentRef { Kind = ContentRefKind.MediaAsset, Id = cue.Media.MediaId },
                    Payload = new RenderPayloadDescriptor
                    {
                        Id = cue.Media.MediaId,
                        Kind = RenderPayloadKind.Video,
                        DisplayName = "Walk In",
                        SourceReference = cue.Target,
                        Detail = new MediaRenderPayload
                        {
                            Media = cue.Media,
                            Target = cue.Target,
                        },
                    },
                }));
        FakeAudienceHostAdapter adapter = new();
        CuePipelineRuntime runtime = PipelineTestRuntime.Create(
            preparation: preparation,
            topology: CreateAudienceTopology(),
            audienceAdapters: [adapter]);
        PreparedMediaCue cue = new()
        {
            Media = new OutputLayerMedia
            {
                MediaId = @"C:\media\walk-in.mp4",
                DisplayName = "Walk In",
                MediaType = "video",
                ResolvedSourcePath = @"C:\media\walk-in.mp4",
            },
        };

        await runtime.DispatchMediaCueAsync(cue, MediaPlaybackLayerTargetNames.MediaUnderlay);
        FakeMediaSlotBinding binding = adapter.Bindings.Values.Should().ContainSingle().Subject;
        await binding.PauseAsync();

        await runtime.DispatchMediaCueAsync(cue, MediaPlaybackLayerTargetNames.MediaUnderlay);

        binding.IsPlaying.Should().BeTrue();
        binding.BindCount.Should().Be(2);
    }

    private static Mock<IOutputTopologyService> CreateAudienceTopology()
    {
        Mock<IOutputTopologyService> topology = new();
        topology.Setup(service => service.GetSnapshot()).Returns(new OutputTopologySnapshot
        {
            Screens = new Dictionary<string, OutputScreen>(StringComparer.OrdinalIgnoreCase)
            {
                [OutputFeedIds.Main] = new OutputScreen
                {
                    Id = OutputFeedIds.Main,
                    Name = "Main",
                    Kind = OutputScreenKind.Audience,
                },
            },
        });
        return topology;
    }

    private sealed class FakeAudienceHostAdapter : IAudienceHostAdapter
    {
        public event EventHandler<HostHealthEvent>? HostHealthRaised
        {
            add { }
            remove { }
        }

        public Dictionary<LayerSlotId, FakeMediaSlotBinding> Bindings { get; } = new();

        public Task EnsureSurfacesAsync(ScreenConfiguration configuration, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ShutdownAsync(string reason, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ApplyFrameAsync(AudienceRenderFrame frame, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IMediaSlotBinding?> BindMediaSlotAsync(
            LayerSlotId slotId,
            RenderPayloadDescriptor payload,
            CancellationToken cancellationToken = default)
        {
            if (!Bindings.TryGetValue(slotId, out FakeMediaSlotBinding? binding))
            {
                binding = new FakeMediaSlotBinding(slotId, payload.Id);
                Bindings[slotId] = binding;
            }

            binding.Bind(payload);
            return Task.FromResult<IMediaSlotBinding?>(binding);
        }
    }

    private sealed class FakeMediaSlotBinding(LayerSlotId slotId, string playerId) : IMediaSlotBinding
    {
        public LayerSlotId SlotId { get; } = slotId;

        public event EventHandler? StateChanged;

        public string PlayerId { get; } = playerId;

        public bool IsReady { get; private set; }

        public bool IsPlaying { get; private set; }

        public TimeSpan Position { get; set; }

        public TimeSpan? Duration { get; private set; } = TimeSpan.FromSeconds(60);

        public double LoopGapMs { get; private set; }

        public int BindCount { get; private set; }

        public void Bind(RenderPayloadDescriptor payload)
        {
            _ = payload;
            BindCount++;
            IsReady = true;
            IsPlaying = true;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public Task PlayAsync(CancellationToken cancellationToken = default)
        {
            IsPlaying = true;
            StateChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task PauseAsync(CancellationToken cancellationToken = default)
        {
            IsPlaying = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
        {
            Position = position;
            StateChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task RestartAsync(CancellationToken cancellationToken = default)
        {
            Position = TimeSpan.Zero;
            IsPlaying = true;
            StateChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
