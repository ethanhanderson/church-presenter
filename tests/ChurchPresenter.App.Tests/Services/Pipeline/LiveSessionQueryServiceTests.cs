using ChurchPresenter.App.Tests.TestSupport;
using ChurchPresenter.Backend.Output;
using ChurchPresenter.Backend.Pipeline.Cues;
using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Models.Media;
using ChurchPresenter.Models.Output;
using ChurchPresenter.Services.Pipeline;

using FluentAssertions;

using Moq;

using BackendOutputLayerKind = ChurchPresenter.Backend.Rendering.OutputLayerKind;

namespace ChurchPresenter.App.Tests.Services.Pipeline;

public sealed class LiveSessionQueryServiceTests
{
    [Fact]
    public async Task Current_projects_active_layers_from_pipeline_snapshot()
    {
        Mock<IOutputRoutingService> routing = PipelineTestRuntime.CreateDefaultRouting();
        routing.SetupGet(service => service.ActiveLookId).Returns("service");
        routing.SetupGet(service => service.Looks).Returns(
        [
            new OutputLookDefinition { Id = "service", Name = "Service" },
            new OutputLookDefinition { Id = "alt", Name = "Alt" },
        ]);

        Mock<ICuePreparationCoordinator> preparation = new();
        preparation
            .Setup(service => service.PrepareSlideAsync(It.IsAny<PreparedSlideCue>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PreparedCueRecord
            {
                Layer = BackendOutputLayerKind.Slide,
                ContentRef = new ContentRef { Kind = ContentRefKind.Slide, Id = "slide-1" },
                Payload = new RenderPayloadDescriptor
                {
                    Id = "slide-1",
                    Kind = RenderPayloadKind.Presentation,
                    DisplayName = "Slide 1",
                    Detail = new PresentationRenderPayload
                    {
                        PresentationPath = @"C:\shows\service.cpres",
                        SlideId = "slide-1",
                        ArrangementInstanceKey = "slide-1:repeat-2",
                    },
                },
            });

        CuePipelineRuntime runtime = PipelineTestRuntime.Create(routing: routing, preparation: preparation);
        await runtime.DispatchSlideCueAsync(new PreparedSlideCue
        {
            PresentationPath = @"C:\shows\service.cpres",
            SlideId = "slide-1",
            InstanceKey = "slide-1:repeat-2",
        });

        LiveSessionQueryService service = new(runtime, routing.Object);
        LiveSessionQuerySnapshot query = service.Current;

        query.ActiveLookId.Should().Be("service");
        query.LivePresentationPath.Should().Be(@"C:\shows\service.cpres");
        query.LiveSlideId.Should().Be("slide-1");
        query.ActiveLayers.Should().ContainSingle(layer =>
            layer.Kind == BackendOutputLayerKind.Slide
            && layer.IsLive
            && layer.PayloadId == "slide-1"
            && layer.PayloadPresentationPath == @"C:\shows\service.cpres"
            && layer.PayloadSlideId == "slide-1"
            && layer.PayloadInstanceKey == "slide-1:repeat-2");
        query.Looks.Should().ContainSingle(look => look.Id == "service" && look.IsActive);
    }

    [Fact]
    public void Current_projects_clear_groups_from_active_look()
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
                    Id = "clear-overlays",
                    Name = "Overlays",
                    Scopes = [OutputClearScope.Messages, OutputClearScope.Props],
                    Layers = OutputRoutingDefaults.CreateClearGroupLayers(OutputClearScope.Messages, OutputClearScope.Props),
                },
            ],
        });

        CuePipelineRuntime runtime = PipelineTestRuntime.Create(routing: routing);
        LiveSessionQueryService service = new(runtime, routing.Object);

        service.Current.ClearGroups.Should().ContainSingle(group =>
            group.Id == "clear-overlays"
            && group.Layers.Contains(BackendOutputLayerKind.Messages)
            && group.Layers.Contains(BackendOutputLayerKind.Props));
    }

    [Fact]
    public void Changed_refreshes_projection_when_routing_changes()
    {
        Mock<IOutputRoutingService> routing = PipelineTestRuntime.CreateDefaultRouting();
        CuePipelineRuntime runtime = PipelineTestRuntime.Create(routing: routing);
        LiveSessionQueryService service = new(runtime, routing.Object);

        LiveSessionQuerySnapshot? raised = null;
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
}
