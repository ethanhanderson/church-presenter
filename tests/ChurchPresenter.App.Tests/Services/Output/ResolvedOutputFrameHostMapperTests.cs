using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Backend.Stage;

using FluentAssertions;

using BackendOutputLayerKind = ChurchPresenter.Backend.Rendering.OutputLayerKind;

namespace ChurchPresenter.App.Tests.Services.Output;

public sealed class ResolvedOutputFrameHostMapperTests
{
    [Fact]
    public void MapAudience_UsesResolvedVisibleLayersWithoutRecomputingRouting()
    {
        var frame = new AudienceRenderFrame
        {
            Sequence = 42,
            ScreenId = "audience",
            LookPresetId = "custom",
            RenderSize = new PixelSize(1280, 720),
            Diagnostics = new RenderDiagnostics { Message = "2 endpoints connected." },
            Layers =
            [
                new RenderLayerDescriptor
                {
                    Kind = BackendOutputLayerKind.Slide,
                    IsVisible = true,
                    Payload = new RenderPayloadDescriptor
                    {
                        Id = "slide-1",
                        Kind = RenderPayloadKind.Presentation,
                        DisplayName = "Amazing grace",
                    },
                },
                new RenderLayerDescriptor
                {
                    Kind = BackendOutputLayerKind.Media,
                    IsVisible = true,
                    IsSuppressed = true,
                    Payload = new RenderPayloadDescriptor
                    {
                        Id = "media-1",
                        Kind = RenderPayloadKind.Video,
                        DisplayName = "Loop",
                    },
                },
            ],
        };

        ResolvedOutputFrameHostSnapshot snapshot = ResolvedOutputFrameHostMapper.Map(frame);

        snapshot.Kind.Should().Be(ResolvedOutputFrameHostKind.Audience);
        snapshot.Sequence.Should().Be(42);
        snapshot.RenderSize.Should().Be(new PixelSize(1280, 720));
        snapshot.Subtitle.Should().Be("Look: custom");
        snapshot.DiagnosticsMessage.Should().Be("2 endpoints connected.");
        snapshot.VisibleItems.Should().ContainSingle(item => item.Id == "slide-1" && item.IsPrimary);
        snapshot.InactiveItems.Should().ContainSingle(item => item.Id == "media-1" && item.IsSuppressed);
    }

    [Fact]
    public void MapStage_UsesStagePayloadsAndLayoutMetadata()
    {
        var frame = new StageRenderFrame
        {
            Sequence = 7,
            ScreenId = "stage",
            StageLayoutId = "confidence",
            CommandMode = StageAudienceCommandMode.StageOnly,
            Payloads =
            [
                new RenderPayloadDescriptor
                {
                    Id = "current",
                    Kind = RenderPayloadKind.Presentation,
                    DisplayName = "Verse 1",
                    SourceReference = "CurrentSlideText",
                },
                new RenderPayloadDescriptor
                {
                    Id = "timer",
                    Kind = RenderPayloadKind.Overlay,
                    DisplayName = "Countdown",
                },
            ],
        };

        ResolvedOutputFrameHostSnapshot snapshot = ResolvedOutputFrameHostMapper.Map(frame);

        snapshot.Kind.Should().Be(ResolvedOutputFrameHostKind.Stage);
        snapshot.ScreenId.Should().Be("stage");
        snapshot.Subtitle.Should().Be("Layout: confidence - Mode: Stage only");
        snapshot.VisibleItems.Should().HaveCount(2);
        snapshot.VisibleItems[0].Should().Match<ResolvedOutputFrameHostItem>(item =>
            item.Id == "current" && item.Label == "CurrentSlideText" && item.IsPrimary);
        snapshot.VisibleItems[1].Label.Should().Be(RenderPayloadKind.Overlay.ToString());
        snapshot.InactiveItems.Should().BeEmpty();
    }
}