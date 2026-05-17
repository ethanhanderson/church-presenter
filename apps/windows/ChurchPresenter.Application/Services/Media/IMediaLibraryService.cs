using ChurchPresenter.Backend.Media;

namespace ChurchPresenter.Services.Media;

/// <summary>
/// Manages the global media library: media playlists, items, and per-asset cue defaults.
/// </summary>
public interface IMediaLibraryService
{
    // ── Playlists ────────────────────────────────────────────────────────────

    /// <summary>Returns all media playlists in the library index order.</summary>
    Task<IReadOnlyList<MediaPlaylistManifest>> GetPlaylistsAsync(CancellationToken ct = default);

    /// <summary>Returns media items stored directly in the root "All Media" location.</summary>
    Task<IReadOnlyList<MediaLibraryItem>> GetRootItemsAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the media library as stable graph assets, deduplicated by asset identity.
    /// </summary>
    Task<IReadOnlyList<MediaAsset>> GetAssetsAsync(CancellationToken ct = default);

    /// <summary>
    /// Resolves an asset by stable id, stored path, or resolved absolute path.
    /// </summary>
    Task<MediaAsset?> ResolveAssetAsync(string assetIdOrPath, CancellationToken ct = default);

    /// <summary>
    /// Resolves a slide-linked media cue into a playback request through the media asset graph.
    /// </summary>
    Task<MediaPlaybackRequest?> ResolvePlaybackRequestAsync(
        SlideMediaCue cue,
        string? ownerReferenceId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Builds a cleanup reference graph from media/audio playlists, presentations, and optional support graph nodes.
    /// </summary>
    Task<MediaCleanupReferenceGraph> BuildCleanupReferenceGraphAsync(
        IEnumerable<PresentationProject> presentations,
        IEnumerable<MediaReferenceNode>? additionalNodes = null,
        CancellationToken ct = default);

    /// <summary>
    /// Resolves a stored path (absolute or content-root-relative) to an absolute path for file I/O and playback.
    /// </summary>
    string ResolveStoredMediaPath(string? storedPath);

    /// <summary>
    /// Copies any library items that still point at external files into <c>Media/Files/</c> and rewrites manifests to content-relative paths.
    /// </summary>
    Task<MediaMigrationResult> MigrateExternalMediaToManagedStorageAsync(CancellationToken ct = default);

    /// <summary>Counts managed vs. external references and missing files for audits.</summary>
    Task<MediaLinkStatistics> GetMediaLinkStatisticsAsync(CancellationToken ct = default);

    /// <summary>Returns a single playlist by id, or <c>null</c> if not found.</summary>
    Task<MediaPlaylistManifest?> GetPlaylistAsync(string playlistId, CancellationToken ct = default);

    /// <summary>Creates a new empty playlist with the given name and returns it.</summary>
    Task<MediaPlaylistManifest> CreatePlaylistAsync(string name, CancellationToken ct = default);

    /// <summary>Renames a playlist. Returns <c>false</c> when the playlist was not found.</summary>
    Task<bool> RenamePlaylistAsync(string playlistId, string newName, CancellationToken ct = default);

    /// <summary>Deletes a playlist and all of its items. Returns <c>false</c> when not found.</summary>
    Task<bool> DeletePlaylistAsync(string playlistId, CancellationToken ct = default);

    // ── Items ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Adds a media file to a playlist. The file is copied into <c>Media/Files/</c>.
    /// Returns the newly created <see cref="MediaLibraryItem"/>.
    /// </summary>
    Task<MediaLibraryItem> AddItemAsync(string playlistId, string filePath, CancellationToken ct = default);

    /// <summary>
    /// Adds a media file directly to the root "All Media" location.
    /// The file is copied into <c>Media/Files/</c>.
    /// Returns the newly created <see cref="MediaLibraryItem"/>.
    /// </summary>
    Task<MediaLibraryItem> AddRootItemAsync(string filePath, CancellationToken ct = default);

    /// <summary>
    /// Removes a media item from a playlist. Returns <c>false</c> when the item was not found.
    /// </summary>
    Task<bool> RemoveItemAsync(string playlistId, string itemId, CancellationToken ct = default);

    /// <summary>
    /// Removes a media item from the root "All Media" location. Returns <c>false</c> when not found.
    /// </summary>
    Task<bool> RemoveRootItemAsync(string itemId, CancellationToken ct = default);

    /// <summary>
    /// Renames a media item in either the root collection or a playlist. Returns <c>false</c> when not found.
    /// </summary>
    Task<bool> RenameItemAsync(string? playlistId, string itemId, string newName, CancellationToken ct = default);

    /// <summary>
    /// Duplicates a media item in either the root collection or a playlist and returns the created copy.
    /// Returns <c>null</c> when the source item was not found.
    /// </summary>
    Task<MediaLibraryItem?> DuplicateItemAsync(string? playlistId, string itemId, CancellationToken ct = default);

    // ── Cue defaults ─────────────────────────────────────────────────────────

    /// <summary>
    /// Persists updated <see cref="MediaCueDefaults"/> for a specific media item.
    /// Returns <c>false</c> when the item was not found.
    /// </summary>
    Task<bool> UpdateItemCueDefaultsAsync(
        string? playlistId,
        string itemId,
        MediaCueDefaults defaults,
        CancellationToken ct = default);

    /// <summary>
    /// Merges file-derived metadata (duration, dimensions) into the stored media item when missing.
    /// Searches root "All Media" first, then each playlist manifest.
    /// </summary>
    /// <returns><c>true</c> when an item was found and the manifest or index was written.</returns>
    Task<bool> UpdateMediaItemFileMetadataAsync(
        string itemId,
        double? durationSeconds,
        int? width,
        int? height,
        CancellationToken ct = default);
}