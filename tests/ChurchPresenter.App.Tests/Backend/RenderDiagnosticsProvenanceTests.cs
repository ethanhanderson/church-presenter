using ChurchPresenter.Backend.Commands;
using ChurchPresenter.Backend.Media;
using ChurchPresenter.Backend.Output;
using ChurchPresenter.Backend.Rendering;

using FluentAssertions;

namespace ChurchPresenter.App.Tests.Backend;

/// <summary>
/// Tests for backend frame provenance and diagnostics carried through the live command pipeline.
/// </summary>
public sealed class RenderDiagnosticsProvenanceTests
{
    [Fact]
    public void Live_command_correlation_flows_to_action_layer_frame_and_recovery_state()
    {
        BackendRenderEngine engine = CreateEngine();
        LiveCommandExecutor executor = new(engine);
        LiveRenderSessionState state = CreateState(screens: [AudienceScreen("main")]);
        LiveCommand command = new()
        {
            Kind = LiveCommandKind.SetLayerPayload,
            Source = new LiveCommandSource
            {
                Kind = LiveCommandSourceKind.Remote,
                Id = "remote-1",
                Actor = "producer",
            },
            Target = LiveCommandTarget.Layer(OutputLayerKind.Slide),
            Payload = Payload("slide-1", RenderPayloadKind.Presentation),
            CorrelationId = "corr-slide-1",
            Transition = new LayerTransitionState
            {
                Type = "fade",
                Duration = TimeSpan.FromMilliseconds(300),
                Phase = LayerTransitionPhase.Pending,
            },
        };

        ActionResult result = executor.Execute(state, command);

        result.Succeeded.Should().BeTrue();
        result.Batch.CorrelationId.Should().Be("corr-slide-1");
        result.Batch.Actions.Should().ContainSingle(action =>
            action.SourceCommandId == command.Id
            && action.CorrelationId == "corr-slide-1"
            && action.Source.Kind == LiveCommandSourceKind.Remote);

        LayerState layer = result.State.Layers[OutputLayerKind.Slide];
        layer.SourceCommandId.Should().Be(command.Id);
        layer.Provenance.CorrelationId.Should().Be("corr-slide-1");
        layer.Provenance.SourceKind.Should().Be(LiveCommandSourceKind.Remote.ToString());
        layer.Provenance.Actor.Should().Be("producer");
        layer.Transition.Type.Should().Be("fade");
        layer.Transition.Provenance.CommandId.Should().Be(command.Id);

        AudienceRenderFrame frame = result.Frames.AudienceFrames["main"];
        frame.Provenance.CorrelationId.Should().Be("corr-slide-1");
        frame.Layers.Should().ContainSingle(renderLayer =>
            renderLayer.Kind == OutputLayerKind.Slide
            && renderLayer.SourceCommandId == command.Id
            && renderLayer.Provenance.CorrelationId == "corr-slide-1"
            && renderLayer.Transition.Type == "fade");

        result.State.GeneratedState.RecoveryDiagnostics.Layers.Should().ContainSingle(recoveryLayer =>
            recoveryLayer.LayerKind == OutputLayerKind.Slide
            && recoveryLayer.CorrelationId == "corr-slide-1"
            && recoveryLayer.SourceKind == LiveCommandSourceKind.Remote.ToString()
            && recoveryLayer.Transition.Type == "fade");
    }

    [Fact]
    public void Clear_command_sets_structured_clear_state_without_removing_other_layers()
    {
        BackendRenderEngine engine = CreateEngine();
        LiveCommandExecutor executor = new(engine);
        LiveRenderSessionState state = CreateState(
            screens: [AudienceScreen("main")],
            layers: ActiveLayers(
                Layer(OutputLayerKind.Slide, "slide-1", RenderPayloadKind.Presentation),
                Layer(OutputLayerKind.Media, "video-1", RenderPayloadKind.Video)));
        LiveCommand clearSlide = new()
        {
            Kind = LiveCommandKind.Clear,
            Source = new LiveCommandSource { Kind = LiveCommandSourceKind.Operator, Id = "toolbar-clear" },
            Clear = new ClearCommand
            {
                Layers = new HashSet<OutputLayerKind> { OutputLayerKind.Slide },
            },
            CorrelationId = "corr-clear-slide",
        };

        ActionResult result = executor.Execute(state, clearSlide);

        result.Succeeded.Should().BeTrue();
        result.State.Layers[OutputLayerKind.Slide].ClearState.Should().Be(LayerClearState.Cleared);
        result.State.Layers[OutputLayerKind.Slide].Provenance.CorrelationId.Should().Be("corr-clear-slide");
        result.State.Layers[OutputLayerKind.Media].ClearState.Should().Be(LayerClearState.None);
        result.State.Layers[OutputLayerKind.Media].Payload.Should().NotBeNull();
        result.Frames.AudienceFrames["main"].Layers.Should()
            .ContainSingle(layer => layer.Kind == OutputLayerKind.Media && layer.IsVisible);
        result.State.GeneratedState.RecoveryDiagnostics.Layers.Should().ContainSingle(layer =>
            layer.LayerKind == OutputLayerKind.Slide
            && layer.ClearState == LayerClearState.Cleared
            && layer.CorrelationId == "corr-clear-slide");
    }

