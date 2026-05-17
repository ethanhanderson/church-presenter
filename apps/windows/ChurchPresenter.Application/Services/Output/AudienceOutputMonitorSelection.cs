
namespace ChurchPresenter.Services.Output;

/// <summary>
/// Resolves requested audience-output monitor indices against the currently available display topology.
/// </summary>
public static class AudienceOutputMonitorSelection
{
    /// <summary>
    /// Resolves the single preferred audience-output monitor. Returns the first valid requested monitor,
    /// or falls back to the first available monitor when no stored target still exists.
    /// </summary>
    public static int? ResolvePreferredMonitorIndex(
        IReadOnlyList<int> requestedMonitorIndices,
        IReadOnlyList<MonitorInfoDto> availableMonitors)
    {
        IReadOnlyList<int> validIndices = ResolveValidMonitorIndices(requestedMonitorIndices, availableMonitors);
        if (validIndices.Count > 0)
            return validIndices[0];

        return availableMonitors
            .OrderBy(monitor => monitor.Index)
            .Select(monitor => (int?)monitor.Index)
            .FirstOrDefault();
    }

    /// <summary>
    /// Returns the subset of requested monitor indices that still exist in the current monitor list.
    /// </summary>
    /// <param name="requestedMonitorIndices">Requested zero-based monitor indices.</param>
    /// <param name="availableMonitors">Currently available monitors.</param>
    /// <returns>Distinct requested indices that map to available monitors, ordered ascending.</returns>
    public static IReadOnlyList<int> ResolveValidMonitorIndices(
        IReadOnlyList<int> requestedMonitorIndices,
        IReadOnlyList<MonitorInfoDto> availableMonitors)
    {
        ArgumentNullException.ThrowIfNull(requestedMonitorIndices);
        ArgumentNullException.ThrowIfNull(availableMonitors);

        HashSet<int> availableIndices = availableMonitors
            .Select(monitor => monitor.Index)
            .ToHashSet();

        return requestedMonitorIndices
            .Where(index => index >= 0)
            .Distinct()
            .OrderBy(index => index)
            .Where(availableIndices.Contains)
            .ToList();
    }
}