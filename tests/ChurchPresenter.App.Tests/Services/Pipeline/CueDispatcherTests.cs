using ChurchPresenter.App.Tests.TestSupport;
using ChurchPresenter.Backend.Media;
using ChurchPresenter.Backend.Overlays;
using ChurchPresenter.Backend.Pipeline.Commands;
using ChurchPresenter.Backend.Pipeline.Cues;
using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Models.Media;
using ChurchPresenter.Services.Pipeline;

using FluentAssertions;

using Moq;

using BackendOutputLayerKind = ChurchPresenter.Backend.Rendering.OutputLayerKind;

namespace ChurchPresenter.App.Tests.Services.Pipeline;

public sealed class CueDispatcherTests
{
    [Fact]
    public async Task ClearLayersAsync_clears_layer_in_snapshot()
    {
        Mock<ICuePreparationCoordinator> preparation = new();
        preparation
            .Setup(service => service.PrepareSlideAsync(It.IsAny<PreparedSlideCue>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSlidePreparedCue("slide-1"));

        CuePipelineRuntime runtime = PipelineTestRuntime.Create(preparation: preparation);
        CueDispatcher dispatcher = new(runtime);

        PreparedSlideCue cue = new()
        {
            PresentationPath = @"C:\shows\service.cpres",
            SlideId = "slide-1",
            InstanceKey = "slide-1",
        };
        DispatchReceipt applied = await dispatcher.DispatchSlideCueAsync(cue);
        applied.Succeeded.Should().BeTrue();
        runtime.Snapshot.SessionState.Layers[BackendOutputLayerKind.Slide].Payload.Should().NotBeNull();

        DispatchReceipt cleared = await dispatcher.ClearLayersAsync([BackendOutputLayerKind.Slide]);
        cleared.Succeeded.Should().BeTrue();
        runtime.Snapshot.LayerSlots[BackendOutputLayerKind.Slide].ClearOverride.Should().BeTrue();
        runtime.Snapshot.SessionState.Layers[BackendOutputLayerKind.Slide].IsCleared.Should().BeTrue();
    }

    [Fact]
    public async Task EndLiveAsync_clears_multiple_layers()
    {
        CuePipelineRuntime runtime = PipelineTestRuntime.Create();
        CueDispatcher dispatcher = new(runtime);

        await dispatcher.SetOverlayAsync(new OverlayContentState
        {
            Id = "msg",
            Name = "Welcome",
            Kind = OverlayContentKind.Message,
            IsVisible = true,
            Text = "Welcome!",
        });

        DispatchReceipt receipt = await dispatcher.EndLiveAsync();
        receipt.Succeeded.Should().BeTrue();

        BackendOutputLayerKind[] clearedLayers =
        [
            BackendOutputLayerKind.Slide,
            BackendOutputLayerKind.Media,
            BackendOutputLayerKind.Audio,
            BackendOutputLayerKind.Announcements,
            BackendOutputLayerKind.Messages,
            BackendOutputLayerKind.Props,
            BackendOutputLayerKind.LiveVideo,
        ];

        foreach (BackendOutputLayerKind layer in clearedLayers)
            runtime.Snapshot.LayerSlots[layer].ClearOverride.Should().BeTrue();
    }

    [Fact]
    public async Task DispatchSlideCueAsync_returns_failure_when_preparation_fails()
    {
        Mock<ICuePreparationCoordinator> preparation = new();
        preparation
            .Setup(service => service.PrepareSlideAsync(It.IsAny<PreparedSlideCue>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PreparedCueRecord?)null);

        CuePipelineRuntime runtime = PipelineTestRuntime.Create(preparation: preparation);
        CueDispatcher dispatcher = new(runtime);

        DispatchReceipt receipt = await dispatcher.DispatchSlideCueAsync(new PreparedSlideCue
        {
            PresentationPath = @"C:\shows\service.cpres",
            SlideId = "slide-1",
        });

        receipt.Succeeded.Should().BeFalse();
        receipt.Error.Should().Contain("preparation failed");
    }

    [Fact]
    public async Task DispatchMediaCueAsync_audio_target_routes_to_audio_layer()
    {
        Mock<ICuePreparationCoordinator> preparation = new();
        preparation
            .Setup(service => service.PrepareMediaAsync(It.IsAny<PreparedMediaCue>(), It.IsAny<CancellationToken>()))
            .Returns<PreparedMediaCue, CancellationToken>((cue, _) =>
                Task.FromResult<PreparedCueRecord?>(new PreparedCueRecord
                {
                    Layer = cue.LayerKind,
                    ContentRef = new ContentRef { Kind = ContentRefKind.AudioCue, Id = cue.Media.MediaId },
                    Payload = new RenderPayloadDescriptor
                    {
                        Id = cue.Media.MediaId,
                        Kind = RenderPayloadKind.Audio,
                        DisplayName = cue.Media.DisplayName ?? string.Empty,
                        Detail = new MediaRenderPayload
                        {
                            Media = cue.Media,
                            Target = cue.Target,
                        },
                    },
                }));

        CuePipelineRuntime runtime = PipelineTestRuntime.Create(preparation: preparation);
        CueDispatcher dispatcher = new(runtime);

        DispatchReceipt receipt = await dispatcher.DispatchMediaCueAsync(
            new PreparedMediaCue
            {
                Media = new OutputLayerMedia
                {
                    MediaId = "walk-in-track",
                    DisplayName = "Walk In Track",
                    MediaType = "audio",
                },
            },
            MediaPlaybackLayerTargetNames.Audio);

        receipt.Succeeded.Should().BeTrue();
        runtime.Snapshot.SessionState.Layers[BackendOutputLayerKind.Audio].Payload.Should().NotBeNull();
        runtime.Snapshot.SessionState.Layers[BackendOutputLayerKind.Media].Payload.Should().BeNull();
        runtime.Snapshot.LayerSlots[BackendOutputLayerKind.Audio].PreparedCue!.Payload.DisplayName.Should().Be("Walk In Track");
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
                DisplayName = "Slide 1",
                Detail = new PresentationRenderPayload
                {
                    PresentationPath = @"C:\shows\service.cpres",
                    SlideId = slideId,
                },
            },
        };
}
