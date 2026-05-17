
namespace ChurchPresenter.Services.Content;

/// <summary>
/// Runs startup content bootstrap, catalog repair, cache cleanup, and diagnostics without blocking the app shell.
/// </summary>
public interface IContentStartupMaintenanceService
{
    /// <summary>Raised whenever the workflow status changes.</summary>
    event EventHandler<ContentStartupMaintenanceChangedEventArgs>? Changed;

    /// <summary>Gets the latest workflow snapshot.</summary>
    ContentStartupMaintenanceSnapshot Current { get; }

    /// <summary>
    /// Runs the workflow. Concurrent callers are serialized so only one maintenance pass mutates content at a time.
    /// </summary>
    Task StartAsync(
        ContentMaintenanceTrigger trigger = ContentMaintenanceTrigger.Startup,
        CancellationToken cancellationToken = default);
}
