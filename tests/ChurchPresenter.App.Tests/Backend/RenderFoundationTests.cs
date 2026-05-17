using ChurchPresenter.Backend.Commands;
using ChurchPresenter.Backend.Output;
using ChurchPresenter.Backend.Rendering;

using FluentAssertions;

namespace ChurchPresenter.App.Tests.Backend;

/// <summary>
/// Foundation tests for the backend render engine contracts.
/// </summary>
public sealed class RenderFoundationTests
{
    [Fact]
    public void Screen_mapping_allows_zero_one_and_many_endpoints()
    {
        Dictionary<string, OutputEndpoint> endpoints = new(StringComparer.OrdinalIgnoreCase)
        {
            ["main-monitor"] = new OutputEndpoint
            {
                Id = "main-monitor",
                Name = "Main Monitor",
                Kind = OutputEndpointKind.LocalDisplay,
                Capabilities = EndpointCapability.LocalWindow,
                Health = EndpointHealth.Connected,
            },
            ["stream-placeholder"] = OutputEndpoint.Placeholder("stream-placeholder", "Stream Setup"),
        };

        ScreenMapping zero = new() { ScreenId = "lobby" };
        ScreenMapping one = new() { ScreenId = "main", EndpointIds = ["main-monitor"] };
        ScreenMapping many = new() { ScreenId = "stream", EndpointIds = ["main-monitor", "stream-placeholder"] };

        zero.ResolveEndpoints(endpoints).Should().BeEmpty();
        one.ResolveEndpoints(endpoints).Should().ContainSingle(endpoint => endpoint.Id == "main-monitor");
        many.ResolveEndpoints(endpoints).Should().HaveCount(2);
    }

    [Fact]
    public void Placeholder_endpoint_participates_without_monitor_or_window()
    {
        OutputEndpoint placeholder = OutputEndpoint.Placeholder("placeholder-main", "Main Placeholder");
        LiveRenderSessionState state = CreateState(
            screens:
            [
                AudienceScreen("main"),
            ],
            endpoints:
            [
                placeholder,
            ],
            mappings:
            [
                new ScreenMapping { ScreenId = "main", EndpointIds = [placeholder.Id] },
            ]);

        RenderFrameSet frames = new BackendRenderFrameResolver().Resolve(state);

        placeholder.Kind.Should().Be(OutputEndpointKind.Placeholder);
        placeholder.NativeId.Should().BeNull();
        placeholder.Health.Should().Be(EndpointHealth.Placeholder);
        frames.AudienceFrames["main"].Diagnostics.EndpointIds.Should().Equal("placeholder-main");
    }

    [Fact]
    public void Frame_store_replaces_stale_screen_snapshots()
    {
        InMemoryRenderFrameStore frameStore = new();
        frameStore.Save(new RenderFrameSet
        {
            AudienceFrames = new Dictionary<string, AudienceRenderFrame>(StringComparer.OrdinalIgnoreCase)
            {
                ["main"] = new AudienceRenderFrame { ScreenId = "main", Sequence = 1 },
                ["lobby"] = new AudienceRenderFrame { ScreenId = "lobby", Sequence = 1 },
            },
            StageFrames = new Dictionary<string, StageRenderFrame>(StringComparer.OrdinalIgnoreCase)
            {
                ["stage"] = new StageRenderFrame { ScreenId = "stage", Sequence = 1 },
            },
        });

        frameStore.Save(new RenderFrameSet
        {
            AudienceFrames = new Dictionary<string, AudienceRenderFrame>(StringComparer.OrdinalIgnoreCase)
            {
                ["main"] = new AudienceRenderFrame { ScreenId = "main", Sequence = 2 },
            },
        });

        frameStore.GetAudienceFrame("main")!.Sequence.Should().Be(2);
        frameStore.GetAudienceFrame("lobby").Should().BeNull();
        frameStore.GetStageFrame("stage").Should().BeNull();
    }

