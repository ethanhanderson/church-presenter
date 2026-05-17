using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;

namespace ChurchPresenter.Hosting;

internal static class WinUiHostServiceRegistration
{
    internal static IServiceCollection AddWinUiHostServices(this IServiceCollection services)
    {
        services.AddSingleton(static _ =>
            DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("DispatcherQueue is required for WinUI host services."));
        services.AddSingleton<MediaPlaybackCoordinator>();
        services.AddSingleton<IMediaPlaybackCoordinator>(static sp => sp.GetRequiredService<MediaPlaybackCoordinator>());
        services.AddSingleton<IMediaPlayerRegistration>(static sp => sp.GetRequiredService<MediaPlaybackCoordinator>());
        services.AddSingleton<IMonitorService, MonitorService>();
        services.AddSingleton<ILocalDisplayCatalogService, LocalDisplayCatalogService>();
        services.AddSingleton<IMonitorIdentifyService, MonitorIdentifyService>();
        services.AddSingleton<IBundleAssetCacheService, BundleAssetCacheService>();
        services.AddSingleton<IMediaPrewarmService, MediaPrewarmService>();
        services.AddSingleton<MediaCachePrimerService>();
        services.AddSingleton<IOutputWindowService, OutputWindowService>();
        services.AddSingleton<IAudienceWindowService, AudienceWindowService>();
        services.AddSingleton<IStageWindowService, StageWindowService>();
        services.AddTransient<MainWindow>();

        return services;
    }
}
