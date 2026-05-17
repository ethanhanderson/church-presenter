using ChurchPresenter.Backend.Output;
using ChurchPresenter.Backend.Rendering;

using FluentAssertions;

using BackendOutputLayerKind = ChurchPresenter.Backend.Rendering.OutputLayerKind;

namespace ChurchPresenter.App.Tests.Services.Output;

/// <summary>
/// Locks the UI-facing output-frame adapter to the current output surface contract while the backend cutover progresses.
/// </summary>
public sealed class OutputFrameSnapshotAdapterTests
{
    [Fact]
    public void Adapt_routes_media_off_without_losing_media_scene()
    {
        PresentationSlide slide = new()
        {
            Id = "s1",
            Background = new SolidSlideBackground { Color = "#000000" },
            MediaCues =
            [
                new SlideMediaCue
                {
                    Id = "walkin",
                    MediaId = @"C:\media\walkin.mp4",
                    MediaType = "video",
                    DisplayName = "Walk In",
                    Target = "mediaUnderlay",
                },
            ],
        };
        PresentationProject project = new()
        {
            Manifest =
            {
                AspectRatio = "4:3",
            },
        };
        project.Slides.Add(slide);

        LiveProductionSnapshot liveProduction = new()
        {
            PlaybackState = new PlaybackState
            {
                IsLive = true,
                Presentation = new PresentationDocument
                {
                    Manifest = new PresentationManifestDto { Title = "Sunday Set" },
                    Project = project,
                },
                CurrentSlideId = "s1",
            },
            SessionState = new LiveRenderSessionState
            {
                ActiveLook = new LookPreset
                {
                    Id = "custom",
                    Name = "Custom",
                    ScreenRoutes =
                    [
                        new ScreenLayerRouting
                        {
                            ScreenId = OutputFeedIds.Stage,
                            Layers =
                            [
                                new LayerRoute { LayerKind = BackendOutputLayerKind.Slide, IsEnabled = true },
                                new LayerRoute { LayerKind = BackendOutputLayerKind.Media, IsEnabled = false },
                                new LayerRoute { LayerKind = BackendOutputLayerKind.Messages, IsEnabled = false },
                            ],
                        },
                    ],
                },
            },
            Frames = new RenderFrameSet
            {
                AudienceFrames = new Dictionary<string, AudienceRenderFrame>(StringComparer.OrdinalIgnoreCase)
                {
                    [OutputFeedIds.Stage] = new AudienceRenderFrame
                    {
                        ScreenId = OutputFeedIds.Stage,
                        Layers =
                        [
                            new RenderLayerDescriptor
                            {
                                Kind = BackendOutputLayerKind.Slide,
                                Payload = new RenderPayloadDescriptor
                                {
                                    Id = "s1",
                                    Kind = RenderPayloadKind.Presentation,
                                    DisplayName = "s1",
                                },
                                IsVisible = true,
                            },
                        ],
                    },
                },
            },
            Topology = new OutputTopologySnapshot
            {
                ScreenDiagnostics = new Dictionary<string, OutputScreenDiagnostics>(StringComparer.OrdinalIgnoreCase)
                {
                    [OutputFeedIds.Stage] = new OutputScreenDiagnostics
                    {
                        ScreenId = OutputFeedIds.Stage,
                        ScreenName = "Stage",
                        Health = EndpointHealth.Connected,
                        Message = "Stage connected.",
                    },
                },
            },
        };

        OutputFrameSnapshot adapted = OutputFrameSnapshotAdapter.Adapt(liveProduction, OutputFeedIds.Stage);

        adapted.ProgramTitle.Should().Be("Sunday Set");
        adapted.RoutesPresentation.Should().BeTrue();
        adapted.RoutesMedia.Should().BeFalse();
        adapted.LayerRoutes.Should().Contain(route => route.Kind == BackendOutputLayerKind.Slide && route.IsEnabled);
        adapted.LayerRoutes.Should().Contain(route => route.Kind == BackendOutputLayerKind.Media && !route.IsEnabled);
        adapted.LayerRoutes.Should().Contain(route => route.Kind == BackendOutputLayerKind.Messages && !route.IsEnabled);
        adapted.Frame.SuppressPresentation.Should().BeFalse();
        adapted.Frame.SuppressMedia.Should().BeTrue();
        adapted.Frame.MediaLayers.MediaUnderlay.Should().NotBeNull();
        adapted.Scene.Media.Underlay.Media.Should().NotBeNull();
        adapted.Scene.Media.Underlay.Media!.MediaId.Should().Be(@"C:\media\walkin.mp4");
        adapted.Frame.OutputAspectRatioOverride.Should().Be("4:3");
        adapted.ScreenDiagnostics.Should().NotBeNull();
        adapted.ScreenDiagnostics!.Message.Should().Be("Stage connected.");
    }

