using ChurchPresenter.Backend.Commands;
using ChurchPresenter.Backend.Output;
using ChurchPresenter.Backend.Overlays;
using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Backend.Stage;

using FluentAssertions;

using Moq;

using BackendOutputLayerKind = ChurchPresenter.Backend.Rendering.OutputLayerKind;

namespace ChurchPresenter.App.Tests.Services.Runtime;

/// <summary>
/// Tests for <see cref="LiveProductionQueryService"/> projections used by modular output/generated-system consumers.
/// </summary>
public sealed class LiveProductionQueryServiceTests
{
    [Fact]
    public void BuildSnapshot_projects_output_routes_and_diagnostics()
    {
        LiveProductionSnapshot snapshot = new()
        {
            SessionState = new LiveRenderSessionState
            {
                Version = 7,
                Layers = new Dictionary<BackendOutputLayerKind, LayerState>(LiveRenderSessionState.CreateEmptyLayers())
                {
                    [BackendOutputLayerKind.Slide] = new LayerState
                    {
                        Kind = BackendOutputLayerKind.Slide,
                        Payload = new RenderPayloadDescriptor
                        {
                            Id = "slide-1",
                            Kind = RenderPayloadKind.Presentation,
                            DisplayName = "Slide 1",
                            SourceReference = "s1",
                            Detail = new PresentationRenderPayload
                            {
                                PresentationPath = @"C:\shows\service.cpres",
                                SlideId = "slide-1",
                                ArrangementInstanceKey = "slide-1:repeat-2",
                            },
                        },
                        IsVisible = true,
                    },
                },
                ActiveLook = new LookPreset
                {
                    Id = "service",
                    Name = "Service",
                    ScreenRoutes =
                    [
                        new ScreenLayerRouting
                        {
                            ScreenId = OutputFeedIds.Main,
                            Layers =
                            [
                                new LayerRoute { LayerKind = BackendOutputLayerKind.Slide, IsEnabled = true },
                                new LayerRoute { LayerKind = BackendOutputLayerKind.Media, IsEnabled = false },
                                new LayerRoute { LayerKind = BackendOutputLayerKind.Messages, IsEnabled = true },
                                new LayerRoute { LayerKind = BackendOutputLayerKind.Mask, IsEnabled = true, MaskId = "main-mask" },
                            ],
                        },
                    ],
                },
                GeneratedState = new GeneratedStateSnapshot
                {
                    HostFeedback = new Dictionary<string, OutputHostFrameFeedbackState>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["main-host"] = new OutputHostFrameFeedbackState
                        {
                            ScreenId = OutputFeedIds.Main,
                            EndpointId = "local-display:0",
                            LastResolvedFrameSequence = 11,
                            LastAppliedFrameSequence = 10,
                            LastAppliedAt = DateTimeOffset.Parse("2026-05-04T18:30:00Z"),
                            LastApplyDuration = TimeSpan.FromMilliseconds(14),
                            DroppedFrameCount = 2,
                            IsVisible = true,
                            EndpointHealth = EndpointHealth.Connected,
                            WindowId = "window-42",
                            Detail = "Host is one frame behind.",
                        },
                    },
                },
            },
            Frames = new RenderFrameSet
            {
                AudienceFrames = new Dictionary<string, AudienceRenderFrame>(StringComparer.OrdinalIgnoreCase)
                {
                    [OutputFeedIds.Main] = new AudienceRenderFrame
                    {
                        Sequence = 11,
                        ScreenId = OutputFeedIds.Main,
                        Layers =
                        [
                            new RenderLayerDescriptor
                            {
                                Kind = BackendOutputLayerKind.Slide,
                                Payload = new RenderPayloadDescriptor
                                {
                                    Id = "slide-1",
                                    Kind = RenderPayloadKind.Presentation,
                                    DisplayName = "slide-1",
                                },
                                IsVisible = true,
                            },
                        ],
                        Diagnostics = new RenderDiagnostics
                        {
                            EndpointIds = ["local-display:0"],
                        },
                    },
                },
                StageFrames = new Dictionary<string, StageRenderFrame>(StringComparer.OrdinalIgnoreCase)
                {
                    [OutputFeedIds.Stage] = new StageRenderFrame
                    {
                        Sequence = 11,
                        ScreenId = OutputFeedIds.Stage,
                        StageLayoutId = "confidence",
                        CommandMode = StageAudienceCommandMode.StageOnly,
                        Payloads =
                        [
                            new RenderPayloadDescriptor
                            {
                                Id = "stage:stage:current-text",
                                Kind = RenderPayloadKind.Overlay,
                                DisplayName = "Amazing grace",
                                SourceReference = "stage-current-slide-text",
                            },
                        ],
                    },
                },
            },
            Topology = new OutputTopologySnapshot
            {
                Screens = new Dictionary<string, OutputScreen>(StringComparer.OrdinalIgnoreCase)
                {
                    [OutputFeedIds.Main] = new OutputScreen
                    {
                        Id = OutputFeedIds.Main,
                        Name = "Main",
                        Kind = OutputScreenKind.Audience,
                    },
                    [OutputFeedIds.Stage] = new OutputScreen
                    {
                        Id = OutputFeedIds.Stage,
                        Name = "Stage",
                        Kind = OutputScreenKind.Stage,
                    },
                },
                Endpoints = new Dictionary<string, OutputEndpoint>(StringComparer.OrdinalIgnoreCase)
                {
                    ["local-display:0"] = new OutputEndpoint
                    {
                        Id = "local-display:0",
                        Name = "Main Display",
                        Kind = OutputEndpointKind.LocalDisplay,
                        Capabilities = EndpointCapability.LocalWindow,
                        Health = EndpointHealth.Connected,
                        NativeId = "0",
                    },
                    ["placeholder:stage"] = OutputEndpoint.Placeholder("placeholder:stage", "Stage Placeholder"),
                },
                ScreenMappings =
                [
                    new ScreenMapping { ScreenId = OutputFeedIds.Main, EndpointIds = ["local-display:0"] },
                    new ScreenMapping { ScreenId = OutputFeedIds.Stage, EndpointIds = ["placeholder:stage"] },
                ],
                ScreenDiagnostics = new Dictionary<string, OutputScreenDiagnostics>(StringComparer.OrdinalIgnoreCase)
                {
                    [OutputFeedIds.Main] = new OutputScreenDiagnostics
                    {
                        ScreenId = OutputFeedIds.Main,
                        ScreenName = "Main",
                        Health = EndpointHealth.Connected,
                        EndpointIds = ["local-display:0"],
                        Message = "Main connected.",
                    },
                    [OutputFeedIds.Stage] = new OutputScreenDiagnostics
                    {
                        ScreenId = OutputFeedIds.Stage,
                        ScreenName = "Stage",
                        Health = EndpointHealth.Placeholder,
                        EndpointIds = ["placeholder:stage"],
                        Message = "Stage placeholder.",
                    },
                },
            },
        };

