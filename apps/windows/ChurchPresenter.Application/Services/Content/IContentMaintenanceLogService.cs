
namespace ChurchPresenter.Services.Content;

/// <summary>
/// Persists and reads content maintenance events for the managed library root.
/// </summary>
public interface IContentMaintenanceLogService
{
    /// <summary>
    /// Gets the absolute path to the persisted maintenance log file.
    /// </summary>
    string GetLogPath();

    /// <summary>
    /// Appends one or more maintenance events to the persistent log.
    /// </summary>
    Task AppendEntriesAsync(IEnumerable<ContentMaintenanceLogEntry> entries, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the most recent maintenance events, newest first.
    /// </summary>
    Task<IReadOnlyList<ContentMaintenanceLogEntry>> ReadRecentEntriesAsync(int maxEntries = 50, CancellationToken cancellationToken = default);
}