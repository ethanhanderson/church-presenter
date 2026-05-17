
namespace ChurchPresenter.Services.Settings;

/// <summary>
/// Validates machine-specific settings and produces a <see cref="SettingsHealthSnapshot"/>
/// that surfaces issues in the settings hub and individual settings pages.
/// Machine-specific failures degrade gracefully: the app stays usable, the issue is logged,
/// and status is surfaced in the UI without blocking startup.
/// </summary>
public interface ISettingsHealthService
{
    /// <summary>Gets the current health snapshot; may be stale until <see cref="ValidateAsync"/> is called.</summary>
    SettingsHealthSnapshot CurrentHealth { get; }

    /// <summary>
    /// Validates machine-specific settings (monitor binding, integrations, content root accessibility, etc.)
    /// and persists the result to <c>MachineState/SettingsHealth.json</c>.
    /// </summary>
    Task<SettingsHealthSnapshot> ValidateAsync(CancellationToken cancellationToken = default);
}