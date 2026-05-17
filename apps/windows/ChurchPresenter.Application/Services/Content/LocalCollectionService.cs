
using Microsoft.Extensions.Logging;

namespace ChurchPresenter.Services.Content;

/// <inheritdoc />
public sealed class LocalCollectionService(
    IContentDirectoryService paths,
    ICatalogService catalog,
    ILibraryRegistryService libraryRegistry,
    IPlaylistRegistryService playlistRegistry,
    IPresentationDocumentService presentations,
    IPresentationProjectService projects,
    ILogger<LocalCollectionService> logger,
    IPresentationItemActionService? presentationActions = null) : ILocalCollectionService
{
    private readonly IContentDirectoryService _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly ICatalogService _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    private readonly ILibraryRegistryService _libraryRegistry = libraryRegistry ?? throw new ArgumentNullException(nameof(libraryRegistry));
    private readonly IPlaylistRegistryService _playlistRegistry = playlistRegistry ?? throw new ArgumentNullException(nameof(playlistRegistry));
    private readonly IPresentationDocumentService _presentations = presentations ?? throw new ArgumentNullException(nameof(presentations));
    private readonly IPresentationProjectService _projects = projects ?? throw new ArgumentNullException(nameof(projects));
    private readonly ILogger<LocalCollectionService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IPresentationItemActionService? _presentationActions = presentationActions;

    /// <inheritdoc />
    public async Task<LibraryDto> EnsureLibraryAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        await _catalog.LoadAsync().ConfigureAwait(false);

        var existing = _catalog.Catalog.Libraries.FirstOrDefault(l =>
            string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            return existing;

        var now = DateTime.UtcNow.ToString("O");
        var library = new LibraryDto
        {
            Id = CreateUniqueId(_catalog.Catalog.Libraries.Select(l => l.Id)),
            Name = name.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
            Presentations = new List<PresentationRefDto>(),
        };

        _catalog.Catalog.Libraries.Add(library);
        await _catalog.SaveAsync().ConfigureAwait(false);
        await _catalog.LoadAsync().ConfigureAwait(false);
        return _catalog.Catalog.Libraries.First(l => string.Equals(l.Id, library.Id, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public async Task<PlaylistDto> EnsurePlaylistAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        await _catalog.LoadAsync().ConfigureAwait(false);

        var existing = _catalog.Catalog.Playlists.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            return existing;

        var now = DateTime.UtcNow.ToString("O");
        var playlist = new PlaylistDto
        {
            Id = CreateUniqueId(_catalog.Catalog.Playlists.Select(p => p.Id)),
            Name = name.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
            Items = new List<PresentationRefDto>(),
        };

        _catalog.Catalog.Playlists.Add(playlist);
        await _catalog.SaveAsync().ConfigureAwait(false);
        await _catalog.LoadAsync().ConfigureAwait(false);
        return _catalog.Catalog.Playlists.First(p => string.Equals(p.Id, playlist.Id, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public async Task RenameLibraryAsync(string libraryId, string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        await _catalog.LoadAsync().ConfigureAwait(false);

        var library = _catalog.Catalog.Libraries.FirstOrDefault(l => string.Equals(l.Id, libraryId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Could not find library '{libraryId}'.");

        var manifest = await _libraryRegistry.LoadAsync(library.Id, cancellationToken).ConfigureAwait(false)
            ?? ToLibraryManifest(library);
        manifest.Name = name.Trim();
        await _libraryRegistry.SaveAsync(manifest, cancellationToken).ConfigureAwait(false);
        await _catalog.LoadAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RenamePlaylistAsync(string playlistId, string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playlistId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        await _catalog.LoadAsync().ConfigureAwait(false);

        var playlist = _catalog.Catalog.Playlists.FirstOrDefault(p => string.Equals(p.Id, playlistId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Could not find playlist '{playlistId}'.");

        var manifest = await _playlistRegistry.LoadAsync(playlist.Id, cancellationToken).ConfigureAwait(false)
            ?? ToPlaylistManifest(playlist);
        manifest.Name = name.Trim();
        await _playlistRegistry.SaveAsync(manifest, cancellationToken).ConfigureAwait(false);
        await _catalog.LoadAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteLibraryAsync(string libraryId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryId);
        await _catalog.LoadAsync().ConfigureAwait(false);

        var ownedPresentationPaths = _catalog.Catalog.Libraries
            .FirstOrDefault(l => string.Equals(l.Id, libraryId, StringComparison.OrdinalIgnoreCase))
            ?.Presentations
            .Select(p => p.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

        if (ownedPresentationPaths.Count > 0 && _presentationActions == null)
            throw new InvalidOperationException("Deleting a library with presentations requires presentation delete actions.");

        foreach (var presentationPath in ownedPresentationPaths)
            await _presentationActions!.DeletePresentationAsync(presentationPath, cancellationToken).ConfigureAwait(false);

        await _libraryRegistry.DeleteAsync(libraryId, cancellationToken).ConfigureAwait(false);
        await _catalog.LoadAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeletePlaylistAsync(string playlistId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playlistId);
        await _catalog.LoadAsync().ConfigureAwait(false);

        await _playlistRegistry.DeleteAsync(playlistId, cancellationToken).ConfigureAwait(false);
        await _catalog.LoadAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MoveLibraryAsync(string libraryId, int targetIndex, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryId);
        await _catalog.LoadAsync().ConfigureAwait(false);

        MoveSource(_catalog.Catalog.Libraries, libraryId, targetIndex, item => item.Id);
        await _catalog.SaveAsync().ConfigureAwait(false);
        await _catalog.LoadAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MovePlaylistAsync(string playlistId, int targetIndex, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playlistId);
        await _catalog.LoadAsync().ConfigureAwait(false);

        MoveSource(_catalog.Catalog.Playlists, playlistId, targetIndex, item => item.Id);
        await _catalog.SaveAsync().ConfigureAwait(false);
        await _catalog.LoadAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PlaylistDto> DuplicatePlaylistAsync(string playlistId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playlistId);
        await _catalog.LoadAsync().ConfigureAwait(false);

        var source = _catalog.Catalog.Playlists.FirstOrDefault(p => string.Equals(p.Id, playlistId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Could not find playlist '{playlistId}'.");

        var now = DateTime.UtcNow.ToString("O");
        var sourceManifest = await _playlistRegistry.LoadAsync(source.Id, cancellationToken).ConfigureAwait(false)
            ?? ToPlaylistManifest(source);
        var duplicate = new PlaylistManifest
        {
            Id = CreateUniqueId((await _playlistRegistry.LoadIndexAsync(cancellationToken).ConfigureAwait(false))
                .Entries.Select(p => p.Id)),
            Name = CreateDuplicateName(sourceManifest.Name, _catalog.Catalog.Playlists.Select(p => p.Name)),
            Description = sourceManifest.Description,
            CreatedAt = now,
            Items = sourceManifest.Items.Select(ClonePresentationRef).ToList(),
        };

        await _playlistRegistry.SaveAsync(duplicate, cancellationToken).ConfigureAwait(false);
        await _catalog.LoadAsync().ConfigureAwait(false);
        return _catalog.Catalog.Playlists.First(p => string.Equals(p.Id, duplicate.Id, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public async Task<ImportedPresentationResult> CreatePresentationAsync(
        string title,
        string? libraryId,
        string? playlistId,
        string? newLibraryName,
        string? newPlaylistName,
        string? aspectRatio = null,
        SlideSizeDto? slideSize = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        await _catalog.LoadAsync().ConfigureAwait(false);

        var now = DateTime.UtcNow.ToString("O");
        var presentationId = Guid.NewGuid().ToString("N");
        var normalizedTitle = title.Trim();
        var normalizedAspectRatio = string.IsNullOrWhiteSpace(aspectRatio) ? "16:9" : aspectRatio.Trim();
        var normalizedSlideSize = PresentationModelUtilities.GetBaseSlideSize(normalizedAspectRatio, slideSize);
        var slide = PresentationModelUtilities.CreateSlide("blank", slideSize: normalizedSlideSize);
        var project = new PresentationProject
        {
            Manifest = new PresentationManifest
            {
                FormatVersion = "1.0.0",
                PresentationId = presentationId,
                Title = normalizedTitle,
                CreatedAt = now,
                UpdatedAt = now,
                AspectRatio = normalizedAspectRatio,
                OutputScaleMode = PresentationModelUtilities.DefaultOutputScaleMode,
                SlideSize = normalizedSlideSize,
                Media = new List<MediaEntry>(),
                Fonts = new List<FontEntry>(),
            },
            Slides = new List<PresentationSlide> { slide },
            Arrangement = PresentationModelUtilities.CreateArrangement([slide]),
        };

        var absolutePath = _paths.GeneratePresentationPath(normalizedTitle, presentationId);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        _projects.Save(project, absolutePath);

        var presentationRef = CreatePresentationRef(
            _paths.ToContentRelativePath(absolutePath),
            normalizedTitle,
            now);

        var (targetLibrary, targetPlaylist) = await AddPresentationToSourcesAsync(
            presentationRef,
            libraryId,
            playlistId,
            newLibraryName,
            newPlaylistName,
            cancellationToken).ConfigureAwait(false);

        await _catalog.SaveAsync().ConfigureAwait(false);
        await _catalog.LoadAsync().ConfigureAwait(false);

        _logger.LogInformation(
            "Created presentation {Title} in library {LibraryId}{PlaylistSuffix}.",
            presentationRef.Title,
            targetLibrary.Id,
            targetPlaylist == null ? string.Empty : $" and playlist {targetPlaylist.Id}");

        return new ImportedPresentationResult
        {
            LocalPath = presentationRef.Path,
            LibraryId = targetLibrary.Id,
            PlaylistId = targetPlaylist?.Id,
            Title = presentationRef.Title,
        };
    }

    /// <inheritdoc />
    public async Task<ImportedPresentationResult> ImportPresentationAsync(
        string sourcePath,
        string? libraryId,
        string? playlistId,
        string? newLibraryName,
        string? newPlaylistName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        await _catalog.LoadAsync().ConfigureAwait(false);

        var imported = CopyIntoLocalCollection(sourcePath);
        var presentationRef = CreatePresentationRef(imported);

        var (targetLibrary, targetPlaylist) = await AddPresentationToSourcesAsync(
            presentationRef,
            libraryId,
            playlistId,
            newLibraryName,
            newPlaylistName,
            cancellationToken).ConfigureAwait(false);

        await _catalog.SaveAsync().ConfigureAwait(false);
        await _catalog.LoadAsync().ConfigureAwait(false);

        _logger.LogInformation(
            "Imported presentation {Title} into library {LibraryId}{PlaylistSuffix}.",
            presentationRef.Title,
            targetLibrary.Id,
            targetPlaylist == null ? string.Empty : $" and playlist {targetPlaylist.Id}");

        return new ImportedPresentationResult
        {
            LocalPath = presentationRef.Path,
            LibraryId = targetLibrary.Id,
            PlaylistId = targetPlaylist?.Id,
            Title = presentationRef.Title,
        };
    }

    /// <inheritdoc />
    public async Task<ImportedLibraryResult> ImportLibraryAsync(
        string sourceFolderPath,
        string? libraryName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFolderPath);
        if (!Directory.Exists(sourceFolderPath))
            throw new DirectoryNotFoundException($"Could not find import folder '{sourceFolderPath}'.");

        var filePaths = Directory.EnumerateFiles(sourceFolderPath, "*.cpres", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (filePaths.Count == 0)
            throw new InvalidOperationException("The selected folder does not contain any .cpres presentations.");

        var targetLibrary = await EnsureLibraryAsync(
            string.IsNullOrWhiteSpace(libraryName) ? Path.GetFileName(sourceFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) : libraryName,
            cancellationToken).ConfigureAwait(false);

        var importedPaths = new List<string>();
        foreach (var filePath in filePaths)
        {
            var imported = await ImportPresentationAsync(
                filePath,
                targetLibrary.Id,
                playlistId: null,
                newLibraryName: null,
                newPlaylistName: null,
                cancellationToken).ConfigureAwait(false);
            importedPaths.Add(imported.LocalPath);
        }

        return new ImportedLibraryResult
        {
            LibraryId = targetLibrary.Id,
            ImportedPresentationPaths = importedPaths,
        };
    }

    /// <inheritdoc />
    public async Task AddPresentationToPlaylistAsync(string playlistId, string presentationPath, int? insertIndex = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playlistId);
        ArgumentException.ThrowIfNullOrWhiteSpace(presentationPath);
        await _catalog.LoadAsync().ConfigureAwait(false);

        var playlist = _catalog.Catalog.Playlists.FirstOrDefault(p => string.Equals(p.Id, playlistId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Could not find playlist '{playlistId}'.");

        var presentationRef = ResolvePresentationRef(presentationPath);
        InsertPresentation(playlist.Items, presentationRef, insertIndex);
        playlist.UpdatedAt = DateTime.UtcNow.ToString("O");
        await _catalog.SaveAsync().ConfigureAwait(false);
        await _catalog.LoadAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemovePresentationFromPlaylistAsync(
        string playlistId,
        string presentationPath,
        int? playlistIndex = null,
        bool removeAllInstances = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playlistId);
        ArgumentException.ThrowIfNullOrWhiteSpace(presentationPath);
        await _catalog.LoadAsync().ConfigureAwait(false);

        var playlist = _catalog.Catalog.Playlists.FirstOrDefault(p => string.Equals(p.Id, playlistId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Could not find playlist '{playlistId}'.");

        if (removeAllInstances)
        {
            playlist.Items.RemoveAll(p => PathsEqual(p.Path, presentationPath));
        }
        else
        {
            var index = ResolvePlaylistItemIndex(playlist.Items, presentationPath, playlistIndex);
            playlist.Items.RemoveAt(index);
        }

        playlist.UpdatedAt = DateTime.UtcNow.ToString("O");
        await _catalog.SaveAsync().ConfigureAwait(false);
        await _catalog.LoadAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MovePlaylistItemAsync(
        string playlistId,
        string presentationPath,
        int delta,
        int? playlistIndex = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playlistId);
        ArgumentException.ThrowIfNullOrWhiteSpace(presentationPath);
        if (delta == 0)
            return;

        await _catalog.LoadAsync().ConfigureAwait(false);

        var playlist = _catalog.Catalog.Playlists.FirstOrDefault(p => string.Equals(p.Id, playlistId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Could not find playlist '{playlistId}'.");

        var index = ResolvePlaylistItemIndex(playlist.Items, presentationPath, playlistIndex);

        var targetIndex = Math.Clamp(index + delta, 0, playlist.Items.Count - 1);
        if (targetIndex == index)
            return;

        var item = playlist.Items[index];
        playlist.Items.RemoveAt(index);
        playlist.Items.Insert(targetIndex, item);
        playlist.UpdatedAt = DateTime.UtcNow.ToString("O");
        await _catalog.SaveAsync().ConfigureAwait(false);
        await _catalog.LoadAsync().ConfigureAwait(false);
    }

    private ImportedPresentationDocument CopyIntoLocalCollection(string sourcePath)
    {
        var fullSourcePath = Path.GetFullPath(sourcePath);
        var presentation = _presentations.Open(fullSourcePath);
        var localPath = ResolveLocalImportPath(fullSourcePath, presentation);

        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
        if (!PathsEqual(fullSourcePath, localPath))
            File.Copy(fullSourcePath, localPath, overwrite: true);

        return new ImportedPresentationDocument
        {
            LocalAbsolutePath = localPath,
            RelativePath = _paths.ToContentRelativePath(localPath),
            Title = string.IsNullOrWhiteSpace(presentation.Manifest.Title)
                ? Path.GetFileNameWithoutExtension(localPath)
                : presentation.Manifest.Title,
            UpdatedAt = string.IsNullOrWhiteSpace(presentation.Manifest.UpdatedAt)
                ? File.GetLastWriteTimeUtc(localPath).ToString("O")
                : presentation.Manifest.UpdatedAt,
        };
    }

    private string ResolveLocalImportPath(string sourcePath, PresentationDocument presentation)
    {
        var contentRoot = Path.GetFullPath(_paths.GetDocumentsDataDirectory());
        if (sourcePath.StartsWith(contentRoot, StringComparison.OrdinalIgnoreCase))
            return sourcePath;

        var presentationId = string.IsNullOrWhiteSpace(presentation.Manifest.PresentationId)
            ? Guid.NewGuid().ToString("N")
            : presentation.Manifest.PresentationId;
        return _paths.GeneratePresentationPath(presentation.Manifest.Title, presentationId);
    }

    private async Task<LibraryDto> ResolveLibraryAsync(string? libraryId, string? newLibraryName, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(newLibraryName))
            return await EnsureLibraryAsync(newLibraryName, cancellationToken).ConfigureAwait(false);

        var existing = _catalog.Catalog.Libraries.FirstOrDefault(l => string.Equals(l.Id, libraryId, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            return existing;

        return await EnsureLibraryAsync("Library", cancellationToken).ConfigureAwait(false);
    }

    private async Task<PlaylistDto> ResolvePlaylistAsync(string? playlistId, string? newPlaylistName, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(newPlaylistName))
            return await EnsurePlaylistAsync(newPlaylistName, cancellationToken).ConfigureAwait(false);

        var existing = _catalog.Catalog.Playlists.FirstOrDefault(p => string.Equals(p.Id, playlistId, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            return existing;

        return await EnsurePlaylistAsync("Playlist", cancellationToken).ConfigureAwait(false);
    }

    private static PresentationRefDto CreatePresentationRef(ImportedPresentationDocument imported) =>
        CreatePresentationRef(imported.RelativePath, imported.Title, imported.UpdatedAt);

    private static PresentationRefDto CreatePresentationRef(string path, string title, string updatedAt) =>
        new()
        {
            Path = path,
            Title = title,
            UpdatedAt = updatedAt,
        };

    private async Task<(LibraryDto Library, PlaylistDto? Playlist)> AddPresentationToSourcesAsync(
        PresentationRefDto presentationRef,
        string? libraryId,
        string? playlistId,
        string? newLibraryName,
        string? newPlaylistName,
        CancellationToken cancellationToken)
    {
        var targetLibrary = await ResolveLibraryAsync(libraryId, newLibraryName, cancellationToken).ConfigureAwait(false);

        PlaylistDto? targetPlaylist = null;
        if (!string.IsNullOrWhiteSpace(playlistId) || !string.IsNullOrWhiteSpace(newPlaylistName))
            targetPlaylist = await ResolvePlaylistAsync(playlistId, newPlaylistName, cancellationToken).ConfigureAwait(false);

        targetLibrary = _catalog.Catalog.Libraries.First(l => string.Equals(l.Id, targetLibrary.Id, StringComparison.OrdinalIgnoreCase));
        UpsertPresentation(targetLibrary.Presentations, presentationRef);
        targetLibrary.UpdatedAt = DateTime.UtcNow.ToString("O");

        if (targetPlaylist != null)
        {
            targetPlaylist = _catalog.Catalog.Playlists.First(p => string.Equals(p.Id, targetPlaylist.Id, StringComparison.OrdinalIgnoreCase));
            targetPlaylist.Items.Add(new PresentationRefDto
            {
                Path = presentationRef.Path,
                Title = presentationRef.Title,
                UpdatedAt = presentationRef.UpdatedAt,
            });
            targetPlaylist.UpdatedAt = DateTime.UtcNow.ToString("O");
        }

        return (targetLibrary, targetPlaylist);
    }

    private PresentationRefDto ResolvePresentationRef(string presentationPath)
    {
        foreach (var library in _catalog.Catalog.Libraries)
        {
            var existing = library.Presentations.FirstOrDefault(p => PathsEqual(p.Path, presentationPath));
            if (existing != null)
            {
                return new PresentationRefDto
                {
                    Path = existing.Path,
                    Title = existing.Title,
                    UpdatedAt = existing.UpdatedAt,
                };
            }
        }

        foreach (var playlist in _catalog.Catalog.Playlists)
        {
            var existing = playlist.Items.FirstOrDefault(p => PathsEqual(p.Path, presentationPath));
            if (existing != null)
            {
                return new PresentationRefDto
                {
                    Path = existing.Path,
                    Title = existing.Title,
                    UpdatedAt = existing.UpdatedAt,
                };
            }
        }

        var absolutePath = _paths.ResolvePresentationPath(presentationPath);
        var presentation = _presentations.Open(absolutePath);
        return new PresentationRefDto
        {
            Path = _paths.ToContentRelativePath(absolutePath),
            Title = string.IsNullOrWhiteSpace(presentation.Manifest.Title)
                ? Path.GetFileNameWithoutExtension(absolutePath)
                : presentation.Manifest.Title,
            UpdatedAt = string.IsNullOrWhiteSpace(presentation.Manifest.UpdatedAt)
                ? File.GetLastWriteTimeUtc(absolutePath).ToString("O")
                : presentation.Manifest.UpdatedAt,
        };
    }

    private static void UpsertPresentation(List<PresentationRefDto> collection, PresentationRefDto presentation)
    {
        var existing = collection.FindIndex(p => PathsEqual(p.Path, presentation.Path));
        if (existing >= 0)
            collection[existing] = presentation;
        else
            collection.Add(presentation);
    }

    private static void InsertPresentation(List<PresentationRefDto> collection, PresentationRefDto presentation, int? insertIndex = null)
    {
        if (insertIndex is not { } index)
        {
            collection.Add(presentation);
            return;
        }

        collection.Insert(Math.Clamp(index, 0, collection.Count), presentation);
    }

    private static int ResolvePlaylistItemIndex(List<PresentationRefDto> collection, string presentationPath, int? playlistIndex)
    {
        if (playlistIndex is { } index
            && index >= 0
            && index < collection.Count
            && PathsEqual(collection[index].Path, presentationPath))
        {
            return index;
        }

        var firstMatchingIndex = collection.FindIndex(p => PathsEqual(p.Path, presentationPath));
        if (firstMatchingIndex < 0)
            throw new InvalidOperationException($"Could not find presentation '{presentationPath}' in playlist.");

        return firstMatchingIndex;
    }

    private static void MoveSource<T>(List<T> collection, string sourceId, int targetIndex, Func<T, string> getId)
    {
        var sourceIndex = collection.FindIndex(item => string.Equals(getId(item), sourceId, StringComparison.OrdinalIgnoreCase));
        if (sourceIndex < 0)
            throw new InvalidOperationException($"Could not find source '{sourceId}'.");

        var item = collection[sourceIndex];
        collection.RemoveAt(sourceIndex);
        var adjustedTargetIndex = sourceIndex < targetIndex ? targetIndex - 1 : targetIndex;
        collection.Insert(Math.Clamp(adjustedTargetIndex, 0, collection.Count), item);
    }

    private static PresentationRefDto ClonePresentationRef(PresentationRefDto source) =>
        new()
        {
            Path = source.Path,
            Title = source.Title,
            UpdatedAt = source.UpdatedAt,
            ArrangementId = source.ArrangementId,
            DestinationLayerId = source.DestinationLayerId,
            ThumbnailData = source.ThumbnailData,
        };

    private static LibraryManifest ToLibraryManifest(LibraryDto library) =>
        new()
        {
            Id = library.Id,
            Name = library.Name,
            Description = library.Description,
            CreatedAt = library.CreatedAt ?? DateTimeOffset.UtcNow.ToString("O"),
            UpdatedAt = library.UpdatedAt,
            DefaultFolder = library.DefaultFolder,
            Presentations = library.Presentations.Select(ClonePresentationRef).ToList(),
        };

    private static PlaylistManifest ToPlaylistManifest(PlaylistDto playlist) =>
        new()
        {
            Id = playlist.Id,
            Name = playlist.Name,
            Description = playlist.Description,
            CreatedAt = playlist.CreatedAt ?? DateTimeOffset.UtcNow.ToString("O"),
            UpdatedAt = playlist.UpdatedAt,
            Items = playlist.Items.Select(ClonePresentationRef).ToList(),
            ExternalSet = playlist.ExternalSet,
            Sync = playlist.Sync,
        };

    private static string CreateDuplicateName(string sourceName, IEnumerable<string> existingNames)
    {
        var normalizedExisting = existingNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var baseName = string.IsNullOrWhiteSpace(sourceName) ? "Playlist" : sourceName.Trim();
        var candidate = $"{baseName} Copy";
        if (!normalizedExisting.Contains(candidate))
            return candidate;

        for (var index = 2; ; index++)
        {
            candidate = $"{baseName} Copy {index}";
            if (!normalizedExisting.Contains(candidate))
                return candidate;
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/');

    private static string CreateUniqueId(IEnumerable<string> existingIds)
    {
        var normalizedExisting = existingIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var next = Guid.NewGuid().ToString();
        while (normalizedExisting.Contains(next))
            next = Guid.NewGuid().ToString();

        return next;
    }

    private sealed class ImportedPresentationDocument
    {
        public required string LocalAbsolutePath { get; init; }

        public required string RelativePath { get; init; }

        public required string Title { get; init; }

        public required string UpdatedAt { get; init; }
    }
}