    [Fact]
    public void Look_routes_layers_per_audience_screen()
    {
        LiveRenderSessionState state = CreateState(
            screens:
            [
                AudienceScreen("main"),
                AudienceScreen("stream"),
            ],
            layers: ActiveLayers(
                Layer(OutputLayerKind.Slide, "slide-1", RenderPayloadKind.Presentation),
                Layer(OutputLayerKind.Media, "walk-in", RenderPayloadKind.Video)),
            look: new LookPreset
            {
                Id = "sunday-am",
                Name = "Sunday AM",
                ScreenRoutes =
                [
                    Route("main", Enabled(OutputLayerKind.Slide), Enabled(OutputLayerKind.Media)),
                    Route("stream", Enabled(OutputLayerKind.Slide, themeVariantId: "lower-third"), Disabled(OutputLayerKind.Media)),
                ],
            });

        RenderFrameSet frames = new BackendRenderFrameResolver().Resolve(state);

        AudienceRenderFrame main = frames.AudienceFrames["main"];
        AudienceRenderFrame stream = frames.AudienceFrames["stream"];

        main.Layers.Single(layer => layer.Kind == OutputLayerKind.Media).IsVisible.Should().BeTrue();
        stream.Layers.Single(layer => layer.Kind == OutputLayerKind.Media).IsVisible.Should().BeFalse();
        stream.Layers.Single(layer => layer.Kind == OutputLayerKind.Slide)
            .Payload.ThemeVariantId.Should().Be("lower-third");
    }

    [Fact]
    public void Look_mask_assignment_creates_screen_specific_mask_layer()
    {
        LiveRenderSessionState state = CreateState(
            screens:
            [
                AudienceScreen("main"),
                AudienceScreen("stream"),
            ],
            layers: ActiveLayers(Layer(OutputLayerKind.Slide, "slide-1", RenderPayloadKind.Presentation)),
            look: new LookPreset
            {
                Id = "masked",
                Name = "Masked",
                ScreenRoutes =
                [
                    Route("main", Enabled(OutputLayerKind.Slide), Enabled(OutputLayerKind.Mask, maskId: "main-mask")),
                    Route("stream", Enabled(OutputLayerKind.Slide), Disabled(OutputLayerKind.Mask)),
                ],
            });

        RenderFrameSet frames = new BackendRenderFrameResolver().Resolve(state);

        frames.AudienceFrames["main"].Layers.Should().ContainSingle(layer =>
            layer.Kind == OutputLayerKind.Mask
            && layer.IsVisible
            && layer.Payload.Id == "mask:main-mask");
        frames.AudienceFrames["stream"].Layers.Should().NotContain(layer => layer.Kind == OutputLayerKind.Mask);
    }

    [Fact]
    public void Clearing_one_layer_does_not_clear_all_layers()
    {
        LiveRenderSessionState state = CreateState(
            screens:
            [
                AudienceScreen("main"),
            ],
            layers: ActiveLayers(
                Layer(OutputLayerKind.Slide, "slide-1", RenderPayloadKind.Presentation),
                Layer(OutputLayerKind.Media, "walk-in", RenderPayloadKind.Video)));
        BackendRenderEngine engine = CreateEngine();
        LiveCommand command = LiveCommandExecutor.ClearLayers([OutputLayerKind.Slide]);

        RenderEngineResult result = engine.Apply(state, new LiveCommandExecutor(engine).Expand(command));

        result.State.Layers[OutputLayerKind.Slide].Payload.Should().BeNull();
        result.State.Layers[OutputLayerKind.Slide].IsCleared.Should().BeTrue();
        result.State.Layers[OutputLayerKind.Media].Payload.Should().NotBeNull();
        result.Frames.AudienceFrames["main"].Layers.Should()
            .ContainSingle(layer => layer.Kind == OutputLayerKind.Media && layer.IsVisible);
        result.Frames.AudienceFrames["main"].Layers.Should()
            .NotContain(layer => layer.Kind == OutputLayerKind.Slide);
    }