        Mock<ILiveProductionFacade> liveProduction = new();
        liveProduction.SetupGet(service => service.Current).Returns(snapshot);

        Mock<IOutputRoutingService> routing = new();
        routing.SetupGet(service => service.ActiveLookId).Returns("service");
        routing.SetupGet(service => service.ActiveLook).Returns(new OutputLookDefinition
        {
            Id = "service",
            Name = "Service",
            ClearGroups =
            [
                new OutputLookClearGroupDefinition
                {
                    Id = "clear-overlays",
                    Name = "Overlays",
                    Scopes = [OutputClearScope.Messages, OutputClearScope.Props],
                    Layers = OutputRoutingDefaults.CreateClearGroupLayers(OutputClearScope.Messages, OutputClearScope.Props),
                },
            ],
        });
        routing.SetupGet(service => service.Looks).Returns(
        [
            new OutputLookDefinition { Id = "service", Name = "Service" },
            new OutputLookDefinition { Id = "alt", Name = "Alt" },
        ]);

        LiveProductionQueryService service = new(liveProduction.Object, routing.Object);
        LiveProductionQuerySnapshot query = service.Current;

        query.Version.Should().Be(7);
        query.ActiveLookId.Should().Be("service");
        query.ActiveLayers.Should().ContainSingle(layer =>
            layer.Kind == BackendOutputLayerKind.Slide
            && layer.IsLive
            && layer.PayloadId == "slide-1"
            && layer.PayloadPresentationPath == @"C:\shows\service.cpres"
            && layer.PayloadSlideId == "slide-1"
            && layer.PayloadInstanceKey == "slide-1:repeat-2");
        query.Endpoints.Should().ContainSingle(endpoint =>
            endpoint.EndpointId == "local-display:0"
            && endpoint.Health == EndpointHealth.Connected
            && endpoint.ScreenIds.Contains(OutputFeedIds.Main));
        query.FrameHealth.Should().ContainSingle(frame =>
            frame.ScreenId == OutputFeedIds.Main
            && frame.ResolvedSequence == 11
            && frame.AppliedSequence == 10
            && frame.IsStale
            && frame.DroppedFrameCount == 2
            && frame.LastApplyDuration == TimeSpan.FromMilliseconds(14)
            && frame.EndpointHealth == EndpointHealth.Connected
            && frame.WindowId == "window-42");
        query.Looks.Should().ContainSingle(look => look.Id == "service" && look.IsActive);
        query.Screens.Should().ContainSingle(screen =>
            screen.ScreenId == OutputFeedIds.Main
            && screen.Health == EndpointHealth.Connected
            && screen.ActiveLookId == "service"
            && screen.ActiveLookName == "Service"
            && screen.HasResolvedFrame
            && screen.FrameSequence == 11
            && screen.RoutesPresentation
            && !screen.RoutesMedia
            && screen.LayerRoutes.Any(route => route.Kind == BackendOutputLayerKind.Media && !route.IsEnabled)
            && screen.LayerRoutes.Any(route => route.Kind == BackendOutputLayerKind.Messages && route.IsEnabled)
            && screen.LayerRoutes.Any(route => route.Kind == BackendOutputLayerKind.Mask && route.MaskId == "main-mask")
            && screen.VisibleAudienceLayers.Contains(BackendOutputLayerKind.Slide)
            && screen.EndpointSummary == "local-display:0"
            && screen.RoutingSummary.Contains("media off")
            && screen.VisibleLayerSummary.Contains(nameof(BackendOutputLayerKind.Slide))
            && screen.FrameSummary.Contains("#11"));
        query.Screens.Should().ContainSingle(screen =>
            screen.ScreenId == OutputFeedIds.Stage
            && screen.HasResolvedFrame
            && screen.FrameSequence == 11
            && screen.StageLayoutId == "confidence"
            && screen.StageCommandMode == StageAudienceCommandMode.StageOnly
            && screen.StagePayloads.Any(payload => payload.DisplayName == "Amazing grace")
            && screen.RoutingSummary.Contains("independent")
            && screen.VisibleLayerSummary.Contains("confidence"));
        query.StageScreens.Should().ContainSingle(stage =>
            stage.ScreenId == OutputFeedIds.Stage
            && stage.StageLayoutId == "confidence"
            && stage.CommandMode == StageAudienceCommandMode.StageOnly);
    }

    [Fact]
    public void Service_refreshes_generated_system_summary_when_live_production_changes()
    {
        Mock<ILiveProductionFacade> liveProduction = new();
        Mock<IOutputRoutingService> routing = new();
        routing.SetupGet(service => service.ActiveLookId).Returns("default");
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
        routing.SetupGet(service => service.Looks).Returns([new OutputLookDefinition { Id = "default", Name = "Default" }]);
        liveProduction.SetupGet(service => service.Current).Returns(LiveProductionSnapshot.Empty);

        LiveProductionQueryService service = new(liveProduction.Object, routing.Object);

        LiveProductionQuerySnapshot? raised = null;
        service.Changed += (_, args) => raised = args.Snapshot;

        LiveProductionSnapshot updated = new()
        {
            SessionState = new LiveRenderSessionState
            {
                GeneratedState = new GeneratedStateSnapshot
                {
                    StageMessageText = "Stand by",
                    Timers = new Dictionary<string, TimerSnapshot>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["service"] = new TimerSnapshot
                        {
                            Id = "service",
                            Name = "Service",
                            Kind = GeneratedTimerKind.Countdown,
                            Status = GeneratedTimerStatus.Running,
                            DisplayValue = "09:54",
                        },
                    },
                    Messages = new Dictionary<string, OverlayContentState>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["msg"] = new OverlayContentState
                        {
                            Id = "msg",
                            Name = "Welcome",
                            Kind = OverlayContentKind.Message,
                            IsVisible = true,
                            Text = "Welcome!",
                        },
                    },
                    CaptureSessions = new Dictionary<string, CaptureSessionState>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["stream"] = new CaptureSessionState
                        {
                            Metadata = new CaptureSessionMetadata
                            {
                                Id = "stream",
                                Name = "Main Stream",
                                SourceScreenId = OutputFeedIds.Main,
                                DestinationKind = CaptureDestinationKind.Rtmp,
                                Destination = "rtmp://example/live",
                            },
                            IsActive = true,
                            Health = CaptureSessionHealth.Healthy,
                        },
                    },
                    RecoveryDiagnostics = new OperatorRecoveryDiagnosticsState
                    {
                        AvailableClearGroupIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "overlays" },
                        StageLayoutsByScreenId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            [OutputFeedIds.Stage] = "confidence",
                        },
                    },
                },
            },
        };

        liveProduction.Raise(facade => facade.Changed += null, new LiveProductionChangedEventArgs { Snapshot = updated });

        raised.Should().NotBeNull();
        raised!.Generated.StageMessageText.Should().Be("Stand by");
        raised.Generated.Timers.Should().ContainSingle(timer => timer.Id == "service");
        raised.Generated.VisibleMessages.Should().ContainSingle(message => message.Id == "msg");
        raised.Generated.ActiveCaptureSessions.Should().ContainSingle(session => session.Metadata.Id == "stream");
        raised.Generated.ClearGroupIds.Should().Contain("overlays");
        raised.Generated.ClearGroups.Should().ContainSingle(group =>
            group.Id == "overlays"
            && group.Layers.Contains(BackendOutputLayerKind.Messages)
            && group.Layers.Contains(BackendOutputLayerKind.Props));
        raised.Generated.StageLayoutsByScreenId.Should().ContainKey(OutputFeedIds.Stage);
    }

    [Fact]
    public void BuildSnapshot_projects_selection_and_media_recovery_issues()
    {
        LiveProductionSnapshot snapshot = new()
        {
            PlaybackState = new PlaybackState
            {
                PresentationPath = @"C:\shows\service.cpres",
                CurrentSlideId = "live-slide",
                CurrentSlideInstanceKey = "live-instance",
                OperatorCursor = new SelectionCursor
                {
                    PresentationPath = @"C:\shows\service.cpres",
                    SlideId = "selected-slide",
                    InstanceKey = "selected-instance",
                },
                UserOverrideSelection = true,
            },
            SessionState = new LiveRenderSessionState
            {
                GeneratedState = new GeneratedStateSnapshot
                {
                    MissingMedia = new Dictionary<string, MissingMediaReferenceState>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["asset-missing"] = new MissingMediaReferenceState
                        {
                            AssetId = "asset-missing",
                            DisplayName = "Walk-in Loop",
                            LastKnownPath = @"C:\media\walk-in.mp4",
                            DiagnosticMessage = "Walk-in Loop is missing.",
                        },
                    },
                    MediaPlayerFailures = new Dictionary<string, MediaPlayerFailureState>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["media-player"] = new MediaPlayerFailureState
                        {
                            PlayerId = "media-player",
                            LayerKind = BackendOutputLayerKind.Media,
                            PayloadId = "asset-missing",
                            Message = "Media player failed to open the source.",
                        },
                    },
                },
            },
        };
        Mock<IOutputRoutingService> routing = new();
        routing.SetupGet(service => service.ActiveLookId).Returns("default");
        routing.SetupGet(service => service.ActiveLook).Returns(new OutputLookDefinition { Id = "default", Name = "Default" });
        routing.SetupGet(service => service.Looks).Returns([new OutputLookDefinition { Id = "default", Name = "Default" }]);

        LiveProductionQuerySnapshot query = BuildQuerySnapshot(snapshot, routing.Object);

        query.Selection.SelectedSlideId.Should().Be("selected-slide");
        query.Selection.LiveSlideId.Should().Be("live-slide");
        query.Selection.IsSelectionLive.Should().BeFalse();
        query.Selection.UserOverrideSelection.Should().BeTrue();
        query.Generated.MissingMedia.Should().ContainSingle(missing => missing.AssetId == "asset-missing");
        query.Generated.MediaPlayerFailures.Should().ContainSingle(failure => failure.PlayerId == "media-player");
        query.MediaIssues.Should().Contain(issue => issue.Kind == "missing-media" && issue.RecoveryActionType == "relink-media");
        query.MediaIssues.Should().Contain(issue => issue.Kind == "player-failure" && issue.RecoveryActionType == "reset-player");
    }

    [Fact]
    public void Service_refreshes_look_projection_when_output_routing_changes()
    {
        Mock<ILiveProductionFacade> liveProduction = new();
        Mock<IOutputRoutingService> routing = new();
        liveProduction.SetupGet(service => service.Current).Returns(LiveProductionSnapshot.Empty);
        routing.SetupGet(service => service.ActiveLookId).Returns("default");
        routing.SetupGet(service => service.ActiveLook).Returns(new OutputLookDefinition { Id = "default", Name = "Default" });
        routing.SetupGet(service => service.Looks).Returns([new OutputLookDefinition { Id = "default", Name = "Default" }]);

        LiveProductionQueryService service = new(liveProduction.Object, routing.Object);

        LiveProductionQuerySnapshot? raised = null;
        service.Changed += (_, args) => raised = args.Snapshot;

        routing.SetupGet(service => service.ActiveLookId).Returns("custom");
        routing.SetupGet(service => service.ActiveLook).Returns(new OutputLookDefinition { Id = "custom", Name = "Custom" });
        routing.SetupGet(service => service.Looks).Returns(
        [
            new OutputLookDefinition { Id = "default", Name = "Default" },
            new OutputLookDefinition { Id = "custom", Name = "Custom" },
        ]);
        routing.Raise(router => router.Changed += null, EventArgs.Empty);

        raised.Should().NotBeNull();
        raised!.ActiveLookId.Should().Be("custom");
        raised.Looks.Should().ContainSingle(look => look.Id == "custom" && look.IsActive);
    }

    private static LiveProductionQuerySnapshot BuildQuerySnapshot(
        LiveProductionSnapshot snapshot,
        IOutputRoutingService routing)
    {
        Mock<ILiveProductionFacade> liveProduction = new();
        liveProduction.SetupGet(service => service.Current).Returns(snapshot);

        return new LiveProductionQueryService(liveProduction.Object, routing).Current;
    }
}