    [Fact]
    public void Adapt_program_surface_ignores_feed_routes_and_preserves_clear_state()
    {
        PresentationSlide slide = new()
        {
            Id = "s1",
            Background = new SolidSlideBackground { Color = "#000000" },
        };
        PresentationProject project = new();
        project.Slides.Add(slide);

        LiveProductionSnapshot liveProduction = new()
        {
            PlaybackState = new PlaybackState
            {
                IsLive = true,
                Presentation = new PresentationDocument
                {
                    Manifest = new PresentationManifestDto { Title = "Prayer Loop" },
                    Project = project,
                },
                CurrentSlideId = "s1",
                IsClear = true,
                IsBlackout = false,
            },
            SessionState = new LiveRenderSessionState
            {
                ActiveLook = new LookPreset
                {
                    Id = "custom",
                    Name = "Custom",
                    ScreenRoutes =
                    [
                        new ScreenLayerRouting
                        {
                            ScreenId = OutputFeedIds.Audience,
                            Layers =
                            [
                                new LayerRoute { LayerKind = BackendOutputLayerKind.Slide, IsEnabled = false },
                                new LayerRoute { LayerKind = BackendOutputLayerKind.Media, IsEnabled = false },
                            ],
                        },
                    ],
                },
            },
        };

        OutputFrameSnapshot adapted = OutputFrameSnapshotAdapter.Adapt(liveProduction, screenId: null);

        adapted.ProgramTitle.Should().Be("Prayer Loop");
        adapted.RoutesPresentation.Should().BeTrue();
        adapted.RoutesMedia.Should().BeTrue();
        adapted.Frame.SuppressPresentation.Should().BeFalse();
        adapted.Frame.SuppressMedia.Should().BeFalse();
        adapted.Frame.IsClear.Should().BeTrue();
        adapted.Scene.IsClear.Should().BeTrue();
        adapted.Scene.Presentation.Slide.Should().BeSameAs(slide);
        adapted.ScreenDiagnostics.Should().BeNull();
    }

    [Fact]
    public void Adapt_clear_slide_suppresses_only_slide_layer()
    {
        PresentationSlide slide = new() { Id = "s1" };
        PresentationProject project = new();
        project.Slides.Add(slide);
        LiveProductionSnapshot liveProduction = CreateSnapshot(project, "s1") with
        {
            PlaybackState = CreatePlaybackState(project, "s1") with
            {
                MediaLayers = CreateMediaLayers("walkin.mp4"),
            },
            SessionState = new LiveRenderSessionState
            {
                Layers = new Dictionary<BackendOutputLayerKind, LayerState>
                {
                    [BackendOutputLayerKind.Slide] = new LayerState
                    {
                        Kind = BackendOutputLayerKind.Slide,
                        IsCleared = true,
                    },
                    [BackendOutputLayerKind.Media] = CreateVisibleLayer(BackendOutputLayerKind.Media, "walkin.mp4", RenderPayloadKind.Video),
                },
            },
            Frames = new RenderFrameSet
            {
                AudienceFrames = new Dictionary<string, AudienceRenderFrame>(StringComparer.OrdinalIgnoreCase)
                {
                    [OutputFeedIds.Audience] = new AudienceRenderFrame
                    {
                        ScreenId = OutputFeedIds.Audience,
                        Layers =
                        [
                            CreateVisibleDescriptor(BackendOutputLayerKind.Media, "walkin.mp4", RenderPayloadKind.Video),
                        ],
                    },
                },
            },
        };

        OutputFrameSnapshot adapted = OutputFrameSnapshotAdapter.Adapt(liveProduction, OutputFeedIds.Audience);

        adapted.Frame.SuppressPresentation.Should().BeTrue();
        adapted.Frame.SuppressMedia.Should().BeFalse();
        adapted.Scene.Presentation.Suppressed.Should().BeTrue();
        adapted.Scene.Media.Suppressed.Should().BeFalse();
        adapted.Scene.Media.Underlay.Media.Should().NotBeNull();
    }

