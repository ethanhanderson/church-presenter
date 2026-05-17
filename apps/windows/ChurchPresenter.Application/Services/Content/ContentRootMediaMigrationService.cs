using System.Text.Json;
using System.Text.Json.Serialization;


using Microsoft.Extensions.Logging;

namespace ChurchPresenter.Services.Content;

/// <summary>
/// Rewrites existing content-root media metadata so stored cues remain compatible with the
/// persistent media-layer playback pipeline.
/// </summary>
public interface IContentRootMediaMigrationService
{
    /// <summary>
    /// Scans managed content, rewrites referenced manifests and bundles, and reports unresolved issues.
    /// </summary>
    Task<ContentRootMediaMigrationResult> RunAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Summary of a bulk content-root media migration pass.
/// </summary>
public sealed class ContentRootMediaMigrationResult
{
    public int MediaItemsScanned { get; set; }

    public int MediaPlaylistsScanned { get; set; }

    public int LibrariesScanned { get; set; }

    public int PlaylistsScanned { get; set; }

    public int ThemesScanned { get; set; }

    public int PresentationsScanned { get; set; }

    public List<string> RewrittenPaths { get; } = new();

    public List<AuditIssue> Issues { get; } = new();
}

/// <inheritdoc />
public sealed class ContentRootMediaMigrationService(
    IContentDirectoryService paths,
    ILibraryRegistryService libraryRegistry,
    IPlaylistRegistryService playlistRegistry,
    IPresentationProjectService presentationProjects,
    IThemeLibraryService themeLibrary,
    ILogger<ContentRootMediaMigrationService> logger) : IContentRootMediaMigrationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IContentDirectoryService _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly ILibraryRegistryService _libraryRegistry = libraryRegistry ?? throw new ArgumentNullException(nameof(libraryRegistry));
    private readonly IPlaylistRegistryService _playlistRegistry = playlistRegistry ?? throw new ArgumentNullException(nameof(playlistRegistry));
    private readonly IPresentationProjectService _presentationProjects = presentationProjects ?? throw new ArgumentNullException(nameof(presentationProjects));
    private readonly IThemeLibraryService _themeLibrary = themeLibrary ?? throw new ArgumentNullException(nameof(themeLibrary));
    private readonly ILogger<ContentRootMediaMigrationService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public async Task<ContentRootMediaMigrationResult> RunAsync(CancellationToken cancellationToken = default)
    {
        await _paths.EnsureDocumentsLayoutAsync().ConfigureAwait(false);

        var result = new ContentRootMediaMigrationResult();
        var mediaLookup = new Dictionary<string, MediaLookupEntry>(StringComparer.OrdinalIgnoreCase);

        await RewriteMediaLibraryAsync(mediaLookup, result, cancellationToken).ConfigureAwait(false);
        await RewriteThemeLibraryAsync(mediaLookup, result, cancellationToken).ConfigureAwait(false);
        await RewritePresentationCatalogAsync(mediaLookup, result, cancellationToken).ConfigureAwait(false);

        return result;
    }

