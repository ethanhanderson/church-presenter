using System.Text.Json;
using System.Text.Json.Serialization;


using Microsoft.Extensions.Logging;

namespace ChurchPresenter.Services.Content;

/// <inheritdoc />
public sealed class LibraryRegistryService : ILibraryRegistryService
{
    private readonly IContentDirectoryService _paths;
    private readonly IContentStore _contentStore;
    private readonly ILogger<LibraryRegistryService> _logger;

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
    public LibraryRegistryService(
        IContentDirectoryService paths,
        IContentStore contentStore,
        ILogger<LibraryRegistryService> logger)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _contentStore = contentStore ?? throw new ArgumentNullException(nameof(contentStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates the registry service with the default file-system content store.
    /// </summary>
    public LibraryRegistryService(
        IContentDirectoryService paths,
        ILogger<LibraryRegistryService> logger)
        : this(paths, ContentStoreDefaults.Instance, logger)
    {
    }

    /// <inheritdoc />
    public bool RegistryExists() =>
        _contentStore.FileExists(_paths.GetLibrariesIndexPath());

    /// <inheritdoc />
    public async Task<DomainIndex> LoadIndexAsync(CancellationToken cancellationToken = default)
    {
        var path = _paths.GetLibrariesIndexPath();
        return await _contentStore.ReadJsonAsync<DomainIndex>(path, JsonOptions, cancellationToken).ConfigureAwait(false)
               ?? new DomainIndex();
    }

    /// <inheritdoc />
    public async Task SaveIndexAsync(DomainIndex index, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(index);
        await _contentStore.WriteJsonAsync(_paths.GetLibrariesIndexPath(), index, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LibraryManifest>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        var index = await LoadIndexAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<LibraryManifest>(index.Entries.Count);

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
                // Index entry exists but manifest file is missing; reconstruct a minimal manifest
                _logger.LogWarning("Library manifest missing for id '{LibraryId}'; reconstructing from index entry.", entry.Id);
                results.Add(new LibraryManifest
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
    public async Task<LibraryManifest?> LoadAsync(string libraryId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryId);
        var index = await LoadIndexAsync(cancellationToken).ConfigureAwait(false);
        var folderName = ContentRegistryFolders.ResolveFolderName(index, libraryId);
        var path = _paths.GetLibraryManifestPath(folderName);
        var manifest = await _contentStore.ReadJsonAsync<LibraryManifest>(path, JsonOptions, cancellationToken).ConfigureAwait(false);
        if (manifest != null)
        {
            manifest.Id = string.IsNullOrWhiteSpace(manifest.Id) ? libraryId : manifest.Id;
            manifest.Name = string.IsNullOrWhiteSpace(manifest.Name) ? manifest.Id : manifest.Name;
            manifest.Presentations ??= new List<PresentationRefDto>();
        }

        return manifest;
    }

    /// <inheritdoc />
    public async Task SaveAsync(LibraryManifest library, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentException.ThrowIfNullOrWhiteSpace(library.Id);

        var index = await LoadIndexAsync(cancellationToken).ConfigureAwait(false);
        var previousFolder = ContentRegistryFolders.ResolveFolderName(index, library.Id);
        var folderName = ContentRegistryFolders.AllocateFolderName(
            _paths.GetLibrariesRootDirectory(),
            index,
            library.Id,
            library.Name);

        var previousDir = _paths.GetLibraryRootDirectory(previousFolder);
        var targetDir = _paths.GetLibraryRootDirectory(folderName);
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

        library.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");

        await _contentStore.WriteJsonAsync(_paths.GetLibraryManifestPath(folderName), library, JsonOptions, cancellationToken).ConfigureAwait(false);

        await UpdateIndexEntryAsync(library, folderName, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string libraryId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryId);

        var index = await LoadIndexAsync(cancellationToken).ConfigureAwait(false);
        var folderName = ContentRegistryFolders.ResolveFolderName(index, libraryId);
        var dir = _paths.GetLibraryRootDirectory(folderName);
        if (_contentStore.DirectoryExists(dir))
        {
            try
            {
                StructuredCatalogCleanup.PrepareDirectoryForDeletion(dir, _logger);
                _contentStore.TryDeleteDirectory(dir, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Could not delete library directory {Path}.", dir);
            }
        }

        index.Entries.RemoveAll(e => string.Equals(e.Id, libraryId, StringComparison.OrdinalIgnoreCase));
        await SaveIndexAsync(index, cancellationToken).ConfigureAwait(false);
    }

    private async Task UpdateIndexEntryAsync(LibraryManifest library, string folderName, CancellationToken cancellationToken)
    {
        var index = await LoadIndexAsync(cancellationToken).ConfigureAwait(false);
        var existing = index.Entries.FirstOrDefault(e => string.Equals(e.Id, library.Id, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.Name = library.Name;
            existing.Description = library.Description;
            existing.UpdatedAt = library.UpdatedAt;
            ContentRegistryFolders.ApplyFolderName(existing, folderName);
        }
        else
        {
            var entry = new DomainIndexEntry
            {
                Id = library.Id,
                Name = library.Name,
                Description = library.Description,
                CreatedAt = library.CreatedAt,
                UpdatedAt = library.UpdatedAt,
            };
            ContentRegistryFolders.ApplyFolderName(entry, folderName);
            index.Entries.Add(entry);
        }

        await SaveIndexAsync(index, cancellationToken).ConfigureAwait(false);
    }
}