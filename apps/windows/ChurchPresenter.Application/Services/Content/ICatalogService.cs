
namespace ChurchPresenter.Services.Content;

/// <summary>
/// Libraries and playlists persisted as JSON beside the document content root.
/// </summary>
public interface ICatalogService
{
    /// <summary>Gets the in-memory catalog model.</summary>
    CatalogDto Catalog { get; }

    /// <summary>Loads catalog files from disk, creating defaults when empty.</summary>
    /// <returns>A task that completes when loading finishes.</returns>
    Task LoadAsync(ContentMaintenanceTrigger trigger = ContentMaintenanceTrigger.Default);

    /// <summary>Persists the current catalog to disk.</summary>
    /// <returns>A task that completes when saving finishes.</returns>
    Task SaveAsync();
}