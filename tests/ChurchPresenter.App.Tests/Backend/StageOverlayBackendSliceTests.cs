using ChurchPresenter.Backend.Commands;
using ChurchPresenter.Backend.Output;
using ChurchPresenter.Backend.Overlays;
using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Backend.Stage;

using FluentAssertions;

namespace ChurchPresenter.App.Tests.Backend;

/// <summary>
/// Focused tests for the combined stage/overlay/backend slice.
/// </summary>
public sealed class StageOverlayBackendSliceTests
{
    [Fact]
    public void Stage_only_layout_command_preserves_audience_output()
    {
        BackendRenderEngine engine = CreateEngine();
        LiveCommandExecutor executor = new(engine);
        LiveRenderSessionState state = CreateState(
            screens:
            [
                AudienceScreen("main"),
                StageScreen("stage"),
            ],
            layers: ActiveLayers(Layer(OutputLayerKind.Slide, "slide-live", RenderPayloadKind.Presentation)));

        ActionResult result = executor.Execute(
            state,
            LiveCommandExecutor.SetStageLayout("stage", "confidence", StageAudienceCommandMode.StageOnly));

        result.Succeeded.Should().BeTrue();
        result.Batch.Actions.Should().ContainSingle(action =>
            action.Kind == LiveActionKind.SetStageLayout
            && action.DeliveryMode == StageAudienceCommandMode.StageOnly);
        result.Frames.AudienceFrames["main"].Layers.Should()
            .ContainSingle(layer => layer.Kind == OutputLayerKind.Slide && layer.Payload.Id == "slide-live" && layer.IsVisible);
        result.Frames.StageFrames["stage"].StageLayoutId.Should().Be("confidence");
        result.Frames.StageFrames["stage"].CommandMode.Should().Be(StageAudienceCommandMode.StageOnly);
    }

    [Fact]
    public void Timer_generated_state_flows_to_stage_layouts_and_token_provider()
    {
        TimerTokenValueProvider provider = new();
        LiveRenderSessionState state = CreateState(
            screens:
            [
                StageScreen("stage"),
            ],
            stageLayouts: new Dictionary<string, StageLayout>(StringComparer.OrdinalIgnoreCase)
            {
                ["confidence"] = new StageLayout
                {
                    Id = "confidence",
                    Name = "Confidence",
                    Elements =
                    [
                        new StageLayoutElement
                        {
                            Id = "service-timer",
                            Kind = StageLayoutElementKind.Timer,
                            SourceId = "service-start",
                        },
                    ],
                },
            },
            stageAssignments: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["stage"] = "confidence",
            },
            generatedState: new GeneratedStateSnapshot
            {
                Timers = new Dictionary<string, TimerSnapshot>(StringComparer.OrdinalIgnoreCase)
                {
                    ["service-start"] = new TimerSnapshot
                    {
                        Id = "service-start",
                        Name = "Service Start",
                        Kind = GeneratedTimerKind.Countdown,
                        Status = GeneratedTimerStatus.Running,
                        Elapsed = TimeSpan.FromSeconds(2),
                        Remaining = TimeSpan.FromMinutes(9) + TimeSpan.FromSeconds(58),
                        DisplayValue = "09:58",
                        ActiveColor = "green",
                    },
                },
            });

        RenderFrameSet frames = new BackendRenderFrameResolver().Resolve(state);
        IReadOnlyList<GeneratedTokenValue> tokens = provider.Resolve(state.GeneratedState, "service-start");

