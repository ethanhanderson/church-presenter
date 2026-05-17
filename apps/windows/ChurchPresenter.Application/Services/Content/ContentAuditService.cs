using System.Text.Json;
using System.Text.Json.Serialization;

using ChurchPresenter.Backend.Media;

using Microsoft.Extensions.Logging;

namespace ChurchPresenter.Services.Content;

/// <inheritdoc />
public sealed class ContentAuditService(
    IContentDirectoryService paths,
    IContentStore contentStore,
    ILibraryRegistryService libraryRegistry,
    IPlaylistRegistryService playlistRegistry,
    IMediaLibraryService mediaLibrary,
    IThemeLibraryService themeLibrary,
    IPresentationProjectService presentationProjects,
    IContentMaintenanceLogService maintenanceLog,
    ILogger<ContentAuditService> logger) : IContentAuditService
{
    private readonly IContentDirectoryService _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly IContentStore _contentStore = contentStore ?? throw new ArgumentNullException(nameof(contentStore));
    private readonly ILibraryRegistryService _libraryRegistry = libraryRegistry ?? throw new ArgumentNullException(nameof(libraryRegistry));
    private readonly IPlaylistRegistryService _playlistRegistry = playlistRegistry ?? throw new ArgumentNullException(nameof(playlistRegistry));
    private readonly IMediaLibraryService _mediaLibrary = mediaLibrary ?? throw new ArgumentNullException(nameof(mediaLibrary));
    private readonly IThemeLibraryService _themeLibrary = themeLibrary ?? throw new ArgumentNullException(nameof(themeLibrary));
    private readonly IPresentationProjectService _presentationProjects = presentationProjects ?? throw new ArgumentNullException(nameof(presentationProjects));
    private readonly IContentMaintenanceLogService _maintenanceLog = maintenanceLog ?? throw new ArgumentNullException(nameof(maintenanceLog));
    private readonly ILogger<ContentAuditService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <inheritdoc />
    public ContentAuditResult? LastAuditResult { get; private set; }

    /// <inheritdoc />
    public async Task<ContentAuditResult> RunAuditAsync(CancellationToken cancellationToken = default)
    {
        await _paths.EnsureDocumentsLayoutAsync().ConfigureAwait(false);

        var contentRoot = _paths.GetDocumentsDataDirectory();
        var issues = new List<AuditIssue>();
        var brokenReferences = new List<BrokenContentReference>();
        var recoveryActions = new List<ContentRecoveryAction>();

        CheckRequiredFolders(contentRoot, issues);
        CheckRootManifest(contentRoot, issues);
        CheckLegacyArtifacts(issues);

        var mediaInventory = await BuildMediaInventoryAsync(issues, brokenReferences, recoveryActions, cancellationToken).ConfigureAwait(false);
        var graphNodes = new List<MediaReferenceNode>();

        var libraryCount = await AuditLibraryRegistryAsync(
                issues,
                brokenReferences,
                recoveryActions,
                cancellationToken)
            .ConfigureAwait(false);
        var playlistCount = await AuditPlaylistRegistryAsync(
                issues,
                brokenReferences,
                recoveryActions,
                cancellationToken)
            .ConfigureAwait(false);
        var themeCount = await AuditThemesAsync(
                mediaInventory,
                graphNodes,
                issues,
                brokenReferences,
                recoveryActions,
                cancellationToken)
            .ConfigureAwait(false);
        var presentationCount = AuditPresentationsAsync(
                mediaInventory,
                graphNodes,
                issues,
                brokenReferences,
                recoveryActions,
                cancellationToken);

        var mediaStats = await _mediaLibrary.GetMediaLinkStatisticsAsync(cancellationToken).ConfigureAwait(false);
        AuditMediaLibrary(issues, mediaStats);
        graphNodes.AddRange(mediaInventory.PlaylistNodes);

        var cleanupGraph = new MediaCleanupReferenceGraph { Nodes = graphNodes };
        var cleanupCandidates = cleanupGraph.Analyze(mediaInventory.Assets)
            .OrderBy(static candidate => candidate.EligibleForCleanup ? 0 : 1)
            .ThenBy(static candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = new ContentAuditResult
        {
            AuditedAt = DateTimeOffset.UtcNow.ToString("O"),
            ContentRootPath = contentRoot,
            Issues = issues,
            BrokenReferences = brokenReferences,
            RecoveryActions = recoveryActions,
            ReferenceGraphNodes = graphNodes,
            CleanupCandidates = cleanupCandidates,
            CleanupPreview = BuildCleanupPreview(cleanupCandidates, graphNodes),
            LibraryCount = libraryCount,
            PlaylistCount = playlistCount,
            ThemeCount = themeCount,
            PresentationCount = presentationCount,
            MediaLibraryItemCount = mediaStats.TotalItems,
            MediaMissingFileCount = mediaStats.MissingFiles,
            MediaExternalPathCount = mediaStats.ExternalPathReferences,
        };

        LastAuditResult = result;

        // Persist audit
        await PersistAuditResultAsync(result, cancellationToken).ConfigureAwait(false);

        // Update root manifest last audit timestamp
        await UpdateRootManifestAuditTimestampAsync(result.AuditedAt, cancellationToken).ConfigureAwait(false);

        // Log to machine maintenance log
        await LogAuditSummaryAsync(result, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Content audit complete: {LibraryCount} libraries, {PlaylistCount} playlists, {ThemeCount} themes, {PresentationCount} presentations, {MediaItems} media items, {IssueCount} issues.",
            libraryCount, playlistCount, themeCount, presentationCount, mediaStats.TotalItems, issues.Count);

        return result;
    }

    /// <inheritdoc />
    public async Task<ContentAuditResult?> LoadLastAuditResultAsync(CancellationToken cancellationToken = default)
    {
        if (LastAuditResult != null)
            return LastAuditResult;

        return await _contentStore.ReadJsonAsync<ContentAuditResult>(_paths.GetContentAuditPath(), JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    // ── Audit checks ──────────────────────────────────────────────────────────

    private void CheckRequiredFolders(string contentRoot, List<AuditIssue> issues)
    {
        var requiredFolders = new[]
        {
            "Libraries",
            "Playlists",
            "Presentations",
            Path.Combine("Presentations", "songs"),
            "Configurations",
            "Themes",
            "Media",
            Path.Combine("Media", "Files"),
            Path.Combine("Media", "Playlists"),
            "Audits",
        };

        foreach (var folder in requiredFolders)
        {
            var path = Path.Combine(contentRoot, folder);
            if (!Directory.Exists(path))
            {
                issues.Add(new AuditIssue
                {
                    Severity = AuditIssueSeverity.Warning,
                    Code = "missing-required-folder",
                    Message = $"Required folder '{folder}' is missing and will be created.",
                    Path = path,
                    AutoRepaired = true,
                });
                Directory.CreateDirectory(path);
            }
        }
    }

    private void CheckRootManifest(string contentRoot, List<AuditIssue> issues)
    {
        var manifestPath = _paths.GetContentRootManifestPath();
        if (!_contentStore.FileExists(manifestPath))
        {
            issues.Add(new AuditIssue
            {
                Severity = AuditIssueSeverity.Info,
                Code = "missing-root-manifest",
                Message = "Content root manifest (ChurchPresenter.Content.json) is missing and will be created on next bootstrap.",
                Path = manifestPath,
            });
            return;
        }

        var manifest = _contentStore.ReadJsonAsync<ContentRootManifest>(manifestPath, JsonOptions).GetAwaiter().GetResult();
        if (manifest == null)
        {
            issues.Add(new AuditIssue
            {
                Severity = AuditIssueSeverity.Warning,
                Code = "invalid-root-manifest",
                Message = "Content root manifest exists but could not be parsed.",
                Path = manifestPath,
            });
            return;
        }

        if (manifest.SchemaVersion < 3)
        {
            issues.Add(new AuditIssue
            {
                Severity = AuditIssueSeverity.Warning,
                Code = "outdated-root-schema",
                Message = $"Content root schema {manifest.SchemaVersion} is older than the canonical schema 3.",
                Path = manifestPath,
            });
        }
    }

    private async Task<int> AuditLibraryRegistryAsync(
        List<AuditIssue> issues,
        List<BrokenContentReference> brokenReferences,
        List<ContentRecoveryAction> recoveryActions,
        CancellationToken cancellationToken)
    {
        if (!_libraryRegistry.RegistryExists())
        {
            issues.Add(new AuditIssue
            {
                Severity = AuditIssueSeverity.Warning,
                Code = "missing-library-registry",
                Message = "Libraries/Index.json does not exist. Migration may be required.",
                Path = _paths.GetLibrariesIndexPath(),
            });
            return 0;
        }

        var index = await _libraryRegistry.LoadIndexAsync(cancellationToken).ConfigureAwait(false);
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var validCount = 0;

        foreach (var entry in index.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Id))
            {
                issues.Add(new AuditIssue
                {
                    Severity = AuditIssueSeverity.Warning,
                    Code = "library-index-empty-id",
                    Message = "A library index entry has an empty ID and will be skipped.",
                });
                continue;
            }

            if (!seenIds.Add(entry.Id))
            {
                issues.Add(new AuditIssue
                {
                    Severity = AuditIssueSeverity.Warning,
                    Code = "library-duplicate-id",
                    Message = $"Duplicate library ID '{entry.Id}' in index.",
                });
                continue;
            }

            var manifestPath = _paths.GetLibraryManifestPath(entry.Id);
            if (!File.Exists(manifestPath))
            {
                issues.Add(new AuditIssue
                {
                    Severity = AuditIssueSeverity.Warning,
                    Code = "library-manifest-missing",
                    Message = $"Library manifest missing for '{entry.Id}'.",
                    Path = manifestPath,
                });
            }
            else
            {
                validCount++;
            }

            var manifest = await _libraryRegistry.LoadAsync(entry.Id, cancellationToken).ConfigureAwait(false);
            if (manifest == null)
                continue;

            foreach (var presentation in manifest.Presentations.Where(static presentation => !string.IsNullOrWhiteSpace(presentation.Path)))
                RecordPresentationReference(
                    issues,
                    brokenReferences,
                    recoveryActions,
                    "library",
                    manifest.Id,
                    manifest.Name,
                    presentation.Path);
        }

        return validCount;
    }

    private async Task<int> AuditPlaylistRegistryAsync(
        List<AuditIssue> issues,
        List<BrokenContentReference> brokenReferences,
        List<ContentRecoveryAction> recoveryActions,
        CancellationToken cancellationToken)
    {
        if (!_playlistRegistry.RegistryExists())
        {
            issues.Add(new AuditIssue
            {
                Severity = AuditIssueSeverity.Warning,
                Code = "missing-playlist-registry",
                Message = "Playlists/Index.json does not exist. Migration may be required.",
                Path = _paths.GetPlaylistsIndexPath(),
            });
            return 0;
        }

        var index = await _playlistRegistry.LoadIndexAsync(cancellationToken).ConfigureAwait(false);
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var validCount = 0;

        foreach (var entry in index.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Id))
            {
                issues.Add(new AuditIssue
                {
                    Severity = AuditIssueSeverity.Warning,
                    Code = "playlist-index-empty-id",
                    Message = "A playlist index entry has an empty ID and will be skipped.",
                });
                continue;
            }

            if (!seenIds.Add(entry.Id))
            {
                issues.Add(new AuditIssue
                {
                    Severity = AuditIssueSeverity.Warning,
                    Code = "playlist-duplicate-id",
                    Message = $"Duplicate playlist ID '{entry.Id}' in index.",
                });
                continue;
            }

            var manifestPath = _paths.GetPlaylistManifestPath(entry.Id);
            if (!File.Exists(manifestPath))
            {
                issues.Add(new AuditIssue
                {
                    Severity = AuditIssueSeverity.Warning,
                    Code = "playlist-manifest-missing",
                    Message = $"Playlist manifest missing for '{entry.Id}'.",
                    Path = manifestPath,
                });
            }
            else
            {
                validCount++;
            }

            var manifest = await _playlistRegistry.LoadAsync(entry.Id, cancellationToken).ConfigureAwait(false);
            if (manifest == null)
                continue;

            foreach (var presentation in manifest.Items.Where(static item => !string.IsNullOrWhiteSpace(item.Path)))
                RecordPresentationReference(
                    issues,
                    brokenReferences,
                    recoveryActions,
                    "service-playlist",
                    manifest.Id,
                    manifest.Name,
                    presentation.Path);
        }

        return validCount;
    }

    private async Task<int> AuditThemesAsync(
        MediaInventory mediaInventory,
        List<MediaReferenceNode> graphNodes,
        List<AuditIssue> issues,
        List<BrokenContentReference> brokenReferences,
        List<ContentRecoveryAction> recoveryActions,
        CancellationToken cancellationToken)
    {
        if (!_contentStore.FileExists(_paths.GetThemesIndexPath()))
        {
            issues.Add(new AuditIssue
            {
                Severity = AuditIssueSeverity.Warning,
                Code = "missing-theme-index",
                Message = "Themes/Index.json does not exist. Startup migration may still be required.",
                Path = _paths.GetThemesIndexPath(),
            });
            return 0;
        }

        var themes = await _themeLibrary.LoadAsync(cancellationToken).ConfigureAwait(false);
        foreach (var theme in themes)
        {
            foreach (var slide in theme.Slides)
            {
                var assetIds = ResolveCueAssetIds(
                    slide.MediaCues ?? [],
                    mediaInventory,
                    issues,
                    brokenReferences,
                    recoveryActions,
                    ownerSurface: "theme",
                    ownerId: theme.Id,
                    ownerDisplayName: $"{theme.Name} / {slide.Name ?? slide.Id}");
                if (assetIds.Count == 0)
                    continue;

                graphNodes.Add(new MediaReferenceNode
                {
                    NodeId = $"theme:{theme.Id}:{slide.Id}",
                    DisplayName = $"{theme.Name} / {slide.Name ?? slide.Id}",
                    Surface = MediaReferenceSurface.Theme,
                    AssetIds = assetIds,
                });
            }
        }

        return themes.Count;
    }

    private int AuditPresentationsAsync(
        MediaInventory mediaInventory,
        List<MediaReferenceNode> graphNodes,
        List<AuditIssue> issues,
        List<BrokenContentReference> brokenReferences,
        List<ContentRecoveryAction> recoveryActions,
        CancellationToken cancellationToken)
    {
        var files = _contentStore.EnumerateFiles(_paths.GetPresentationsRootDirectory(), "*.cpres", SearchOption.AllDirectories);
        var count = 0;

        foreach (var file in files.OrderBy(static file => file, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            PresentationProject project;
            try
            {
                project = _presentationProjects.Open(_paths.ToContentRelativePath(file));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
            {
                issues.Add(new AuditIssue
                {
                    Severity = AuditIssueSeverity.Warning,
                    Code = "presentation-open-failed",
                    Message = $"Presentation '{Path.GetFileName(file)}' could not be opened during audit.",
                    Path = file,
                });
                continue;
            }

            count++;
            foreach (var slide in project.Slides)
            {
                var assetIds = ResolveCueAssetIds(
                    slide.MediaCues,
                    mediaInventory,
                    issues,
                    brokenReferences,
                    recoveryActions,
                    ownerSurface: "slide",
                    ownerId: slide.Id,
                    ownerDisplayName: $"{project.Manifest.Title} / {slide.Id}");
                if (assetIds.Count > 0)
                {
                    graphNodes.Add(new MediaReferenceNode
                    {
                        NodeId = $"slide:{project.Manifest.PresentationId}:{slide.Id}",
                        DisplayName = $"{project.Manifest.Title} / {slide.Id}",
                        Surface = MediaReferenceSurface.Slide,
                        AssetIds = assetIds,
                    });
                }
            }

            foreach (var embeddedTheme in project.EmbeddedThemes.Where(static entry => entry.Template != null))
            {
                foreach (var slide in embeddedTheme.Template!.Slides)
                {
                    var assetIds = ResolveCueAssetIds(
                        slide.MediaCues ?? [],
                        mediaInventory,
                        issues,
                        brokenReferences,
                        recoveryActions,
                        ownerSurface: "embedded-theme",
                        ownerId: embeddedTheme.Template.Id,
                        ownerDisplayName: $"{project.Manifest.Title} / {embeddedTheme.Template.Name}");
                    if (assetIds.Count == 0)
                        continue;

                    graphNodes.Add(new MediaReferenceNode
                    {
                        NodeId = $"embedded-theme:{project.Manifest.PresentationId}:{embeddedTheme.Template.Id}:{slide.Id}",
                        DisplayName = $"{project.Manifest.Title} / {embeddedTheme.Template.Name}",
                        Surface = MediaReferenceSurface.Theme,
                        AssetIds = assetIds,
                    });
                }
            }
        }

        return count;
    }

    private void AuditMediaLibrary(List<AuditIssue> issues, MediaLinkStatistics stats)
    {
        if (stats.MissingFiles > 0)
        {
            issues.Add(new AuditIssue
            {
                Severity = AuditIssueSeverity.Warning,
                Code = "media-missing-files",
                Message = $"{stats.MissingFiles} media library item(s) reference files that are missing on disk.",
                Path = _paths.GetMediaIndexPath(),
            });
        }

        if (stats.ExternalPathReferences > 0)
        {
            issues.Add(new AuditIssue
            {
                Severity = AuditIssueSeverity.Info,
                Code = "media-external-paths",
                Message = $"{stats.ExternalPathReferences} media item(s) still reference paths outside managed Media/Files (restart the app to copy them into the content library).",
                Path = _paths.GetMediaIndexPath(),
            });
        }
    }

    private static ContentCleanupPreview BuildCleanupPreview(
        IReadOnlyList<MediaCleanupCandidate> cleanupCandidates,
        IReadOnlyList<MediaReferenceNode> graphNodes)
    {
        var eligibleCount = cleanupCandidates.Count(static candidate => candidate.EligibleForCleanup);
        var protectedCount = cleanupCandidates.Count(static candidate => !candidate.EligibleForCleanup);
        return new ContentCleanupPreview
        {
            CandidateCount = cleanupCandidates.Count,
            EligibleForCleanupCount = eligibleCount,
            ProtectedReferenceCount = protectedCount,
            RequiresDestructiveConfirmation = eligibleCount > 0,
            Summary = eligibleCount == 0
                ? "No managed media assets are currently eligible for cleanup."
                : $"{eligibleCount} managed media asset(s) are eligible for cleanup after preview confirmation.",
            GeneratedAt = DateTimeOffset.UtcNow.ToString("O"),
            ReferenceGraphFingerprint = BuildReferenceGraphFingerprint(graphNodes),
        };
    }

    private static string BuildReferenceGraphFingerprint(IEnumerable<MediaReferenceNode> graphNodes)
    {
        var normalized = string.Join(
            "|",
            graphNodes
                .OrderBy(static node => node.NodeId, StringComparer.OrdinalIgnoreCase)
                .Select(static node => $"{node.NodeId}:{string.Join(",", node.AssetIds.Order(StringComparer.OrdinalIgnoreCase))}"));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

    private void CheckLegacyArtifacts(List<AuditIssue> issues)
    {
        if (File.Exists(_paths.GetLibrariesJsonPath()))
        {
            issues.Add(new AuditIssue
            {
                Severity = AuditIssueSeverity.Info,
                Code = "legacy-aggregate-file-present",
                Message = "Legacy libraries.json found. Migration will remove it on next startup.",
                Path = _paths.GetLibrariesJsonPath(),
            });
        }

        if (File.Exists(_paths.GetPlaylistsJsonPath()))
        {
            issues.Add(new AuditIssue
            {
                Severity = AuditIssueSeverity.Info,
                Code = "legacy-aggregate-file-present",
                Message = "Legacy playlists.json found. Migration will remove it on next startup.",
                Path = _paths.GetPlaylistsJsonPath(),
            });
        }

        if (File.Exists(_paths.GetThemesJsonPath()))
        {
            issues.Add(new AuditIssue
            {
                Severity = AuditIssueSeverity.Info,
                Code = "legacy-theme-file-present",
                Message = "Legacy themes/themes.json is still present. Startup migration should import it into canonical Themes/ storage.",
                Path = _paths.GetThemesJsonPath(),
            });
        }
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    private async Task PersistAuditResultAsync(ContentAuditResult result, CancellationToken cancellationToken)
    {
        var path = _paths.GetContentAuditPath();
        try
        {
            await _contentStore.WriteJsonAsync(path, result, JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not persist content audit result.");
        }
    }

    private async Task UpdateRootManifestAuditTimestampAsync(string auditedAt, CancellationToken cancellationToken)
    {
        var manifestPath = _paths.GetContentRootManifestPath();
        if (!_contentStore.FileExists(manifestPath))
            return;

        try
        {
            var manifest = await _contentStore.ReadJsonAsync<ContentRootManifest>(manifestPath, JsonOptions, cancellationToken).ConfigureAwait(false);
            if (manifest == null)
                return;

            manifest.LastAuditAt = auditedAt;
            await _contentStore.WriteJsonAsync(manifestPath, manifest, JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not update root manifest audit timestamp.");
        }
    }

    private async Task LogAuditSummaryAsync(ContentAuditResult result, CancellationToken cancellationToken)
    {
        var severity = result.HasErrors ? "error" : result.HasWarnings ? "warning" : "info";
        var issueText = result.Issues.Count == 0
            ? "No issues found."
            : $"{result.Issues.Count} issue{(result.Issues.Count == 1 ? string.Empty : "s")} detected.";

        var mediaSuffix = result.MediaLibraryItemCount > 0
            ? $" {result.MediaLibraryItemCount} media item(s) ({result.MediaMissingFileCount} missing, {result.MediaExternalPathCount} external path(s))."
            : string.Empty;

        await _maintenanceLog.AppendEntriesAsync(
            new[]
            {
                new ContentMaintenanceLogEntry
                {
                    Timestamp = result.AuditedAt,
                    Trigger = ContentMaintenanceTrigger.ManualScan.ToString(),
                    Severity = severity,
                    EventType = "content-audit",
                    Message = $"Audit complete: {result.LibraryCount} libraries, {result.PlaylistCount} playlists, {result.ThemeCount} themes, {result.PresentationCount} presentations.{mediaSuffix} {issueText} {result.BrokenReferences.Count} broken reference(s), {result.CleanupCandidates.Count(candidate => candidate.EligibleForCleanup)} cleanup candidate(s).",
                    Path = result.ContentRootPath,
                },
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<MediaInventory> BuildMediaInventoryAsync(
        List<AuditIssue> issues,
        List<BrokenContentReference> brokenReferences,
        List<ContentRecoveryAction> recoveryActions,
        CancellationToken cancellationToken)
    {
        var assets = new List<MediaAsset>();
        var lookup = new Dictionary<string, MediaAsset>(StringComparer.OrdinalIgnoreCase);
        var playlistNodes = new List<MediaReferenceNode>();
        var managedFiles = _contentStore.EnumerateFiles(_paths.GetManagedMediaFilesDirectory(), "*.*", SearchOption.AllDirectories);

        foreach (var item in await _mediaLibrary.GetRootItemsAsync(cancellationToken).ConfigureAwait(false))
            AddMediaAsset(item, assets, lookup, managedFiles, issues, brokenReferences, recoveryActions);

        foreach (var playlist in await _mediaLibrary.GetPlaylistsAsync(cancellationToken).ConfigureAwait(false))
        {
            var assetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in playlist.Items)
            {
                var asset = AddMediaAsset(item, assets, lookup, managedFiles, issues, brokenReferences, recoveryActions);
                assetIds.Add(asset.AssetId);
            }

            if (assetIds.Count > 0)
            {
                playlistNodes.Add(new MediaReferenceNode
                {
                    NodeId = $"media-playlist:{playlist.Id}",
                    DisplayName = playlist.Name,
                    Surface = MediaReferenceSurface.MediaPlaylist,
                    AssetIds = assetIds,
                });
            }
        }

        return new MediaInventory(assets, lookup, playlistNodes, managedFiles);
    }

    private MediaAsset AddMediaAsset(
        MediaLibraryItem item,
        List<MediaAsset> assets,
        Dictionary<string, MediaAsset> lookup,
        IReadOnlyList<string> managedFiles,
        List<AuditIssue> issues,
        List<BrokenContentReference> brokenReferences,
        List<ContentRecoveryAction> recoveryActions)
    {
        var resolvedPath = _mediaLibrary.ResolveStoredMediaPath(item.Path);
        var storagePolicy = IsManagedPath(item.Path) ? MediaStoragePolicy.Managed : MediaStoragePolicy.Referenced;
        var availability = string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath)
            ? MediaAvailability.Missing(resolvedPath, "Media file is missing.", DateTimeOffset.UtcNow)
            : MediaAvailability.Available(resolvedPath, DateTimeOffset.UtcNow);

        var asset = new MediaAsset
        {
            AssetId = item.Id,
            DisplayName = string.IsNullOrWhiteSpace(item.Name) ? item.Id : item.Name,
            Kind = ResolveAssetKind(item.Type),
            StoragePolicy = storagePolicy,
            OriginalSourcePath = item.Path,
            ResolvedPath = availability.IsPlayable ? resolvedPath : null,
            Availability = availability,
        };

        assets.Add(asset);
        StoreLookup(lookup, item.Id, asset);
        StoreLookup(lookup, item.Path, asset);
        StoreLookup(lookup, resolvedPath, asset);

        if (!availability.IsPlayable)
        {
            var suggestion = SuggestManagedFileReplacement(item.Path, managedFiles);
            issues.Add(new AuditIssue
            {
                Severity = AuditIssueSeverity.Warning,
                Code = "media-file-missing",
                Message = $"Media item '{asset.DisplayName}' points at a missing file.",
                Path = resolvedPath,
            });
            brokenReferences.Add(new BrokenContentReference
            {
                Surface = "media-library",
                OwnerId = item.Id,
                DisplayName = asset.DisplayName,
                ReferenceKind = "media-file",
                ReferenceValue = item.Path,
                ResolvedPath = resolvedPath,
                SuggestedRepairPath = suggestion,
            });
            if (!string.IsNullOrWhiteSpace(suggestion))
            {
                recoveryActions.Add(new ContentRecoveryAction
                {
                    ActionId = $"media:{item.Id}",
                    ActionType = "relink-media",
                    Message = $"Suggested relink target found for media item '{asset.DisplayName}'.",
                    SourcePath = resolvedPath,
                    TargetPath = suggestion,
                });
            }
        }

        return asset;
    }

    private HashSet<string> ResolveCueAssetIds(
        IEnumerable<SlideMediaCue>? cues,
        MediaInventory mediaInventory,
        List<AuditIssue> issues,
        List<BrokenContentReference> brokenReferences,
        List<ContentRecoveryAction> recoveryActions,
        string ownerSurface,
        string ownerId,
        string ownerDisplayName)
    {
        var assetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cue in cues ?? [])
        {
            if (TryResolveAsset(mediaInventory.Lookup, cue.MediaId, out var asset))
            {
                assetIds.Add(asset.AssetId);
                continue;
            }

            var suggestion = SuggestManagedFileReplacement(cue.MediaId, mediaInventory.ManagedFiles);
            issues.Add(new AuditIssue
            {
                Severity = AuditIssueSeverity.Warning,
                Code = "cue-media-reference-missing",
                Message = $"'{ownerDisplayName}' references media '{cue.MediaId}' that is not present in the canonical media store.",
                Path = cue.MediaId,
            });
            brokenReferences.Add(new BrokenContentReference
            {
                Surface = ownerSurface,
                OwnerId = ownerId,
                DisplayName = ownerDisplayName,
                ReferenceKind = "media-cue",
                ReferenceValue = cue.MediaId,
                SuggestedRepairPath = suggestion,
            });
            if (!string.IsNullOrWhiteSpace(suggestion))
            {
                recoveryActions.Add(new ContentRecoveryAction
                {
                    ActionId = $"{ownerSurface}:{ownerId}:{cue.Id}",
                    ActionType = "relink-cue-media",
                    Message = $"Suggested relink target found for missing cue media on '{ownerDisplayName}'.",
                    SourcePath = cue.MediaId,
                    TargetPath = suggestion,
                });
            }
        }

        return assetIds;
    }

    private void RecordPresentationReference(
        List<AuditIssue> issues,
        List<BrokenContentReference> brokenReferences,
        List<ContentRecoveryAction> recoveryActions,
        string surface,
        string ownerId,
        string ownerDisplayName,
        string presentationPath)
    {
        var resolvedPath = _paths.ResolvePresentationPath(presentationPath);
        if (File.Exists(resolvedPath))
            return;

        var suggestion = SuggestPresentationReplacement(presentationPath);
        issues.Add(new AuditIssue
        {
            Severity = AuditIssueSeverity.Warning,
            Code = "presentation-reference-missing",
            Message = $"'{ownerDisplayName}' references a presentation that is missing from the canonical Presentations/ tree.",
            Path = resolvedPath,
        });
        brokenReferences.Add(new BrokenContentReference
        {
            Surface = surface,
            OwnerId = ownerId,
            DisplayName = ownerDisplayName,
            ReferenceKind = "presentation",
            ReferenceValue = presentationPath,
            ResolvedPath = resolvedPath,
            SuggestedRepairPath = suggestion,
        });
        if (!string.IsNullOrWhiteSpace(suggestion))
        {
            recoveryActions.Add(new ContentRecoveryAction
            {
                ActionId = $"{surface}:{ownerId}:{presentationPath}",
                ActionType = "relink-presentation",
                Message = $"Suggested relink target found for '{ownerDisplayName}'.",
                SourcePath = resolvedPath,
                TargetPath = suggestion,
            });
        }
    }

    private string? SuggestPresentationReplacement(string presentationPath)
    {
        var fileName = Path.GetFileName(presentationPath.Replace('/', Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        return _contentStore.EnumerateFiles(_paths.GetPresentationsRootDirectory(), fileName, SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static string? SuggestManagedFileReplacement(string? referenceValue, IReadOnlyList<string> managedFiles)
    {
        var fileName = Path.GetFileName(referenceValue?.Replace('/', Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        return managedFiles
            .FirstOrDefault(path => string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryResolveAsset(
        IReadOnlyDictionary<string, MediaAsset> lookup,
        string? key,
        out MediaAsset asset)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            asset = default!;
            return false;
        }

        var normalizedKey = NormalizeLookupKey(key);
        if (normalizedKey != null && lookup.TryGetValue(normalizedKey, out var resolvedAsset) && resolvedAsset is not null)
        {
            asset = resolvedAsset;
            return true;
        }

        asset = default!;
        return false;
    }

    private static void StoreLookup(IDictionary<string, MediaAsset> lookup, string? key, MediaAsset asset)
    {
        var normalizedKey = NormalizeLookupKey(key);
        if (normalizedKey != null)
            lookup[normalizedKey] = asset;
    }

    private static string? NormalizeLookupKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return Path.IsPathRooted(trimmed)
            ? Path.GetFullPath(trimmed).Replace('\\', '/')
            : trimmed.Replace('\\', '/');
    }

    private static bool IsManagedPath(string? storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath))
            return false;

        return storedPath.Replace('\\', '/').TrimStart('/')
            .StartsWith("Media/Files/", StringComparison.OrdinalIgnoreCase);
    }

    private static MediaAssetKind ResolveAssetKind(string? mediaType) =>
        string.Equals(mediaType, "video", StringComparison.OrdinalIgnoreCase)
            ? MediaAssetKind.Video
            : string.Equals(mediaType, "audio", StringComparison.OrdinalIgnoreCase)
                ? MediaAssetKind.Audio
                : MediaAssetKind.Image;

    private sealed record MediaInventory(
        IReadOnlyList<MediaAsset> Assets,
        IReadOnlyDictionary<string, MediaAsset> Lookup,
        IReadOnlyList<MediaReferenceNode> PlaylistNodes,
        IReadOnlyList<string> ManagedFiles);
}