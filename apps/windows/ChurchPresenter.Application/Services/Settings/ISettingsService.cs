
namespace ChurchPresenter.Services.Settings;

/// <summary>
/// Application settings stored as JSON under the per-user local app data folder
/// (see <see cref="IContentDirectoryService.GetAppDataDirectory"/>), matching the Windows guidance for
/// <see href="https://learn.microsoft.com/windows/apps/design/app-settings/store-and-retrieve-app-data">storing app data</see>.
/// </summary>
public interface ISettingsService
{
    /// <summary>Gets the current settings snapshot.</summary>
    AppSettingsDto Settings { get; }

    /// <summary>Loads settings from disk, or defaults if missing.</summary>
    /// <returns>A task that completes when loading finishes.</returns>
    Task LoadAsync();

    /// <summary>Writes the current settings to disk.</summary>
    /// <returns>A task that completes when saving finishes.</returns>
    Task SaveAsync();

    /// <summary>Applies a mutator to the live settings object.</summary>
    /// <param name="mutator">Delegate that updates <see cref="AppSettingsDto"/> fields.</param>
    void Update(Action<AppSettingsDto> mutator);
}