        frames.StageFrames["stage"].Payloads.Should().ContainSingle(payload =>
            payload.Id == "timer:service-start"
            && payload.DisplayName == "09:58"
            && payload.ThemeVariantId == "green");
        tokens.Should().Contain(token => token.Token == "timer.display" && token.Value == "09:58");
        tokens.Should().Contain(token => token.Token == "timer.status" && token.Value == GeneratedTimerStatus.Running.ToString());
    }

    [Fact]
    public void Overlay_layer_identity_reserves_distinct_layer_roles()
    {
        OverlayLayerIdentity.TryGetAudienceLayer(OverlayContentKind.Message, out OutputLayerKind messageLayer).Should().BeTrue();
        OverlayLayerIdentity.TryGetAudienceLayer(OverlayContentKind.Prop, out OutputLayerKind propLayer).Should().BeTrue();
        OverlayLayerIdentity.TryGetAudienceLayer(OverlayContentKind.Announcement, out OutputLayerKind announcementLayer).Should().BeTrue();
        OverlayLayerIdentity.TryGetAudienceLayer(OverlayContentKind.Mask, out OutputLayerKind maskLayer).Should().BeTrue();
        OverlayLayerIdentity.TryGetAudienceLayer(OverlayContentKind.StageMessage, out _).Should().BeFalse();

        messageLayer.Should().Be(OutputLayerKind.Messages);
        propLayer.Should().Be(OutputLayerKind.Props);
        announcementLayer.Should().Be(OutputLayerKind.Announcements);
        maskLayer.Should().Be(OutputLayerKind.Mask);
        OverlayLayerIdentity.IsStageOnly(OverlayContentKind.StageMessage).Should().BeTrue();
    }

    [Fact]
    public void Overlay_command_updates_generated_state_and_audience_layer()
    {
        BackendRenderEngine engine = CreateEngine();
        LiveCommandExecutor executor = new(engine);
        LiveRenderSessionState state = CreateState(
            screens:
            [
                AudienceScreen("main"),
            ]);

        ActionResult result = executor.Execute(
            state,
            LiveCommandExecutor.SetOverlay(new OverlayContentState
            {
                Id = "welcome-message",
                Name = "Welcome",
                Kind = OverlayContentKind.Message,
                IsVisible = true,
                Text = "Welcome!",
            }));

        result.Succeeded.Should().BeTrue();
        result.State.GeneratedState.Messages.Should().ContainKey("welcome-message");
        result.State.GeneratedState.Messages["welcome-message"].Text.Should().Be("Welcome!");
        result.State.Layers[OutputLayerKind.Messages].IsVisible.Should().BeTrue();
        result.Frames.AudienceFrames["main"].Layers.Should().ContainSingle(layer =>
            layer.Kind == OutputLayerKind.Messages
            && layer.IsVisible
            && layer.Payload.DisplayName == "Welcome!");
    }

    [Fact]
    public void Stage_message_overlay_command_updates_stage_frames_without_touching_audience_layers()
    {
        BackendRenderEngine engine = CreateEngine();
        LiveCommandExecutor executor = new(engine);
        LiveRenderSessionState state = CreateState(
            screens:
            [
                AudienceScreen("main"),
                StageScreen("stage"),
            ],
            stageLayouts: new Dictionary<string, StageLayout>(StringComparer.OrdinalIgnoreCase)
            {
                ["confidence"] = new StageLayout
                {
                    Id = "confidence",
                    Name = "Confidence",
                    Elements =
                    [
                        new StageLayoutElement
                        {
                            Id = "stage-message",
                            Kind = StageLayoutElementKind.StageMessage,
                        },
                    ],
                },
            },
            stageAssignments: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["stage"] = "confidence",
            });

        ActionResult result = executor.Execute(
            state,
            LiveCommandExecutor.SetOverlay(new OverlayContentState
            {
                Id = "hold-message",
                Name = "Hold",
                Kind = OverlayContentKind.StageMessage,
                IsVisible = true,
                Text = "Stand by",
            }));

        result.Succeeded.Should().BeTrue();
        result.State.GeneratedState.StageMessageText.Should().Be("Stand by");
        result.State.Layers[OutputLayerKind.Messages].Payload.Should().BeNull();
        result.Frames.StageFrames["stage"].Payloads.Should().ContainSingle(payload =>
            payload.SourceReference == "stage-message"
            && payload.DisplayName == "Stand by");
    }

    [Fact]
    public void Macro_expansion_flattens_commands_into_one_batch()
    {
        BackendRenderEngine engine = CreateEngine();
        LiveCommandExecutor executor = new(engine);
        LiveMacroDefinition macro = new()
        {
            Id = "walk-in",
            Name = "Walk In",
            Commands =
            [
                LiveCommandExecutor.SetLayerPayload(
                    OutputLayerKind.Messages,
                    Payload("countdown-message", RenderPayloadKind.Overlay)),
                LiveCommandExecutor.SetStageLayout("stage", "confidence", StageAudienceCommandMode.StageOnly),
            ],
        };

        ActionBatch batch = executor.ExpandMacro(macro);

        batch.MacroId.Should().Be("walk-in");
        batch.Source.Kind.Should().Be(LiveCommandSourceKind.Macro);
        batch.Source.Id.Should().Be("walk-in");
        batch.Actions.Should().HaveCount(2);
        batch.Actions.Should().Contain(action =>
            action.Kind == LiveActionKind.SetStageLayout
            && action.DeliveryMode == StageAudienceCommandMode.StageOnly);
    }

    [Fact]
    public void Multi_command_expansion_uses_same_normalizer_as_individual_commands()
    {
        BackendRenderEngine engine = CreateEngine();
        LiveCommandExecutor executor = new(engine);
        LiveCommandSource source = new() { Kind = LiveCommandSourceKind.Automation, Id = "playback-sync" };
        LiveCommand[] commands =
        [
            LiveCommandExecutor.SetLayerPayload(
                OutputLayerKind.Slide,
                Payload("slide-live", RenderPayloadKind.Presentation),
                source),
            LiveCommandExecutor.ClearLayers([OutputLayerKind.Media, OutputLayerKind.Audio], source),
        ];

        ActionBatch batch = executor.Expand(commands, source);

        batch.Source.Should().Be(source);
        batch.Actions.Should().HaveCount(2);
        batch.Actions.Should().Contain(action =>
            action.Kind == LiveActionKind.SetLayerPayload
            && action.Target.LayerKind == OutputLayerKind.Slide
            && action.Payload!.Id == "slide-live");
        batch.Actions.Should().Contain(action =>
            action.Kind == LiveActionKind.ClearLayers
            && action.Clear!.Layers.SetEquals(new HashSet<OutputLayerKind>
            {
                OutputLayerKind.Media,
                OutputLayerKind.Audio,
            }));
    }

    [Fact]
    public void Clear_group_command_clears_only_configured_layers()
    {
        BackendRenderEngine engine = CreateEngine();
        LiveCommandExecutor executor = new(engine);
        LiveRenderSessionState state = CreateState(
            screens:
            [
                AudienceScreen("main"),
            ],
            layers: ActiveLayers(
                Layer(OutputLayerKind.Messages, "message-live", RenderPayloadKind.Overlay),
                Layer(OutputLayerKind.Props, "prop-live", RenderPayloadKind.Overlay),
                Layer(OutputLayerKind.Slide, "slide-live", RenderPayloadKind.Presentation)),
            look: new LookPreset
            {
                Id = "service",
                Name = "Service",
                ClearGroups =
                [
                    new ClearGroup
                    {
                        Id = "overlay-reset",
                        Name = "Overlay Reset",
                        Layers = new HashSet<OutputLayerKind>
                        {
                            OutputLayerKind.Messages,
                            OutputLayerKind.Props,
                        },
                    },
                ],
            });

        ActionResult result = executor.Execute(state, LiveCommandExecutor.ClearGroup("overlay-reset"));

        result.Succeeded.Should().BeTrue();
        result.State.Layers[OutputLayerKind.Messages].Payload.Should().BeNull();
        result.State.Layers[OutputLayerKind.Props].Payload.Should().BeNull();
        result.State.Layers[OutputLayerKind.Slide].Payload.Should().NotBeNull();
        result.State.GeneratedState.RecoveryDiagnostics.AvailableClearGroupIds.Should().Contain("overlay-reset");
    }

    [Fact]
    public void Capture_session_state_surfaces_in_stage_payloads_and_recovery_state()
    {
        BackendRenderEngine engine = CreateEngine();
        LiveCommandExecutor executor = new(engine);
        LiveRenderSessionState state = CreateState(
            screens:
            [
                AudienceScreen("main"),
                StageScreen("stage"),
            ],
            stageLayouts: new Dictionary<string, StageLayout>(StringComparer.OrdinalIgnoreCase)
            {
                ["confidence"] = new StageLayout
                {
                    Id = "confidence",
                    Name = "Confidence",
                    Elements =
                    [
                        new StageLayoutElement
                        {
                            Id = "capture",
                            Kind = StageLayoutElementKind.CaptureStatus,
                            SourceId = "stream-main",
                        },
                    ],
                },
            },
            generatedState: new GeneratedStateSnapshot
            {
                CaptureSessions = new Dictionary<string, CaptureSessionState>(StringComparer.OrdinalIgnoreCase)
                {
                    ["stream-main"] = new CaptureSessionState
                    {
                        Metadata = new CaptureSessionMetadata
                        {
                            Id = "stream-main",
                            Name = "Main Stream",
                            SourceScreenId = "main",
                            DestinationKind = CaptureDestinationKind.Rtmp,
                            Destination = "rtmp://example/live",
                            Codec = "h264",
                            Resolution = "1920x1080",
                            FrameRate = 30,
                        },
                        IsActive = true,
                        Health = CaptureSessionHealth.Degraded,
                        Elapsed = TimeSpan.FromMinutes(12),
                        Detail = "Dropping frames",
                    },
                },
            });

        ActionResult result = executor.Execute(
            state,
            LiveCommandExecutor.SetStageLayout("stage", "confidence", StageAudienceCommandMode.StageOnly));

        result.Frames.StageFrames["stage"].Payloads.Should().ContainSingle(payload =>
            payload.Id == "capture:stream-main"
            && payload.DisplayName == "Main Stream: Degraded");
        result.State.GeneratedState.RecoveryDiagnostics.ActiveCaptureSessionIds.Should().Contain("stream-main");
        result.State.GeneratedState.RecoveryDiagnostics.StageLayoutsByScreenId.Should().ContainKey("stage");
    }

    [Fact]
    public void Timer_and_capture_commands_mutate_generated_state_through_shared_pipeline()
    {
        BackendRenderEngine engine = CreateEngine();
        LiveCommandExecutor executor = new(engine);
        LiveRenderSessionState state = CreateState(
            screens:
            [
                StageScreen("stage"),
            ],
            stageLayouts: new Dictionary<string, StageLayout>(StringComparer.OrdinalIgnoreCase)
            {
                ["confidence"] = new StageLayout
                {
                    Id = "confidence",
                    Name = "Confidence",
                    Elements =
                    [
                        new StageLayoutElement
                        {
                            Id = "service-timer",
                            Kind = StageLayoutElementKind.Timer,
                            SourceId = "service-start",
                        },
                        new StageLayoutElement
                        {
                            Id = "capture",
                            Kind = StageLayoutElementKind.CaptureStatus,
                            SourceId = "stream-main",
                        },
                    ],
                },
            },
            stageAssignments: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["stage"] = "confidence",
            });

        ActionBatch batch = new()
        {
            Source = new LiveCommandSource { Kind = LiveCommandSourceKind.Automation, Id = "automation" },
            Actions =
            [
                executor.Expand(LiveCommandExecutor.SetTimer(new TimerSnapshot
                {
                    Id = "service-start",
                    Name = "Service Start",
                    Kind = GeneratedTimerKind.Countdown,
                    Status = GeneratedTimerStatus.Running,
                    Elapsed = TimeSpan.FromSeconds(5),
                    Remaining = TimeSpan.FromMinutes(9) + TimeSpan.FromSeconds(55),
                    DisplayValue = "09:55",
                    ActiveColor = "green",
                })).Actions.Single(),
                executor.Expand(LiveCommandExecutor.SetCaptureSession(new CaptureSessionState
                {
                    Metadata = new CaptureSessionMetadata
                    {
                        Id = "stream-main",
                        Name = "Main Stream",
                        SourceScreenId = "stage",
                        DestinationKind = CaptureDestinationKind.Rtmp,
                        Destination = "rtmp://example/live",
                    },
                    IsActive = true,
                    Health = CaptureSessionHealth.Healthy,
                })).Actions.Single(),
            ],
        };

        ActionResult result = executor.Execute(state, batch);

        result.Succeeded.Should().BeTrue();
        result.State.GeneratedState.Timers.Should().ContainKey("service-start");
        result.State.GeneratedState.CaptureSessions.Should().ContainKey("stream-main");
        result.Frames.StageFrames["stage"].Payloads.Should().Contain(payload =>
            payload.Id == "timer:service-start" && payload.DisplayName == "09:55");
        result.Frames.StageFrames["stage"].Payloads.Should().Contain(payload =>
            payload.Id == "capture:stream-main" && payload.DisplayName == "Main Stream: Healthy");
        result.State.GeneratedState.RecoveryDiagnostics.ActiveCaptureSessionIds.Should().Contain("stream-main");
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

    private static LiveRenderSessionState CreateState(
        IReadOnlyList<OutputScreen>? screens = null,
        IReadOnlyDictionary<OutputLayerKind, LayerState>? layers = null,
        LookPreset? look = null,
        IReadOnlyDictionary<string, StageLayout>? stageLayouts = null,
        IReadOnlyDictionary<string, string>? stageAssignments = null,
        GeneratedStateSnapshot? generatedState = null)
    {
        return new LiveRenderSessionState
        {
            Screens = (screens ?? Array.Empty<OutputScreen>())
                .ToDictionary(screen => screen.Id, StringComparer.OrdinalIgnoreCase),
            Layers = layers ?? LiveRenderSessionState.CreateEmptyLayers(),
            ActiveLook = look ?? new LookPreset { Id = "default", Name = "Default" },
            StageLayouts = stageLayouts
                ?? new Dictionary<string, StageLayout>(StringComparer.OrdinalIgnoreCase),
            StageLayoutIdsByScreenId = stageAssignments
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            GeneratedState = generatedState ?? new GeneratedStateSnapshot(),
        };
    }
}