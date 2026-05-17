using System.Text.Json;
using System.Text.Json.Serialization;


using Microsoft.Extensions.Logging;

namespace ChurchPresenter.Services.Content;

/// <summary>
/// Bootstraps the managed content root on startup.
///
/// Migration flow (idempotent and resumable):
/// <list type="number">
///   <item>Create TitleCase folder layout and root manifest if absent.</item>
///   <item>Detect legacy artifacts: lowercase folders, aggregate files, old structured library.json folders.</item>
///   <item>Convert aggregate libraries.json / playlists.json → registry manifests.</item>
///   <item>Convert old structured catalog.json → registry manifests.</item>
///   <item>Write MigrationHistory.json (portable) and LastRun.json (machine-local).</item>
///   <item>Only remove legacy files after registry validation succeeds.</item>
/// </list>
/// </summary>
public sealed class ContentBootstrapService(
    IContentDirectoryService paths,
    IContentStore contentStore,
    ILibraryRegistryService libraryRegistry,
    IPlaylistRegistryService playlistRegistry,
    IThemeLibraryService themeLibrary,
    IContentRootMediaMigrationService contentRootMediaMigration,
    IContentMaintenanceLogService maintenanceLog,
    ILogger<ContentBootstrapService> logger) : IContentBootstrapService
{
    private readonly IContentDirectoryService _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly IContentStore _contentStore = contentStore ?? throw new ArgumentNullException(nameof(contentStore));
    private readonly ILibraryRegistryService _libraryRegistry = libraryRegistry ?? throw new ArgumentNullException(nameof(libraryRegistry));
    private readonly IPlaylistRegistryService _playlistRegistry = playlistRegistry ?? throw new ArgumentNullException(nameof(playlistRegistry));
    private readonly IThemeLibraryService _themeLibrary = themeLibrary ?? throw new ArgumentNullException(nameof(themeLibrary));
    private readonly IContentRootMediaMigrationService _contentRootMediaMigration = contentRootMediaMigration ?? throw new ArgumentNullException(nameof(contentRootMediaMigration));
    private readonly IContentMaintenanceLogService _maintenanceLog = maintenanceLog ?? throw new ArgumentNullException(nameof(maintenanceLog));
    private readonly ILogger<ContentBootstrapService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    private const string LegacyCatalogFileName = "catalog.json";
    private const int CurrentSchemaVersion = 3;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <inheritdoc />
    public MigrationState CurrentMigrationState { get; private set; } = MigrationState.NotStarted;

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _paths.EnsureDocumentsLayoutAsync().ConfigureAwait(false);
        await EnsureRootManifestAsync(cancellationToken).ConfigureAwait(false);

        if (await IsAlreadyMigratedAsync(cancellationToken).ConfigureAwait(false))
        {
            CurrentMigrationState = MigrationState.NotNeeded;
            return;
        }

        CurrentMigrationState = MigrationState.InProgress;
        var actions = new List<string>();
        string? errorMessage = null;
        var succeeded = false;

        try
        {
            await RunMigrationAsync(actions, cancellationToken).ConfigureAwait(false);
            await UpdateRootManifestMigrationMetadataAsync(cancellationToken).ConfigureAwait(false);
            succeeded = true;
            CurrentMigrationState = MigrationState.Completed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            errorMessage = ex.Message;
            CurrentMigrationState = MigrationState.Failed;
            _logger.LogWarning(ex, "Content migration did not complete cleanly. The app will continue with the current state.");
        }
        finally
        {
            await RecordMigrationResultAsync(succeeded, actions, errorMessage, cancellationToken).ConfigureAwait(false);
        }
    }

    // ── Migration detection ───────────────────────────────────────────────────

    private async Task<bool> IsAlreadyMigratedAsync(CancellationToken cancellationToken)
    {
        var rootManifest = await TryReadRootManifestAsync(cancellationToken).ConfigureAwait(false);
        if (rootManifest == null
            || rootManifest.SchemaVersion < CurrentSchemaVersion
            || string.IsNullOrWhiteSpace(rootManifest.LastMigratedAt))
        {
            return false;
        }

        var lastRun = await TryReadLastRunAsync(cancellationToken).ConfigureAwait(false);
        return lastRun == null || (lastRun.Succeeded && lastRun.ToSchemaVersion >= CurrentSchemaVersion);
    }

    private bool HasLegacyStructuredCatalog()
    {
        var librariesRoot = _paths.GetLibrariesDirectory();
        if (!Directory.Exists(librariesRoot))
            return false;

        return StructuredCatalogCleanup.EnumerateDirectoriesSafe(librariesRoot, SearchOption.TopDirectoryOnly, _logger)
            .Any(dir => File.Exists(Path.Combine(dir, "library.json")));
    }

    // ── Migration execution ───────────────────────────────────────────────────

    private async Task RunMigrationAsync(List<string> actions, CancellationToken cancellationToken)
    {
        actions.Add("created-fresh-root");

        var catalog = await ReadLegacyCatalogAsync(cancellationToken).ConfigureAwait(false);
        if (catalog == null)
        {
            _logger.LogInformation("No legacy catalog data found; proceeding with content compatibility rewrite only.");
            actions.Add("no-legacy-data");
        }
        else
        {
            _logger.LogInformation(
                "Migrating {LibraryCount} libraries and {PlaylistCount} playlists to structured registry format.",
                catalog.Libraries.Count,
                catalog.Playlists.Count);

            // Write library manifests
            foreach (var library in catalog.Libraries.Where(l => !string.IsNullOrWhiteSpace(l.Id)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var manifest = new LibraryManifest
                {
                    Id = library.Id,
                    Name = string.IsNullOrWhiteSpace(library.Name) ? library.Id : library.Name,
                    Description = library.Description,
                    CreatedAt = library.CreatedAt ?? DateTimeOffset.UtcNow.ToString("O"),
                    UpdatedAt = library.UpdatedAt,
                    DefaultFolder = library.DefaultFolder,
                    Presentations = library.Presentations?.Select(ClonePresentationRef).ToList() ?? new List<PresentationRefDto>(),
                };
                await _libraryRegistry.SaveAsync(manifest, cancellationToken).ConfigureAwait(false);
                actions.Add($"migrated-library:{library.Id}");
            }

            // Write playlist manifests
            foreach (var playlist in catalog.Playlists.Where(p => !string.IsNullOrWhiteSpace(p.Id)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var manifest = new PlaylistManifest
                {
                    Id = playlist.Id,
                    Name = string.IsNullOrWhiteSpace(playlist.Name) ? playlist.Id : playlist.Name,
                    Description = playlist.Description,
                    CreatedAt = playlist.CreatedAt ?? DateTimeOffset.UtcNow.ToString("O"),
                    UpdatedAt = playlist.UpdatedAt,
                    Items = playlist.Items?.Select(ClonePresentationRef).ToList() ?? new List<PresentationRefDto>(),
                    ExternalSet = playlist.ExternalSet,
                    Sync = playlist.Sync,
                };
                await _playlistRegistry.SaveAsync(manifest, cancellationToken).ConfigureAwait(false);
                actions.Add($"migrated-playlist:{playlist.Id}");
            }

            // Validate registries before deleting legacy data
            var libraryIndex = await _libraryRegistry.LoadIndexAsync(cancellationToken).ConfigureAwait(false);
            var playlistIndex = await _playlistRegistry.LoadIndexAsync(cancellationToken).ConfigureAwait(false);

            if (libraryIndex.Entries.Count < catalog.Libraries.Count(l => !string.IsNullOrWhiteSpace(l.Id)))
            {
                _logger.LogWarning("Registry validation: fewer library entries than expected after migration. Retaining legacy files for safety.");
            }
            else
            {
                // Safe to clean up legacy files now
                await CleanupLegacyFilesAsync(actions, cancellationToken).ConfigureAwait(false);
            }
        }

        var importedThemeCount = await ImportLegacyThemesAsync(cancellationToken).ConfigureAwait(false);
        if (importedThemeCount > 0)
        {
            actions.Add($"migrated-themes:{importedThemeCount}");
            if (catalog == null)
                await CleanupLegacyFilesAsync(actions, cancellationToken).ConfigureAwait(false);
        }

        var contentRewrite = await _contentRootMediaMigration.RunAsync(cancellationToken).ConfigureAwait(false);
        actions.Add($"content-media-rewrite:{contentRewrite.RewrittenPaths.Count}");
        if (contentRewrite.Issues.Count > 0)
            actions.Add($"content-media-issues:{contentRewrite.Issues.Count}");

        await AppendContentRewriteLogEntriesAsync(contentRewrite, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Migration completed: {ActionCount} actions performed, {RewriteCount} files rewritten, {IssueCount} issues reported.",
            actions.Count,
            contentRewrite.RewrittenPaths.Count,
            contentRewrite.Issues.Count);
    }

    private async Task<CatalogDto?> ReadLegacyCatalogAsync(CancellationToken cancellationToken)
    {
        // Priority 1: aggregate libraries.json + playlists.json
        var hasLibraries = File.Exists(_paths.GetLibrariesJsonPath());
        var hasPlaylists = File.Exists(_paths.GetPlaylistsJsonPath());
        if (hasLibraries || hasPlaylists)
        {
            return new CatalogDto
            {
                Libraries = hasLibraries
                    ? await ReadOrEmptyAsync<List<LibraryDto>>(_paths.GetLibrariesJsonPath(), cancellationToken).ConfigureAwait(false) ?? new List<LibraryDto>()
                    : new List<LibraryDto>(),
                Playlists = hasPlaylists
                    ? await ReadOrEmptyAsync<List<PlaylistDto>>(_paths.GetPlaylistsJsonPath(), cancellationToken).ConfigureAwait(false) ?? new List<PlaylistDto>()
                    : new List<PlaylistDto>(),
            };
        }

        // Priority 2: structured library.json + per-playlist json files
        var structuredCatalog = await TryReadStructuredCatalogAsync(cancellationToken).ConfigureAwait(false);
        if (structuredCatalog != null)
            return structuredCatalog;

        // Priority 3: legacy app data catalog.json
        var legacyPath = Path.Combine(_paths.GetAppDataDirectory(), LegacyCatalogFileName);
        if (File.Exists(legacyPath))
        {
            var catalog = await ReadOrEmptyAsync<CatalogDto>(legacyPath, cancellationToken).ConfigureAwait(false);
            if (catalog != null)
            {
                _logger.LogInformation("Reading legacy {File}.", LegacyCatalogFileName);
                return catalog;
            }
        }

        return null;
    }

    private async Task<CatalogDto?> TryReadStructuredCatalogAsync(CancellationToken cancellationToken)
    {
        var librariesRoot = _paths.GetLibrariesDirectory();
        var playlistsRoot = _paths.GetPlaylistsDirectory();

        var structuredLibraryFiles = StructuredCatalogCleanup.EnumerateFilesSafe(
            librariesRoot, "library.json", SearchOption.AllDirectories, _logger);
        var structuredPlaylistFiles = StructuredCatalogCleanup.EnumerateFilesSafe(
            playlistsRoot, "*.json", SearchOption.TopDirectoryOnly, _logger)
            .Where(p => !string.Equals(Path.GetFileName(p), "playlists.json", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (structuredLibraryFiles.Count == 0 && structuredPlaylistFiles.Count == 0)
            return null;

        var libraries = new List<LibraryDto>();
        foreach (var file in structuredLibraryFiles.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var dto = await ReadOrEmptyAsync<LibraryDto>(file, cancellationToken).ConfigureAwait(false);
            if (dto == null)
                continue;

            var libraryId = Path.GetFileName(Path.GetDirectoryName(file)) ?? dto.Id;
            dto.Id = string.IsNullOrWhiteSpace(dto.Id) ? libraryId : dto.Id;
            dto.Name = string.IsNullOrWhiteSpace(dto.Name) ? dto.Id : dto.Name;
            dto.Presentations ??= new List<PresentationRefDto>();
            libraries.Add(dto);
        }

        var playlists = new List<PlaylistDto>();
        foreach (var file in structuredPlaylistFiles.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var dto = await ReadOrEmptyAsync<PlaylistDto>(file, cancellationToken).ConfigureAwait(false);
            if (dto == null)
                continue;

            dto.Id = string.IsNullOrWhiteSpace(dto.Id)
                ? Path.GetFileNameWithoutExtension(file)
                : dto.Id;
            dto.Name = string.IsNullOrWhiteSpace(dto.Name) ? dto.Id : dto.Name;
            dto.Items ??= new List<PresentationRefDto>();
            playlists.Add(dto);
        }

        return new CatalogDto { Libraries = libraries, Playlists = playlists };
    }

    private async Task CleanupLegacyFilesAsync(List<string> actions, CancellationToken cancellationToken)
    {
        // Delete legacy aggregate files
        foreach (var path in new[] { _paths.GetLibrariesJsonPath(), _paths.GetPlaylistsJsonPath(), _paths.GetThemesJsonPath() })
        {
            if (!_contentStore.FileExists(path))
                continue;

            if (_contentStore.TryDeleteFile(path))
            {
                actions.Add($"deleted-legacy-file:{Path.GetFileName(path)}");
                _logger.LogInformation("Deleted legacy aggregate file {Path}.", path);
            }
        }

        // Delete legacy structured library directories (those containing library.json)
        StructuredCatalogCleanup.DeleteLegacyStructuredCatalogArtifacts(
            _paths.GetLibrariesDirectory(),
            _paths.GetPlaylistsDirectory(),
            _paths.GetPlaylistsJsonPath(),
            _logger,
            "Could not delete legacy library directory {Path}.",
            "Could not delete legacy playlist file {Path}.");
        actions.Add("cleaned-legacy-structured-artifacts");

        // Delete legacy app data catalog.json
        var legacyCatalogPath = Path.Combine(_paths.GetAppDataDirectory(), LegacyCatalogFileName);
        if (_contentStore.FileExists(legacyCatalogPath))
        {
            if (_contentStore.TryDeleteFile(legacyCatalogPath))
                actions.Add($"deleted-legacy-{LegacyCatalogFileName}");
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    // ── Root manifest ─────────────────────────────────────────────────────────

    private async Task EnsureRootManifestAsync(CancellationToken cancellationToken)
    {
        var path = _paths.GetContentRootManifestPath();
        if (_contentStore.FileExists(path))
            return;

        var manifest = new ContentRootManifest
        {
            SchemaVersion = CurrentSchemaVersion,
            ContentRootId = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
            MachineStateDirectory = "MachineState/",
            MigrationHistoryPath = "Audits/MigrationHistory.json",
            ResetWorkflow =
            [
                "Move or archive the current OneDrive Church Presenter root.",
                "Delete the machine-local ContentRootBinding and migration last-run files.",
                "Restart ChurchPresenter to create a fresh canonical root and import only content you still want."
            ],
            FolderMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Libraries"] = "Libraries",
                ["Playlists"] = "Playlists",
                ["Presentations"] = "Presentations",
                ["Configurations"] = "Configurations",
                ["Themes"] = "Themes",
                ["Media"] = "Media",
                ["Audits"] = "Audits",
            },
        };

        await _contentStore.WriteJsonAsync(path, manifest, JsonOptions, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Created content root manifest at {Path}.", path);
    }

    private async Task<ContentRootManifest?> TryReadRootManifestAsync(CancellationToken cancellationToken)
    {
        return await _contentStore.ReadJsonAsync<ContentRootManifest>(
                _paths.GetContentRootManifestPath(),
                JsonOptions,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task UpdateRootManifestMigrationMetadataAsync(CancellationToken cancellationToken)
    {
        var path = _paths.GetContentRootManifestPath();
        var manifest = await TryReadRootManifestAsync(cancellationToken).ConfigureAwait(false)
                       ?? new ContentRootManifest
                       {
                           ContentRootId = Guid.NewGuid().ToString("N"),
                           CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
                           FolderMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                       };

        manifest.SchemaVersion = CurrentSchemaVersion;
        manifest.LastMigratedAt = DateTimeOffset.UtcNow.ToString("O");
        manifest.PortableRootKind = string.IsNullOrWhiteSpace(manifest.PortableRootKind) ? "onedrive-documents" : manifest.PortableRootKind;
        manifest.PortableRootName = string.IsNullOrWhiteSpace(manifest.PortableRootName) ? "Church Presenter" : manifest.PortableRootName;
        manifest.MachineStateDirectory = string.IsNullOrWhiteSpace(manifest.MachineStateDirectory) ? "MachineState/" : manifest.MachineStateDirectory;
        manifest.MigrationHistoryPath = string.IsNullOrWhiteSpace(manifest.MigrationHistoryPath) ? "Audits/MigrationHistory.json" : manifest.MigrationHistoryPath;
        manifest.ResetWorkflow ??= new List<string>();
        if (manifest.ResetWorkflow.Count == 0)
        {
            manifest.ResetWorkflow.Add("Move or archive the current OneDrive Church Presenter root.");
            manifest.ResetWorkflow.Add("Delete the machine-local ContentRootBinding and migration last-run files.");
            manifest.ResetWorkflow.Add("Restart ChurchPresenter to create a fresh canonical root and import only content you still want.");
        }
        manifest.FolderMap ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await _contentStore.WriteJsonAsync(path, manifest, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private async Task AppendContentRewriteLogEntriesAsync(
        ContentRootMediaMigrationResult contentRewrite,
        CancellationToken cancellationToken)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("O");
        var entries = new List<ContentMaintenanceLogEntry>
        {
            new()
            {
                Timestamp = timestamp,
                Trigger = ContentMaintenanceTrigger.Startup.ToString(),
                Severity = contentRewrite.Issues.Count == 0 ? "info" : "warning",
                EventType = "content-media-rewrite",
                Message = $"Bulk content rewrite scanned {contentRewrite.PresentationsScanned} presentations, {contentRewrite.ThemesScanned} themes, {contentRewrite.MediaItemsScanned} media items, and reported {contentRewrite.Issues.Count} issue(s).",
            },
        };

        entries.AddRange(contentRewrite.Issues.Select(issue => new ContentMaintenanceLogEntry
        {
            Timestamp = timestamp,
            Trigger = ContentMaintenanceTrigger.Startup.ToString(),
            Severity = issue.Severity switch
            {
                AuditIssueSeverity.Error => "error",
                AuditIssueSeverity.Warning => "warning",
                _ => "info",
            },
            EventType = $"content-media-rewrite-{issue.Code}",
            Message = issue.Message,
            Path = issue.Path,
        }));

        await _maintenanceLog.AppendEntriesAsync(entries, cancellationToken).ConfigureAwait(false);
    }

    // ── Migration history ─────────────────────────────────────────────────────

    private async Task RecordMigrationResultAsync(
        bool succeeded,
        List<string> actions,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        var entry = new MigrationHistoryEntry
        {
            RunAt = DateTimeOffset.UtcNow.ToString("O"),
            FromSchemaVersion = 0,
            ToSchemaVersion = CurrentSchemaVersion,
            Succeeded = succeeded,
            Actions = actions,
            ErrorMessage = errorMessage,
        };

        // Write to portable Audits/MigrationHistory.json
        try
        {
            await AppendMigrationHistoryAsync(entry, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not write portable migration history.");
        }

        // Write machine-local LastRun.json
        try
        {
            var lastRun = new MigrationLastRun
            {
                RunAt = entry.RunAt,
                Succeeded = succeeded,
                FromSchemaVersion = entry.FromSchemaVersion,
                ToSchemaVersion = entry.ToSchemaVersion,
                CompletedSteps = actions,
                ErrorMessage = errorMessage,
            };
            var lastRunPath = _paths.GetMigrationLastRunPath();
            Directory.CreateDirectory(Path.GetDirectoryName(lastRunPath)!);
            await File.WriteAllTextAsync(lastRunPath, JsonSerializer.Serialize(lastRun, JsonOptions), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not write LastRun.json.");
        }
    }

    private async Task AppendMigrationHistoryAsync(MigrationHistoryEntry entry, CancellationToken cancellationToken)
    {
        var historyPath = _paths.GetMigrationHistoryPath();
        _contentStore.EnsureDirectory(Path.GetDirectoryName(historyPath)!);

        var history = await _contentStore.ReadJsonAsync<MigrationHistory>(historyPath, JsonOptions, cancellationToken).ConfigureAwait(false)
                      ?? new MigrationHistory();

        history.Entries.Add(entry);
        await _contentStore.WriteJsonAsync(historyPath, history, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private async Task<MigrationLastRun?> TryReadLastRunAsync(CancellationToken cancellationToken)
    {
        return await _contentStore.ReadJsonAsync<MigrationLastRun>(
                _paths.GetMigrationLastRunPath(),
                JsonOptions,
                cancellationToken)
            .ConfigureAwait(false);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<T?> ReadOrEmptyAsync<T>(string path, CancellationToken cancellationToken) where T : class
    {
        return await _contentStore.ReadJsonAsync<T>(path, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static PresentationRefDto ClonePresentationRef(PresentationRefDto p) =>
        new()
        {
            Path = p.Path,
            Title = p.Title,
            UpdatedAt = p.UpdatedAt,
            ArrangementId = p.ArrangementId,
            DestinationLayerId = p.DestinationLayerId,
            ThumbnailData = p.ThumbnailData,
        };

    private async Task<int> ImportLegacyThemesAsync(CancellationToken cancellationToken)
    {
        var legacyPath = _paths.GetThemesJsonPath();
        if (!_contentStore.FileExists(legacyPath) || _contentStore.FileExists(_paths.GetThemesIndexPath()))
            return 0;

        var legacyFile = await _contentStore.ReadJsonAsync<ThemeLibraryFile>(legacyPath, JsonOptions, cancellationToken).ConfigureAwait(false);
        var themes = legacyFile?.Themes;
        if (themes == null || themes.Count == 0)
            themes = await _contentStore.ReadJsonAsync<List<ThemeTemplate>>(legacyPath, JsonOptions, cancellationToken).ConfigureAwait(false);
        themes ??= [];
        if (themes.Count == 0)
            return 0;

        await _themeLibrary.SaveAsync(themes, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Imported {ThemeCount} theme(s) from legacy themes.json into canonical Themes/ storage.", themes.Count);
        return themes.Count;
    }
}