    [Fact]
    public void Stage_frames_resolve_independently_from_audience_looks()
    {
        LiveRenderSessionState state = CreateState(
            screens:
            [
                AudienceScreen("main"),
                StageScreen("stage"),
            ],
            layers: ActiveLayers(Layer(OutputLayerKind.Slide, "slide-1", RenderPayloadKind.Presentation)),
            look: new LookPreset
            {
                Id = "audience-blackout",
                Name = "Audience Blackout",
                ScreenRoutes =
                [
                    Route("main", Disabled(OutputLayerKind.Slide)),
                ],
            },
            stageLayouts: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["stage"] = "confidence",
            });

        RenderFrameSet frames = new BackendRenderFrameResolver().Resolve(state);

        frames.AudienceFrames["main"].Layers.Single(layer => layer.Kind == OutputLayerKind.Slide)
            .IsVisible.Should().BeFalse();
        frames.StageFrames["stage"].StageLayoutId.Should().Be("confidence");
        frames.StageFrames["stage"].Payloads.Should().ContainSingle(payload => payload.Id == "slide-1");
    }

    [Fact]
    public void Action_batch_from_live_command_mutates_state_and_produces_new_frames()
    {
        InMemoryRenderFrameStore frameStore = new();
        BackendRenderEngine engine = CreateEngine(frameStore);
        LiveCommandExecutor executor = new(engine);
        LiveRenderSessionState state = CreateState(
            screens:
            [
                AudienceScreen("main"),
            ]);
        LiveCommand command = LiveCommandExecutor.SetLayerPayload(
            OutputLayerKind.Slide,
            Payload("slide-2", RenderPayloadKind.Presentation));

        ActionResult result = executor.Execute(state, command);

        result.Succeeded.Should().BeTrue();
        result.Batch.SourceCommandId.Should().Be(command.Id);
        result.Batch.Actions.Should().ContainSingle(action => action.Kind == LiveActionKind.SetLayerPayload);
        result.Frames.AudienceFrames["main"].Sequence.Should().Be(1);
        result.Frames.AudienceFrames["main"].Layers.Should()
            .ContainSingle(layer => layer.Kind == OutputLayerKind.Slide && layer.Payload.Id == "slide-2");
        frameStore.GetAudienceFrame("main").Should().NotBeNull();
    }

    [Fact]
    public void Audience_frame_preserves_presentation_scene_payload_detail()
    {
        SlideScene scene = new()
        {
            Id = "scene:presentation-1:slide-1:default",
            Version = "v1",
            SlideId = "slide-1",
            Nodes =
            [
                new TextSceneNode
                {
                    Id = "text-1",
                    Text = "Welcome",
                    Transform = new SceneNodeTransform { Width = 800, Height = 100 },
                },
            ],
        };
        LiveRenderSessionState state = CreateState(
            screens:
            [
                AudienceScreen("stream"),
            ],
            layers: ActiveLayers(new LayerState
            {
                Kind = OutputLayerKind.Slide,
                IsVisible = true,
                Payload = new RenderPayloadDescriptor
                {
                    Id = "slide-1",
                    Kind = RenderPayloadKind.Presentation,
                    DisplayName = "Slide 1",
                    Detail = new PresentationRenderPayload
                    {
                        PresentationId = "presentation-1",
                        SlideId = "slide-1",
                        Scene = scene,
                        VariantScenes = new Dictionary<string, SlideScene>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["lower-third"] = scene with
                            {
                                Id = "scene:presentation-1:slide-1:lower-third",
                                Nodes =
                                [
                                    new TextSceneNode
                                    {
                                        Id = "lower-third-text",
                                        Text = "Welcome",
                                        Transform = new SceneNodeTransform { Width = 600, Height = 80 },
                                    },
                                ],
                            },
                        },
                    },
                },
            }),
            look: new LookPreset
            {
                Id = "stream-look",
                Name = "Stream Look",
                ScreenRoutes =
                [
                    Route("stream", Enabled(OutputLayerKind.Slide, themeVariantId: "lower-third")),
                ],
            });

        AudienceRenderFrame frame = new BackendRenderFrameResolver().Resolve(state).AudienceFrames["stream"];

        RenderPayloadDescriptor payload = frame.Layers.Single(layer => layer.Kind == OutputLayerKind.Slide).Payload;
        payload.Detail.Should().BeOfType<PresentationRenderPayload>()
            .Which.Scene.Nodes.Should().ContainSingle(node => node.Id == "lower-third-text");
        payload.Detail.As<PresentationRenderPayload>().ThemeVariantId.Should().Be("lower-third");
    }

    private static BackendRenderEngine CreateEngine(IRenderFrameStore? frameStore = null)
    {
        return new BackendRenderEngine(new BackendRenderFrameResolver(), frameStore);
    }

    private static OutputScreen AudienceScreen(string id)
    {
        return new OutputScreen
        {
            Id = id,
            Name = id,
            Kind = OutputScreenKind.Audience,
        };
    }

    private static OutputScreen StageScreen(string id)
    {
        return new OutputScreen
        {
            Id = id,
            Name = id,
            Kind = OutputScreenKind.Stage,
        };
    }

    private static LayerState Layer(OutputLayerKind kind, string payloadId, RenderPayloadKind payloadKind)
    {
        return new LayerState
        {
            Kind = kind,
            Payload = Payload(payloadId, payloadKind),
            IsVisible = true,
        };
    }

    private static RenderPayloadDescriptor Payload(string id, RenderPayloadKind payloadKind)
    {
        return new RenderPayloadDescriptor
        {
            Id = id,
            Kind = payloadKind,
            DisplayName = id,
        };
    }

    private static IReadOnlyDictionary<OutputLayerKind, LayerState> ActiveLayers(params LayerState[] layers)
    {
        Dictionary<OutputLayerKind, LayerState> states = new(LiveRenderSessionState.CreateEmptyLayers());
        foreach (LayerState layer in layers)
        {
            states[layer.Kind] = layer;
        }

        return states;
    }

    private static ScreenLayerRouting Route(string screenId, params LayerRoute[] layers)
    {
        return new ScreenLayerRouting
        {
            ScreenId = screenId,
            Layers = layers,
        };
    }

    private static LayerRoute Enabled(OutputLayerKind layerKind, string? themeVariantId = null, string? maskId = null)
    {
        return new LayerRoute
        {
            LayerKind = layerKind,
            IsEnabled = true,
            ThemeVariantId = themeVariantId,
            MaskId = maskId,
        };
    }

    private static LayerRoute Disabled(OutputLayerKind layerKind)
    {
        return new LayerRoute
        {
            LayerKind = layerKind,
            IsEnabled = false,
        };
    }

    private static LiveRenderSessionState CreateState(
        IReadOnlyList<OutputScreen>? screens = null,
        IReadOnlyList<OutputEndpoint>? endpoints = null,
        IReadOnlyList<ScreenMapping>? mappings = null,
        IReadOnlyDictionary<OutputLayerKind, LayerState>? layers = null,
        LookPreset? look = null,
        IReadOnlyDictionary<string, string>? stageLayouts = null)
    {
        return new LiveRenderSessionState
        {
            Screens = (screens ?? Array.Empty<OutputScreen>())
                .ToDictionary(screen => screen.Id, StringComparer.OrdinalIgnoreCase),
            Endpoints = (endpoints ?? Array.Empty<OutputEndpoint>())
                .ToDictionary(endpoint => endpoint.Id, StringComparer.OrdinalIgnoreCase),
            ScreenMappings = mappings ?? Array.Empty<ScreenMapping>(),
            Layers = layers ?? LiveRenderSessionState.CreateEmptyLayers(),
            ActiveLook = look ?? new LookPreset { Id = "default", Name = "Default" },
            StageLayoutIdsByScreenId = stageLayouts
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        };
    }
}