    [Fact]
    public void Render_errors_and_endpoint_health_are_attached_to_resolved_frames()
    {
        BackendRenderEngine engine = CreateEngine();
        LiveCommandExecutor executor = new(engine);
        LiveRenderSessionState state = CreateState(
            screens: [AudienceScreen("main")],
            endpoints:
            [
                new OutputEndpoint
                {
                    Id = "local-display:1",
                    Name = "Projector",
                    Kind = OutputEndpointKind.LocalDisplay,
                    Health = EndpointHealth.Missing,
                },
            ],
            mappings:
            [
                new ScreenMapping { ScreenId = "main", EndpointIds = ["local-display:1", "capture:missing"] },
            ]);
        LiveCommand invalidCommand = new()
        {
            Kind = LiveCommandKind.SetLayerPayload,
            Source = new LiveCommandSource { Kind = LiveCommandSourceKind.Automation, Id = "macro-invalid" },
            Target = LiveCommandTarget.Layer(OutputLayerKind.Media),
            CorrelationId = "corr-invalid",
        };

        ActionResult result = executor.Execute(state, invalidCommand);

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle("SetLayerPayload action requires a target layer and payload.");
        result.State.RenderErrors.Should().ContainSingle(error =>
            error.LayerKind == OutputLayerKind.Media
            && error.Provenance.CorrelationId == "corr-invalid");

        RenderDiagnostics diagnostics = result.Frames.AudienceFrames["main"].Diagnostics;
        diagnostics.EndpointIds.Should().Equal("local-display:1", "capture:missing");
        diagnostics.Endpoints.Should().Contain(endpoint =>
            endpoint.EndpointId == "local-display:1"
            && endpoint.Health == EndpointHealth.Missing
            && endpoint.Kind == OutputEndpointKind.LocalDisplay);
        diagnostics.Endpoints.Should().Contain(endpoint =>
            endpoint.EndpointId == "capture:missing"
            && endpoint.Health == EndpointHealth.Missing
            && endpoint.Kind == null);
        diagnostics.RenderErrors.Should().ContainSingle(error =>
            error.Provenance.CorrelationId == "corr-invalid"
            && error.Message.Contains("SetLayerPayload", StringComparison.Ordinal));
        diagnostics.Message.Should().Contain("SetLayerPayload");
    }

    [Fact]
    public void Player_state_hook_flows_from_layer_state_to_frame_descriptor()
    {
        MediaPlaybackCoordinationSnapshot playerState = new()
        {
            ActiveRequest = new MediaPlaybackRequest
            {
                CueId = "cue-1",
                AssetId = "asset-1",
                DisplayName = "Loop",
                AssetKind = MediaAssetKind.Video,
                LayerTarget = MediaPlaybackLayerTarget.MediaUnderlay,
                IsPlayable = true,
            },
            Authority = MediaPlaybackAuthority.Authority,
            ActivePlayerCount = 2,
            IsPlaying = true,
            Position = TimeSpan.FromSeconds(12),
            Duration = TimeSpan.FromMinutes(3),
        };
        LiveRenderSessionState state = CreateState(
            screens: [AudienceScreen("main")],
            layers: ActiveLayers(Layer(OutputLayerKind.Media, "asset-1", RenderPayloadKind.Video) with
            {
                PlayerState = playerState,
            }));

        RenderFrameSet frames = new BackendRenderFrameResolver().Resolve(state);

        RenderLayerDescriptor mediaLayer = frames.AudienceFrames["main"].Layers
            .Single(layer => layer.Kind == OutputLayerKind.Media);
        mediaLayer.PlayerState.Should().BeSameAs(playerState);
        mediaLayer.PlayerState!.HasActiveCue.Should().BeTrue();
    }

    private static BackendRenderEngine CreateEngine()
    {
        return new BackendRenderEngine(new BackendRenderFrameResolver());
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

    private static LiveRenderSessionState CreateState(
        IReadOnlyList<OutputScreen>? screens = null,
        IReadOnlyList<OutputEndpoint>? endpoints = null,
        IReadOnlyList<ScreenMapping>? mappings = null,
        IReadOnlyDictionary<OutputLayerKind, LayerState>? layers = null)
    {
        return new LiveRenderSessionState
        {
            Screens = (screens ?? Array.Empty<OutputScreen>())
                .ToDictionary(screen => screen.Id, StringComparer.OrdinalIgnoreCase),
            Endpoints = (endpoints ?? Array.Empty<OutputEndpoint>())
                .ToDictionary(endpoint => endpoint.Id, StringComparer.OrdinalIgnoreCase),
            ScreenMappings = mappings ?? Array.Empty<ScreenMapping>(),
            Layers = layers ?? LiveRenderSessionState.CreateEmptyLayers(),
            ActiveLook = new LookPreset { Id = "default", Name = "Default" },
        };
    }
}
