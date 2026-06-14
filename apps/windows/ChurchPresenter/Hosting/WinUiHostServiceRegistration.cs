using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;

using ChurchPresenter.Services.Output;

namespace ChurchPresenter.Hosting;

internal static class WinUiHostServiceRegistration
{
    internal static IServiceCollection AddWinUiHostServices(this IServiceCollection services)
    {
        services.AddSingleton(static _ =>
            DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("DispatcherQueue is required for WinUI host services."));
        services.AddSingleton<IMonitorService, MonitorService>();
        services.AddSingleton<ILocalDisplayCatalogService, LocalDisplayCatalogService>();
        services.AddSingleton<IMonitorIdentifyService, MonitorIdentifyService>();
        services.AddSingleton<IBundleAssetCacheService, BundleAssetCacheService>();
        services.AddSingleton<IMediaThumbnailCache, MediaThumbnailCache>();
        services.AddSingleton<ISlideDeckThumbnailCache, SlideDeckThumbnailCache>();
        services.AddSingleton<IShowRenderSession, ShowRenderSession>();
        services.AddSingleton<IShowDeckThumbnailService, ShowDeckThumbnailService>();
        services.AddSingleton<IMediaStartupWarmupService, MediaStartupWarmupService>();
        services.AddSingleton<IOutputWindowService, OutputWindowService>();
        services.AddSingleton<IAudienceWindowService, AudienceWindowService>();
        services.AddSingleton<IStageWindowService, StageWindowService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IShellNavigationCoordinator, ShellNavigationCoordinator>();
        services.AddTransient<MainWindow>();

        return services;
    }
}
