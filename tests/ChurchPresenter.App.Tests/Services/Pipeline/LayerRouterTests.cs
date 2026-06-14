using ChurchPresenter.App.Tests.Reference;
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

namespace ChurchPresenter.App.Tests.Services.Pipeline;

[ReferenceArea(ReferenceAreas.LivePipeline)]
public sealed class LayerRouterTests
{
    [Fact]
    public void ApplyPreparedCue_sets_layer_slot_and_payload()
    {
        LayerRouter router = CreateRouter();
        LiveSessionSnapshot snapshot = LiveSessionSnapshot.CreateEmpty();
        PreparedCueRecord prepared = CreateSlidePreparedCue("slide-1");

        LiveSessionSnapshot updated = router.ApplyPreparedCue(snapshot, prepared);

        updated.LayerSlots[BackendOutputLayerKind.Slide].PreparedCue.Should().Be(prepared);
        updated.SessionState.Layers[BackendOutputLayerKind.Slide].Payload!.Id.Should().Be("slide-1");
        updated.LayerSlots[BackendOutputLayerKind.Slide].ClearOverride.Should().BeFalse();
    }

    [Fact]
    public void ApplyPreparedCue_prefers_cue_transition_over_show_default()
    {
        ShowTransitionDefaults defaults = new();
        defaults.SetGlobalSlideFallback(new SlideTransition { Type = "fade", Duration = 800 });
        LayerRouter router = CreateRouter(defaults: defaults);
        PreparedCueRecord prepared = CreateSlidePreparedCue("slide-1") with
        {
            Transition = new SlideTransition { Type = "cut", Duration = 0 },
        };

        LiveSessionSnapshot updated = router.ApplyPreparedCue(LiveSessionSnapshot.CreateEmpty(), prepared);

        updated.SessionState.Layers[BackendOutputLayerKind.Slide].Transition.Type.Should().Be("cut");
        updated.SessionState.Layers[BackendOutputLayerKind.Slide].Transition.Duration.Should().Be(TimeSpan.Zero);
        updated.SessionState.Layers[BackendOutputLayerKind.Slide].Transition.Phase.Should().Be(LayerTransitionPhase.Pending);
    }

    [Fact]
    public void ApplyPreparedCue_uses_media_global_default_when_cue_has_no_transition()
    {
        ShowTransitionDefaults defaults = new();
        defaults.SetGlobalMediaFallback(new SlideTransition { Type = "fade", Duration = 400 });
        LayerRouter router = CreateRouter(defaults: defaults);

        LiveSessionSnapshot updated = router.ApplyPreparedCue(
            LiveSessionSnapshot.CreateEmpty(),
            CreateMediaPreparedCue("media-1"));

        updated.SessionState.Layers[BackendOutputLayerKind.Media].Transition.Type.Should().Be("fade");
        updated.SessionState.Layers[BackendOutputLayerKind.Media].Transition.Duration.Should().Be(TimeSpan.FromMilliseconds(400));
        updated.SessionState.Layers[BackendOutputLayerKind.Media].Transition.Phase.Should().Be(LayerTransitionPhase.Pending);
    }

    [Fact]
    public void ApplyPreparedCue_prefers_media_cue_transition_over_show_default()
    {
        ShowTransitionDefaults defaults = new();
        defaults.SetGlobalMediaFallback(new SlideTransition { Type = "fade", Duration = 400 });
        LayerRouter router = CreateRouter(defaults: defaults);
        PreparedCueRecord prepared = CreateMediaPreparedCue("media-1") with
        {
            Transition = new SlideTransition { Type = "cut", Duration = 0 },
        };

        LiveSessionSnapshot updated = router.ApplyPreparedCue(LiveSessionSnapshot.CreateEmpty(), prepared);

        updated.SessionState.Layers[BackendOutputLayerKind.Media].Transition.Type.Should().Be("cut");
        updated.SessionState.Layers[BackendOutputLayerKind.Media].Transition.Duration.Should().Be(TimeSpan.Zero);
        updated.SessionState.Layers[BackendOutputLayerKind.Media].Transition.Phase.Should().Be(LayerTransitionPhase.Pending);
    }

    [Fact]
    public void ApplyPreparedCue_does_not_apply_visible_transition_to_audio_layer()
    {
        LayerRouter router = CreateRouter();
        PreparedCueRecord prepared = CreateMediaPreparedCue("audio-1") with
        {
            Layer = BackendOutputLayerKind.Audio,
            Transition = new SlideTransition { Type = "fade", Duration = 400 },
        };

        LiveSessionSnapshot updated = router.ApplyPreparedCue(LiveSessionSnapshot.CreateEmpty(), prepared);

        updated.SessionState.Layers[BackendOutputLayerKind.Audio].Transition.Phase.Should().Be(LayerTransitionPhase.None);
    }

