
namespace ChurchPresenter.Services.Content;

/// <summary>
/// Exports and imports portable library and playlist collection packages.
/// </summary>
public interface ICollectionPackageService
{
    /// <summary>
    /// Exports a library and its referenced presentations into a portable package archive.
    /// </summary>
    Task ExportLibraryAsync(string libraryId, string destinationPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports a playlist and its referenced presentations into a portable package archive.
    /// </summary>
    Task ExportPlaylistAsync(string playlistId, string destinationPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Previews a library package import without writing local files.
    /// </summary>
    Task<CollectionPackagePreview> PreviewLibraryImportAsync(string packagePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Previews a playlist package import without writing local files.
    /// </summary>
    Task<CollectionPackagePreview> PreviewPlaylistImportAsync(string packagePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports a portable library package into the local content collection.
    /// </summary>
    Task<ImportedLibraryPackageResult> ImportLibraryAsync(string packagePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports a portable library package into the local content collection after preview confirmation.
    /// </summary>
    Task<ImportedLibraryPackageResult> ImportLibraryAsync(string packagePath, CollectionPackageImportOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports a portable playlist package into the local content collection.
    /// </summary>
    Task<ImportedPlaylistPackageResult> ImportPlaylistAsync(string packagePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports a portable playlist package into the local content collection after preview confirmation.
    /// </summary>
    Task<ImportedPlaylistPackageResult> ImportPlaylistAsync(string packagePath, CollectionPackageImportOptions options, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result for a library package import.
/// </summary>
public sealed class ImportedLibraryPackageResult
{
    public required string LibraryId { get; init; }

    public required IReadOnlyList<string> ImportedPresentationPaths { get; init; }
}

/// <summary>
/// Result for a playlist package import.
/// </summary>
public sealed class ImportedPlaylistPackageResult
{
    public required string PlaylistId { get; init; }

    public required IReadOnlyList<string> ImportedPresentationPaths { get; init; }
}