    [Fact]
    public void Adapt_clear_media_suppresses_only_media_layer()
    {
        PresentationSlide slide = new() { Id = "s1" };
        PresentationProject project = new();
        project.Slides.Add(slide);
        LiveProductionSnapshot liveProduction = CreateSnapshot(project, "s1") with
        {
            PlaybackState = CreatePlaybackState(project, "s1") with
            {
                MediaLayers = CreateMediaLayers("walkin.mp4"),
            },
            SessionState = new LiveRenderSessionState
            {
                Layers = new Dictionary<BackendOutputLayerKind, LayerState>
                {
                    [BackendOutputLayerKind.Slide] = CreateVisibleLayer(BackendOutputLayerKind.Slide, "s1", RenderPayloadKind.Presentation),
                    [BackendOutputLayerKind.Media] = new LayerState
                    {
                        Kind = BackendOutputLayerKind.Media,
                        IsCleared = true,
                    },
                },
            },
            Frames = new RenderFrameSet
            {
                AudienceFrames = new Dictionary<string, AudienceRenderFrame>(StringComparer.OrdinalIgnoreCase)
                {
                    [OutputFeedIds.Audience] = new AudienceRenderFrame
                    {
                        ScreenId = OutputFeedIds.Audience,
                        Layers =
                        [
                            CreateVisibleDescriptor(BackendOutputLayerKind.Slide, "s1", RenderPayloadKind.Presentation),
                        ],
                    },
                },
            },
        };

        OutputFrameSnapshot adapted = OutputFrameSnapshotAdapter.Adapt(liveProduction, OutputFeedIds.Audience);

        adapted.Frame.SuppressPresentation.Should().BeFalse();
        adapted.Frame.SuppressMedia.Should().BeTrue();
        adapted.Scene.Presentation.Suppressed.Should().BeFalse();
        adapted.Scene.Presentation.Slide.Should().BeSameAs(slide);
        adapted.Scene.Media.Suppressed.Should().BeTrue();
        adapted.Scene.Media.Underlay.Media.Should().NotBeNull();
    }

    [Fact]
    public void Adapt_default_look_routes_slide_and_media_when_route_is_not_explicit()
    {
        PresentationProject project = new();
        project.Slides.Add(new PresentationSlide { Id = "s1" });
        LiveProductionSnapshot liveProduction = CreateSnapshot(project, "s1") with
        {
            PlaybackState = CreatePlaybackState(project, "s1") with
            {
                MediaLayers = CreateMediaLayers("walkin.mp4"),
            },
            SessionState = new LiveRenderSessionState
            {
                ActiveLook = new LookPreset { Id = "default", Name = "Default" },
                Layers = new Dictionary<BackendOutputLayerKind, LayerState>
                {
                    [BackendOutputLayerKind.Slide] = CreateVisibleLayer(BackendOutputLayerKind.Slide, "s1", RenderPayloadKind.Presentation),
                    [BackendOutputLayerKind.Media] = CreateVisibleLayer(BackendOutputLayerKind.Media, "walkin.mp4", RenderPayloadKind.Video),
                },
            },
            Frames = new RenderFrameSet
            {
                AudienceFrames = new Dictionary<string, AudienceRenderFrame>(StringComparer.OrdinalIgnoreCase)
                {
                    [OutputFeedIds.Stream] = new AudienceRenderFrame
                    {
                        ScreenId = OutputFeedIds.Stream,
                        Layers =
                        [
                            CreateVisibleDescriptor(BackendOutputLayerKind.Slide, "s1", RenderPayloadKind.Presentation),
                            CreateVisibleDescriptor(BackendOutputLayerKind.Media, "walkin.mp4", RenderPayloadKind.Video),
                        ],
                    },
                },
            },
        };

        OutputFrameSnapshot adapted = OutputFrameSnapshotAdapter.Adapt(liveProduction, OutputFeedIds.Stream);

        adapted.RoutesPresentation.Should().BeTrue();
        adapted.RoutesMedia.Should().BeTrue();
        adapted.Frame.SuppressPresentation.Should().BeFalse();
        adapted.Frame.SuppressMedia.Should().BeFalse();
    }

    private static LiveProductionSnapshot CreateSnapshot(PresentationProject project, string slideId)
    {
        return new LiveProductionSnapshot
        {
            PlaybackState = CreatePlaybackState(project, slideId),
            SessionState = new LiveRenderSessionState(),
        };
    }

    private static PlaybackState CreatePlaybackState(PresentationProject project, string slideId)
    {
        return new PlaybackState
        {
            IsLive = true,
            Presentation = new PresentationDocument { Project = project },
            CurrentSlideId = slideId,
        };
    }

    private static MediaLayersState CreateMediaLayers(string mediaId)
    {
        return new MediaLayersState
        {
            MediaUnderlay = new OutputLayerMedia
            {
                MediaId = mediaId,
                MediaType = "video",
                DisplayName = mediaId,
            },
        };
    }

    private static LayerState CreateVisibleLayer(
        BackendOutputLayerKind layerKind,
        string id,
        RenderPayloadKind payloadKind)
    {
        return new LayerState
        {
            Kind = layerKind,
            Payload = CreatePayload(id, payloadKind),
            IsVisible = true,
        };
    }

    private static RenderLayerDescriptor CreateVisibleDescriptor(
        BackendOutputLayerKind layerKind,
        string id,
        RenderPayloadKind payloadKind)
    {
        return new RenderLayerDescriptor
        {
            Kind = layerKind,
            Payload = CreatePayload(id, payloadKind),
            IsVisible = true,
        };
    }

    private static RenderPayloadDescriptor CreatePayload(string id, RenderPayloadKind payloadKind)
    {
        return new RenderPayloadDescriptor
        {
            Id = id,
            Kind = payloadKind,
            DisplayName = id,
            SourceReference = id,
        };
    }
}