    [Fact]
    public void ClearLayers_marks_layers_cleared_with_override()
    {
        LayerRouter router = CreateRouter();
        LiveSessionSnapshot snapshot = router.ApplyPreparedCue(
            LiveSessionSnapshot.CreateEmpty(),
            CreateSlidePreparedCue("slide-1"));

        LiveSessionSnapshot cleared = router.ClearLayers(snapshot, [BackendOutputLayerKind.Slide]);

        cleared.LayerSlots[BackendOutputLayerKind.Slide].ClearOverride.Should().BeTrue();
        cleared.SessionState.Layers[BackendOutputLayerKind.Slide].IsCleared.Should().BeTrue();
    }

    [Fact]
    public void ClearLayers_resolves_clear_transition_for_visual_layer()
    {
        ShowTransitionDefaults defaults = new();
        defaults.SetGlobalSlideFallback(new SlideTransition { Type = "fade", Duration = 350 });
        LayerRouter router = CreateRouter(defaults: defaults);
        LiveSessionSnapshot snapshot = router.ApplyPreparedCue(
            LiveSessionSnapshot.CreateEmpty(),
            CreateSlidePreparedCue("slide-1"));

        LiveSessionSnapshot cleared = router.ClearLayers(snapshot, [BackendOutputLayerKind.Slide]);

        cleared.LayerSlots[BackendOutputLayerKind.Slide].Transition.Type.Should().Be("fade");
        cleared.LayerSlots[BackendOutputLayerKind.Slide].Transition.Duration.Should().Be(TimeSpan.FromMilliseconds(350));
        cleared.LayerSlots[BackendOutputLayerKind.Slide].Transition.Phase.Should().Be(LayerTransitionPhase.Pending);
        cleared.LayerSlots[BackendOutputLayerKind.Slide].PriorPayload!.Id.Should().Be("slide-1");
    }

    [Fact]
    public void ClearGroup_clears_configured_group_layers()
    {
        Mock<IOutputRoutingService> routing = PipelineTestRuntime.CreateDefaultRouting();
        routing.SetupGet(service => service.ActiveLook).Returns(new OutputLookDefinition
        {
            Id = "default",
            Name = "Default",
            ClearGroups =
            [
                new OutputLookClearGroupDefinition
                {
                    Id = "overlays",
                    Name = "Overlays",
                    Scopes = [OutputClearScope.Messages, OutputClearScope.Props],
                    Layers = OutputRoutingDefaults.CreateClearGroupLayers(OutputClearScope.Messages, OutputClearScope.Props),
                },
            ],
        });

        LayerRouter router = CreateRouter(routing);
        LiveSessionSnapshot snapshot = LiveSessionSnapshot.CreateEmpty();
        snapshot = router.ApplyPreparedCue(snapshot, CreateOverlayPreparedCue(BackendOutputLayerKind.Messages, "welcome"));
        snapshot = router.ApplyPreparedCue(snapshot, CreateOverlayPreparedCue(BackendOutputLayerKind.Props, "lower-third"));

        LiveSessionSnapshot cleared = router.ClearGroup(snapshot, "overlays");

        cleared.SessionState.Layers[BackendOutputLayerKind.Messages].IsCleared.Should().BeTrue();
        cleared.SessionState.Layers[BackendOutputLayerKind.Props].IsCleared.Should().BeTrue();
        cleared.LayerSlots[BackendOutputLayerKind.Messages].ClearOverride.Should().BeTrue();
        cleared.LayerSlots[BackendOutputLayerKind.Props].ClearOverride.Should().BeTrue();
    }

    private static LayerRouter CreateRouter(
        Mock<IOutputRoutingService>? routing = null,
        IShowTransitionDefaults? defaults = null)
    {
        routing ??= PipelineTestRuntime.CreateDefaultRouting();
        BackendRenderFrameResolver frameResolver = new();
        BackendRenderEngine renderEngine = new(frameResolver);
        LiveCommandExecutor commandExecutor = new(renderEngine);
        return new LayerRouter(renderEngine, commandExecutor, routing.Object, defaults ?? new ShowTransitionDefaults());
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
                Detail = new PresentationRenderPayload
                {
                    PresentationPath = @"C:\shows\service.cpres",
                    SlideId = slideId,
                },
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
                Kind = RenderPayloadKind.Image,
                DisplayName = mediaId,
            },
        };

    private static PreparedCueRecord CreateOverlayPreparedCue(BackendOutputLayerKind layer, string id) =>
        new()
        {
            Layer = layer,
            ContentRef = new ContentRef { Kind = ContentRefKind.Message, Id = id },
            Payload = new RenderPayloadDescriptor
            {
                Id = id,
                Kind = RenderPayloadKind.Overlay,
                DisplayName = id,
            },
        };
}
