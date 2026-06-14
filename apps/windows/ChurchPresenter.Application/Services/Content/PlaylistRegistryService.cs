using System.Text.Json;
using System.Text.Json.Serialization;


using Microsoft.Extensions.Logging;

namespace ChurchPresenter.Services.Content;

/// <inheritdoc />
public sealed class PlaylistRegistryService : IPlaylistRegistryService
{
    private readonly IContentDirectoryService _paths;
    private readonly IContentStore _contentStore;
    private readonly ILogger<PlaylistRegistryService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Creates the registry service with the shared content store abstraction.
    /// </summary>
    public PlaylistRegistryService(
        IContentDirectoryService paths,
        IContentStore contentStore,
        ILogger<PlaylistRegistryService> logger)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _contentStore = contentStore ?? throw new ArgumentNullException(nameof(contentStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates the registry service with the default file-system content store.
    /// </summary>
    public PlaylistRegistryService(
        IContentDirectoryService paths,
        ILogger<PlaylistRegistryService> logger)
        : this(paths, ContentStoreDefaults.Instance, logger)
    {
    }

    /// <inheritdoc />
    public bool RegistryExists() =>
        _contentStore.FileExists(_paths.GetPlaylistsIndexPath());

    /// <inheritdoc />
    public async Task<DomainIndex> LoadIndexAsync(CancellationToken cancellationToken = default)
    {
        var path = _paths.GetPlaylistsIndexPath();
        return await _contentStore.ReadJsonAsync<DomainIndex>(path, JsonOptions, cancellationToken).ConfigureAwait(false)
               ?? new DomainIndex();
    }

    /// <inheritdoc />
    public async Task SaveIndexAsync(DomainIndex index, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(index);
        await _contentStore.WriteJsonAsync(_paths.GetPlaylistsIndexPath(), index, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PlaylistManifest>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        var index = await LoadIndexAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<PlaylistManifest>(index.Entries.Count);

        foreach (var entry in index.Entries.Where(e => !string.IsNullOrWhiteSpace(e.Id)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifest = await LoadAsync(entry.Id, cancellationToken).ConfigureAwait(false);
            if (manifest != null)
            {
                results.Add(manifest);
            }
            else
            {
                _logger.LogWarning("Playlist manifest missing for id '{PlaylistId}'; reconstructing from index entry.", entry.Id);
                results.Add(new PlaylistManifest
                {
                    Id = entry.Id,
                    Name = entry.Name,
                    Description = entry.Description,
                    CreatedAt = entry.CreatedAt,
                    UpdatedAt = entry.UpdatedAt,
                });
            }
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<PlaylistManifest?> LoadAsync(string playlistId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playlistId);
        var index = await LoadIndexAsync(cancellationToken).ConfigureAwait(false);
        var folderName = ContentRegistryFolders.ResolveFolderName(index, playlistId);
        var path = _paths.GetPlaylistManifestPath(folderName);
        var manifest = await _contentStore.ReadJsonAsync<PlaylistManifest>(path, JsonOptions, cancellationToken).ConfigureAwait(false);
        if (manifest != null)
        {
            manifest.Id = string.IsNullOrWhiteSpace(manifest.Id) ? playlistId : manifest.Id;
            manifest.Name = string.IsNullOrWhiteSpace(manifest.Name) ? manifest.Id : manifest.Name;
            manifest.Items ??= new List<PresentationRefDto>();
        }

        return manifest;
    }

    /// <inheritdoc />
    public async Task SaveAsync(PlaylistManifest playlist, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playlist);
        ArgumentException.ThrowIfNullOrWhiteSpace(playlist.Id);

        var index = await LoadIndexAsync(cancellationToken).ConfigureAwait(false);
        var previousFolder = ContentRegistryFolders.ResolveFolderName(index, playlist.Id);
        var folderName = ContentRegistryFolders.AllocateFolderName(
            _paths.GetPlaylistsRootDirectory(),
            index,
            playlist.Id,
            playlist.Name);

        var previousDir = _paths.GetPlaylistRootDirectory(previousFolder);
        var targetDir = _paths.GetPlaylistRootDirectory(folderName);
        if (!string.Equals(previousDir, targetDir, StringComparison.OrdinalIgnoreCase))
        {
            if (_contentStore.DirectoryExists(previousDir))
                Directory.Move(previousDir, targetDir);
            else
                _contentStore.EnsureDirectory(targetDir);
        }
        else
        {
            _contentStore.EnsureDirectory(targetDir);
        }

        playlist.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");

        await _contentStore.WriteJsonAsync(_paths.GetPlaylistManifestPath(folderName), playlist, JsonOptions, cancellationToken).ConfigureAwait(false);

        await UpdateIndexEntryAsync(playlist, folderName, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string playlistId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playlistId);

        var index = await LoadIndexAsync(cancellationToken).ConfigureAwait(false);
        var folderName = ContentRegistryFolders.ResolveFolderName(index, playlistId);
        var dir = _paths.GetPlaylistRootDirectory(folderName);
        if (_contentStore.DirectoryExists(dir))
        {
            try
            {
                StructuredCatalogCleanup.PrepareDirectoryForDeletion(dir, _logger);
                _contentStore.TryDeleteDirectory(dir, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Could not delete playlist directory {Path}.", dir);
            }
        }

        index.Entries.RemoveAll(e => string.Equals(e.Id, playlistId, StringComparison.OrdinalIgnoreCase));
        await SaveIndexAsync(index, cancellationToken).ConfigureAwait(false);
    }

    private async Task UpdateIndexEntryAsync(PlaylistManifest playlist, string folderName, CancellationToken cancellationToken)
    {
        var index = await LoadIndexAsync(cancellationToken).ConfigureAwait(false);
        var existing = index.Entries.FirstOrDefault(e => string.Equals(e.Id, playlist.Id, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.Name = playlist.Name;
            existing.Description = playlist.Description;
            existing.UpdatedAt = playlist.UpdatedAt;
            ContentRegistryFolders.ApplyFolderName(existing, folderName);
        }
        else
        {
            var entry = new DomainIndexEntry
            {
                Id = playlist.Id,
                Name = playlist.Name,
                Description = playlist.Description,
                CreatedAt = playlist.CreatedAt,
                UpdatedAt = playlist.UpdatedAt,
            };
            ContentRegistryFolders.ApplyFolderName(entry, folderName);
            index.Entries.Add(entry);
        }

        await SaveIndexAsync(index, cancellationToken).ConfigureAwait(false);
    }
}