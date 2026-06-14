using System.Diagnostics;

using ChurchPresenter.App.Tests.TestSupport;
using ChurchPresenter.Backend.Media;
using ChurchPresenter.Backend.Output;
using ChurchPresenter.Backend.Pipeline.Cues;
using ChurchPresenter.Backend.Pipeline.HostAdapter;
using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Models.Media;
using ChurchPresenter.Models.Output;
using ChurchPresenter.Services.Pipeline;

using FluentAssertions;

using Moq;

namespace ChurchPresenter.App.Tests.Services.Pipeline;

/// <summary>
/// Benchmarks the take-live latency for video cues. The contract under test: once a media slot
/// has been warmed (decoder opened to its first frame), taking the cue live must start playback
/// in well under 50ms because the take-live path reuses the warm binding instead of cold-opening
/// the decoder. This mirrors the host-side <c>ProgramRenderService</c> behavior where
/// <c>BindMediaSlotAsync</c> preloads to ready and the apply path skips redundant rebinding.
/// </summary>
public sealed class MediaTakeLiveLatencyTests
{
    private const int InstantPlaybackBudgetMs = 50;

    /// <summary>The cold decoder open cost the warm path must avoid paying on take-live.</summary>
    private static readonly TimeSpan ColdDecoderOpenCost = TimeSpan.FromMilliseconds(250);

    [Fact]
    public async Task Prewarmed_media_cue_starts_playing_in_under_50ms()
    {
        WarmingAudienceHostAdapter adapter = new(ColdDecoderOpenCost);
        CuePipelineRuntime runtime = CreateRuntime(adapter);
        PreparedMediaCue cue = CreateVideoCue();

        // Prewarm: first take-live pays the one-time cold decoder open. This models startup warmup
        // or arming the cue from the media drawer / slide selection before the operator takes it live.
        await runtime.DispatchMediaCueAsync(cue, MediaPlaybackLayerTargetNames.MediaUnderlay);

        WarmingMediaSlotBinding binding = adapter.Bindings.Values.Should().ContainSingle().Subject;
        binding.IsReady.Should().BeTrue();
        await binding.PauseAsync();

        // Take live again from the warm slot.
        Stopwatch watch = Stopwatch.StartNew();
        await runtime.DispatchMediaCueAsync(cue, MediaPlaybackLayerTargetNames.MediaUnderlay);
        watch.Stop();

        binding.IsPlaying.Should().BeTrue("the warm cue should be playing once taken live");
        watch.Elapsed.TotalMilliseconds.Should().BeLessThan(
            InstantPlaybackBudgetMs,
            "a prewarmed video cue must start playing instantly without re-opening the decoder");
    }

    [Fact]
    public async Task Take_live_reuses_warm_binding_without_reopening_decoder()
    {
        WarmingAudienceHostAdapter adapter = new(ColdDecoderOpenCost);
        CuePipelineRuntime runtime = CreateRuntime(adapter);
        PreparedMediaCue cue = CreateVideoCue();

        await runtime.DispatchMediaCueAsync(cue, MediaPlaybackLayerTargetNames.MediaUnderlay);
        await runtime.DispatchMediaCueAsync(cue, MediaPlaybackLayerTargetNames.MediaUnderlay);
        await runtime.DispatchMediaCueAsync(cue, MediaPlaybackLayerTargetNames.MediaUnderlay);

        WarmingMediaSlotBinding binding = adapter.Bindings.Values.Should().ContainSingle().Subject;
        binding.ColdOpenCount.Should().Be(1, "the decoder should only be cold-opened once and reused thereafter");
    }

    private static CuePipelineRuntime CreateRuntime(WarmingAudienceHostAdapter adapter)
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

        return PipelineTestRuntime.Create(
            preparation: preparation,
            topology: CreateAudienceTopology(),
            audienceAdapters: [adapter]);
    }

    private static PreparedMediaCue CreateVideoCue() =>
        new()
        {
            Media = new OutputLayerMedia
            {
                MediaId = @"C:\media\walk-in.mp4",
                DisplayName = "Walk In",
                MediaType = "video",
                ResolvedSourcePath = @"C:\media\walk-in.mp4",
                Loop = true,
            },
        };

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

    /// <summary>
    /// Audience host adapter that models the real warm-then-reuse media slot lifecycle: the first
    /// bind of a (slot, source) pays a cold decoder-open cost, later binds reuse the warm slot.
    /// </summary>
    private sealed class WarmingAudienceHostAdapter(TimeSpan coldOpenCost) : IAudienceHostAdapter
    {
        public event EventHandler<HostHealthEvent>? HostHealthRaised
        {
            add { }
            remove { }
        }

        public Dictionary<LayerSlotId, WarmingMediaSlotBinding> Bindings { get; } = new();

        public Task EnsureSurfacesAsync(ScreenConfiguration configuration, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ShutdownAsync(string reason, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ApplyFrameAsync(AudienceRenderFrame frame, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public async Task<IMediaSlotBinding?> BindMediaSlotAsync(
            LayerSlotId slotId,
            RenderPayloadDescriptor payload,
            CancellationToken cancellationToken = default)
        {
            if (payload.Detail is not MediaRenderPayload media
                || string.IsNullOrWhiteSpace(media.Media.ResolvedSourcePath))
            {
                return null;
            }

            if (!Bindings.TryGetValue(slotId, out WarmingMediaSlotBinding? binding))
            {
                binding = new WarmingMediaSlotBinding(slotId, payload.Id);
                Bindings[slotId] = binding;
            }

            await binding
                .WarmAsync(media.Media.ResolvedSourcePath, media.Media.Loop, coldOpenCost, cancellationToken)
                .ConfigureAwait(false);
            await binding.PlayAsync(cancellationToken).ConfigureAwait(false);
            return binding;
        }
    }

    private sealed class WarmingMediaSlotBinding(LayerSlotId slotId, string playerId) : IMediaSlotBinding
    {
        private string? _boundSource;
        private bool _boundLoop;

        public LayerSlotId SlotId { get; } = slotId;

        public event EventHandler? StateChanged;

        public string PlayerId { get; } = playerId;

        public bool IsReady { get; private set; }

        public bool IsPlaying { get; private set; }

        public TimeSpan Position { get; private set; }

        public TimeSpan? Duration => TimeSpan.FromSeconds(60);

        public double LoopGapMs => 0;

        public int ColdOpenCount { get; private set; }

        public async Task WarmAsync(string sourcePath, bool loop, TimeSpan coldOpenCost, CancellationToken cancellationToken)
        {
            bool isWarm = IsReady
                && string.Equals(_boundSource, sourcePath, StringComparison.OrdinalIgnoreCase)
                && _boundLoop == loop;
            if (isWarm)
                return;

            // Cold open: decode to first frame. Warm/prewarmed slots skip this entirely.
            await Task.Delay(coldOpenCost, cancellationToken).ConfigureAwait(false);
            ColdOpenCount++;
            _boundSource = sourcePath;
            _boundLoop = loop;
            IsReady = true;
            Position = TimeSpan.Zero;
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
