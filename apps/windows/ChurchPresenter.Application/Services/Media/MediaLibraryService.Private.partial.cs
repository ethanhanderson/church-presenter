using System.Collections.Generic;
using System.Text.Json;


using Microsoft.Extensions.Logging;

namespace ChurchPresenter.Services.Media;

public sealed partial class MediaLibraryService
{
    // ── Private helpers ──────────────────────────────────────────────────────

    private void AccumulateStats(MediaLibraryItem item, MediaLinkStatistics stats)
    {
        stats.TotalItems++;
        var abs = ResolveStoredMediaPath(item.Path);
        if (IsManagedMediaRelativePath(item.Path))
            stats.ManagedItems++;

        if (string.IsNullOrWhiteSpace(abs) || !File.Exists(abs))
        {
            stats.MissingFiles++;
            return;
        }

        if (!IsManagedMediaRelativePath(item.Path))
            stats.ExternalPathReferences++;
    }

    private async Task<bool> TryMigrateItemPathAsync(MediaLibraryItem item, MediaMigrationResult result, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(item.Path))
        {
            result.MissingSourceFiles++;
            return false;
        }

        if (IsManagedMediaRelativePath(item.Path))
        {
            if (File.Exists(ResolveStoredMediaPath(item.Path)))
            {
                result.SkippedAlreadyManaged++;
                return false;
            }

            result.MissingSourceFiles++;
            return false;
        }

        var sourceAbs = ResolveStoredMediaPath(item.Path);
        if (!File.Exists(sourceAbs))
        {
            result.MissingSourceFiles++;
            return false;
        }

        var managedRoot = Path.GetFullPath(_dirs.GetManagedMediaFilesDirectory());
        if (sourceAbs.StartsWith(managedRoot, StringComparison.OrdinalIgnoreCase))
        {
            var rel = ToContentRelativeMediaPath(sourceAbs);
            if (IsManagedMediaRelativePath(rel))
            {
                item.Path = rel;
                result.SkippedAlreadyManaged++;
                return true;
            }
        }

