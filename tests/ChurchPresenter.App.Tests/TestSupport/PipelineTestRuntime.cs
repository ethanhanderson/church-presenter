using ChurchPresenter.Backend.Commands;
using ChurchPresenter.Backend.Output;
using ChurchPresenter.Backend.Pipeline.Cues;
using ChurchPresenter.Backend.Pipeline.Diagnostics;
using ChurchPresenter.Backend.Pipeline.HostAdapter;
using ChurchPresenter.Backend.Pipeline.Rendering;
using ChurchPresenter.Backend.Pipeline.Routing;
using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Backend.Stage;
using ChurchPresenter.Models.Output;
using ChurchPresenter.Services.Output;
using ChurchPresenter.Services.Pipeline;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace ChurchPresenter.App.Tests.TestSupport;

internal static class PipelineTestRuntime
{
    internal static CuePipelineRuntime Create(
        Mock<IOutputRoutingService>? routing = null,
        Mock<ICuePreparationCoordinator>? preparation = null,
        Mock<IOutputTopologyService>? topology = null,
        IEnumerable<IAudienceHostAdapter>? audienceAdapters = null)
    {
        BackendRenderFrameResolver frameResolver = new();
        BackendRenderEngine renderEngine = new(frameResolver);
        LiveCommandExecutor commandExecutor = new(renderEngine);
        routing ??= CreateDefaultRouting();
        topology ??= CreateDefaultTopology();

        Mock<IStageLayoutRegistryService> stageLayouts = new();
        stageLayouts.Setup(service => service.GetLayouts()).Returns(new Dictionary<string, StageLayout>());
        stageLayouts.Setup(service => service.GetDefaultLayoutIdsByScreenId()).Returns(new Dictionary<string, string>());

        LayerRouter layerRouter = new(renderEngine, commandExecutor, routing.Object, new ShowTransitionDefaults());
        FrameComposer frameComposer = new(frameResolver);
        preparation ??= new Mock<ICuePreparationCoordinator>();

        return new CuePipelineRuntime(
            commandExecutor,
            layerRouter,
            frameComposer,
            preparation.Object,
            routing.Object,
            topology.Object,
            stageLayouts.Object,
            new PipelineDiagnostics(),
            NullLogger<CuePipelineRuntime>.Instance,
            audienceAdapters: audienceAdapters);
    }

    internal static Mock<IOutputRoutingService> CreateDefaultRouting()
    {
        Mock<IOutputRoutingService> routing = new();
        routing.SetupGet(service => service.ActiveLookId).Returns("default");
        routing.SetupGet(service => service.ActiveLook).Returns(new OutputLookDefinition
        {
            Id = "default",
            Name = "Default",
        });
        routing.SetupGet(service => service.Looks).Returns(
        [
            new OutputLookDefinition { Id = "default", Name = "Default" },
        ]);
        routing.SetupGet(service => service.Feeds).Returns(Array.Empty<OutputFeedDefinition>());
        return routing;
    }

    internal static Mock<IOutputTopologyService> CreateDefaultTopology()
    {
        Mock<IOutputTopologyService> topology = new();
        topology.Setup(service => service.GetSnapshot()).Returns(new OutputTopologySnapshot());
        return topology;
    }
}
