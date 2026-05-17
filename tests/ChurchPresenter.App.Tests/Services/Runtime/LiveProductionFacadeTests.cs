using ChurchPresenter.Backend.Commands;
using ChurchPresenter.Backend.Output;
using ChurchPresenter.Backend.Overlays;
using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Backend.Stage;

using FluentAssertions;

using Moq;

namespace ChurchPresenter.App.Tests.Services.Runtime;

/// <summary>
/// Regression tests for <see cref="LiveProductionFacade"/> as the higher-level bridge into the backend command pipeline.
/// </summary>
public sealed class LiveProductionFacadeTests
{
    [Fact]
    public void SetOverlay_updates_generated_state_and_persists_across_playback_rebuilds()
    {
        PlaybackState currentPlaybackState = CreatePlaybackState("s1");
        Mock<IPlaybackEngine> playback = CreatePlaybackEngine(() => currentPlaybackState);
        FakeOutputRoutingService routing = new();
        FakeOutputTopologyService topology = new();
        LiveProductionFacade facade = CreateFacade(playback.Object, routing, topology);

        ActionResult result = facade.SetOverlay(new OverlayContentState
        {
            Id = "welcome",
            Name = "Welcome",
            Kind = OverlayContentKind.Message,
            IsVisible = true,
            Text = "Welcome!",
        });

        result.Succeeded.Should().BeTrue();
        facade.Current.SessionState.GeneratedState.Messages.Should().ContainKey("welcome");
        facade.Current.Frames.AudienceFrames[OutputFeedIds.Main].Layers.Should().ContainSingle(layer =>
            layer.Kind == ChurchPresenter.Backend.Rendering.OutputLayerKind.Messages
            && layer.IsVisible
            && layer.Payload.DisplayName == "Welcome!");

        currentPlaybackState = CreatePlaybackState("s2");
        playback.Raise(engine => engine.StateChanged += null, new PlaybackStateChangedEventArgs { State = currentPlaybackState });

        facade.Current.SessionState.GeneratedState.Messages.Should().ContainKey("welcome");
        facade.Current.SessionState.Layers[ChurchPresenter.Backend.Rendering.OutputLayerKind.Slide].Payload!.Id.Should().Be("s2");
        facade.Current.Frames.AudienceFrames[OutputFeedIds.Main].Layers.Should().Contain(layer =>
            layer.Kind == ChurchPresenter.Backend.Rendering.OutputLayerKind.Messages
            && layer.Payload.DisplayName == "Welcome!");
    }

    [Fact]
    public void Runtime_commands_update_the_facade_frame_store()
    {
        PlaybackState currentPlaybackState = CreatePlaybackState("s1");
        Mock<IPlaybackEngine> playback = CreatePlaybackEngine(() => currentPlaybackState);
        FakeOutputRoutingService routing = new();
        FakeOutputTopologyService topology = new();
        InMemoryRenderFrameStore frameStore = new();
        LiveProductionFacade facade = CreateFacade(playback.Object, routing, topology, frameStore: frameStore);

        facade.SetOverlay(new OverlayContentState
        {
            Id = "welcome",
            Name = "Welcome",
            Kind = OverlayContentKind.Message,
            IsVisible = true,
            Text = "Welcome!",
        });

        frameStore.GetAudienceFrame(OutputFeedIds.Main)!.Layers.Should().ContainSingle(layer =>
            layer.Kind == ChurchPresenter.Backend.Rendering.OutputLayerKind.Messages
            && layer.IsVisible
            && layer.Payload.DisplayName == "Welcome!");
    }

