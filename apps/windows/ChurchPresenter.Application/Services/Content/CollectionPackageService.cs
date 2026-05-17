using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;


using Microsoft.Extensions.Logging;

namespace ChurchPresenter.Services.Content;

/// <summary>
/// Exports and imports portable library and playlist package archives.
/// </summary>
public sealed class CollectionPackageService(
    IContentDirectoryService paths,
    ICatalogService catalog,
    IPresentationDocumentService presentations,
    ILogger<CollectionPackageService> logger,
    IContentStore? contentStore = null,
    IContentChangeBus? contentChanges = null) : ICollectionPackageService
{
    private const string PackageFormatVersion = "1.0.0";
    private const string LibraryPackageType = "library";
    private const string PlaylistPackageType = "playlist";

    private readonly IContentDirectoryService _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly ICatalogService _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    private readonly IPresentationDocumentService _presentations = presentations ?? throw new ArgumentNullException(nameof(presentations));
    private readonly ILogger<CollectionPackageService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IContentStore _contentStore = contentStore ?? ContentStoreDefaults.Instance;
    private readonly IContentChangeBus? _contentChanges = contentChanges;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <inheritdoc />
    public async Task ExportLibraryAsync(string libraryId, string destinationPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        await _catalog.LoadAsync().ConfigureAwait(false);
        var library = _catalog.Catalog.Libraries.FirstOrDefault(candidate =>
                          string.Equals(candidate.Id, libraryId, StringComparison.OrdinalIgnoreCase))
                      ?? throw new InvalidOperationException($"Could not find library '{libraryId}'.");

        var packagedRefs = BuildPackagedRefs(library.Presentations);
        var packagedLibrary = CloneLibrary(library, packagedRefs.PackagedRefs);
        var manifest = new CollectionPackageManifest
        {
            FormatVersion = PackageFormatVersion,
            PackageType = LibraryPackageType,
            Name = library.Name,
            ExportedAt = DateTime.UtcNow.ToString("O"),
        };

        await WritePackageAsync(
            destinationPath,
            manifest,
            "library.json",
            JsonSerializer.Serialize(packagedLibrary, JsonOptions),
            packagedRefs.BundleEntries,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ExportPlaylistAsync(string playlistId, string destinationPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playlistId);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        await _catalog.LoadAsync().ConfigureAwait(false);
        var playlist = _catalog.Catalog.Playlists.FirstOrDefault(candidate =>
                           string.Equals(candidate.Id, playlistId, StringComparison.OrdinalIgnoreCase))
                       ?? throw new InvalidOperationException($"Could not find playlist '{playlistId}'.");

        var packagedRefs = BuildPackagedRefs(playlist.Items);
        var packagedPlaylist = ClonePlaylist(playlist, packagedRefs.PackagedRefs);
        var manifest = new CollectionPackageManifest
        {
            FormatVersion = PackageFormatVersion,
            PackageType = PlaylistPackageType,
            Name = playlist.Name,
            ExportedAt = DateTime.UtcNow.ToString("O"),
        };

        await WritePackageAsync(
            destinationPath,
            manifest,
            "playlist.json",
            JsonSerializer.Serialize(packagedPlaylist, JsonOptions),
            packagedRefs.BundleEntries,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<CollectionPackagePreview> PreviewLibraryImportAsync(string packagePath, CancellationToken cancellationToken = default) =>
        PreviewImportAsync(packagePath, LibraryPackageType, "library.json", cancellationToken);

    /// <inheritdoc />
    public Task<CollectionPackagePreview> PreviewPlaylistImportAsync(string packagePath, CancellationToken cancellationToken = default) =>
        PreviewImportAsync(packagePath, PlaylistPackageType, "playlist.json", cancellationToken);

    /// <inheritdoc />
    public Task<ImportedLibraryPackageResult> ImportLibraryAsync(string packagePath, CancellationToken cancellationToken = default) =>
        ImportLibraryAsync(packagePath, new CollectionPackageImportOptions(), cancellationToken);

    /// <inheritdoc />
    public async Task<ImportedLibraryPackageResult> ImportLibraryAsync(
        string packagePath,
        CollectionPackageImportOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentNullException.ThrowIfNull(options);
        var preview = await PreviewLibraryImportAsync(packagePath, cancellationToken).ConfigureAwait(false);
        await EnsureConfirmedAsync(preview, options).ConfigureAwait(false);
        await _paths.EnsureDocumentsLayoutAsync().ConfigureAwait(false);
        EnsurePreviewStillCurrent(preview, await PreviewLibraryImportAsync(packagePath, cancellationToken).ConfigureAwait(false));

        using var archive = ZipFile.OpenRead(packagePath);
        var manifest = ReadRequiredEntry<CollectionPackageManifest>(archive, "manifest.json");
        ValidateManifest(manifest, LibraryPackageType);
        var library = ReadRequiredEntry<LibraryDto>(archive, "library.json");
        library.Presentations ??= new List<PresentationRefDto>();

        var importedRefs = await ImportPackagePresentationsAsync(archive, library.Presentations, options, cancellationToken).ConfigureAwait(false);
        await _catalog.LoadAsync().ConfigureAwait(false);

        var now = DateTime.UtcNow.ToString("O");
        var importedLibrary = CloneLibrary(library, importedRefs.RemappedRefs);
        importedLibrary.Id = CreateUniqueCatalogId(_catalog.Catalog.Libraries.Select(candidate => candidate.Id), library.Id);
        importedLibrary.CreatedAt ??= now;
        importedLibrary.UpdatedAt = now;

        _catalog.Catalog.Libraries.Add(importedLibrary);
        await _catalog.SaveAsync().ConfigureAwait(false);
        await _catalog.LoadAsync().ConfigureAwait(false);
        _contentChanges?.Publish(new ContentChangeEvent
        {
            Kind = ContentChangeKind.PackageImportCompleted,
            SubjectId = packagePath,
            Stamp = preview.PackageStamp,
            Source = nameof(CollectionPackageService),
        });

        return new ImportedLibraryPackageResult
        {
            LibraryId = importedLibrary.Id,
            ImportedPresentationPaths = importedRefs.UniqueImportedPaths,
        };
    }

    /// <inheritdoc />
    public Task<ImportedPlaylistPackageResult> ImportPlaylistAsync(string packagePath, CancellationToken cancellationToken = default) =>
        ImportPlaylistAsync(packagePath, new CollectionPackageImportOptions(), cancellationToken);

    /// <inheritdoc />
    public async Task<ImportedPlaylistPackageResult> ImportPlaylistAsync(
        string packagePath,
        CollectionPackageImportOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentNullException.ThrowIfNull(options);
        var preview = await PreviewPlaylistImportAsync(packagePath, cancellationToken).ConfigureAwait(false);
        await EnsureConfirmedAsync(preview, options).ConfigureAwait(false);
        await _paths.EnsureDocumentsLayoutAsync().ConfigureAwait(false);
        EnsurePreviewStillCurrent(preview, await PreviewPlaylistImportAsync(packagePath, cancellationToken).ConfigureAwait(false));

        using var archive = ZipFile.OpenRead(packagePath);
        var manifest = ReadRequiredEntry<CollectionPackageManifest>(archive, "manifest.json");
        ValidateManifest(manifest, PlaylistPackageType);
        var playlist = ReadRequiredEntry<PlaylistDto>(archive, "playlist.json");
        playlist.Items ??= new List<PresentationRefDto>();

        var importedRefs = await ImportPackagePresentationsAsync(archive, playlist.Items, options, cancellationToken).ConfigureAwait(false);
        await _catalog.LoadAsync().ConfigureAwait(false);

        var now = DateTime.UtcNow.ToString("O");
        var importedPlaylist = ClonePlaylist(playlist, importedRefs.RemappedRefs);
        importedPlaylist.Id = CreateUniqueCatalogId(_catalog.Catalog.Playlists.Select(candidate => candidate.Id), playlist.Id);
        importedPlaylist.CreatedAt ??= now;
        importedPlaylist.UpdatedAt = now;

        _catalog.Catalog.Playlists.Add(importedPlaylist);
        await _catalog.SaveAsync().ConfigureAwait(false);
        await _catalog.LoadAsync().ConfigureAwait(false);
        _contentChanges?.Publish(new ContentChangeEvent
        {
            Kind = ContentChangeKind.PackageImportCompleted,
            SubjectId = packagePath,
            Stamp = preview.PackageStamp,
            Source = nameof(CollectionPackageService),
        });

        return new ImportedPlaylistPackageResult
        {
            PlaylistId = importedPlaylist.Id,
            ImportedPresentationPaths = importedRefs.UniqueImportedPaths,
        };
    }

    private PackagedPresentationSet BuildPackagedRefs(IEnumerable<PresentationRefDto> refs)
    {
        var packagePathsBySource = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var bundledEntries = new List<PackagedPresentationEntry>();
        var packagedRefs = new List<PresentationRefDto>();
        var usedPackagePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var presentation in refs)
        {
            if (string.IsNullOrWhiteSpace(presentation.Path))
                continue;

            var absolutePath = _paths.ResolvePresentationPath(presentation.Path);
            if (!File.Exists(absolutePath))
                throw new FileNotFoundException($"Could not find presentation bundle '{presentation.Path}' to package.", absolutePath);

            if (!packagePathsBySource.TryGetValue(absolutePath, out var packagePath))
            {
                packagePath = EnsureUniquePackagePath(BuildPackagePresentationPath(presentation.Path), usedPackagePaths);
                packagePathsBySource[absolutePath] = packagePath;
                bundledEntries.Add(new PackagedPresentationEntry(absolutePath, packagePath));
            }

            packagedRefs.Add(new PresentationRefDto
            {
                Path = packagePath,
                Title = presentation.Title,
                UpdatedAt = presentation.UpdatedAt,
                ThumbnailData = presentation.ThumbnailData,
            });
        }

        return new PackagedPresentationSet(packagedRefs, bundledEntries);
    }

    private async Task WritePackageAsync(
        string destinationPath,
        CollectionPackageManifest manifest,
        string metadataEntryPath,
        string metadataJson,
        IReadOnlyList<PackagedPresentationEntry> bundleEntries,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        using var file = new FileStream(fullPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: false);

        WriteStringEntry(archive, "manifest.json", JsonSerializer.Serialize(manifest, JsonOptions));
        WriteStringEntry(archive, metadataEntryPath, metadataJson);

        foreach (var entry in bundleEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var source = File.OpenRead(entry.SourcePath);
            var archiveEntry = archive.CreateEntry(entry.PackagePath, CompressionLevel.Optimal);
            await using var archiveStream = archiveEntry.Open();
            await source.CopyToAsync(archiveStream, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<CollectionPackagePreview> PreviewImportAsync(
        string packagePath,
        string expectedPackageType,
        string metadataEntryPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        using var archive = ZipFile.OpenRead(packagePath);
        var manifest = ReadRequiredEntry<CollectionPackageManifest>(archive, "manifest.json");
        ValidateManifest(manifest, expectedPackageType);
        IReadOnlyList<PresentationRefDto> refs = expectedPackageType == LibraryPackageType
            ? ReadRequiredEntry<LibraryDto>(archive, metadataEntryPath).Presentations ?? []
            : ReadRequiredEntry<PlaylistDto>(archive, metadataEntryPath).Items ?? [];

        var changes = new List<CollectionPackagePreviewChange>
        {
            new()
            {
                Kind = SupportPackageChangeKind.Add,
                Path = metadataEntryPath,
                Message = $"Will add a new {expectedPackageType} entry to the local catalog.",
            },
        };
        var copyRequirements = new List<PackageCopyRequirement>();
        var seenPackagePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var presentation in refs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(presentation.Path))
                continue;

            var packagePresentationPath = BuildPackagePresentationPath(presentation.Path);
            if (!seenPackagePaths.Add(packagePresentationPath))
                continue;

            var entry = GetArchiveEntry(archive, packagePresentationPath)
                        ?? throw new InvalidDataException($"Package is missing presentation entry '{packagePresentationPath}'.");
            var preview = await PreviewPresentationEntryAsync(entry, packagePresentationPath, cancellationToken).ConfigureAwait(false);
            changes.Add(preview.Change);
            copyRequirements.AddRange(preview.CopyRequirements);
        }

        return new CollectionPackagePreview
        {
            PackageType = manifest.PackageType,
            Name = manifest.Name,
            Changes = changes,
            CopyRequirements = copyRequirements,
            PackageStamp = _contentStore.GetStamp(packagePath, includeHash: true).Value,
        };
    }

    private async Task<(CollectionPackagePreviewChange Change, IReadOnlyList<PackageCopyRequirement> CopyRequirements)> PreviewPresentationEntryAsync(
        ZipArchiveEntry archiveEntry,
        string packagePresentationPath,
        CancellationToken cancellationToken)
    {
        var tempPath = await ExtractArchiveEntryToTempAsync(archiveEntry, cancellationToken).ConfigureAwait(false);
        try
        {
            var document = _presentations.Open(tempPath);
            var preferredDestination = ResolvePreferredPresentationPath(packagePresentationPath);
            var relativeDestination = _paths.ToContentRelativePath(preferredDestination);
            var copyRequirements = new List<PackageCopyRequirement>
            {
                new()
                {
                    Kind = PackageCopyRequirementKind.CopyPresentationBundle,
                    SourcePath = packagePresentationPath,
                    DestinationPath = relativeDestination,
                    ByteSize = archiveEntry.Length,
                    Message = "Presentation bundle will be copied from the package.",
                },
            };

            foreach (var media in document.Manifest.Media.Where(static media => !string.IsNullOrWhiteSpace(media.Path)))
            {
                copyRequirements.Add(new PackageCopyRequirement
                {
                    Kind = PackageCopyRequirementKind.CopyEmbeddedMediaPayload,
                    SourcePath = media.Path,
                    DestinationPath = $"{relativeDestination}::{media.Path}",
                    Message = "Presentation bundle carries an embedded media payload.",
                });
            }

            if (!File.Exists(preferredDestination))
            {
                return (CreateCollectionChange(
                        SupportPackageChangeKind.Add,
                        relativeDestination,
                        "Will add presentation bundle.",
                        isDestructive: false,
                        requiresConfirmation: false,
                        copyRequirements),
                    copyRequirements);
            }

            if (FilesMatch(preferredDestination, tempPath))
            {
                return (CreateCollectionChange(
                        SupportPackageChangeKind.Unchanged,
                        relativeDestination,
                        "Presentation bundle already matches package.",
                        isDestructive: false,
                        requiresConfirmation: false,
                        copyRequirements),
                    copyRequirements);
            }

            return (CreateCollectionChange(
                    SupportPackageChangeKind.Conflict,
                    relativeDestination,
                    "A different presentation bundle already exists at this path; import will copy side-by-side unless destructive replace is enabled.",
                    isDestructive: false,
                    requiresConfirmation: true,
                    copyRequirements),
                copyRequirements);
        }
        finally
        {
            TryDeleteTempFile(tempPath);
        }
    }

    private static Task EnsureConfirmedAsync(CollectionPackagePreview preview, CollectionPackageImportOptions options)
    {
        if (preview.HasDestructiveChanges && options.AllowDestructiveReplace != true)
            throw new InvalidOperationException("Collection package import would replace local files. Preview the changes and enable destructive replace to continue.");

        if (preview.RequiresConfirmation && options.ReplaceConflictingPresentationBundles && options.AllowDestructiveReplace != true)
            throw new InvalidOperationException("Replacing conflicting presentation bundles requires destructive replace confirmation.");

        return Task.CompletedTask;
    }

    private async Task<ImportedPresentationSet> ImportPackagePresentationsAsync(
        ZipArchive archive,
        IReadOnlyList<PresentationRefDto> refs,
        CollectionPackageImportOptions options,
        CancellationToken cancellationToken)
    {
        var importedRefByPackagePath = new Dictionary<string, PresentationRefDto>(StringComparer.OrdinalIgnoreCase);
        var uniqueImportedPaths = new List<string>();
        var seenImportedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var remappedRefs = new List<PresentationRefDto>();

        foreach (var presentation in refs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(presentation.Path))
                continue;

            var packagePath = BuildPackagePresentationPath(presentation.Path);
            if (!importedRefByPackagePath.TryGetValue(packagePath, out var importedRef))
            {
                var entry = GetArchiveEntry(archive, packagePath)
                            ?? throw new InvalidDataException($"Package is missing presentation entry '{packagePath}'.");
                importedRef = await ImportArchivePresentationAsync(entry, packagePath, presentation, options, cancellationToken).ConfigureAwait(false);
                importedRefByPackagePath[packagePath] = importedRef;
                if (seenImportedPaths.Add(importedRef.Path))
                    uniqueImportedPaths.Add(importedRef.Path);
            }

            remappedRefs.Add(new PresentationRefDto
            {
                Path = importedRef.Path,
                Title = importedRef.Title,
                UpdatedAt = importedRef.UpdatedAt,
                ThumbnailData = presentation.ThumbnailData,
            });
        }

        return new ImportedPresentationSet(remappedRefs, uniqueImportedPaths);
    }

    private async Task<PresentationRefDto> ImportArchivePresentationAsync(
        ZipArchiveEntry archiveEntry,
        string packagePresentationPath,
        PresentationRefDto sourceRef,
        CollectionPackageImportOptions options,
        CancellationToken cancellationToken)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.cpres");
        try
        {
            await using (var entryStream = archiveEntry.Open())
            await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await entryStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
            }

            var document = _presentations.Open(tempPath);
            var destinationPath = ResolveImportDestinationPath(tempPath, packagePresentationPath, document, options);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

            if (!File.Exists(destinationPath) || options.ReplaceConflictingPresentationBundles)
                File.Copy(tempPath, destinationPath, overwrite: options.ReplaceConflictingPresentationBundles);

            return new PresentationRefDto
            {
                Path = _paths.ToContentRelativePath(destinationPath),
                Title = string.IsNullOrWhiteSpace(document.Manifest.Title) ? sourceRef.Title : document.Manifest.Title,
                UpdatedAt = string.IsNullOrWhiteSpace(document.Manifest.UpdatedAt)
                    ? sourceRef.UpdatedAt
                    : document.Manifest.UpdatedAt,
                ThumbnailData = sourceRef.ThumbnailData,
            };
        }
        finally
        {
            TryDeleteTempFile(tempPath);
        }
    }

    private string ResolveImportDestinationPath(
        string tempPath,
        string packagePresentationPath,
        PresentationDocument document,
        CollectionPackageImportOptions options)
    {
        var preferredAbsolutePath = ResolvePreferredPresentationPath(packagePresentationPath);
        if (options.ReplaceConflictingPresentationBundles)
            return preferredAbsolutePath;

        if (!File.Exists(preferredAbsolutePath) || FilesMatch(preferredAbsolutePath, tempPath))
            return preferredAbsolutePath;

        var presentationId = string.IsNullOrWhiteSpace(document.Manifest.PresentationId)
            ? Guid.NewGuid().ToString()
            : document.Manifest.PresentationId;
        var generated = _paths.GeneratePresentationPath(document.Manifest.Title, presentationId);
        if (!File.Exists(generated) || FilesMatch(generated, tempPath))
            return generated;

        var directory = Path.GetDirectoryName(generated)!;
        var fileName = Path.GetFileNameWithoutExtension(generated);
        var extension = Path.GetExtension(generated);
        var suffix = 2;
        while (true)
        {
            var candidate = Path.Combine(directory, $"{fileName}_{suffix}{extension}");
            if (!File.Exists(candidate) || FilesMatch(candidate, tempPath))
                return candidate;
            suffix++;
        }
    }

    private string ResolvePreferredPresentationPath(string packagePresentationPath)
    {
        var preferredRelativePath = BuildPackagePresentationPath(packagePresentationPath);
        return Path.Combine(
            _paths.GetDocumentsDataDirectory(),
            preferredRelativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private async Task<string> ExtractArchiveEntryToTempAsync(ZipArchiveEntry archiveEntry, CancellationToken cancellationToken)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.cpres");
        await using var entryStream = archiveEntry.Open();
        await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await entryStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
        return tempPath;
    }

    private void TryDeleteTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not delete temporary package file {Path}.", tempPath);
        }
    }

    private CollectionPackagePreviewChange CreateCollectionChange(
        SupportPackageChangeKind kind,
        string path,
        string message,
        bool isDestructive,
        bool requiresConfirmation,
        IReadOnlyList<PackageCopyRequirement> copyRequirements) =>
        new()
        {
            Kind = kind,
            Path = path,
            Message = message,
            IsDestructive = isDestructive,
            RequiresConfirmation = requiresConfirmation,
            CopyRequirements = copyRequirements,
            DestinationStamp = _contentStore.GetStamp(_paths.ResolvePresentationPath(path)).Value,
        };

    private static void EnsurePreviewStillCurrent(CollectionPackagePreview expected, CollectionPackagePreview actual)
    {
        var expectedSignature = BuildPreviewSignature(expected.Changes);
        var actualSignature = BuildPreviewSignature(actual.Changes);
        if (!string.Equals(expectedSignature, actualSignature, StringComparison.Ordinal))
            throw new InvalidOperationException("Collection package import preview is stale. Reopen the preview and try again.");
    }

    private static string BuildPreviewSignature(IEnumerable<CollectionPackagePreviewChange> changes) =>
        string.Join(
            "|",
            changes
                .OrderBy(static change => change.Path, StringComparer.OrdinalIgnoreCase)
                .Select(static change => $"{change.Kind}:{change.Path}:{change.DestinationStamp?.LastWriteTimeUtc?.Ticks}:{change.DestinationStamp?.Length}:{change.DestinationStamp?.Sha256}"));

    private static bool FilesMatch(string leftPath, string rightPath)
    {
        var leftInfo = new FileInfo(leftPath);
        var rightInfo = new FileInfo(rightPath);
        if (!leftInfo.Exists || !rightInfo.Exists || leftInfo.Length != rightInfo.Length)
            return false;

        using var left = File.OpenRead(leftPath);
        using var right = File.OpenRead(rightPath);
        while (true)
        {
            var leftByte = left.ReadByte();
            var rightByte = right.ReadByte();
            if (leftByte != rightByte)
                return false;
            if (leftByte < 0)
                return true;
        }
    }

    private static LibraryDto CloneLibrary(LibraryDto library, IReadOnlyList<PresentationRefDto> presentations) =>
        new()
        {
            Id = library.Id,
            Name = library.Name,
            Description = library.Description,
            CreatedAt = library.CreatedAt,
            UpdatedAt = library.UpdatedAt,
            DefaultFolder = library.DefaultFolder,
            Presentations = presentations.Select(ClonePresentationRef).ToList(),
            ExtensionData = CloneExtensionData(library.ExtensionData),
        };

    private static PlaylistDto ClonePlaylist(PlaylistDto playlist, IReadOnlyList<PresentationRefDto> items) =>
        new()
        {
            Id = playlist.Id,
            Name = playlist.Name,
            Description = playlist.Description,
            CreatedAt = playlist.CreatedAt,
            UpdatedAt = playlist.UpdatedAt,
            ExternalSet = playlist.ExternalSet == null
                ? null
                : new ExternalSetLinkDto
                {
                    SetId = playlist.ExternalSet.SetId,
                    GroupId = playlist.ExternalSet.GroupId,
                    SyncedAt = playlist.ExternalSet.SyncedAt,
                    ServiceDate = playlist.ExternalSet.ServiceDate,
                    RemoteVersion = playlist.ExternalSet.RemoteVersion,
                    ExtensionData = CloneExtensionData(playlist.ExternalSet.ExtensionData),
                },
            Sync = playlist.Sync == null
                ? null
                : new SyncMetadata
                {
                    Status = playlist.Sync.Status,
                    LastSyncAttempt = playlist.Sync.LastSyncAttempt,
                    ConflictUrl = playlist.Sync.ConflictUrl,
                    Error = playlist.Sync.Error,
                    ExtensionData = CloneExtensionData(playlist.Sync.ExtensionData),
                },
            Items = items.Select(ClonePresentationRef).ToList(),
            ExtensionData = CloneExtensionData(playlist.ExtensionData),
        };

    private static PresentationRefDto ClonePresentationRef(PresentationRefDto presentation) =>
        new()
        {
            Path = presentation.Path,
            Title = presentation.Title,
            UpdatedAt = presentation.UpdatedAt,
            ArrangementId = presentation.ArrangementId,
            DestinationLayerId = presentation.DestinationLayerId,
            ThumbnailData = presentation.ThumbnailData,
        };

    private static Dictionary<string, JsonElement>? CloneExtensionData(Dictionary<string, JsonElement>? extensionData) =>
        extensionData == null
            ? null
            : new Dictionary<string, JsonElement>(extensionData, StringComparer.OrdinalIgnoreCase);

    private static string CreateUniqueCatalogId(IEnumerable<string> existingIds, string? preferredId)
    {
        var normalizedExisting = existingIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(preferredId) && !normalizedExisting.Contains(preferredId))
            return preferredId;

        var next = Guid.NewGuid().ToString();
        while (normalizedExisting.Contains(next))
            next = Guid.NewGuid().ToString();

        return next;
    }

    private static string BuildPackagePresentationPath(string path)
    {
        var normalized = NormalizePath(path);
        if (Path.IsPathRooted(normalized))
            normalized = $"presentations/{Path.GetFileName(normalized)}";
        else if (!normalized.StartsWith("presentations/", StringComparison.OrdinalIgnoreCase))
            normalized = $"presentations/{Path.GetFileName(normalized)}";

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(static segment => segment == "." || segment == ".."))
            throw new InvalidDataException($"Invalid package presentation path '{path}'.");

        return string.Join('/', segments);
    }

    private static string EnsureUniquePackagePath(string packagePath, ISet<string> usedPaths)
    {
        if (usedPaths.Add(packagePath))
            return packagePath;

        var directory = Path.GetDirectoryName(packagePath.Replace('/', Path.DirectorySeparatorChar))?
                            .Replace(Path.DirectorySeparatorChar, '/')
                        ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(packagePath);
        var extension = Path.GetExtension(packagePath);
        var suffix = 2;
        while (true)
        {
            var candidate = string.IsNullOrWhiteSpace(directory)
                ? $"{fileName}_{suffix}{extension}"
                : $"{directory}/{fileName}_{suffix}{extension}";
            if (usedPaths.Add(candidate))
                return candidate;
            suffix++;
        }
    }

    private static void ValidateManifest(CollectionPackageManifest manifest, string expectedType)
    {
        if (!string.Equals(manifest.FormatVersion, PackageFormatVersion, StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported collection package format '{manifest.FormatVersion}'.");
        if (!string.Equals(manifest.PackageType, expectedType, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Expected a {expectedType} package but found '{manifest.PackageType}'.");
    }

    private static T ReadRequiredEntry<T>(ZipArchive archive, string entryPath) where T : class
    {
        var entry = GetArchiveEntry(archive, entryPath)
                    ?? throw new InvalidDataException($"Package is missing required entry '{entryPath}'.");
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        return JsonSerializer.Deserialize<T>(json, JsonOptions)
               ?? throw new InvalidDataException($"Package entry '{entryPath}' is invalid.");
    }

    private static ZipArchiveEntry? GetArchiveEntry(ZipArchive archive, string entryPath)
    {
        var normalized = NormalizePath(entryPath);
        return archive.GetEntry(normalized)
               ?? archive.Entries.FirstOrDefault(entry =>
                   string.Equals(NormalizePath(entry.FullName), normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static void WriteStringEntry(ZipArchive archive, string entryPath, string content)
    {
        var entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream);
        writer.Write(content);
    }

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/');

    private sealed class CollectionPackageManifest
    {
        [JsonPropertyName("formatVersion")]
        public string FormatVersion { get; set; } = PackageFormatVersion;

        [JsonPropertyName("packageType")]
        public string PackageType { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("exportedAt")]
        public string ExportedAt { get; set; } = string.Empty;
    }

    private sealed record PackagedPresentationEntry(string SourcePath, string PackagePath);

    private sealed record PackagedPresentationSet(
        IReadOnlyList<PresentationRefDto> PackagedRefs,
        IReadOnlyList<PackagedPresentationEntry> BundleEntries);

    private sealed record ImportedPresentationSet(
        IReadOnlyList<PresentationRefDto> RemappedRefs,
        IReadOnlyList<string> UniqueImportedPaths);
}