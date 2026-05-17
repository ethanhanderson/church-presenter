using System.Text.Json;


using Microsoft.Extensions.Logging;

namespace ChurchPresenter.Services.Media;

/// <inheritdoc />
public sealed partial class MediaLibraryService(
    IContentDirectoryService contentDirectories,
    ILogger<MediaLibraryService> logger,
    IContentChangeBus? contentChanges = null) : IMediaLibraryService
{
    private readonly IContentDirectoryService _dirs = contentDirectories ?? throw new ArgumentNullException(nameof(contentDirectories));
    private readonly ILogger<MediaLibraryService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IContentChangeBus? _contentChanges = contentChanges;

    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // ── Playlists ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<IReadOnlyList<MediaPlaylistManifest>> GetPlaylistsAsync(CancellationToken ct = default)
    {
        var index = await ReadIndexAsync(ct).ConfigureAwait(false);
        var results = new List<MediaPlaylistManifest>(index.Playlists.Count);
        foreach (var entry in index.Playlists)
        {
            var manifest = await GetPlaylistAsync(entry.Id, ct).ConfigureAwait(false);
            if (manifest != null)
                results.Add(manifest);
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MediaLibraryItem>> GetRootItemsAsync(CancellationToken ct = default)
    {
        var index = await ReadIndexAsync(ct).ConfigureAwait(false);
        return index.Items
            .Select(CloneItem)
            .ToList();
    }

    /// <inheritdoc />
    public string ResolveStoredMediaPath(string? storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath))
            return string.Empty;

        var trimmed = storedPath.Trim();
        if (Path.IsPathRooted(trimmed))
            return Path.GetFullPath(trimmed);

        var root = Path.GetFullPath(_dirs.GetDocumentsDataDirectory());
        return Path.GetFullPath(Path.Combine(root, trimmed.Replace('/', Path.DirectorySeparatorChar)));
    }

    /// <inheritdoc />
    public async Task<MediaMigrationResult> MigrateExternalMediaToManagedStorageAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_dirs.GetManagedMediaFilesDirectory());

        var result = new MediaMigrationResult();
        var index = await ReadIndexAsync(ct).ConfigureAwait(false);
        var changed = false;

        foreach (var item in index.Items)
        {
            ct.ThrowIfCancellationRequested();
            result.TotalItemsScanned++;
            if (await TryMigrateItemPathAsync(item, result, ct).ConfigureAwait(false))
                changed = true;
        }

        foreach (var entry in index.Playlists)
        {
            ct.ThrowIfCancellationRequested();
            var manifest = await GetPlaylistAsync(entry.Id, ct).ConfigureAwait(false);
            if (manifest == null)
                continue;

            var playlistChanged = false;
            foreach (var item in manifest.Items)
            {
                ct.ThrowIfCancellationRequested();
                result.TotalItemsScanned++;
                if (await TryMigrateItemPathAsync(item, result, ct).ConfigureAwait(false))
                    playlistChanged = true;
            }

            if (playlistChanged)
            {
                manifest.UpdatedAt = DateTime.UtcNow.ToString("o");
                await WritePlaylistAsync(manifest, ct).ConfigureAwait(false);
                changed = true;
            }
        }

        if (changed)
            await WriteIndexAsync(index, ct).ConfigureAwait(false);

        if (result.CopiedIntoManagedStorage > 0)
        {
            _logger.LogInformation(
                "Media migration: copied {Copied} item(s) into managed Media/Files (skipped {Skipped}, missing {Missing}).",
                result.CopiedIntoManagedStorage,
                result.SkippedAlreadyManaged,
                result.MissingSourceFiles);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<MediaLinkStatistics> GetMediaLinkStatisticsAsync(CancellationToken ct = default)
    {
        var stats = new MediaLinkStatistics();
        var index = await ReadIndexAsync(ct).ConfigureAwait(false);

        foreach (var item in index.Items)
        {
            ct.ThrowIfCancellationRequested();
            AccumulateStats(item, stats);
        }

        foreach (var entry in index.Playlists)
        {
            var manifest = await GetPlaylistAsync(entry.Id, ct).ConfigureAwait(false);
            if (manifest == null)
                continue;

            foreach (var item in manifest.Items)
            {
                ct.ThrowIfCancellationRequested();
                AccumulateStats(item, stats);
            }
        }

        return stats;
    }

    /// <inheritdoc />
    public async Task<MediaPlaylistManifest?> GetPlaylistAsync(string playlistId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playlistId);
        var path = _dirs.GetMediaPlaylistManifestPath(playlistId);
        if (!File.Exists(path))
            return null;

        try
        {
            await using var fs = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<MediaPlaylistManifest>(fs, _json, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read media playlist {Id}.", playlistId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<MediaPlaylistManifest> CreatePlaylistAsync(string name, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var id = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow.ToString("o");
        var manifest = new MediaPlaylistManifest
        {
            Id = id,
            Name = name.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };

        var dir = _dirs.GetMediaPlaylistDirectory(id);
        Directory.CreateDirectory(dir);
        await WritePlaylistAsync(manifest, ct).ConfigureAwait(false);
        await AddIndexEntryAsync(manifest, ct).ConfigureAwait(false);

        return manifest;
    }

    /// <inheritdoc />
    public async Task<bool> RenamePlaylistAsync(string playlistId, string newName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playlistId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        var manifest = await GetPlaylistAsync(playlistId, ct).ConfigureAwait(false);
        if (manifest == null)
            return false;

        manifest.Name = newName.Trim();
        manifest.UpdatedAt = DateTime.UtcNow.ToString("o");
        await WritePlaylistAsync(manifest, ct).ConfigureAwait(false);
        await UpdateIndexEntryNameAsync(playlistId, manifest.Name, ct).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> DeletePlaylistAsync(string playlistId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playlistId);

        var dir = _dirs.GetMediaPlaylistDirectory(playlistId);
        if (!Directory.Exists(dir))
            return false;

        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete media playlist directory {Dir}.", dir);
        }

        await RemoveIndexEntryAsync(playlistId, ct).ConfigureAwait(false);
        return true;
    }
}