    [Fact]
    public void Playback_snapshot_targets_announcements_layer_when_requested()
    {
        PlaybackState currentPlaybackState = CreatePlaybackState("s1") with
        {
            PresentationLayerKind = ChurchPresenter.Backend.Rendering.OutputLayerKind.Announcements,
        };
        Mock<IPlaybackEngine> playback = CreatePlaybackEngine(() => currentPlaybackState);
        FakeOutputRoutingService routing = new();
        FakeOutputTopologyService topology = new();

        LiveProductionFacade facade = CreateFacade(playback.Object, routing, topology);

        facade.Current.SessionState.Layers[ChurchPresenter.Backend.Rendering.OutputLayerKind.Announcements]
            .Payload.Should().NotBeNull();
        facade.Current.SessionState.Layers[ChurchPresenter.Backend.Rendering.OutputLayerKind.Slide]
            .Payload.Should().BeNull();
        facade.Current.Frames.AudienceFrames[OutputFeedIds.Main].Layers.Should().ContainSingle(layer =>
            layer.Kind == ChurchPresenter.Backend.Rendering.OutputLayerKind.Announcements
            && layer.IsVisible);
    }

    [Fact]
    public async Task SetLookAsync_rebuilds_snapshot_and_preserves_stage_runtime_overrides()
    {
        PlaybackState currentPlaybackState = CreatePlaybackState("s1");
        Mock<IPlaybackEngine> playback = CreatePlaybackEngine(() => currentPlaybackState);
        FakeOutputRoutingService routing = new();
        FakeOutputTopologyService topology = new();
        LiveProductionFacade facade = CreateFacade(playback.Object, routing, topology);

        ActionResult stageResult = facade.SetStageLayout(OutputFeedIds.Stage, "confidence", StageAudienceCommandMode.StageOnly);

        stageResult.Succeeded.Should().BeTrue();
        facade.Current.Frames.StageFrames[OutputFeedIds.Stage].StageLayoutId.Should().Be("confidence");
        facade.Current.Frames.StageFrames[OutputFeedIds.Stage].CommandMode.Should().Be(StageAudienceCommandMode.StageOnly);

        await facade.SetLookAsync("streamless");

        facade.Current.SessionState.ActiveLook.Id.Should().Be("streamless");
        facade.Current.Frames.StageFrames[OutputFeedIds.Stage].StageLayoutId.Should().Be("confidence");
        facade.Current.Topology.ResolveDiagnostics(OutputFeedIds.Main).Health.Should().Be(EndpointHealth.Connected);
    }

    [Fact]
    public async Task Look_routes_project_mask_payloads_and_theme_variants_into_audience_frames()
    {
        PlaybackState currentPlaybackState = CreatePlaybackState("s1");
        Mock<IPlaybackEngine> playback = CreatePlaybackEngine(() => currentPlaybackState);
        FakeOutputRoutingService routing = new();
        await routing.SetRoutesAsync(
        [
            new OutputLookFeedRouting
            {
                FeedId = OutputFeedIds.Main,
                Slide = true,
                Media = true,
                Layers =
                [
                    new OutputLayerRouteDefinition { Layer = "slide", Enabled = true },
                    new OutputLayerRouteDefinition { Layer = "mask", Enabled = true, MaskId = "main-mask" },
                ],
            },
            new OutputLookFeedRouting
            {
                FeedId = OutputFeedIds.Stream,
                Slide = true,
                Media = true,
                Layers =
                [
                    new OutputLayerRouteDefinition { Layer = "slide", Enabled = true, ThemeVariantId = "lower-third" },
                    new OutputLayerRouteDefinition { Layer = "mask", Enabled = false },
                ],
            },
        ]);
        FakeOutputTopologyService topology = new();

        LiveProductionFacade facade = CreateFacade(playback.Object, routing, topology);

        facade.Current.Frames.AudienceFrames[OutputFeedIds.Main].Layers.Should().ContainSingle(layer =>
            layer.Kind == ChurchPresenter.Backend.Rendering.OutputLayerKind.Mask
            && layer.Payload.Id == "mask:main-mask");
        facade.Current.Frames.AudienceFrames[OutputFeedIds.Stream].Layers.Should().NotContain(layer =>
            layer.Kind == ChurchPresenter.Backend.Rendering.OutputLayerKind.Mask);
        facade.Current.Frames.AudienceFrames[OutputFeedIds.Stream].Layers.Single(layer =>
                layer.Kind == ChurchPresenter.Backend.Rendering.OutputLayerKind.Slide)
            .Payload.ThemeVariantId.Should().Be("lower-third");
    }

