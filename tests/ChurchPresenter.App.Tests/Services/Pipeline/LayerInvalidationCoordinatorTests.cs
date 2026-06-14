using ChurchPresenter.App.Tests.Reference;
using ChurchPresenter.App.Tests.TestSupport;
using ChurchPresenter.Backend.Pipeline.Cues;
using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Models.Content;
using ChurchPresenter.Models.Media;
using ChurchPresenter.Services.Content;
using ChurchPresenter.Services.Pipeline;

using FluentAssertions;

using Moq;

using BackendOutputLayerKind = ChurchPresenter.Backend.Rendering.OutputLayerKind;

namespace ChurchPresenter.App.Tests.Services.Pipeline;

[ReferenceArea(ReferenceAreas.CacheAndRecovery)]
public sealed class LayerInvalidationCoordinatorTests
{
    [Fact]
    public async Task HandleContentChanged_replaces_live_slide_payload_when_presentation_changes()
    {
        const string presentationPath = @"C:\shows\service.cpres";
        Mock<ICuePreparationCoordinator> preparation = new();
        PreparedCueRecord initialCue = CreateSlidePreparedCue("slide-1", presentationPath, "ThemeA");
        PreparedCueRecord refreshedCue = CreateSlidePreparedCue("slide-1-refreshed", presentationPath, "ThemeA");

        preparation
            .Setup(service => service.PrepareSlideAsync(It.IsAny<PreparedSlideCue>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(initialCue);
        preparation
            .Setup(service => service.PrepareAsync(It.IsAny<CueRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(refreshedCue);

        CuePipelineRuntime runtime = PipelineTestRuntime.Create(preparation: preparation);
        await runtime.DispatchSlideCueAsync(new PreparedSlideCue
        {
            PresentationPath = presentationPath,
            SlideId = "slide-1",
        });

        LayerInvalidationCoordinator coordinator = new(preparation.Object, runtime);
        coordinator.HandleContentChanged(new ContentChangeEvent
        {
            Kind = ContentChangeKind.PresentationUpdated,
            SubjectId = presentationPath,
        });

        await WaitUntilAsync(() =>
            runtime.Snapshot.LayerSlots[BackendOutputLayerKind.Slide].PreparedCue?.Payload.Id == "slide-1-refreshed");

        runtime.Snapshot.LayerSlots[BackendOutputLayerKind.Slide].PreparedCue.Should().Be(refreshedCue);
        runtime.Snapshot.SessionState.Layers[BackendOutputLayerKind.Slide].Payload!.Id.Should().Be("slide-1-refreshed");
    }

    [Fact]
    public async Task HandleContentChanged_replaces_live_slide_payload_when_theme_changes()
    {
        const string presentationPath = @"C:\shows\service.cpres";
        Mock<ICuePreparationCoordinator> preparation = new();
        PreparedCueRecord initialCue = CreateSlidePreparedCue("slide-1", presentationPath, "ThemeA");
        PreparedCueRecord refreshedCue = CreateSlidePreparedCue("slide-1-themed", presentationPath, "ThemeA");

        preparation
            .Setup(service => service.PrepareSlideAsync(It.IsAny<PreparedSlideCue>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(initialCue);
        preparation
            .Setup(service => service.PrepareAsync(It.IsAny<CueRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(refreshedCue);

        CuePipelineRuntime runtime = PipelineTestRuntime.Create(preparation: preparation);
        await runtime.DispatchSlideCueAsync(new PreparedSlideCue
        {
            PresentationPath = presentationPath,
            SlideId = "slide-1",
        });

        LayerInvalidationCoordinator coordinator = new(preparation.Object, runtime);
        coordinator.HandleContentChanged(new ContentChangeEvent
        {
            Kind = ContentChangeKind.ThemeChanged,
            SubjectId = "ThemeA",
        });

        await WaitUntilAsync(() =>
            runtime.Snapshot.LayerSlots[BackendOutputLayerKind.Slide].PreparedCue?.Payload.Id == "slide-1-themed");

        runtime.Snapshot.LayerSlots[BackendOutputLayerKind.Slide].PreparedCue.Should().Be(refreshedCue);
    }

    private static PreparedCueRecord CreateSlidePreparedCue(
        string slideId,
        string presentationPath,
        string themeVersion) =>
        new()
        {
            Layer = BackendOutputLayerKind.Slide,
            ContentRef = new ContentRef
            {
                Kind = ContentRefKind.Slide,
                Id = slideId,
                Version = presentationPath,
            },
            ResourceStamp = new ResourceStamp
            {
                ContentVersion = presentationPath,
                ThemeVersion = themeVersion,
                ResourceHash = slideId,
            },
            Payload = new RenderPayloadDescriptor
            {
                Id = slideId,
                Kind = RenderPayloadKind.Presentation,
                DisplayName = slideId,
                Detail = new PresentationRenderPayload
                {
                    PresentationPath = presentationPath,
                    SlideId = slideId,
                    ThemeId = themeVersion,
                },
            },
        };

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(2));
        while (!predicate())
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Delay(25, timeout.Token);
        }
    }
}