    private async Task RewriteMediaLibraryAsync(
        IDictionary<string, MediaLookupEntry> mediaLookup,
        ContentRootMediaMigrationResult result,
        CancellationToken cancellationToken)
    {
        var mediaIndexPath = _paths.GetMediaIndexPath();
        var mediaIndex = await ReadJsonOrDefaultAsync<MediaLibraryIndex>(mediaIndexPath, result, cancellationToken).ConfigureAwait(false);
        if (mediaIndex == null)
            return;

        foreach (var item in mediaIndex.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NormalizeMediaItem(item, mediaIndexPath, mediaLookup, result);
        }

        await WriteJsonAsync(mediaIndexPath, mediaIndex, cancellationToken).ConfigureAwait(false);
        result.RewrittenPaths.Add(ToRelativePath(mediaIndexPath));

        foreach (var playlistEntry in mediaIndex.Playlists.Where(entry => !string.IsNullOrWhiteSpace(entry.Id)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.MediaPlaylistsScanned++;

            var playlistPath = _paths.GetMediaPlaylistManifestPath(playlistEntry.Id);
            var playlist = await ReadJsonOrDefaultAsync<MediaPlaylistManifest>(playlistPath, result, cancellationToken).ConfigureAwait(false);
            if (playlist == null)
            {
                AddIssue(
                    result,
                    AuditIssueSeverity.Warning,
                    "media-playlist-missing",
                    $"Media playlist '{playlistEntry.Id}' is listed in Media/Index.json but its manifest is missing.",
                    playlistPath);
                continue;
            }

            foreach (var item in playlist.Items)
                NormalizeMediaItem(item, playlistPath, mediaLookup, result);

            await WriteJsonAsync(playlistPath, playlist, cancellationToken).ConfigureAwait(false);
            result.RewrittenPaths.Add(ToRelativePath(playlistPath));
        }
    }

    private async Task RewriteThemeLibraryAsync(
        IDictionary<string, MediaLookupEntry> mediaLookup,
        ContentRootMediaMigrationResult result,
        CancellationToken cancellationToken)
    {
        var themesIndexPath = _paths.GetThemesIndexPath();
        if (!File.Exists(themesIndexPath))
            return;

        var themes = await _themeLibrary.LoadAsync(cancellationToken).ConfigureAwait(false);
        result.ThemesScanned = themes.Count;

        foreach (var theme in themes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NormalizeTheme(theme, themesIndexPath, mediaLookup, result);
        }

        await _themeLibrary.SaveAsync(themes, cancellationToken).ConfigureAwait(false);
        result.RewrittenPaths.Add(ToRelativePath(themesIndexPath));
        foreach (var theme in themes.Where(static theme => !string.IsNullOrWhiteSpace(theme.Id)))
            result.RewrittenPaths.Add(ToRelativePath(_paths.GetThemeFilePath(theme.Id)));
    }

    private async Task RewritePresentationCatalogAsync(
        IDictionary<string, MediaLookupEntry> mediaLookup,
        ContentRootMediaMigrationResult result,
        CancellationToken cancellationToken)
    {
        var referencedPresentationPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var libraries = await _libraryRegistry.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        result.LibrariesScanned = libraries.Count;
        foreach (var library in libraries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NormalizePresentationRefs(library.Presentations, referencedPresentationPaths, result, "library-reference-missing", library.Id);
            await _libraryRegistry.SaveAsync(library, cancellationToken).ConfigureAwait(false);
            result.RewrittenPaths.Add(ToRelativePath(_paths.GetLibraryManifestPath(library.Id)));
        }

        var playlists = await _playlistRegistry.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        result.PlaylistsScanned = playlists.Count;
        foreach (var playlist in playlists)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NormalizePresentationRefs(playlist.Items, referencedPresentationPaths, result, "playlist-reference-missing", playlist.Id);
            await _playlistRegistry.SaveAsync(playlist, cancellationToken).ConfigureAwait(false);
            result.RewrittenPaths.Add(ToRelativePath(_paths.GetPlaylistManifestPath(playlist.Id)));
        }

        foreach (var presentationPath in referencedPresentationPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var absolutePath = _paths.ResolvePresentationPath(presentationPath);
            if (!File.Exists(absolutePath))
            {
                AddIssue(
                    result,
                    AuditIssueSeverity.Warning,
                    "presentation-missing",
                    $"Referenced presentation '{presentationPath}' could not be found for migration.",
                    absolutePath);
                continue;
            }

            try
            {
                var project = _presentationProjects.Open(presentationPath);
                NormalizeProject(project, presentationPath, mediaLookup, result);
                _presentationProjects.Save(project, presentationPath);
                result.PresentationsScanned++;
                result.RewrittenPaths.Add(ToRelativePath(absolutePath));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                _logger.LogWarning(ex, "Failed to migrate presentation {Path}.", presentationPath);
                AddIssue(
                    result,
                    AuditIssueSeverity.Warning,
                    "presentation-open-failed",
                    $"Presentation '{presentationPath}' could not be opened for migration: {ex.Message}",
                    absolutePath);
            }
        }
    }

