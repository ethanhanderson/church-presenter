
using Microsoft.Extensions.Logging;

namespace ChurchPresenter.Services.Show;

/// <summary>
/// Performs presentation-level sidebar actions such as add, move, duplicate, rename, and bundle export.
/// </summary>
public interface IPresentationItemActionService
{
    /// <summary>
    /// Adds an existing presentation reference to a library without removing it from the current source.
    /// </summary>
    Task AddPresentationToLibraryAsync(string libraryId, string presentationPath, int? insertIndex = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds an existing presentation reference to a playlist without removing it from the current source.
    /// </summary>
    Task AddPresentationToPlaylistAsync(string playlistId, string presentationPath, int? insertIndex = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a presentation from one library to another.
    /// </summary>
    Task MovePresentationToLibraryAsync(string sourceLibraryId, string targetLibraryId, string presentationPath, int? insertIndex = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a presentation from one playlist to another.
    /// </summary>
    Task MovePresentationToPlaylistAsync(
        string sourcePlaylistId,
        string targetPlaylistId,
        string presentationPath,
        int? insertIndex = null,
        int? sourcePlaylistIndex = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Duplicates a presentation bundle and adds the duplicate to the requested targets.
    /// </summary>
    Task<PresentationItemMutationResult> DuplicatePresentationAsync(
        string sourcePresentationPath,
        string targetLibraryId,
        string? targetPlaylistId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames a presentation, updating its manifest title, managed file path, and stored catalog references.
    /// </summary>
    Task<PresentationItemRenameResult> RenamePresentationAsync(string sourcePresentationPath, string newTitle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a presentation bundle, removing all catalog/workspace references and the managed file.
    /// </summary>
    Task<PresentationItemDeleteResult> DeletePresentationAsync(string sourcePresentationPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports a single portable presentation bundle to the specified destination path.
    /// </summary>
    Task ExportPresentationBundleAsync(string sourcePresentationPath, string destinationPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the default arrangement for one library or playlist presentation reference.
    /// </summary>
    Task SetPresentationReferenceArrangementAsync(
        string? libraryId,
        string? playlistId,
        string presentationPath,
        string? arrangementId,
        int? playlistIndex = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the destination output layer for one library or playlist presentation reference.
    /// </summary>
    Task SetPresentationReferenceDestinationAsync(
        string? libraryId,
        string? playlistId,
        string presentationPath,
        string? destinationLayerId,
        int? playlistIndex = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the slide size for an existing presentation bundle.
    /// </summary>
    Task ResizePresentationAsync(string sourcePresentationPath, SlideSizeDto slideSize, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result from duplicating a presentation into a new managed bundle path.
/// </summary>
public sealed class PresentationItemMutationResult
{
    public required string PresentationPath { get; init; }

    public required string Title { get; init; }

    public required string LibraryId { get; init; }

    public string? PlaylistId { get; init; }
}

/// <summary>
/// Result from renaming a presentation and remapping its stored references.
/// </summary>
public sealed class PresentationItemRenameResult
{
    public required string OldPresentationPath { get; init; }

    public required string NewPresentationPath { get; init; }

    public required string Title { get; init; }
}

/// <summary>
/// Result from deleting a presentation and all of its stored references.
/// </summary>
public sealed class PresentationItemDeleteResult
{
    public required string PresentationPath { get; init; }

    public required string Title { get; init; }

    public bool DeletedBundleFile { get; init; }
}

/// <inheritdoc />
public sealed class PresentationItemActionService(
    IContentDirectoryService content,
    ICatalogService catalog,
    IWorkspaceService workspace,
    ISettingsService settings,
    IPresentationProjectService projects,
    IPresentationDocumentService documents,
    ILogger<PresentationItemActionService> logger,
    IContentChangeBus? contentChanges = null,
    IContentStore? contentStore = null) : IPresentationItemActionService
{
    private readonly IContentDirectoryService _content = content ?? throw new ArgumentNullException(nameof(content));
    private readonly ICatalogService _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    private readonly IWorkspaceService _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
    private readonly ISettingsService _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly IPresentationProjectService _projects = projects ?? throw new ArgumentNullException(nameof(projects));
    private readonly IPresentationDocumentService _documents = documents ?? throw new ArgumentNullException(nameof(documents));
    private readonly ILogger<PresentationItemActionService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IContentChangeBus? _contentChanges = contentChanges;
    private readonly IContentStore _contentStore = contentStore ?? ContentStoreDefaults.Instance;

    /// <inheritdoc />
    public async Task AddPresentationToLibraryAsync(string libraryId, string presentationPath, int? insertIndex = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(presentationPath);

        await _catalog.LoadAsync().ConfigureAwait(false);

        var library = _catalog.Catalog.Libraries.FirstOrDefault(item => string.Equals(item.Id, libraryId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Could not find library '{libraryId}'.");

        var presentationRef = ResolvePresentationRef(presentationPath);
        UpsertPresentation(library.Presentations, presentationRef, insertIndex);
        library.UpdatedAt = DateTime.UtcNow.ToString("O");

        await _catalog.SaveAsync().ConfigureAwait(false);
        await _catalog.LoadAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task AddPresentationToPlaylistAsync(string playlistId, string presentationPath, int? insertIndex = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playlistId);
        ArgumentException.ThrowIfNullOrWhiteSpace(presentationPath);

        await _catalog.LoadAsync().ConfigureAwait(false);

        var playlist = _catalog.Catalog.Playlists.FirstOrDefault(item => string.Equals(item.Id, playlistId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Could not find playlist '{playlistId}'.");

        var presentationRef = ResolvePresentationRef(presentationPath);
        InsertPresentation(playlist.Items, presentationRef, insertIndex);
        playlist.UpdatedAt = DateTime.UtcNow.ToString("O");

        await _catalog.SaveAsync().ConfigureAwait(false);
        await _catalog.LoadAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MovePresentationToLibraryAsync(string sourceLibraryId, string targetLibraryId, string presentationPath, int? insertIndex = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceLibraryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetLibraryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(presentationPath);

        await _catalog.LoadAsync().ConfigureAwait(false);

        var source = _catalog.Catalog.Libraries.FirstOrDefault(item => string.Equals(item.Id, sourceLibraryId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Could not find library '{sourceLibraryId}'.");
        var target = _catalog.Catalog.Libraries.FirstOrDefault(item => string.Equals(item.Id, targetLibraryId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Could not find library '{targetLibraryId}'.");

        if (string.Equals(sourceLibraryId, targetLibraryId, StringComparison.OrdinalIgnoreCase))
        {
            MovePresentationInList(source.Presentations, presentationPath, insertIndex);
            source.UpdatedAt = DateTime.UtcNow.ToString("O");
            await _catalog.SaveAsync().ConfigureAwait(false);
            await _catalog.LoadAsync().ConfigureAwait(false);
            return;
        }

        var presentationRef = source.Presentations.FirstOrDefault(item => PathsEqual(item.Path, presentationPath)) is { } sourceRef
            ? ClonePresentationRef(sourceRef)
            : ResolvePresentationRef(presentationPath);
        UpsertPresentation(target.Presentations, presentationRef, insertIndex);
        source.Presentations.RemoveAll(item => PathsEqual(item.Path, presentationPath));
        source.UpdatedAt = DateTime.UtcNow.ToString("O");
        target.UpdatedAt = source.UpdatedAt;

        await _catalog.SaveAsync().ConfigureAwait(false);
        await _catalog.LoadAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MovePresentationToPlaylistAsync(
        string sourcePlaylistId,
        string targetPlaylistId,
        string presentationPath,
        int? insertIndex = null,
        int? sourcePlaylistIndex = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePlaylistId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPlaylistId);
        ArgumentException.ThrowIfNullOrWhiteSpace(presentationPath);

        await _catalog.LoadAsync().ConfigureAwait(false);

        var source = _catalog.Catalog.Playlists.FirstOrDefault(item => string.Equals(item.Id, sourcePlaylistId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Could not find playlist '{sourcePlaylistId}'.");
        var target = _catalog.Catalog.Playlists.FirstOrDefault(item => string.Equals(item.Id, targetPlaylistId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Could not find playlist '{targetPlaylistId}'.");

        if (string.Equals(sourcePlaylistId, targetPlaylistId, StringComparison.OrdinalIgnoreCase))
        {
            MovePresentationInList(source.Items, presentationPath, insertIndex, sourcePlaylistIndex);
            source.UpdatedAt = DateTime.UtcNow.ToString("O");
            await _catalog.SaveAsync().ConfigureAwait(false);
            await _catalog.LoadAsync().ConfigureAwait(false);
            return;
        }

        var presentationRef = RemoveFirstPresentation(source.Items, presentationPath, sourcePlaylistIndex)
            ?? ResolvePresentationRef(presentationPath);
        InsertPresentation(target.Items, presentationRef, insertIndex);
        source.UpdatedAt = DateTime.UtcNow.ToString("O");
        target.UpdatedAt = source.UpdatedAt;

        await _catalog.SaveAsync().ConfigureAwait(false);
        await _catalog.LoadAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PresentationItemMutationResult> DuplicatePresentationAsync(
        string sourcePresentationPath,
        string targetLibraryId,
        string? targetPlaylistId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePresentationPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetLibraryId);

        await _catalog.LoadAsync().ConfigureAwait(false);

        var targetLibrary = _catalog.Catalog.Libraries.FirstOrDefault(item => string.Equals(item.Id, targetLibraryId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Could not find library '{targetLibraryId}'.");
        var targetPlaylist = string.IsNullOrWhiteSpace(targetPlaylistId)
            ? null
            : _catalog.Catalog.Playlists.FirstOrDefault(item => string.Equals(item.Id, targetPlaylistId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Could not find playlist '{targetPlaylistId}'.");

        var sourceProject = _projects.Open(sourcePresentationPath);
        var duplicate = PresentationModelUtilities.CloneProject(sourceProject);
        duplicate.Manifest.PresentationId = Guid.NewGuid().ToString("N");
        duplicate.Manifest.Title = CreateDuplicateTitle(duplicate.Manifest.Title);
        duplicate.Manifest.CreatedAt = DateTime.UtcNow.ToString("O");
        duplicate.Manifest.UpdatedAt = duplicate.Manifest.CreatedAt;

        var destinationAbsolutePath = EnsureUniqueFilePath(
            _content.GeneratePresentationPath(duplicate.Manifest.Title, duplicate.Manifest.PresentationId),
            sourcePresentationPath);

        _projects.Save(duplicate, destinationAbsolutePath);

        var duplicateRef = CreatePresentationRef(destinationAbsolutePath);
        UpsertPresentation(targetLibrary.Presentations, duplicateRef);
        targetLibrary.UpdatedAt = DateTime.UtcNow.ToString("O");

        if (targetPlaylist != null)
        {
            targetPlaylist.Items.Add(ClonePresentationRef(duplicateRef));
            targetPlaylist.UpdatedAt = targetLibrary.UpdatedAt;
        }

        await _catalog.SaveAsync().ConfigureAwait(false);
        await _catalog.LoadAsync().ConfigureAwait(false);

        _logger.LogInformation(
            "Duplicated presentation {SourcePath} to {DestinationPath}.",
            sourcePresentationPath,
            destinationAbsolutePath);
        _contentChanges?.Publish(new ContentChangeEvent
        {
            Kind = ContentChangeKind.PresentationAdded,
            SubjectId = duplicateRef.Path,
            Source = nameof(PresentationItemActionService),
        });

        return new PresentationItemMutationResult
        {
            PresentationPath = duplicateRef.Path,
            Title = duplicateRef.Title,
            LibraryId = targetLibrary.Id,
            PlaylistId = targetPlaylist?.Id,
        };
    }

    /// <inheritdoc />
    public async Task<PresentationItemRenameResult> RenamePresentationAsync(string sourcePresentationPath, string newTitle, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePresentationPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(newTitle);

        await _catalog.LoadAsync().ConfigureAwait(false);

        var project = _projects.Open(sourcePresentationPath);
        var oldAbsolutePath = _content.ResolvePresentationPath(sourcePresentationPath);
        var oldRelativePath = _content.ToContentRelativePath(oldAbsolutePath);

        project.Manifest.Title = newTitle.Trim();
        project.Manifest.UpdatedAt = DateTime.UtcNow.ToString("O");
        if (string.IsNullOrWhiteSpace(project.Manifest.PresentationId))
            project.Manifest.PresentationId = Guid.NewGuid().ToString("N");

        var desiredAbsolutePath = _content.GeneratePresentationPath(project.Manifest.Title, project.Manifest.PresentationId);
        var destinationAbsolutePath = EnsureUniqueFilePath(desiredAbsolutePath, oldAbsolutePath);

        _projects.Save(project, destinationAbsolutePath);

        if (!PathsEqual(oldAbsolutePath, destinationAbsolutePath) && File.Exists(oldAbsolutePath))
            File.Delete(oldAbsolutePath);

        var newRelativePath = _content.ToContentRelativePath(destinationAbsolutePath);
        RemapCatalogPresentationPath(oldRelativePath, newRelativePath, project.Manifest.Title, project.Manifest.UpdatedAt);
        RemapWorkspacePresentationPath(oldRelativePath, newRelativePath);

        await _catalog.SaveAsync().ConfigureAwait(false);
        await _catalog.LoadAsync().ConfigureAwait(false);
        await _workspace.SaveAsync().ConfigureAwait(false);

        _logger.LogInformation(
            "Renamed presentation {OldPath} to {NewPath}.",
            oldRelativePath,
            newRelativePath);
        _contentChanges?.Publish(new ContentChangeEvent
        {
            Kind = PathsEqual(oldRelativePath, newRelativePath)
                ? ContentChangeKind.PresentationUpdated
                : ContentChangeKind.PresentationRenamed,
            SubjectId = newRelativePath,
            PreviousSubjectId = oldRelativePath,
            Source = nameof(PresentationItemActionService),
        });

        return new PresentationItemRenameResult
        {
            OldPresentationPath = oldRelativePath,
            NewPresentationPath = newRelativePath,
            Title = project.Manifest.Title,
        };
    }

    /// <inheritdoc />
    public async Task<PresentationItemDeleteResult> DeletePresentationAsync(string sourcePresentationPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePresentationPath);

        await _catalog.LoadAsync().ConfigureAwait(false);
        await _settings.LoadAsync().ConfigureAwait(false);

        var presentationRef = ResolvePresentationRef(sourcePresentationPath);
        var absolutePath = _content.ResolvePresentationPath(sourcePresentationPath);
        var relativePath = _content.ToContentRelativePath(absolutePath);
        var deletedBundleFile = DeleteBundleFile(absolutePath);

        var updatedAt = DateTime.UtcNow.ToString("O");

        foreach (var library in _catalog.Catalog.Libraries)
        {
            if (library.Presentations.RemoveAll(item => PathsEqual(item.Path, relativePath)) > 0)
                library.UpdatedAt = updatedAt;
        }

        foreach (var playlist in _catalog.Catalog.Playlists)
        {
            if (playlist.Items.RemoveAll(item => PathsEqual(item.Path, relativePath)) > 0)
                playlist.UpdatedAt = updatedAt;
        }

        if (PathsEqual(_workspace.Workspace.SelectedPresentationPath, relativePath))
            _workspace.Update(item => item.SelectedPresentationPath = null);

        _settings.Update(settings =>
        {
            settings.RecentFiles.RemoveAll(item => PathsEqual(item.Path, relativePath));
        });

        await _catalog.SaveAsync().ConfigureAwait(false);
        await _catalog.LoadAsync().ConfigureAwait(false);
        await _workspace.SaveAsync().ConfigureAwait(false);
        await _settings.SaveAsync().ConfigureAwait(false);

        _logger.LogInformation(
            "Deleted presentation {PresentationPath}; bundle file removed: {DeletedBundleFile}.",
            relativePath,
            deletedBundleFile);
        _contentChanges?.Publish(new ContentChangeEvent
        {
            Kind = ContentChangeKind.PresentationDeleted,
            SubjectId = relativePath,
            Source = nameof(PresentationItemActionService),
        });

        return new PresentationItemDeleteResult
        {
            PresentationPath = relativePath,
            Title = presentationRef.Title,
            DeletedBundleFile = deletedBundleFile,
        };
    }

    private bool DeleteBundleFile(string absolutePath)
    {
        var result = _contentStore.TryDeleteFileDetailed(absolutePath);
        if (result.Succeeded)
            return result.Value == true;

        if (result.Failure?.Kind == ContentAccessFailureKind.Missing)
            return false;

        throw new IOException(result.Failure?.Message ?? $"Could not delete presentation bundle '{absolutePath}'.");
    }

    /// <inheritdoc />
    public async Task ExportPresentationBundleAsync(string sourcePresentationPath, string destinationPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePresentationPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var sourceAbsolutePath = _content.ResolvePresentationPath(sourcePresentationPath);
        if (!File.Exists(sourceAbsolutePath))
            throw new FileNotFoundException("Could not find presentation bundle to export.", sourceAbsolutePath);

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await using var source = File.Open(sourceAbsolutePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var destination = File.Create(destinationPath);
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetPresentationReferenceArrangementAsync(
        string? libraryId,
        string? playlistId,
        string presentationPath,
        string? arrangementId,
        int? playlistIndex = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presentationPath);
        cancellationToken.ThrowIfCancellationRequested();

        await _catalog.LoadAsync().ConfigureAwait(false);

        var reference = ResolveScopedPresentationRef(libraryId, playlistId, presentationPath, playlistIndex)
            ?? throw new InvalidOperationException("Could not find the presentation reference to update.");

        reference.ArrangementId = string.IsNullOrWhiteSpace(arrangementId) ? null : arrangementId.Trim();
        TouchScopedCollection(libraryId, playlistId);

        await _catalog.SaveAsync().ConfigureAwait(false);
        await _catalog.LoadAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetPresentationReferenceDestinationAsync(
        string? libraryId,
        string? playlistId,
        string presentationPath,
        string? destinationLayerId,
        int? playlistIndex = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presentationPath);
        cancellationToken.ThrowIfCancellationRequested();

        await _catalog.LoadAsync().ConfigureAwait(false);

        var reference = ResolveScopedPresentationRef(libraryId, playlistId, presentationPath, playlistIndex)
            ?? throw new InvalidOperationException("Could not find the presentation reference to update.");

        reference.DestinationLayerId = string.IsNullOrWhiteSpace(destinationLayerId) ? null : destinationLayerId.Trim();
        TouchScopedCollection(libraryId, playlistId);

        await _catalog.SaveAsync().ConfigureAwait(false);
        await _catalog.LoadAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ResizePresentationAsync(string sourcePresentationPath, SlideSizeDto slideSize, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePresentationPath);
        ArgumentNullException.ThrowIfNull(slideSize);
        cancellationToken.ThrowIfCancellationRequested();

        if (slideSize.Width <= 0 || slideSize.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(slideSize), "Slide size must be greater than zero.");

        var project = _projects.Open(sourcePresentationPath);
        project.Manifest.SlideSize = new SlideSizeDto
        {
            Width = slideSize.Width,
            Height = slideSize.Height,
        };
        project.Manifest.AspectRatio = FormatAspectRatio(slideSize.Width, slideSize.Height);
        project.Manifest.UpdatedAt = DateTime.UtcNow.ToString("O");

        foreach (var slide in project.Slides)
            PresentationModelUtilities.NormalizeSlide(slide, project.Manifest.SlideSize);

        PresentationModelUtilities.ReconcileArrangement(project);
        _projects.Save(project, sourcePresentationPath);

        await _catalog.LoadAsync().ConfigureAwait(false);
        UpdateCatalogPresentationTimestamp(sourcePresentationPath, project.Manifest.UpdatedAt);
        await _catalog.SaveAsync().ConfigureAwait(false);
        await _catalog.LoadAsync().ConfigureAwait(false);

        _contentChanges?.Publish(new ContentChangeEvent
        {
            Kind = ContentChangeKind.PresentationUpdated,
            SubjectId = _content.ToContentRelativePath(_content.ResolvePresentationPath(sourcePresentationPath)),
            Source = nameof(PresentationItemActionService),
        });
    }

    private PresentationRefDto ResolvePresentationRef(string presentationPath)
    {
        foreach (var library in _catalog.Catalog.Libraries)
        {
            var existing = library.Presentations.FirstOrDefault(item => PathsEqual(item.Path, presentationPath));
            if (existing != null)
                return ClonePresentationRef(existing);
        }

        foreach (var playlist in _catalog.Catalog.Playlists)
        {
            var existing = playlist.Items.FirstOrDefault(item => PathsEqual(item.Path, presentationPath));
            if (existing != null)
                return ClonePresentationRef(existing);
        }

        return CreatePresentationRef(_content.ResolvePresentationPath(presentationPath));
    }

    private PresentationRefDto CreatePresentationRef(string absolutePresentationPath)
    {
        var document = _documents.Open(absolutePresentationPath);
        var resolvedPath = _content.ResolvePresentationPath(document.SourcePath);
        return new PresentationRefDto
        {
            Path = _content.ToContentRelativePath(resolvedPath),
            Title = string.IsNullOrWhiteSpace(document.Manifest.Title)
                ? Path.GetFileNameWithoutExtension(resolvedPath)
                : document.Manifest.Title,
            UpdatedAt = string.IsNullOrWhiteSpace(document.Manifest.UpdatedAt)
                ? File.GetLastWriteTimeUtc(resolvedPath).ToString("O")
                : document.Manifest.UpdatedAt,
        };
    }

    private void RemapCatalogPresentationPath(string oldPath, string newPath, string title, string? updatedAt)
    {
        foreach (var library in _catalog.Catalog.Libraries)
        {
            var changed = false;
            foreach (var item in library.Presentations.Where(item => PathsEqual(item.Path, oldPath)))
            {
                item.Path = newPath;
                item.Title = title;
                item.UpdatedAt = updatedAt ?? string.Empty;
                changed = true;
            }

            if (changed)
                library.UpdatedAt = DateTime.UtcNow.ToString("O");
        }

        foreach (var playlist in _catalog.Catalog.Playlists)
        {
            var changed = false;
            foreach (var item in playlist.Items.Where(item => PathsEqual(item.Path, oldPath)))
            {
                item.Path = newPath;
                item.Title = title;
                item.UpdatedAt = updatedAt ?? string.Empty;
                changed = true;
            }

            if (changed)
                playlist.UpdatedAt = DateTime.UtcNow.ToString("O");
        }
    }

    private void RemapWorkspacePresentationPath(string oldPath, string newPath)
    {
        if (!PathsEqual(_workspace.Workspace.SelectedPresentationPath, oldPath))
            return;

        _workspace.Update(workspace => workspace.SelectedPresentationPath = newPath);
    }

    private PresentationRefDto? ResolveScopedPresentationRef(string? libraryId, string? playlistId, string presentationPath, int? playlistIndex = null)
    {
        if (!string.IsNullOrWhiteSpace(playlistId))
        {
            var playlist = _catalog.Catalog.Playlists
                .FirstOrDefault(playlist => string.Equals(playlist.Id, playlistId, StringComparison.OrdinalIgnoreCase))
                ?.Items;
            if (playlist == null)
                return null;

            return ResolvePresentationAtIndexOrFirst(playlist, presentationPath, playlistIndex);
        }

        if (!string.IsNullOrWhiteSpace(libraryId))
        {
            return _catalog.Catalog.Libraries
                .FirstOrDefault(library => string.Equals(library.Id, libraryId, StringComparison.OrdinalIgnoreCase))
                ?.Presentations.FirstOrDefault(item => PathsEqual(item.Path, presentationPath));
        }

        return null;
    }

    private void TouchScopedCollection(string? libraryId, string? playlistId)
    {
        var updatedAt = DateTime.UtcNow.ToString("O");
        if (!string.IsNullOrWhiteSpace(playlistId))
        {
            var playlist = _catalog.Catalog.Playlists.FirstOrDefault(item =>
                string.Equals(item.Id, playlistId, StringComparison.OrdinalIgnoreCase));
            if (playlist != null)
                playlist.UpdatedAt = updatedAt;
            return;
        }

        if (!string.IsNullOrWhiteSpace(libraryId))
        {
            var library = _catalog.Catalog.Libraries.FirstOrDefault(item =>
                string.Equals(item.Id, libraryId, StringComparison.OrdinalIgnoreCase));
            if (library != null)
                library.UpdatedAt = updatedAt;
        }
    }

    private void UpdateCatalogPresentationTimestamp(string presentationPath, string? updatedAt)
    {
        var resolvedPath = _content.ToContentRelativePath(_content.ResolvePresentationPath(presentationPath));
        var timestamp = string.IsNullOrWhiteSpace(updatedAt) ? DateTime.UtcNow.ToString("O") : updatedAt;

        foreach (var library in _catalog.Catalog.Libraries)
        {
            var changed = false;
            foreach (var item in library.Presentations.Where(item => PathsEqual(item.Path, resolvedPath)))
            {
                item.UpdatedAt = timestamp;
                changed = true;
            }

            if (changed)
                library.UpdatedAt = timestamp;
        }

        foreach (var playlist in _catalog.Catalog.Playlists)
        {
            var changed = false;
            foreach (var item in playlist.Items.Where(item => PathsEqual(item.Path, resolvedPath)))
            {
                item.UpdatedAt = timestamp;
                changed = true;
            }

            if (changed)
                playlist.UpdatedAt = timestamp;
        }
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

    private static void UpsertPresentation(List<PresentationRefDto> collection, PresentationRefDto presentation, int? insertIndex = null)
    {
        var index = collection.FindIndex(item => PathsEqual(item.Path, presentation.Path));
        if (index >= 0)
            collection.RemoveAt(index);

        InsertPresentation(collection, presentation, AdjustInsertIndexAfterRemoval(insertIndex, index));
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

    private static PresentationRefDto? RemoveFirstPresentation(List<PresentationRefDto> collection, string presentationPath, int? playlistIndex = null)
    {
        var index = ResolvePresentationIndex(collection, presentationPath, playlistIndex);
        if (index < 0)
            return null;

        var item = collection[index];
        collection.RemoveAt(index);
        return ClonePresentationRef(item);
    }

    private static void MovePresentationInList(List<PresentationRefDto> collection, string presentationPath, int? insertIndex, int? playlistIndex = null)
    {
        if (insertIndex is not { } targetIndex)
            return;

        var sourceIndex = ResolvePresentationIndex(collection, presentationPath, playlistIndex);
        if (sourceIndex < 0)
            throw new InvalidOperationException($"Could not find presentation '{presentationPath}'.");

        var item = ClonePresentationRef(collection[sourceIndex]);
        collection.RemoveAt(sourceIndex);
        InsertPresentation(collection, item, AdjustInsertIndexAfterRemoval(targetIndex, sourceIndex));
    }

    private static int? AdjustInsertIndexAfterRemoval(int? insertIndex, int removedIndex)
    {
        if (insertIndex is not { } index || removedIndex < 0)
            return insertIndex;

        return removedIndex < index ? index - 1 : index;
    }

    private static PresentationRefDto? ResolvePresentationAtIndexOrFirst(List<PresentationRefDto> collection, string presentationPath, int? playlistIndex)
    {
        var index = ResolvePresentationIndex(collection, presentationPath, playlistIndex);
        return index < 0 ? null : collection[index];
    }

    private static int ResolvePresentationIndex(List<PresentationRefDto> collection, string presentationPath, int? playlistIndex)
    {
        if (playlistIndex is { } index
            && index >= 0
            && index < collection.Count
            && PathsEqual(collection[index].Path, presentationPath))
        {
            return index;
        }

        return collection.FindIndex(item => PathsEqual(item.Path, presentationPath));
    }

    private static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        return string.Equals(
            left.Replace('\\', '/').Trim(),
            right.Replace('\\', '/').Trim(),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureUniqueFilePath(string desiredPath, string sourcePath)
    {
        var fullDesiredPath = Path.GetFullPath(desiredPath);
        var fullSourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullDesiredPath) || string.Equals(fullDesiredPath, fullSourcePath, StringComparison.OrdinalIgnoreCase))
            return fullDesiredPath;

        var directory = Path.GetDirectoryName(fullDesiredPath)!;
        var name = Path.GetFileNameWithoutExtension(fullDesiredPath);
        var extension = Path.GetExtension(fullDesiredPath);

        var counter = 2;
        string candidate;
        do
        {
            candidate = Path.Combine(directory, $"{name}_{counter}{extension}");
            counter++;
        } while (File.Exists(candidate));

        return candidate;
    }

    private static string CreateDuplicateTitle(string title)
    {
        var trimmed = title.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? "Untitled Copy" : $"{trimmed} Copy";
    }

    private static string FormatAspectRatio(int width, int height)
    {
        var divisor = GreatestCommonDivisor(width, height);
        return $"{width / divisor}:{height / divisor}";
    }

    private static int GreatestCommonDivisor(int left, int right)
    {
        left = Math.Abs(left);
        right = Math.Abs(right);
        while (right != 0)
        {
            var remainder = left % right;
            left = right;
            right = remainder;
        }

        return Math.Max(1, left);
    }
}