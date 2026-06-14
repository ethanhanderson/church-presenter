using ChurchPresenter.App.Tests.Reference;
using ChurchPresenter.Backend.Output;
using ChurchPresenter.Backend.Pipeline.Rendering;
using ChurchPresenter.Backend.Pipeline.Routing;
using ChurchPresenter.Backend.Rendering;

using FluentAssertions;

namespace ChurchPresenter.App.Tests.Services.Pipeline;

[ReferenceArea(ReferenceAreas.OutputRouting)]
public sealed class FrameComposerTests
{
    [Fact]
    public void Compose_produces_audience_frames_for_default_snapshot()
    {
        BackendRenderFrameResolver frameResolver = new();
        FrameComposer composer = new(frameResolver);
        LiveSessionSnapshot snapshot = LiveSessionSnapshot.CreateEmpty() with
        {
            SessionState = LiveSessionSnapshot.CreateEmpty().SessionState with
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
                ActiveLook = new LookPreset
                {
                    Id = "default",
                    Name = "Default",
                    ScreenRoutes =
                    [
                        new ScreenLayerRouting
                        {
                            ScreenId = OutputFeedIds.Main,
                            Layers =
                            [
                                new LayerRoute { LayerKind = OutputLayerKind.Slide, IsEnabled = true },
                            ],
                        },
                    ],
                },
            },
        };

        RenderFrameSet frames = composer.Compose(snapshot);

