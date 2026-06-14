using ChurchPresenter.App.Tests.TestSupport;
using ChurchPresenter.Backend.Commands;
using ChurchPresenter.Backend.Output;
using ChurchPresenter.Backend.Pipeline.Cues;
using ChurchPresenter.Backend.Pipeline.Routing;
using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Models.Output;

using FluentAssertions;

using Moq;

using BackendOutputLayerKind = ChurchPresenter.Backend.Rendering.OutputLayerKind;

namespace ChurchPresenter.App.Tests.Reference;

/// <summary>
/// Layer routing and live-session rules from <c>docs/architecture/backend-application.md</c>:
/// slide cues never modify the media slot and vice versa.
/// </summary>
[ReferenceArea(ReferenceAreas.LivePipeline)]
public sealed class LivePipelineReferenceTests
{
    [Fact]
    public void Slide_take_updates_only_slide_slot_and_leaves_media_playing()
    {
        LayerRouter router = CreateRouter();
        LiveSessionSnapshot snapshot = router.ApplyPreparedCue(
            LiveSessionSnapshot.CreateEmpty(),
            CreateMediaPreparedCue("walk-in"));
        snapshot = router.ApplyPreparedCue(snapshot, CreateSlidePreparedCue("slide-2"));

        snapshot.LayerSlots[BackendOutputLayerKind.Slide].PreparedCue!.ContentRef.Id.Should().Be("slide-2");
        snapshot.LayerSlots[BackendOutputLayerKind.Media].PreparedCue!.ContentRef.Id.Should().Be("walk-in");
        snapshot.SessionState.Layers[BackendOutputLayerKind.Media].IsCleared.Should().BeFalse();
        snapshot.SessionState.Layers[BackendOutputLayerKind.Media].Payload.Should().NotBeNull();
    }

    [Fact]
    public void Media_take_updates_only_media_slot_and_preserves_slide_payload()
    {
        LayerRouter router = CreateRouter();
        LiveSessionSnapshot snapshot = router.ApplyPreparedCue(
            LiveSessionSnapshot.CreateEmpty(),
            CreateSlidePreparedCue("slide-1"));
        snapshot = router.ApplyPreparedCue(snapshot, CreateMediaPreparedCue("announcement"));

        snapshot.SessionState.Layers[BackendOutputLayerKind.Slide].Payload!.Id.Should().Be("slide-1");
        snapshot.SessionState.Layers[BackendOutputLayerKind.Media].Payload!.Id.Should().Be("announcement");
        snapshot.LayerSlots[BackendOutputLayerKind.Slide].ClearOverride.Should().BeFalse();
    }

    [Fact]
    public void Clear_slide_slot_does_not_clear_media_slot()
    {
        LayerRouter router = CreateRouter();
        LiveSessionSnapshot snapshot = router.ApplyPreparedCue(
            LiveSessionSnapshot.CreateEmpty(),
            CreateSlidePreparedCue("slide-1"));
        snapshot = router.ApplyPreparedCue(snapshot, CreateMediaPreparedCue("walk-in"));

        LiveSessionSnapshot cleared = router.ClearLayers(snapshot, [BackendOutputLayerKind.Slide]);

        cleared.LayerSlots[BackendOutputLayerKind.Slide].ClearOverride.Should().BeTrue();
        cleared.SessionState.Layers[BackendOutputLayerKind.Slide].IsCleared.Should().BeTrue();
        cleared.SessionState.Layers[BackendOutputLayerKind.Media].IsCleared.Should().BeFalse();
        cleared.SessionState.Layers[BackendOutputLayerKind.Media].Payload.Should().NotBeNull();
    }

    private static LayerRouter CreateRouter()
    {
        Mock<IOutputRoutingService> routing = PipelineTestRuntime.CreateDefaultRouting();
        BackendRenderFrameResolver frameResolver = new();
        BackendRenderEngine renderEngine = new(frameResolver);
        LiveCommandExecutor commandExecutor = new(renderEngine);
        return new LayerRouter(renderEngine, commandExecutor, routing.Object, new ShowTransitionDefaults());
    }

    private static PreparedCueRecord CreateSlidePreparedCue(string slideId) =>
        new()
        {
            Layer = BackendOutputLayerKind.Slide,
            ContentRef = new ContentRef { Kind = ContentRefKind.Slide, Id = slideId },
            Payload = new RenderPayloadDescriptor
            {
                Id = slideId,
                Kind = RenderPayloadKind.Presentation,
                DisplayName = slideId,
            },
        };

    private static PreparedCueRecord CreateMediaPreparedCue(string mediaId) =>
        new()
        {
            Layer = BackendOutputLayerKind.Media,
            ContentRef = new ContentRef { Kind = ContentRefKind.MediaAsset, Id = mediaId },
            Payload = new RenderPayloadDescriptor
            {
                Id = mediaId,
                Kind = RenderPayloadKind.Video,
                DisplayName = mediaId,
            },
        };
}
