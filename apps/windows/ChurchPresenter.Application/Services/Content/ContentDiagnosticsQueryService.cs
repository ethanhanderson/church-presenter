using ChurchPresenter.Backend.Media;

namespace ChurchPresenter.Services.Content;

/// <summary>
/// Query surface for content-root, catalog, cache, and media availability diagnostics.
/// </summary>
public interface IContentDiagnosticsQueryService
{
    /// <summary>Builds the current content diagnostics snapshot.</summary>
    Task<ContentDiagnosticsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class ContentDiagnosticsQueryService(
    IContentDirectoryService contentDirectories,
    IContentStore contentStore,
    ICatalogService catalog,
    IMediaLibraryService mediaLibrary,
    IShowSessionCache sessionCache) : IContentDiagnosticsQueryService
{
    private readonly IContentDirectoryService _contentDirectories = contentDirectories ?? throw new ArgumentNullException(nameof(contentDirectories));
    private readonly IContentStore _contentStore = contentStore ?? throw new ArgumentNullException(nameof(contentStore));
    private readonly ICatalogService _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    private readonly IMediaLibraryService _mediaLibrary = mediaLibrary ?? throw new ArgumentNullException(nameof(mediaLibrary));
    private readonly IShowSessionCache _sessionCache = sessionCache ?? throw new ArgumentNullException(nameof(sessionCache));

    /// <inheritdoc />
    public async Task<ContentDiagnosticsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        List<ContentDiagnosticItem> diagnostics = new();
        List<ContentRecoveryActionQuery> actions = new();

        AddContentRootDiagnostics(diagnostics, actions);
        AddCatalogDiagnostics(diagnostics, actions);
        AddPresentationCacheDiagnostics(diagnostics, actions);
        await AddMediaDiagnosticsAsync(diagnostics, actions, cancellationToken).ConfigureAwait(false);

        return new ContentDiagnosticsSnapshot
        {
            Diagnostics = diagnostics,
            RecoveryActions = actions
                .DistinctBy(static action => action.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
        };
    }

    private void AddContentRootDiagnostics(
        ICollection<ContentDiagnosticItem> diagnostics,
        ICollection<ContentRecoveryActionQuery> actions)
    {
        var root = _contentDirectories.GetDocumentsDataDirectory();
        if (_contentStore.DirectoryExists(root))
            return;

        diagnostics.Add(new ContentDiagnosticItem
        {
            Id = "content-root-missing",
            Title = "Content root unavailable",
            Message = $"Content root '{root}' is missing or unavailable.",
            Severity = "error",
            FailureKind = ContentAccessFailureKind.Missing,
            SubjectId = root,
        });
        actions.Add(new ContentRecoveryActionQuery
        {
            Id = "repair-content-root",
            Label = "Repair content root",
            ActionType = "repair-content-root",
            SubjectId = root,
        });
    }

    private void AddCatalogDiagnostics(
        ICollection<ContentDiagnosticItem> diagnostics,
        ICollection<ContentRecoveryActionQuery> actions)
    {
        if (_catalog.Catalog.Libraries.Count > 0 || _catalog.Catalog.Playlists.Count > 0)
            return;

        diagnostics.Add(new ContentDiagnosticItem
        {
            Id = "catalog-empty",
            Title = "Catalog is empty",
            Message = "No libraries or playlists are currently loaded.",
            Severity = "info",
        });
        actions.Add(new ContentRecoveryActionQuery
        {
            Id = "reload-catalog",
            Label = "Reload catalog",
            ActionType = "reload-catalog",
        });
    }

    private void AddPresentationCacheDiagnostics(
        ICollection<ContentDiagnosticItem> diagnostics,
        ICollection<ContentRecoveryActionQuery> actions)
    {
        var pruned = _sessionCache.PruneMissingFiles();
        foreach (var path in pruned)
        {
            diagnostics.Add(new ContentDiagnosticItem
            {
                Id = $"presentation-cache-pruned:{path}",
                Title = "Presentation cache pruned",
                Message = $"Removed stale cached presentation '{path}'.",
                Severity = "warning",
                FailureKind = ContentAccessFailureKind.Outdated,
                SubjectId = path,
            });
            actions.Add(new ContentRecoveryActionQuery
            {
                Id = "clear-affected-caches",
                Label = "Clear affected caches",
                ActionType = "clear-affected-caches",
                SubjectId = path,
            });
        }
    }

    private async Task AddMediaDiagnosticsAsync(
        ICollection<ContentDiagnosticItem> diagnostics,
        ICollection<ContentRecoveryActionQuery> actions,
        CancellationToken cancellationToken)
    {
        foreach (var asset in await _mediaLibrary.GetAssetsAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (asset.Availability.Status != MediaAvailabilityStatus.Missing)
                continue;

            diagnostics.Add(new ContentDiagnosticItem
            {
                Id = $"media-missing:{asset.AssetId}",
                Title = $"Missing media: {asset.DisplayName}",
                Message = asset.Availability.DiagnosticMessage ?? "Media file is missing.",
                Severity = "warning",
                FailureKind = asset.Availability.FailureKind ?? ContentAccessFailureKind.Missing,
                SubjectId = asset.AssetId,
            });
            actions.Add(new ContentRecoveryActionQuery
            {
                Id = $"relink-media:{asset.AssetId}",
                Label = "Relink media",
                ActionType = "relink-media",
                SubjectId = asset.AssetId,
            });
        }
    }
}
