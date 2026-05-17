using ChurchPresenter.Backend.Commands;
using ChurchPresenter.Backend.Rendering;

using Microsoft.Extensions.DependencyInjection;

namespace ChurchPresenter.Hosting;

internal static class LiveProductionServiceRegistration
{
    internal static IServiceCollection AddLiveProductionServices(this IServiceCollection services)
    {
        services.AddSingleton<PlaybackEngine>();
        services.AddSingleton<IPlaybackEngine>(static sp => sp.GetRequiredService<PlaybackEngine>());
        services.AddSingleton<ILiveSessionService>(static sp => sp.GetRequiredService<PlaybackEngine>());
        services.AddSingleton<IRenderFrameStore, InMemoryRenderFrameStore>();
        services.AddSingleton<IRenderFrameResolver, BackendRenderFrameResolver>();
        services.AddSingleton<ISlideSceneCompiler, SlideSceneCompiler>();
        services.AddSingleton<IBackendRenderEngine, BackendRenderEngine>();
        services.AddSingleton<ILiveCommandExecutor, LiveCommandExecutor>();
        services.AddSingleton<IOutputTopologyService, OutputTopologyService>();
        services.AddSingleton<IOutputRoutingService, OutputRoutingService>();
        services.AddSingleton<ILiveProductionFacade, LiveProductionFacade>();
        services.AddSingleton<ILiveProductionQueryService, LiveProductionQueryService>();
        services.AddSingleton<ILiveDiagnosticsRecoveryQueryService, LiveDiagnosticsRecoveryQueryService>();
        services.AddSingleton<ILiveMediaQueryService, LiveMediaQueryService>();
        services.AddSingleton<IOutputHostFeedbackQueryService, OutputHostFeedbackQueryService>();
        services.AddSingleton<ProgramOutputFrameFacade>();
        services.AddSingleton<IOutputFrameFacade>(static sp => sp.GetRequiredService<ProgramOutputFrameFacade>());
        services.AddSingleton<AudienceOutputFrameFacade>();
        services.AddSingleton<StageOutputFrameFacade>();
        services.AddSingleton<IShowTimerService, ShowTimerService>();
        services.AddSingleton<ISlideActionExecutionService, SlideActionExecutionService>();
        services.AddSingleton<IShowContentBrowseService, ShowContentBrowseService>();
        services.AddSingleton<IShowMediaDrawerService, ShowMediaDrawerService>();
        services.AddSingleton<IShowControlsService, ShowControlsService>();

        return services;
    }
}