    private void NormalizeTheme(
        ThemeTemplate theme,
        string themesPath,
        IDictionary<string, MediaLookupEntry> mediaLookup,
        ContentRootMediaMigrationResult result)
    {
        theme.Name = NormalizeRequiredString(theme.Name, fallback: theme.Id);
        foreach (var slide in theme.Slides)
        {
            slide.Name = NormalizeOptionalString(slide.Name);
            if (slide.MediaCues == null)
                continue;

            foreach (var cue in slide.MediaCues)
                NormalizeCue(cue, project: null, themesPath, mediaLookup, result, $"theme:{theme.Id}/{slide.Id}");
        }
    }

    private void NormalizeProject(
        PresentationProject project,
        string projectPath,
        IDictionary<string, MediaLookupEntry> mediaLookup,
        ContentRootMediaMigrationResult result)
    {
        foreach (var slide in project.Slides)
        {
            foreach (var cue in slide.MediaCues)
                NormalizeCue(cue, project, projectPath, mediaLookup, result, $"slide:{slide.Id}");
        }

        foreach (var embeddedTheme in project.EmbeddedThemes)
        {
            if (embeddedTheme.Template == null)
                continue;

            NormalizeTheme(embeddedTheme.Template, projectPath, mediaLookup, result);
        }
    }

    private void NormalizeCue(
        SlideMediaCue cue,
        PresentationProject? project,
        string containerPath,
        IDictionary<string, MediaLookupEntry> mediaLookup,
        ContentRootMediaMigrationResult result,
        string cueOwner)
    {
        cue.MediaId = NormalizeRequiredString(cue.MediaId, fallback: cue.Id);
        cue.MediaType = NormalizeRequiredString(cue.MediaType, fallback: "image");
        cue.Target = SlideMediaLayerBuilder.MapCueTarget(cue.Target);
        cue.Fit = NormalizeOptionalString(cue.Fit);
        cue.DisplayName = MediaCueDisplayNameResolver.Normalize(cue.DisplayName);
        cue.Transition = TransitionStorageNormalizer.NormalizeForStorage(cue.Transition);

        if (cue.DisplayName == null)
        {
            cue.DisplayName = MediaCueDisplayNameResolver.ResolveProjectMediaDisplayName(project, cue.MediaId)
                ?? ResolveLookupDisplayName(mediaLookup, cue.MediaId);
        }

        if (cue.DisplayName == null)
        {
            AddIssue(
                result,
                AuditIssueSeverity.Warning,
                "cue-display-name-unresolved",
                $"Could not resolve a stored display name for cue '{cue.Id}' ({cueOwner}) in '{ToRelativePath(containerPath)}'.",
                containerPath);
        }

        if (!CueReferenceExists(cue, project, mediaLookup))
        {
            AddIssue(
                result,
                AuditIssueSeverity.Warning,
                "cue-media-reference-missing",
                $"Cue '{cue.Id}' ({cueOwner}) references media '{cue.MediaId}' that could not be resolved.",
                containerPath);
        }
    }

