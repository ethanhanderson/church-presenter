
namespace ChurchPresenter.Adapters.Display;

/// <summary>Enumerates connected monitors for output routing.</summary>
public interface IMonitorService
{
    /// <summary>Returns monitors sorted by work area position, or an empty list on failure.</summary>
    /// <returns>Ordered monitor descriptors.</returns>
    IReadOnlyList<MonitorInfoDto> GetMonitors();
}