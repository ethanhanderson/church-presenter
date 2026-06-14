#nullable enable



using ChurchPresenter.Adapters.Rendering.Pipeline;

using ChurchPresenter.Backend.Commands;

using ChurchPresenter.Backend.Pipeline.Commands;

using ChurchPresenter.Backend.Pipeline.Cues;

using ChurchPresenter.Backend.Pipeline.Diagnostics;

using ChurchPresenter.Backend.Pipeline.Rendering;

using ChurchPresenter.Backend.Pipeline.Routing;

using ChurchPresenter.Backend.Rendering;

using ChurchPresenter.Services.Pipeline;

using ChurchPresenter.Services.Runtime;




using Microsoft.Extensions.DependencyInjection;



namespace ChurchPresenter.Hosting;



internal static class PipelineServiceRegistration

{

    internal static IServiceCollection AddPipelineServices(this IServiceCollection services)

    {

        services.AddSingleton<IPreparedCueCache, PreparedCueCache>();

        services.AddSingleton<ILiveInputCatalog, LiveInputCatalog>();

        services.AddSingleton<IMaskAssetCatalog, MaskAssetCatalog>();

        services.AddSingleton<ICuePreparationCoordinator, CuePreparationCoordinator>();

        services.AddSingleton<ILayerRouter, LayerRouter>();

        services.AddSingleton<FrameComposer>();

        services.AddSingleton<IPipelineDiagnostics, PipelineDiagnostics>();

        services.AddSingleton<ILiveCommandBus, LiveCommandBus>();



        services.AddSingleton<CuePipelineRuntime>(static sp => new CuePipelineRuntime(

            sp.GetRequiredService<ILiveCommandExecutor>(),

            sp.GetRequiredService<ILayerRouter>(),

            sp.GetRequiredService<FrameComposer>(),

            sp.GetRequiredService<ICuePreparationCoordinator>(),

            sp.GetRequiredService<IOutputRoutingService>(),

            sp.GetRequiredService<IOutputTopologyService>(),

            sp.GetRequiredService<IStageLayoutRegistryService>(),

            sp.GetRequiredService<IPipelineDiagnostics>(),

            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CuePipelineRuntime>>(),

            audienceAdapters: [sp.GetRequiredService<ProgramRenderService>()],

            stageAdapters: [sp.GetRequiredService<ProgramRenderService>()]));



        services.AddSingleton<ICueDispatcher, CueDispatcher>();

        services.AddSingleton<ILiveSessionQueryService, LiveSessionQueryService>();

        services.AddSingleton<IMediaTransportService, MediaTransportService>();

        services.AddSingleton<IOperatorSelectionService, OperatorSelectionService>();

        services.AddSingleton<IOutputTargetService, OutputTargetService>();

        services.AddSingleton<LayerInvalidationCoordinator>();

        services.AddSingleton<IRapidSeekCoordinator, RapidSeekCoordinator>();



        services.AddSingleton<IRenderFrameStore, InMemoryRenderFrameStore>();

        services.AddSingleton<IRenderFrameResolver, BackendRenderFrameResolver>();

        services.AddSingleton<ISlideSceneCompiler, SlideSceneCompiler>();

        services.AddSingleton<IBackendRenderEngine, BackendRenderEngine>();

        services.AddSingleton<ILiveCommandExecutor, LiveCommandExecutor>();

        services.AddSingleton<IOutputTopologyService, OutputTopologyService>();

        services.AddSingleton<IOutputRoutingService, OutputRoutingService>();

        services.AddSingleton<IShowTimerService, ShowTimerService>();

        services.AddSingleton<ISlideActionExecutionService, SlideActionExecutionService>();

        services.AddSingleton<IShowContentBrowseService, ShowContentBrowseService>();

        services.AddSingleton<IShowMediaDrawerService, ShowMediaDrawerService>();

        services.AddSingleton<IShowControlsService, ShowControlsService>();

        services.AddSingleton<ILiveDiagnosticsRecoveryQueryService, LiveDiagnosticsRecoveryQueryService>();

        services.AddSingleton<ILiveMediaQueryService, LiveMediaQueryService>();

        services.AddSingleton<IOutputHostFeedbackQueryService, OutputHostFeedbackQueryService>();



        return services;

    }

}


