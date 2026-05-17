
namespace ChurchPresenter.Services.Content;

/// <summary>
/// Reads and writes the structured playlist registry stored as
/// <c>Playlists/Index.json</c> + <c>Playlists/&lt;id&gt;/Playlist.json</c> under the content root.
/// </summary>
public interface IPlaylistRegistryService
{
    /// <summary>Loads the playlists index without loading per-playlist manifests.</summary>
    Task<DomainIndex> LoadIndexAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves the playlists index.</summary>
    Task SaveIndexAsync(DomainIndex index, CancellationToken cancellationToken = default);

    /// <summary>Loads all playlist manifests, deriving metadata from the index when manifests are missing.</summary>
    Task<IReadOnlyList<PlaylistManifest>> LoadAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Loads a single playlist manifest by id; returns <c>null</c> when not found.</summary>
    Task<PlaylistManifest?> LoadAsync(string playlistId, CancellationToken cancellationToken = default);

    /// <summary>Saves a playlist manifest and updates the index entry.</summary>
    Task SaveAsync(PlaylistManifest playlist, CancellationToken cancellationToken = default);

    /// <summary>Deletes a playlist manifest and its folder, and removes the entry from the index.</summary>
    Task DeleteAsync(string playlistId, CancellationToken cancellationToken = default);

    /// <summary>Returns <c>true</c> when at least an index file exists in the Playlists root.</summary>
    bool RegistryExists();
}