    private void NormalizeMediaItem(
        MediaLibraryItem item,
        string sourcePath,
        IDictionary<string, MediaLookupEntry> mediaLookup,
        ContentRootMediaMigrationResult result)
    {
        result.MediaItemsScanned++;
        item.Id = NormalizeRequiredString(item.Id, fallback: Guid.NewGuid().ToString("N"));
        item.Name = NormalizeRequiredString(item.Name, fallback: string.Empty);
        item.Path = NormalizeStoredPath(item.Path);
        item.Type = NormalizeRequiredString(item.Type, fallback: MediaInference.ResolveEffectiveMediaType(item.Type, item.Path));
        item.CueDefaults ??= new MediaCueDefaults();
        item.CueDefaults.Target = SlideMediaLayerBuilder.MapCueTarget(item.CueDefaults.Target);
        item.CueDefaults.Fit = NormalizeOptionalString(item.CueDefaults.Fit) ?? "cover";
        item.CueDefaults.Transition = TransitionStorageNormalizer.NormalizeForStorage(item.CueDefaults.Transition);

        var resolvedPath = ResolveMediaPath(item.Path);
        StoreLookup(mediaLookup, item, resolvedPath);

        if (resolvedPath == null || !File.Exists(resolvedPath))
        {
            AddIssue(
                result,
                AuditIssueSeverity.Warning,
                "media-file-missing",
                $"Media item '{item.Id}' points at a missing file '{item.Path}'.",
                sourcePath);
        }
    }

    private void NormalizePresentationRefs(
        IList<PresentationRefDto> refs,
        ISet<string> referencedPresentationPaths,
        ContentRootMediaMigrationResult result,
        string missingCode,
        string ownerId)
    {
        foreach (var reference in refs)
        {
            reference.Path = NormalizePresentationReferencePath(reference.Path);
            reference.Title = NormalizeRequiredString(reference.Title, fallback: reference.Title);
            reference.UpdatedAt = NormalizeOptionalString(reference.UpdatedAt) ?? reference.UpdatedAt;
            referencedPresentationPaths.Add(reference.Path);

            if (File.Exists(_paths.ResolvePresentationPath(reference.Path)))
                continue;

            AddIssue(
                result,
                AuditIssueSeverity.Warning,
                missingCode,
                $"Catalog entry '{ownerId}' references missing presentation '{reference.Path}'.",
                _paths.ResolvePresentationPath(reference.Path));
        }
    }

    private bool CueReferenceExists(
        SlideMediaCue cue,
        PresentationProject? project,
        IDictionary<string, MediaLookupEntry> mediaLookup)
    {
        if (project?.Manifest?.Media.Any(entry =>
                string.Equals(NormalizeLookupKey(entry.Id), NormalizeLookupKey(cue.MediaId), StringComparison.OrdinalIgnoreCase)
                || string.Equals(NormalizeLookupKey(entry.Path), NormalizeLookupKey(cue.MediaId), StringComparison.OrdinalIgnoreCase)
                || string.Equals(NormalizeLookupKey(entry.SourcePath), NormalizeLookupKey(cue.MediaId), StringComparison.OrdinalIgnoreCase)) == true)
        {
            return true;
        }

        if (TryResolveLookup(mediaLookup, cue.MediaId, out var lookupEntry))
            return lookupEntry.ResolvedPath == null || File.Exists(lookupEntry.ResolvedPath);

        var resolvedPath = ResolveMediaPath(cue.MediaId);
        return resolvedPath != null && File.Exists(resolvedPath);
    }

    private string? ResolveLookupDisplayName(IDictionary<string, MediaLookupEntry> mediaLookup, string? mediaId)
    {
        return TryResolveLookup(mediaLookup, mediaId, out var entry)
            ? entry.DisplayName
            : null;
    }

    private bool TryResolveLookup(
        IDictionary<string, MediaLookupEntry> mediaLookup,
        string? mediaId,
        out MediaLookupEntry entry)
    {
        var key = NormalizeLookupKey(mediaId);
        if (key != null && mediaLookup.TryGetValue(key, out entry))
            return true;

        var resolvedPath = ResolveMediaPath(mediaId);
        if (resolvedPath != null && mediaLookup.TryGetValue(NormalizeLookupKey(resolvedPath)!, out entry))
            return true;

        var relativePath = resolvedPath == null ? null : _paths.ToContentRelativePath(resolvedPath);
        if (relativePath != null && mediaLookup.TryGetValue(NormalizeLookupKey(relativePath)!, out entry))
            return true;

        entry = default;
        return false;
    }

