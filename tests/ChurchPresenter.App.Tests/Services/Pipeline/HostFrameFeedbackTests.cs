using ChurchPresenter.App.Tests.Reference;
using ChurchPresenter.App.Tests.TestSupport;
using ChurchPresenter.Backend.Media;
using ChurchPresenter.Backend.Output;
using ChurchPresenter.Backend.Overlays;
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
/// Verifies the host-to-backend frame-application feedback write-path. After every compose/apply
/// cycle, <c>CuePipelineRuntime</c> must record per-screen feedback (resolved/applied backend frame
/// sequence, apply timing, visibility) into <see cref="GeneratedStateSnapshot.HostFeedback"/> so the
/// operator diagnostics projection can surface stale or dropped frames. This is the contract that
/// makes Phase 4 host feedback observable rather than a no-op.
/// </summary>
[ReferenceArea(ReferenceAreas.OutputRouting)]
public sealed class HostFrameFeedbackTests
{
    [Fact]
    public async Task Applying_a_cue_with_audience_enabled_records_current_visible_feedback()
    {
        CuePipelineRuntime runtime = CreateRuntime();
        await runtime.SetAudienceEnabledAsync(true);

        await runtime.DispatchMediaCueAsync(CreateVideoCue(), MediaPlaybackLayerTargetNames.MediaUnderlay);

        OutputHostFrameFeedbackState feedback = ResolveMainFeedback(runtime);
        feedback.IsVisible.Should().BeTrue("the audience output is enabled");
        feedback.LastResolvedFrameSequence.Should().NotBeNull("a frame was composed for the audience screen");
        feedback.LastAppliedFrameSequence.Should().Be(
            feedback.LastResolvedFrameSequence,
            "an enabled screen applies the frame it resolved, so it is not stale");
        feedback.EndpointHealth.Should().Be(EndpointHealth.Connected);
    }

    [Fact]
    public async Task Applying_a_cue_with_audience_disabled_records_resolved_ahead_of_applied()
    {
        // Audience output defaults to disabled. The screen still resolves a frame, but the applied
        // sequence must not advance, so diagnostics read it as stale (resolved ahead of applied).
        CuePipelineRuntime runtime = CreateRuntime();

        await runtime.DispatchMediaCueAsync(CreateVideoCue(), MediaPlaybackLayerTargetNames.MediaUnderlay);

        OutputHostFrameFeedbackState feedback = ResolveMainFeedback(runtime);
        feedback.IsVisible.Should().BeFalse("the audience output is disabled");
        feedback.LastResolvedFrameSequence.Should().NotBeNull("the frame is still composed even when output is off");
        feedback.LastAppliedFrameSequence.Should().BeNull("a disabled screen never applies a frame");
    }

    private static OutputHostFrameFeedbackState ResolveMainFeedback(CuePipelineRuntime runtime)
    {
        IReadOnlyDictionary<string, OutputHostFrameFeedbackState> feedback =
            runtime.Snapshot.SessionState.GeneratedState.HostFeedback;
        feedback.Should().ContainKey(OutputFeedIds.Main);
        return feedback[OutputFeedIds.Main];
    }

    private static CuePipelineRuntime CreateRuntime()
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
            audienceAdapters: [new NoopAudienceHostAdapter()]);
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

    private sealed class NoopAudienceHostAdapter : IAudienceHostAdapter
    {
        public event EventHandler<HostHealthEvent>? HostHealthRaised
        {
            add { }
            remove { }
        }

        public Task EnsureSurfacesAsync(ScreenConfiguration configuration, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ShutdownAsync(string reason, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ApplyFrameAsync(AudienceRenderFrame frame, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IMediaSlotBinding?> BindMediaSlotAsync(
            LayerSlotId slotId,
            RenderPayloadDescriptor payload,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IMediaSlotBinding?>(null);
    }
}