        frames.AudienceFrames.Should().ContainKey(OutputFeedIds.Main);
        frames.AudienceFrames[OutputFeedIds.Main].ScreenId.Should().Be(OutputFeedIds.Main);
    }

    [Fact]
    public void Compose_orders_audience_layers_in_documented_output_stack()
    {
        BackendRenderFrameResolver frameResolver = new();
        FrameComposer composer = new(frameResolver);
        Dictionary<OutputLayerKind, LayerState> layers = new(LiveRenderSessionState.CreateEmptyLayers())
        {
            [OutputLayerKind.Audio] = CreateLiveLayer(OutputLayerKind.Audio),
            [OutputLayerKind.Messages] = CreateLiveLayer(OutputLayerKind.Messages),
            [OutputLayerKind.Props] = CreateLiveLayer(OutputLayerKind.Props),
            [OutputLayerKind.Announcements] = CreateLiveLayer(OutputLayerKind.Announcements),
            [OutputLayerKind.Slide] = CreateLiveLayer(OutputLayerKind.Slide),
            [OutputLayerKind.Media] = CreateLiveLayer(OutputLayerKind.Media),
            [OutputLayerKind.LiveVideo] = CreateLiveLayer(OutputLayerKind.LiveVideo),
        };
        LiveSessionSnapshot snapshot = LiveSessionSnapshot.CreateEmpty() with
        {
            SessionState = LiveSessionSnapshot.CreateEmpty().SessionState with
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
                Layers = layers,
            },
        };

        RenderFrameSet frames = composer.Compose(snapshot);

        frames.AudienceFrames[OutputFeedIds.Main].Layers.Select(static layer => layer.Kind).Should().Equal(
            OutputLayerKind.LiveVideo,
            OutputLayerKind.Media,
            OutputLayerKind.Slide,
            OutputLayerKind.Announcements,
            OutputLayerKind.Props,
            OutputLayerKind.Messages,
            OutputLayerKind.Audio);
    }

    [Fact]
    public void Compose_keeps_cleared_prior_payload_for_host_clear_transition()
    {
        BackendRenderFrameResolver frameResolver = new();
        FrameComposer composer = new(frameResolver);
        LayerTransitionState clearTransition = new()
        {
            Type = "fade",
            Duration = TimeSpan.FromMilliseconds(300),
            Phase = LayerTransitionPhase.Pending,
        };
        RenderPayloadDescriptor priorPayload = new()
        {
            Id = "slide-1",
            Kind = RenderPayloadKind.Presentation,
            DisplayName = "Slide 1",
        };
        LiveSessionSnapshot snapshot = LiveSessionSnapshot.CreateEmpty() with
        {
            SessionState = LiveSessionSnapshot.CreateEmpty().SessionState with
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
                Layers = new Dictionary<OutputLayerKind, LayerState>(LiveRenderSessionState.CreateEmptyLayers())
                {
                    [OutputLayerKind.Slide] = new LayerState
                    {
                        Kind = OutputLayerKind.Slide,
                        IsCleared = true,
                        ClearState = LayerClearState.Cleared,
                    },
                },
            },
            LayerSlots = new Dictionary<OutputLayerKind, LayerSlot>(LiveSessionSnapshot.CreateEmpty().LayerSlots)
            {
                [OutputLayerKind.Slide] = new LayerSlot
                {
                    Kind = OutputLayerKind.Slide,
                    ClearOverride = true,
                    PriorPayload = priorPayload,
                    Transition = clearTransition,
                    State = new LayerState
                    {
                        Kind = OutputLayerKind.Slide,
                        IsCleared = true,
                        ClearState = LayerClearState.Cleared,
                    },
                },
            },
        };

        RenderFrameSet frames = composer.Compose(snapshot);

        RenderLayerDescriptor layer = frames.AudienceFrames[OutputFeedIds.Main].Layers
            .Single(descriptor => descriptor.Kind == OutputLayerKind.Slide);
        layer.Payload.Should().Be(priorPayload);
        layer.IsVisible.Should().BeFalse();
        layer.ClearState.Should().Be(LayerClearState.Cleared);
        layer.Transition.Should().Be(clearTransition);
    }

    [Fact]
    public void Compose_emits_background_vs_media_note_when_slide_background_media_and_media_layer_active()
    {
        BackendRenderFrameResolver frameResolver = new();
        FrameComposer composer = new(frameResolver);

        Dictionary<OutputLayerKind, LayerState> layers = new(LiveRenderSessionState.CreateEmptyLayers())
        {
            [OutputLayerKind.Slide] = new LayerState
            {
                Kind = OutputLayerKind.Slide,
                IsVisible = true,
                Payload = new RenderPayloadDescriptor
                {
                    Id = "slide-1",
                    Kind = RenderPayloadKind.Presentation,
                    Detail = new PresentationRenderPayload
                    {
                        Scene = SlideScene.Empty with
                        {
                            Background = new SceneBackground { MediaId = "bg-media", MediaType = "image" },
                        },
                    },
                },
            },
            [OutputLayerKind.Media] = new LayerState
            {
                Kind = OutputLayerKind.Media,
                IsVisible = true,
                Payload = new RenderPayloadDescriptor
                {
                    Id = "media-1",
                    Kind = RenderPayloadKind.Video,
                    Detail = new MediaRenderPayload { Media = new OutputLayerMedia { MediaId = "media-1" } },
                },
            },
        };

        LiveSessionSnapshot snapshot = LiveSessionSnapshot.CreateEmpty() with
        {
            SessionState = LiveSessionSnapshot.CreateEmpty().SessionState with
            {
                Screens = new Dictionary<string, OutputScreen>(StringComparer.OrdinalIgnoreCase)
                {
                    [OutputFeedIds.Main] = new OutputScreen { Id = OutputFeedIds.Main, Name = "Main", Kind = OutputScreenKind.Audience },
                },
                ActiveLook = new LookPreset
                {
                    Id = "default",
                    Name = "Default",
                    ScreenRoutes =
                    [
                        new ScreenLayerRouting
                        {
                            ScreenId = OutputFeedIds.Main,
                            Layers =
                            [
                                new LayerRoute { LayerKind = OutputLayerKind.Slide, IsEnabled = true },
                                new LayerRoute { LayerKind = OutputLayerKind.Media, IsEnabled = true },
                            ],
                        },
                    ],
                },
                Layers = layers,
            },
        };

        RenderFrameSet frames = composer.Compose(snapshot);

        frames.AudienceFrames[OutputFeedIds.Main].Diagnostics.Notes
            .Should().Contain(FrameComposer.BackgroundAboveMediaNote);
    }

    private static LayerState CreateLiveLayer(OutputLayerKind layerKind) =>
        new()
        {
            Kind = layerKind,
            IsVisible = true,
            Payload = new RenderPayloadDescriptor
            {
                Id = layerKind.ToString(),
                Kind = layerKind == OutputLayerKind.Audio
                    ? RenderPayloadKind.Audio
                    : RenderPayloadKind.Overlay,
                DisplayName = layerKind.ToString(),
            },
        };
}
