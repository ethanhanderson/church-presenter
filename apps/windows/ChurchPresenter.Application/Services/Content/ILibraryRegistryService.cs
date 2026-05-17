
namespace ChurchPresenter.Services.Content;

/// <summary>
/// Reads and writes the structured library registry stored as
/// <c>Libraries/Index.json</c> + <c>Libraries/&lt;id&gt;/Library.json</c> under the content root.
/// </summary>
public interface ILibraryRegistryService
{
    /// <summary>Loads the libraries index without loading per-library manifests.</summary>
    Task<DomainIndex> LoadIndexAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves the libraries index.</summary>
    Task SaveIndexAsync(DomainIndex index, CancellationToken cancellationToken = default);

    /// <summary>Loads all library manifests, deriving metadata from the index when manifests are missing.</summary>
    Task<IReadOnlyList<LibraryManifest>> LoadAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Loads a single library manifest by id; returns <c>null</c> when not found.</summary>
    Task<LibraryManifest?> LoadAsync(string libraryId, CancellationToken cancellationToken = default);

    /// <summary>Saves a library manifest and updates the index entry.</summary>
    Task SaveAsync(LibraryManifest library, CancellationToken cancellationToken = default);

    /// <summary>Deletes a library manifest and its folder, and removes the entry from the index.</summary>
    Task DeleteAsync(string libraryId, CancellationToken cancellationToken = default);

    /// <summary>Returns <c>true</c> when at least an index file exists in the Libraries root.</summary>
    bool RegistryExists();
}