    private void StoreLookup(
        IDictionary<string, MediaLookupEntry> mediaLookup,
        MediaLibraryItem item,
        string? resolvedPath)
    {
        var entry = new MediaLookupEntry(MediaCueDisplayNameResolver.Normalize(item.Name), resolvedPath);
        StoreLookupKey(mediaLookup, item.Id, entry);
        StoreLookupKey(mediaLookup, item.Path, entry);

        if (resolvedPath != null)
        {
            StoreLookupKey(mediaLookup, resolvedPath, entry);
            StoreLookupKey(mediaLookup, _paths.ToContentRelativePath(resolvedPath), entry);
        }
    }

    private static void StoreLookupKey(
        IDictionary<string, MediaLookupEntry> mediaLookup,
        string? key,
        MediaLookupEntry entry)
    {
        var normalizedKey = NormalizeLookupKey(key);
        if (normalizedKey == null)
            return;

        if (!mediaLookup.TryGetValue(normalizedKey, out var existing))
        {
            mediaLookup[normalizedKey] = entry;
            return;
        }

        var displayName = existing.DisplayName ?? entry.DisplayName;
        var resolvedPath = existing.ResolvedPath ?? entry.ResolvedPath;
        mediaLookup[normalizedKey] = new MediaLookupEntry(displayName, resolvedPath);
    }

    private string NormalizePresentationReferencePath(string path)
    {
        var trimmed = NormalizeRequiredString(path, fallback: path);
        if (string.IsNullOrWhiteSpace(trimmed))
            return trimmed;

        if (Path.IsPathRooted(trimmed))
        {
            var relativeOrAbsolute = _paths.ToContentRelativePath(Path.GetFullPath(trimmed));
            return Path.IsPathRooted(relativeOrAbsolute)
                ? Path.GetFullPath(trimmed)
                : relativeOrAbsolute.Replace('\\', '/');
        }

        return trimmed.Replace('\\', '/');
    }

    private string NormalizeStoredPath(string path)
    {
        var trimmed = NormalizeRequiredString(path, fallback: path);
        if (string.IsNullOrWhiteSpace(trimmed))
            return trimmed;

        if (Path.IsPathRooted(trimmed))
            return Path.GetFullPath(trimmed);

        return trimmed.Replace('\\', '/');
    }

    private string? ResolveMediaPath(string? path)
    {
        var trimmed = NormalizeOptionalString(path);
        if (trimmed == null)
            return null;

        if (Path.IsPathRooted(trimmed))
            return Path.GetFullPath(trimmed);

        return Path.GetFullPath(Path.Combine(_paths.GetDocumentsDataDirectory(), trimmed.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string NormalizeRequiredString(string? value, string fallback)
    {
        var normalized = NormalizeOptionalString(value);
        return normalized ?? fallback;
    }

    private static string? NormalizeOptionalString(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private string ToRelativePath(string path) =>
        _paths.ToContentRelativePath(path).Replace('\\', '/');

    private static string? NormalizeLookupKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (Path.IsPathRooted(trimmed))
            trimmed = Path.GetFullPath(trimmed);

        return trimmed.Replace('\\', '/');
    }

    private async Task<T?> ReadJsonOrDefaultAsync<T>(
        string path,
        ContentRootMediaMigrationResult result,
        CancellationToken cancellationToken) where T : class
    {
        if (!File.Exists(path))
            return null;

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(ex, "Could not read {Path} during content media migration.", path);
            AddIssue(
                result,
                AuditIssueSeverity.Warning,
                "content-read-failed",
                $"Could not read '{path}' during content media migration: {ex.Message}",
                path);
            return null;
        }
    }

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static void AddIssue(
        ContentRootMediaMigrationResult result,
        AuditIssueSeverity severity,
        string code,
        string message,
        string? path)
    {
        result.Issues.Add(new AuditIssue
        {
            Severity = severity,
            Code = code,
            Message = message,
            Path = path,
        });
    }

    private readonly record struct MediaLookupEntry(string? DisplayName, string? ResolvedPath);
}