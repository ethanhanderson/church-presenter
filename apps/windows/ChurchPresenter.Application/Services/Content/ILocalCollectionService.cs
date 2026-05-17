
namespace ChurchPresenter.Services.Content;

/// <summary>
/// Manages the local documents-backed collection of libraries, playlists, and presentation files.
/// </summary>
public interface ILocalCollectionService
{
    /// <summary>
    /// Creates or reuses a library with the provided display name.
    /// </summary>
    Task<LibraryDto> EnsureLibraryAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or reuses a playlist with the provided display name.
    /// </summary>
    Task<PlaylistDto> EnsurePlaylistAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames an existing library without changing its identifier.
    /// </summary>
    Task RenameLibraryAsync(string libraryId, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames an existing playlist without changing its identifier.
    /// </summary>
    Task RenamePlaylistAsync(string playlistId, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a library entry and all presentations owned by that library from the local collection.
    /// </summary>
    Task DeleteLibraryAsync(string libraryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a playlist entry from the local collection.
    /// </summary>
    Task DeletePlaylistAsync(string playlistId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves an existing library to an absolute position in the library source order.
    /// </summary>
    Task MoveLibraryAsync(string libraryId, int targetIndex, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves an existing playlist to an absolute position in the playlist source order.
    /// </summary>
    Task MovePlaylistAsync(string playlistId, int targetIndex, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an independent copy of an existing playlist.
    /// </summary>
    Task<PlaylistDto> DuplicatePlaylistAsync(string playlistId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a blank presentation in the local collection and assigns it to a library/playlist.
    /// </summary>
    Task<ImportedPresentationResult> CreatePresentationAsync(
        string title,
        string? libraryId,
        string? playlistId,
        string? newLibraryName,
        string? newPlaylistName,
        string? aspectRatio = null,
        SlideSizeDto? slideSize = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports a single presentation into the local collection, assigns it to a library/playlist, and returns the local entry.
    /// </summary>
    Task<ImportedPresentationResult> ImportPresentationAsync(
        string sourcePath,
        string? libraryId,
        string? playlistId,
        string? newLibraryName,
        string? newPlaylistName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports all presentation bundles from a folder into a new or existing local library.
    /// </summary>
    Task<ImportedLibraryResult> ImportLibraryAsync(
        string sourceFolderPath,
        string? libraryName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds an existing presentation reference to a playlist.
    /// </summary>
    Task AddPresentationToPlaylistAsync(string playlistId, string presentationPath, int? insertIndex = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a presentation reference from a playlist.
    /// </summary>
    Task RemovePresentationFromPlaylistAsync(
        string playlistId,
        string presentationPath,
        int? playlistIndex = null,
        bool removeAllInstances = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a playlist item up or down by one position.
    /// </summary>
    Task MovePlaylistItemAsync(
        string playlistId,
        string presentationPath,
        int delta,
        int? playlistIndex = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result for a presentation imported into the local collection.
/// </summary>
public sealed class ImportedPresentationResult
{
    public required string LocalPath { get; init; }

    public required string LibraryId { get; init; }

    public string? PlaylistId { get; init; }

    public required string Title { get; init; }
}

/// <summary>
/// Result for a library import operation.
/// </summary>
public sealed class ImportedLibraryResult
{
    public required string LibraryId { get; init; }

    public required IReadOnlyList<string> ImportedPresentationPaths { get; init; }
}