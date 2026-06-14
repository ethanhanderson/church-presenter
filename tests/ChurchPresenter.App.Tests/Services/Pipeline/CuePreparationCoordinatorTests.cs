using ChurchPresenter.Backend.Pipeline.Cues;
using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Services.Media;
using ChurchPresenter.Services.Themes;

using FluentAssertions;

using Moq;

namespace ChurchPresenter.App.Tests.Services.Pipeline;

public sealed class CuePreparationCoordinatorTests
{
    [Fact]
    public async Task PrepareMediaAsync_reuses_warmed_cache_record()
    {
        PreparedCueCache cache = new();
        PreparedMediaCue cue = CreateMediaCue("media-1");
        PreparedCueRecord warmed = CreateMediaRecord("warmed-media-1");
        cache.Set(CreateMediaRequest("media-1"), warmed);
        CuePreparationCoordinator coordinator = CreateCoordinator(cache: cache);

        PreparedCueRecord? result = await coordinator.PrepareMediaAsync(cue);

        result.Should().BeSameAs(warmed);
    }

    [Fact]
    public async Task PrepareMediaAsync_preserves_media_transition_for_layer_router()
    {
        CuePreparationCoordinator coordinator = CreateCoordinator();

        PreparedCueRecord? result = await coordinator.PrepareMediaAsync(CreateMediaCue(
            "media-1",
            transition: new SlideTransition { Type = "cut", Duration = 0 }));

        result!.Transition!.Type.Should().Be("cut");
        result.Transition.Duration.Should().Be(0);
    }

    private static CuePreparationCoordinator CreateCoordinator(
        IPreparedCueCache? cache = null)
    {
        Mock<ICuePreparationService> cuePreparation = new(MockBehavior.Strict);
        Mock<ISlideSceneCompiler> slideSceneCompiler = new(MockBehavior.Strict);
        Mock<IThemeResolutionService> themeResolution = new(MockBehavior.Strict);

        return new CuePreparationCoordinator(
            cuePreparation.Object,
            slideSceneCompiler.Object,
            themeResolution.Object,
            cache ?? new PreparedCueCache(),
            new LiveInputCatalog(),
            new MaskAssetCatalog());
    }

    [Fact]
    public async Task PrepareAsync_live_video_builds_live_video_payload_from_catalog()
    {
        CuePreparationCoordinator coordinator = CreateCoordinator();
        CueRequest request = new()
        {
            CueKind = CueKind.LiveVideo,
            Layer = OutputLayerKind.LiveVideo,
            ContentRef = new ContentRef { Kind = ContentRefKind.MediaAsset, Id = LiveInputCatalog.DefaultCameraId },
        };

        PreparedCueRecord? result = await coordinator.PrepareAsync(request);

        result.Should().NotBeNull();
        result!.Layer.Should().Be(OutputLayerKind.LiveVideo);
        result.Payload.Kind.Should().Be(RenderPayloadKind.LiveVideo);
        result.Payload.Detail.Should().BeOfType<LiveVideoRenderPayload>()
            .Which.SourceId.Should().Be(LiveInputCatalog.DefaultCameraId);
    }

    [Fact]
    public async Task PrepareAsync_mask_builds_mask_payload_with_scene()
    {
        CuePreparationCoordinator coordinator = CreateCoordinator();
        CueRequest request = new()
        {
            CueKind = CueKind.Mask,
            Layer = OutputLayerKind.Mask,
            ContentRef = new ContentRef { Kind = ContentRefKind.Mask, Id = MaskAssetCatalog.FullFrameMaskId },
        };

        PreparedCueRecord? result = await coordinator.PrepareAsync(request);

        result.Should().NotBeNull();
        result!.Layer.Should().Be(OutputLayerKind.Mask);
        result.Payload.Kind.Should().Be(RenderPayloadKind.Mask);
        MaskRenderPayload mask = result.Payload.Detail.Should().BeOfType<MaskRenderPayload>().Subject;
        mask.MaskKind.Should().Be("vector");
        mask.Scene.Should().NotBeNull();
        mask.Scene!.Nodes.Should().ContainSingle();
    }

    [Fact]
    public async Task PrepareAsync_mask_falls_back_to_full_frame_for_unknown_id()
    {
        CuePreparationCoordinator coordinator = CreateCoordinator();
        CueRequest request = new()
        {
            CueKind = CueKind.Mask,
            Layer = OutputLayerKind.Mask,
            ContentRef = new ContentRef { Kind = ContentRefKind.Mask, Id = "mask:custom-circle" },
        };

        PreparedCueRecord? result = await coordinator.PrepareAsync(request);

        result.Should().NotBeNull();
        MaskRenderPayload mask = result!.Payload.Detail.Should().BeOfType<MaskRenderPayload>().Subject;
        mask.MaskId.Should().Be("mask:custom-circle");
        mask.Scene.Should().NotBeNull();
    }

    private static PreparedMediaCue CreateMediaCue(string mediaId, SlideTransition? transition = null) =>
        new()
        {
            Media = new OutputLayerMedia
            {
                MediaId = mediaId,
                DisplayName = mediaId,
                MediaType = "image",
                ResolvedSourcePath = $@"C:\media\{mediaId}.png",
                Transition = transition,
            },
        };

    private static CueRequest CreateMediaRequest(string mediaId) =>
        new()
        {
            CueKind = CueKind.Media,
            Layer = OutputLayerKind.Media,
            ContentRef = new ContentRef
            {
                Kind = ContentRefKind.MediaAsset,
                Id = mediaId,
                Version = $@"C:\media\{mediaId}.png",
            },
        };

    private static PreparedCueRecord CreateMediaRecord(string mediaId) =>
        new()
        {
            Layer = OutputLayerKind.Media,
            ContentRef = new ContentRef { Kind = ContentRefKind.MediaAsset, Id = mediaId },
            Payload = new RenderPayloadDescriptor
            {
                Id = mediaId,
                Kind = RenderPayloadKind.Image,
                DisplayName = mediaId,
            },
        };
}
