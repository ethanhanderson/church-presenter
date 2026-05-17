namespace ChurchPresenter.Adapters.Display;

/// <summary>
/// Shows temporary on-screen labels so operators can map picker rows to physical monitors.
/// </summary>
public interface IMonitorIdentifyService
{
    /// <summary>
    /// Gets whether an identify overlay is currently visible.
    /// </summary>
    bool IsIdentificationActive { get; }

    /// <summary>
    /// Toggles the identify overlay for one monitor.
    /// </summary>
    void ToggleIdentifier(int monitorIndex);

    /// <summary>
    /// Hides any visible identify overlays.
    /// </summary>
    void HideIdentifiers();
}