        item.Path = await CopyImportedFileIntoManagedStorageAsync(sourceAbs, item.Id, ct).ConfigureAwait(false);
        result.CopiedIntoManagedStorage++;
        return true;
    }

    private string ToContentRelativeMediaPath(string absolutePath)
    {
        var root = Path.GetFullPath(_dirs.GetDocumentsDataDirectory());
        var full = Path.GetFullPath(absolutePath);
        var rel = Path.GetRelativePath(root, full);
        return rel.StartsWith("..", StringComparison.Ordinal) ? full : rel.Replace('\\', '/');
    }

    private async Task<MediaLibraryItem> CreateItemFromImportedFileAsync(string filePath, CancellationToken ct)
    {
        var fullSource = Path.GetFullPath(filePath);
        if (!File.Exists(fullSource))
            throw new FileNotFoundException("Could not import media file.", fullSource);

        var id = Guid.NewGuid().ToString("N");
        var storedPath = await CopyImportedFileIntoManagedStorageAsync(fullSource, id, ct).ConfigureAwait(false);
        var type = InferMediaType(fullSource);
        return new MediaLibraryItem
        {
            Id = id,
            Name = Path.GetFileNameWithoutExtension(fullSource),
            Path = storedPath,
            Type = type,
            AddedAt = DateTime.UtcNow.ToString("o"),
            CueDefaults = BuildDefaultCueSettings(type),
        };
    }

    private async Task<string> CopyImportedFileIntoManagedStorageAsync(string sourceAbsolutePath, string itemId, CancellationToken ct)
    {
        Directory.CreateDirectory(_dirs.GetManagedMediaFilesDirectory());
        var ext = Path.GetExtension(sourceAbsolutePath);
        if (string.IsNullOrWhiteSpace(ext))
            ext = ".bin";

        var relative = $"Media/Files/{itemId}{ext}";
        var dest = Path.Combine(_dirs.GetManagedMediaFilesDirectory(), $"{itemId}{ext}");

        if (!string.Equals(Path.GetFullPath(sourceAbsolutePath), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
        {
            await Task.Run(() => File.Copy(sourceAbsolutePath, dest, overwrite: true), ct).ConfigureAwait(false);
        }

        return relative;
    }

    private async Task<MediaLibraryItem> CreateDuplicateItemAsync(
        MediaLibraryItem source,
        IEnumerable<string> existingNames,
        CancellationToken ct)
    {
        var duplicate = CloneItem(source);
        duplicate.Id = Guid.NewGuid().ToString("N");
        duplicate.Name = CreateDuplicateName(source.Name, existingNames);
        duplicate.AddedAt = DateTime.UtcNow.ToString("o");

        var sourceAbs = ResolveStoredMediaPath(source.Path);
        if (File.Exists(sourceAbs))
            duplicate.Path = await CopyImportedFileIntoManagedStorageAsync(sourceAbs, duplicate.Id, ct).ConfigureAwait(false);

        return duplicate;
    }

    private static bool IsManagedMediaRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var n = path.Replace('\\', '/').TrimStart('/');
        return n.StartsWith("Media/Files/", StringComparison.OrdinalIgnoreCase);
    }

    private void TryDeleteManagedMediaFile(string? storedPath)
    {
        if (!IsManagedMediaRelativePath(storedPath))
            return;

        var abs = ResolveStoredMediaPath(storedPath);
        if (!File.Exists(abs))
            return;

        try
        {
            File.Delete(abs);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not delete managed media file {Path}.", abs);
        }
    }

    private async Task WritePlaylistAsync(MediaPlaylistManifest manifest, CancellationToken ct)
    {
        var path = _dirs.GetMediaPlaylistManifestPath(manifest.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(manifest, _json);
        await File.WriteAllTextAsync(path, json, ct).ConfigureAwait(false);
    }

    private async Task<MediaLibraryIndex> ReadIndexAsync(CancellationToken ct)
    {
        var path = _dirs.GetMediaIndexPath();
        if (!File.Exists(path))
            return new MediaLibraryIndex();

        try
        {
            await using var fs = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<MediaLibraryIndex>(fs, _json, ct).ConfigureAwait(false)
                   ?? new MediaLibraryIndex();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read media library index at {Path}.", path);
            return new MediaLibraryIndex();
        }
    }

    private async Task WriteIndexAsync(MediaLibraryIndex index, CancellationToken ct)
    {
        var path = _dirs.GetMediaIndexPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(index, _json);
        await File.WriteAllTextAsync(path, json, ct).ConfigureAwait(false);
    }

    private async Task AddIndexEntryAsync(MediaPlaylistManifest manifest, CancellationToken ct)
    {
        var index = await ReadIndexAsync(ct).ConfigureAwait(false);
        index.Playlists.RemoveAll(e => string.Equals(e.Id, manifest.Id, StringComparison.OrdinalIgnoreCase));
        index.Playlists.Add(new MediaPlaylistIndexEntry
        {
            Id = manifest.Id,
            Name = manifest.Name,
            CreatedAt = manifest.CreatedAt,
            UpdatedAt = manifest.UpdatedAt,
        });
        await WriteIndexAsync(index, ct).ConfigureAwait(false);
    }

    private async Task UpdateIndexEntryNameAsync(string playlistId, string newName, CancellationToken ct)
    {
        var index = await ReadIndexAsync(ct).ConfigureAwait(false);
        var entry = index.Playlists.FirstOrDefault(e =>
            string.Equals(e.Id, playlistId, StringComparison.OrdinalIgnoreCase));
        if (entry != null)
        {
            entry.Name = newName;
            entry.UpdatedAt = DateTime.UtcNow.ToString("o");
            await WriteIndexAsync(index, ct).ConfigureAwait(false);
        }
    }

    private async Task RemoveIndexEntryAsync(string playlistId, CancellationToken ct)
    {
        var index = await ReadIndexAsync(ct).ConfigureAwait(false);
        var removed = index.Playlists.RemoveAll(e =>
            string.Equals(e.Id, playlistId, StringComparison.OrdinalIgnoreCase));
        if (removed > 0)
            await WriteIndexAsync(index, ct).ConfigureAwait(false);
    }

    private static string InferMediaType(string filePath) => MediaInference.InferMediaTypeFromPath(filePath);

    private static MediaCueDefaults BuildDefaultCueSettings(string mediaType) => new()
    {
        Target = "mediaUnderlay",
        Fit = "cover",
        Autoplay = !string.Equals(mediaType, "image", StringComparison.OrdinalIgnoreCase),
        Loop = string.Equals(mediaType, "video", StringComparison.OrdinalIgnoreCase),
        Muted = string.Equals(mediaType, "video", StringComparison.OrdinalIgnoreCase),
    };

    private static MediaLibraryItem CloneItem(MediaLibraryItem item) => new()
    {
        Id = item.Id,
        Name = item.Name,
        Path = item.Path,
        Type = item.Type,
        Mime = item.Mime,
        Duration = item.Duration,
        Width = item.Width,
        Height = item.Height,
        AddedAt = item.AddedAt,
        CueDefaults = new MediaCueDefaults
        {
            Target = item.CueDefaults.Target,
            Fit = item.CueDefaults.Fit,
            Autoplay = item.CueDefaults.Autoplay,
            Loop = item.CueDefaults.Loop,
            Muted = item.CueDefaults.Muted,
            Transition = CloneSlideTransition(item.CueDefaults.Transition),
        },
    };

    private static SlideTransition? CloneSlideTransition(SlideTransition? source)
    {
        if (source == null)
        {
            return null;
        }

        return new SlideTransition
        {
            Type = source.Type,
            Duration = source.Duration,
            Easing = source.Easing,
            Parameters = source.Parameters == null
                ? null
                : new Dictionary<string, string>(source.Parameters, StringComparer.OrdinalIgnoreCase),
        };
    }

    private static string CreateDuplicateName(string sourceName, IEnumerable<string> existingNames)
    {
        var baseName = string.IsNullOrWhiteSpace(sourceName) ? "Copy" : $"{sourceName} Copy";
        var usedNames = new HashSet<string>(existingNames.Where(name => !string.IsNullOrWhiteSpace(name)), StringComparer.OrdinalIgnoreCase);
        if (!usedNames.Contains(baseName))
            return baseName;

        var suffix = 2;
        while (usedNames.Contains($"{baseName} {suffix}"))
            suffix++;

        return $"{baseName} {suffix}";
    }
}