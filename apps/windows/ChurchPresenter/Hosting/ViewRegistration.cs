using Microsoft.Extensions.DependencyInjection;

namespace ChurchPresenter.Hosting;

internal static class ViewRegistration
{
    internal static IServiceCollection AddAppViewModels(this IServiceCollection services)
    {
        services.AddSingleton<OutputViewModel>();
        services.AddSingleton<AudienceOutputViewModel>();
        services.AddSingleton<StageOutputViewModel>();
        services.AddSingleton<AppNavigationViewModel>();
        services.AddSingleton<ShowViewModel>();
        services.AddTransient<EditViewModel>();
        services.AddTransient<ReflowViewModel>();
        services.AddTransient<ThemesViewModel>();
        services.AddSingleton<SettingsViewModel>();

        return services;
    }

    internal static IServiceCollection AddAppPages(this IServiceCollection services)
    {
        foreach (Type pageType in AppNavigationRoute.ActivePageTypes)
            services.AddTransient(pageType);

        return services;
    }
}