    [Fact]
    public void Constructor_snapshot_uses_portable_stage_layout_defaults()
    {
        PlaybackState currentPlaybackState = CreatePlaybackState("s1");
        Mock<IPlaybackEngine> playback = CreatePlaybackEngine(() => currentPlaybackState);
        FakeOutputRoutingService routing = new();
        FakeOutputTopologyService topology = new();
        FakeStageLayoutRegistryService stageLayouts = new()
        {
            Layouts = new Dictionary<string, StageLayout>(StringComparer.OrdinalIgnoreCase)
            {
                ["confidence"] = new StageLayout
                {
                    Id = "confidence",
                    Name = "Confidence",
                    Elements =
                    [
                        new StageLayoutElement
                        {
                            Id = "current-text",
                            Kind = StageLayoutElementKind.CurrentSlideText,
                        },
                    ],
                },
            },
            DefaultLayoutIdsByScreenId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [OutputFeedIds.Stage] = "confidence",
            },
        };

        LiveProductionFacade facade = CreateFacade(playback.Object, routing, topology, stageLayouts);

        facade.Current.SessionState.StageLayouts.Should().ContainKey("confidence");
        facade.Current.SessionState.StageLayoutIdsByScreenId.Should().ContainKey(OutputFeedIds.Stage);
        facade.Current.Frames.StageFrames[OutputFeedIds.Stage].StageLayoutId.Should().Be("confidence");
    }

    [Fact]
    public void Constructor_snapshot_populates_current_next_and_notes_for_stage_layouts()
    {
        PlaybackState currentPlaybackState = CreatePlaybackState("s1");
        Mock<IPlaybackEngine> playback = CreatePlaybackEngine(() => currentPlaybackState);
        FakeOutputRoutingService routing = new();
        FakeOutputTopologyService topology = new();
        FakeStageLayoutRegistryService stageLayouts = new()
        {
            Layouts = new Dictionary<string, StageLayout>(StringComparer.OrdinalIgnoreCase)
            {
                ["confidence"] = new StageLayout
                {
                    Id = "confidence",
                    Name = "Confidence",
                    Elements =
                    [
                        new StageLayoutElement
                        {
                            Id = "current-text",
                            Kind = StageLayoutElementKind.CurrentSlideText,
                        },
                        new StageLayoutElement
                        {
                            Id = "next-text",
                            Kind = StageLayoutElementKind.NextSlideText,
                        },
                        new StageLayoutElement
                        {
                            Id = "notes",
                            Kind = StageLayoutElementKind.Notes,
                        },
                    ],
                },
            },
            DefaultLayoutIdsByScreenId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [OutputFeedIds.Stage] = "confidence",
            },
        };

        LiveProductionFacade facade = CreateFacade(playback.Object, routing, topology, stageLayouts);

        StageRenderFrame stageFrame = facade.Current.Frames.StageFrames[OutputFeedIds.Stage];
        facade.Current.SessionState.StagePresentation.CurrentSlideText.Should().Be("Amazing grace");
        facade.Current.SessionState.StagePresentation.NextSlideText.Should().Be("How sweet the sound");
        facade.Current.SessionState.StagePresentation.Notes.Should().Be("Start softly.");
        stageFrame.Payloads.Should().Contain(payload =>
            payload.SourceReference == "stage-current-slide-text"
            && payload.DisplayName == "Amazing grace");
        stageFrame.Payloads.Should().Contain(payload =>
            payload.SourceReference == "stage-next-slide-text"
            && payload.DisplayName == "How sweet the sound");
        stageFrame.Payloads.Should().Contain(payload =>
            payload.SourceReference == "stage-notes"
            && payload.DisplayName == "Start softly.");
    }

    [Fact]
    public void ClearGroup_clears_runtime_overlay_and_keeps_it_cleared_after_playback_rebuild()
    {
        PlaybackState currentPlaybackState = CreatePlaybackState("s1");
        Mock<IPlaybackEngine> playback = CreatePlaybackEngine(() => currentPlaybackState);
        FakeOutputRoutingService routing = new();
        FakeOutputTopologyService topology = new();
        LiveProductionFacade facade = CreateFacade(playback.Object, routing, topology);

        facade.SetOverlay(new OverlayContentState
        {
            Id = "welcome",
            Name = "Welcome",
            Kind = OverlayContentKind.Message,
            IsVisible = true,
            Text = "Welcome!",
        });

        ActionResult clearResult = facade.ClearGroup("overlay-reset");

        clearResult.Succeeded.Should().BeTrue();
        facade.Current.SessionState.GeneratedState.Messages.Should().ContainKey("welcome");
        facade.Current.SessionState.GeneratedState.Messages["welcome"].IsVisible.Should().BeFalse();
        facade.Current.Frames.AudienceFrames[OutputFeedIds.Main].Layers.Should().NotContain(layer =>
            layer.Kind == ChurchPresenter.Backend.Rendering.OutputLayerKind.Messages
            && layer.IsVisible);

        currentPlaybackState = CreatePlaybackState("s2");
        playback.Raise(engine => engine.StateChanged += null, new PlaybackStateChangedEventArgs { State = currentPlaybackState });

        facade.Current.SessionState.GeneratedState.Messages.Should().NotContainKey("welcome");
        facade.Current.Frames.AudienceFrames[OutputFeedIds.Main].Layers.Should().NotContain(layer =>
            layer.Kind == ChurchPresenter.Backend.Rendering.OutputLayerKind.Messages
            && layer.IsVisible);
    }

    [Fact]
    public void ClearLayers_uses_backend_layer_state_across_playback_rebuilds_until_new_payload()
    {
        PlaybackState currentPlaybackState = CreatePlaybackState("s1");
        Mock<IPlaybackEngine> playback = CreatePlaybackEngine(() => currentPlaybackState);
        FakeOutputRoutingService routing = new();
        FakeOutputTopologyService topology = new();
        LiveProductionFacade facade = CreateFacade(playback.Object, routing, topology);

        ActionResult clearResult = facade.ClearLayers([ChurchPresenter.Backend.Rendering.OutputLayerKind.Slide]);

        clearResult.Succeeded.Should().BeTrue();
        facade.Current.SessionState.Layers[ChurchPresenter.Backend.Rendering.OutputLayerKind.Slide].IsCleared.Should().BeTrue();

        currentPlaybackState = CreatePlaybackState("s1");
        playback.Raise(engine => engine.StateChanged += null, new PlaybackStateChangedEventArgs { State = currentPlaybackState });

        facade.Current.SessionState.Layers[ChurchPresenter.Backend.Rendering.OutputLayerKind.Slide].IsCleared.Should().BeTrue();

        currentPlaybackState = CreatePlaybackState("s2");
        playback.Raise(engine => engine.StateChanged += null, new PlaybackStateChangedEventArgs { State = currentPlaybackState });

        facade.Current.SessionState.Layers[ChurchPresenter.Backend.Rendering.OutputLayerKind.Slide].IsCleared.Should().BeFalse();
        facade.Current.SessionState.Layers[ChurchPresenter.Backend.Rendering.OutputLayerKind.Slide].Payload!.Id.Should().Be("s2");
    }

    [Fact]
    public void ReleaseClearedLayers_restores_same_payload_after_operator_takes_layer_live_again()
    {
        PlaybackState currentPlaybackState = CreatePlaybackState("s1");
        Mock<IPlaybackEngine> playback = CreatePlaybackEngine(() => currentPlaybackState);
        FakeOutputRoutingService routing = new();
        FakeOutputTopologyService topology = new();
        LiveProductionFacade facade = CreateFacade(playback.Object, routing, topology);

        facade.ClearLayers([ChurchPresenter.Backend.Rendering.OutputLayerKind.Slide]);

        ActionResult result = facade.ReleaseClearedLayers([ChurchPresenter.Backend.Rendering.OutputLayerKind.Slide]);

        result.Succeeded.Should().BeTrue();
        facade.Current.SessionState.Layers[ChurchPresenter.Backend.Rendering.OutputLayerKind.Slide].IsCleared.Should().BeFalse();
        facade.Current.SessionState.Layers[ChurchPresenter.Backend.Rendering.OutputLayerKind.Slide].Payload!.Id.Should().Be("s1");
        facade.Current.Frames.AudienceFrames[OutputFeedIds.Main].Layers.Should().ContainSingle(layer =>
            layer.Kind == ChurchPresenter.Backend.Rendering.OutputLayerKind.Slide
            && layer.IsVisible
            && layer.Payload.Id == "s1");
    }

    [Fact]
    public void ExecuteMacro_applies_generated_commands_and_persists_runtime_overrides()
    {
        PlaybackState currentPlaybackState = CreatePlaybackState("s1");
        Mock<IPlaybackEngine> playback = CreatePlaybackEngine(() => currentPlaybackState);
        FakeOutputRoutingService routing = new();
        FakeOutputTopologyService topology = new();
        LiveProductionFacade facade = CreateFacade(playback.Object, routing, topology);
        LiveMacroDefinition macro = new()
        {
            Id = "walk-in",
            Name = "Walk In",
            Commands =
            [
                LiveCommandExecutor.SetOverlay(
                    new OverlayContentState
                    {
                        Id = "welcome",
                        Name = "Welcome",
                        Kind = OverlayContentKind.Message,
                        IsVisible = true,
                        Text = "Welcome!",
                    },
                    new LiveCommandSource { Kind = LiveCommandSourceKind.Macro, Id = "walk-in" }),
                LiveCommandExecutor.SetTimer(
                    new TimerSnapshot
                    {
                        Id = "service-start",
                        Name = "Service Start",
                        Kind = GeneratedTimerKind.Countdown,
                        Status = GeneratedTimerStatus.Running,
                        Remaining = TimeSpan.FromMinutes(5),
                        DisplayValue = "05:00",
                    },
                    new LiveCommandSource { Kind = LiveCommandSourceKind.Macro, Id = "walk-in" }),
            ],
        };

        ActionResult result = facade.ExecuteMacro(macro);

        result.Succeeded.Should().BeTrue();
        result.Batch.MacroId.Should().Be("walk-in");
        facade.Current.SessionState.GeneratedState.Messages.Should().ContainKey("welcome");
        facade.Current.SessionState.GeneratedState.Timers.Should().ContainKey("service-start");

        currentPlaybackState = CreatePlaybackState("s2");
        playback.Raise(engine => engine.StateChanged += null, new PlaybackStateChangedEventArgs { State = currentPlaybackState });

        facade.Current.SessionState.GeneratedState.Messages.Should().ContainKey("welcome");
        facade.Current.SessionState.GeneratedState.Timers.Should().ContainKey("service-start");
    }

    [Fact]
    public void ReportOutputHostFeedback_tracks_applied_frame_and_persists_across_rebuilds()
    {
        PlaybackState currentPlaybackState = CreatePlaybackState("s1");
        Mock<IPlaybackEngine> playback = CreatePlaybackEngine(() => currentPlaybackState);
        FakeOutputRoutingService routing = new();
        FakeOutputTopologyService topology = new();
        LiveProductionFacade facade = CreateFacade(playback.Object, routing, topology);

        long resolvedSequence = facade.Current.Frames.AudienceFrames[OutputFeedIds.Main].Sequence;

        facade.ReportOutputHostFeedback(new OutputHostFrameFeedbackState
        {
            ScreenId = OutputFeedIds.Main,
            EndpointId = "local-display:0",
            LastAppliedFrameSequence = resolvedSequence,
            LastApplyDuration = TimeSpan.FromMilliseconds(12),
            EndpointHealth = EndpointHealth.Connected,
            IsVisible = true,
            WindowId = "window-42",
            Detail = "Frame applied.",
        });

        OutputHostFrameFeedbackState feedback = facade.Current.SessionState.GeneratedState.HostFeedback.Values.Should().ContainSingle().Which;
        feedback.LastResolvedFrameSequence.Should().Be(resolvedSequence);
        feedback.LastAppliedFrameSequence.Should().Be(resolvedSequence);
        feedback.LastApplyDuration.Should().Be(TimeSpan.FromMilliseconds(12));
        feedback.EndpointHealth.Should().Be(EndpointHealth.Connected);
        feedback.WindowId.Should().Be("window-42");

        currentPlaybackState = CreatePlaybackState("s2");
        playback.Raise(engine => engine.StateChanged += null, new PlaybackStateChangedEventArgs { State = currentPlaybackState });

        facade.Current.SessionState.GeneratedState.HostFeedback.Values.Should().ContainSingle().Which.EndpointId.Should().Be("local-display:0");
    }

    [Fact]
    public void ReportMediaPlayerFailure_surfaces_failure_in_generated_state()
    {
        PlaybackState currentPlaybackState = CreatePlaybackState("s1");
        Mock<IPlaybackEngine> playback = CreatePlaybackEngine(() => currentPlaybackState);
        FakeOutputRoutingService routing = new();
        FakeOutputTopologyService topology = new();
        LiveProductionFacade facade = CreateFacade(playback.Object, routing, topology);

        facade.ReportMediaPlayerFailure(new MediaPlayerFailureState
        {
            PlayerId = "main-media",
            LayerKind = ChurchPresenter.Backend.Rendering.OutputLayerKind.Media,
            PayloadId = "clip-1",
            Message = "Media player failed to open clip-1.",
        });

        facade.Current.SessionState.GeneratedState.MediaPlayerFailures.Should().ContainKey("main-media");
        facade.Current.SessionState.GeneratedState.MediaPlayerFailures["main-media"].PayloadId.Should().Be("clip-1");
    }

    private static LiveProductionFacade CreateFacade(
        IPlaybackEngine playback,
        IOutputRoutingService routing,
        IOutputTopologyService topology,
        IStageLayoutRegistryService? stageLayouts = null,
        IRenderFrameStore? frameStore = null)
    {
        BackendRenderFrameResolver frameResolver = new();
        BackendRenderEngine renderEngine = new(frameResolver, new InMemoryRenderFrameStore());

        return new LiveProductionFacade(
            playback,
            routing,
            topology,
            new LiveCommandExecutor(renderEngine),
            frameResolver,
            frameStore ?? new InMemoryRenderFrameStore(),
            stageLayouts ?? new FakeStageLayoutRegistryService());
    }

    private static Mock<IPlaybackEngine> CreatePlaybackEngine(Func<PlaybackState> currentState)
    {
        ArgumentNullException.ThrowIfNull(currentState);

        Mock<IPlaybackEngine> playback = new();
        playback.SetupGet(engine => engine.CurrentState).Returns(() => currentState());
        playback.SetupGet(engine => engine.IsAudienceEnabled).Returns(() => currentState().IsAudienceEnabled);
        playback.SetupGet(engine => engine.IsStageEnabled).Returns(() => currentState().IsStageEnabled);
        playback.SetupGet(engine => engine.IsLive).Returns(() => currentState().IsLive);
        playback.SetupGet(engine => engine.Presentation).Returns(() => currentState().Presentation);
        playback.SetupGet(engine => engine.PresentationPath).Returns(() => currentState().PresentationPath);
        playback.SetupGet(engine => engine.CurrentSlideId).Returns(() => currentState().CurrentSlideId);
        playback.SetupGet(engine => engine.CurrentSlideInstanceKey).Returns(() => currentState().CurrentSlideInstanceKey);
        playback.SetupGet(engine => engine.CurrentSlideIndex).Returns(() => currentState().CurrentSlideIndex);
        playback.SetupGet(engine => engine.CurrentBuildIndex).Returns(() => currentState().BuildIndex);
        playback.SetupGet(engine => engine.IsBlackout).Returns(() => currentState().IsBlackout);
        playback.SetupGet(engine => engine.IsClear).Returns(() => currentState().IsClear);
        playback.SetupGet(engine => engine.VisibleLayerIds).Returns(() => currentState().VisibleLayerIds);
        playback.SetupGet(engine => engine.MediaLayers).Returns(() => currentState().MediaLayers);
        playback.SetupGet(engine => engine.Suppress).Returns(() => currentState().Suppress);
        playback.SetupGet(engine => engine.IsClearing).Returns(() => currentState().IsClearing);
        playback.SetupGet(engine => engine.CanUndoClearPresentation).Returns(() => false);
        playback.SetupGet(engine => engine.CanUndoClearMedia).Returns(() => false);
        playback.SetupGet(engine => engine.HasMoreBuilds).Returns(() => false);
        return playback;
    }

    private static PlaybackState CreatePlaybackState(string slideId)
    {
        PresentationProject project = new()
        {
            Manifest = new PresentationManifest { Title = "Service", AspectRatio = "16:9" },
            Slides =
            [
                new PresentationSlide
                {
                    Id = "s1",
                    Type = "song",
                    SectionLabel = "Verse 1",
                    Notes = "Start softly.",
                    Layers =
                    [
                        new TextLayer
                        {
                            Id = "s1-text",
                            Name = "Lyrics",
                            Content = "Amazing grace",
                        },
                    ],
                },
                new PresentationSlide
                {
                    Id = "s2",
                    Type = "song",
                    SectionLabel = "Verse 2",
                    Layers =
                    [
                        new TextLayer
                        {
                            Id = "s2-text",
                            Name = "Lyrics",
                            Content = "How sweet the sound",
                        },
                    ],
                },
            ],
        };

        return new PlaybackState
        {
            IsLive = true,
            IsAudienceEnabled = true,
            IsStageEnabled = true,
            PresentationPath = $@"C:\presentations\{slideId}.cpres",
            CurrentSlideId = slideId,
            CurrentSlideIndex = string.Equals(slideId, "s2", StringComparison.OrdinalIgnoreCase) ? 1 : 0,
            Presentation = new PresentationDocument
            {
                Manifest = new PresentationManifestDto { Title = "Service" },
                Project = project,
            },
        };
    }

    private sealed class FakeOutputRoutingService : IOutputRoutingService
    {
        private readonly List<OutputLookDefinition> _looks =
        [
            CreateDefaultLookWithClearGroups(),
            new OutputLookDefinition
            {
                Id = "streamless",
                Name = "Streamless",
                Routes =
                [
                    new OutputLookFeedRouting { FeedId = OutputFeedIds.Main, Slide = true, Media = true },
                    new OutputLookFeedRouting { FeedId = OutputFeedIds.Stream, Slide = true, Media = false },
                    new OutputLookFeedRouting { FeedId = OutputFeedIds.Lobby, Slide = true, Media = true },
                ],
            },
        ];

        public event EventHandler? Changed;

        public IReadOnlyList<OutputFeedDefinition> Feeds => OutputRoutingDefaults.BuiltInFeeds;

        public IReadOnlyList<OutputLookDefinition> Looks => _looks.Select(static look => look.Clone()).ToArray();

        public string ActiveLookId { get; private set; } = OutputLookIds.Default;

        public OutputLookDefinition ActiveLook =>
            _looks.First(look => string.Equals(look.Id, ActiveLookId, StringComparison.OrdinalIgnoreCase)).Clone();

        public bool RoutesLayer(string feedId, OutputLayerKind layerKind)
        {
            return ActiveLook.ResolveRouting(feedId).Routes(layerKind);
        }

        public Task SetActiveLookAsync(string lookId, CancellationToken cancellationToken = default)
        {
            ActiveLookId = _looks.Any(look => string.Equals(look.Id, lookId, StringComparison.OrdinalIgnoreCase))
                ? lookId
                : OutputLookIds.Default;
            Changed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task ResetToDefaultAsync(CancellationToken cancellationToken = default)
        {
            ActiveLookId = OutputLookIds.Default;
            Changed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task SetLayerRoutingAsync(string feedId, OutputLayerKind layerKind, bool enabled, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task SetRoutesAsync(IEnumerable<OutputLookFeedRouting> routes, CancellationToken cancellationToken = default)
        {
            OutputLookDefinition activeLook = _looks.First(look =>
                string.Equals(look.Id, ActiveLookId, StringComparison.OrdinalIgnoreCase));
            activeLook.Routes = routes.Select(static route => route.Clone()).ToList();
            Changed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task SetClearGroupsAsync(IEnumerable<OutputLookClearGroupDefinition> clearGroups, CancellationToken cancellationToken = default)
        {
            OutputLookDefinition activeLook = _looks.First(look =>
                string.Equals(look.Id, ActiveLookId, StringComparison.OrdinalIgnoreCase));
            activeLook.ClearGroups = clearGroups.Select(static group => group.Clone()).ToList();
            Changed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        private static OutputLookDefinition CreateDefaultLookWithClearGroups()
        {
            OutputLookDefinition look = OutputRoutingDefaults.CreateDefaultLook();
            look.ClearGroups =
            [
                new OutputLookClearGroupDefinition
                {
                    Id = "overlay-reset",
                    Name = "Overlay Reset",
                    Layers =
                    [
                        "messages",
                        "props",
                    ],
                },
            ];
            return look;
        }
    }

    private sealed class FakeOutputTopologyService : IOutputTopologyService
    {
        private readonly OutputTopologySnapshot _snapshot = new()
        {
            Screens = new Dictionary<string, OutputScreen>(StringComparer.OrdinalIgnoreCase)
            {
                [OutputFeedIds.Main] = new OutputScreen
                {
                    Id = OutputFeedIds.Main,
                    Name = "Main",
                    Kind = OutputScreenKind.Audience,
                },
                [OutputFeedIds.Stream] = new OutputScreen
                {
                    Id = OutputFeedIds.Stream,
                    Name = "Stream",
                    Kind = OutputScreenKind.Audience,
                },
                [OutputFeedIds.Stage] = new OutputScreen
                {
                    Id = OutputFeedIds.Stage,
                    Name = "Stage",
                    Kind = OutputScreenKind.Stage,
                },
            },
            ScreenDiagnostics = new Dictionary<string, OutputScreenDiagnostics>(StringComparer.OrdinalIgnoreCase)
            {
                [OutputFeedIds.Main] = new OutputScreenDiagnostics
                {
                    ScreenId = OutputFeedIds.Main,
                    ScreenName = "Main",
                    Health = EndpointHealth.Connected,
                    Message = "Connected.",
                },
                [OutputFeedIds.Stream] = new OutputScreenDiagnostics
                {
                    ScreenId = OutputFeedIds.Stream,
                    ScreenName = "Stream",
                    Health = EndpointHealth.Connected,
                    Message = "Connected.",
                },
                [OutputFeedIds.Stage] = new OutputScreenDiagnostics
                {
                    ScreenId = OutputFeedIds.Stage,
                    ScreenName = "Stage",
                    Health = EndpointHealth.Connected,
                    Message = "Connected.",
                },
            },
        };

        public IReadOnlyList<OutputFeedDefinition> AudienceScreens => OutputRoutingDefaults.BuiltInFeeds;

        public OutputTopologySnapshot GetSnapshot() => _snapshot;
    }

    private sealed class FakeStageLayoutRegistryService : IStageLayoutRegistryService
    {
        public IReadOnlyDictionary<string, StageLayout> Layouts { get; init; } =
            new Dictionary<string, StageLayout>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, string> DefaultLayoutIdsByScreenId { get; init; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, StageLayout> GetLayouts() => Layouts;

        public IReadOnlyDictionary<string, string> GetDefaultLayoutIdsByScreenId() => DefaultLayoutIdsByScreenId;
    }
}