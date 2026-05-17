using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ChurchPresenter.Hosting;

/// <summary>
/// Configures the application's dependency injection container, logging, and view registrations.
/// </summary>
/// <remarks>
/// Follows the WinUI MVVM tutorial pattern for <see cref="ServiceCollection"/> registration
/// (<see href="https://learn.microsoft.com/windows/apps/tutorials/winui-mvvm-toolkit/dependency-injection"/>).
/// </remarks>
public static class AppServices
{
    /// <summary>
    /// Builds the root <see cref="IServiceProvider"/> for the WinUI process.
    /// </summary>
    /// <returns>The configured service provider.</returns>
    public static IServiceProvider Build()
    {
        ServiceCollection services = new();

        services
            .AddAppLogging()
            .AddApplicationServices()
            .AddLiveProductionServices()
            .AddWinUiHostServices()
            .AddAppViewModels()
            .AddAppPages();

        return services.BuildServiceProvider();
    }

    private static IServiceCollection AddAppLogging(this IServiceCollection services)
    {
        services.AddLogging(static b =>
        {
            b.AddDebug();
#if DEBUG
            b.SetMinimumLevel(LogLevel.Debug);
#else
            b.SetMinimumLevel(LogLevel.Information);
#endif
        });

        return services;
    }

}