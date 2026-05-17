
namespace ChurchPresenter.Adapters.Display;

/// <summary>
/// Bridges the existing monitor enumeration service into the backend-facing local-display catalog.
/// </summary>
public sealed class LocalDisplayCatalogService(IMonitorService monitors) : ILocalDisplayCatalogService
{
    private readonly IMonitorService _monitors = monitors ?? throw new ArgumentNullException(nameof(monitors));

    /// <inheritdoc />
    public IReadOnlyList<MonitorInfoDto> GetDisplays() => _monitors.GetMonitors();
}