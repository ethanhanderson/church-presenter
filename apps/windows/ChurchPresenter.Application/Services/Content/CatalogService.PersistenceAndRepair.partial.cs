using System.Text.Json;
using System.Text.Json.Serialization;


using Microsoft.Extensions.Logging;

namespace ChurchPresenter.Services.Content;

public sealed partial class CatalogService
{
    // ── Persistence ──────────────────────────────────────────────────────────

    private async Task PersistToRegistriesAsync(CatalogDto catalog)
    {
        var maintenance = new MaintenanceRunState(ContentMaintenanceTrigger.Default);

        var libraries = CoalesceLibraries(catalog.Libraries, maintenance)
            .Where(static l => !string.IsNullOrWhiteSpace(l.Id))
            .ToList();
        var playlists = CoalescePlaylists(catalog.Playlists, maintenance)
            .Where(static p => !string.IsNullOrWhiteSpace(p.Id))
            .ToList();

        // Save library manifests
        foreach (var library in libraries)
        {
            var manifest = new LibraryManifest
            {
                Id = library.Id,
                Name = library.Name,
                Description = library.Description,
                CreatedAt = library.CreatedAt ?? DateTimeOffset.UtcNow.ToString("O"),
                UpdatedAt = library.UpdatedAt,
                DefaultFolder = library.DefaultFolder,
                Presentations = library.Presentations.Select(ClonePresentationRef).ToList(),
            };
            try
            {
                await _libraryRegistry.SaveAsync(manifest).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not save library manifest for '{LibraryId}'.", library.Id);
            }
        }

        // Save playlist manifests
        foreach (var playlist in playlists)
        {
            var manifest = new PlaylistManifest
            {
                Id = playlist.Id,
                Name = playlist.Name,
                Description = playlist.Description,
                CreatedAt = playlist.CreatedAt ?? DateTimeOffset.UtcNow.ToString("O"),
                UpdatedAt = playlist.UpdatedAt,
                Items = playlist.Items.Select(ClonePresentationRef).ToList(),
                ExternalSet = playlist.ExternalSet == null ? null : CloneExternalSet(playlist.ExternalSet),
                Sync = playlist.Sync == null ? null : CloneSyncMetadata(playlist.Sync),
            };
            try
            {
                await _playlistRegistry.SaveAsync(manifest).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not save playlist manifest for '{PlaylistId}'.", playlist.Id);
            }
        }

        // Remove manifests for entries that were removed from the catalog
        await PruneDeletedLibraryManifestsAsync(libraries).ConfigureAwait(false);
        await PruneDeletedPlaylistManifestsAsync(playlists).ConfigureAwait(false);

        await SaveLibraryIndexOrderAsync(libraries).ConfigureAwait(false);
        await SavePlaylistIndexOrderAsync(playlists).ConfigureAwait(false);
    }

    private async Task SaveLibraryIndexOrderAsync(IReadOnlyList<LibraryDto> libraries)
    {
        var index = new DomainIndex
        {
            Entries = libraries.Select(library => new DomainIndexEntry
            {
                Id = library.Id,
                Name = library.Name,
                Description = library.Description,
                CreatedAt = library.CreatedAt ?? DateTimeOffset.UtcNow.ToString("O"),
                UpdatedAt = library.UpdatedAt,
            }).ToList(),
        };

        await _libraryRegistry.SaveIndexAsync(index).ConfigureAwait(false);
    }

    private async Task SavePlaylistIndexOrderAsync(IReadOnlyList<PlaylistDto> playlists)
    {
        var index = new DomainIndex
        {
            Entries = playlists.Select(playlist => new DomainIndexEntry
            {
                Id = playlist.Id,
                Name = playlist.Name,
                Description = playlist.Description,
                CreatedAt = playlist.CreatedAt ?? DateTimeOffset.UtcNow.ToString("O"),
                UpdatedAt = playlist.UpdatedAt,
            }).ToList(),
        };

        await _playlistRegistry.SaveIndexAsync(index).ConfigureAwait(false);
    }

    private async Task PruneDeletedLibraryManifestsAsync(IEnumerable<LibraryDto> activeLibraries)
    {
        var activeIds = activeLibraries.Select(l => l.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var index = await _libraryRegistry.LoadIndexAsync().ConfigureAwait(false);
        var toRemove = index.Entries
            .Where(e => !activeIds.Contains(e.Id))
            .Select(e => e.Id)
            .ToList();

        foreach (var id in toRemove)
        {
            try { await _libraryRegistry.DeleteAsync(id).ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not prune deleted library '{LibraryId}'.", id); }
        }
    }

    private async Task PruneDeletedPlaylistManifestsAsync(IEnumerable<PlaylistDto> activePlaylists)
    {
        var activeIds = activePlaylists.Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var index = await _playlistRegistry.LoadIndexAsync().ConfigureAwait(false);
        var toRemove = index.Entries
            .Where(e => !activeIds.Contains(e.Id))
            .Select(e => e.Id)
            .ToList();

        foreach (var id in toRemove)
        {
            try { await _playlistRegistry.DeleteAsync(id).ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not prune deleted playlist '{PlaylistId}'.", id); }
        }
    }

    // ── Presentation discovery and normalization ──────────────────────────────

    private Dictionary<string, PresentationRefDto> DiscoverPresentations()
    {
        var presentationsRoot = _paths.GetPresentationsRootDirectory();
        if (!Directory.Exists(presentationsRoot))
            return new Dictionary<string, PresentationRefDto>(StringComparer.OrdinalIgnoreCase);

        var discovered = new Dictionary<string, PresentationRefDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in EnumerateFilesSafe(presentationsRoot, "*.cpres", SearchOption.AllDirectories))
        {
            try
            {
                var parsed = _cpres.Open(file);
                var manifest = JsonSerializer.Deserialize<PresentationManifestDto>(parsed.ManifestJson, JsonOptions)
                    ?? new PresentationManifestDto();

                var relativePath = _paths.ToContentRelativePath(file);
                var key = NormalizePath(relativePath);
                discovered[key] = new PresentationRefDto
                {
                    Path = relativePath,
                    Title = string.IsNullOrWhiteSpace(manifest.Title)
                        ? Path.GetFileNameWithoutExtension(file)
                        : manifest.Title,
                    UpdatedAt = string.IsNullOrWhiteSpace(manifest.UpdatedAt)
                        ? File.GetLastWriteTimeUtc(file).ToString("O")
                        : manifest.UpdatedAt,
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipping unreadable presentation bundle at {Path}.", file);
            }
        }

        return discovered;
    }

    private void NormalizeLibraries(
        List<LibraryDto> libraries,
        IReadOnlyDictionary<string, PresentationRefDto> discoveredPresentations)
    {
        foreach (var library in libraries)
            library.Presentations = NormalizePresentationRefs(library.Presentations, discoveredPresentations, preserveOrder: true, allowDuplicates: false);
    }

    private void NormalizePlaylists(
        List<PlaylistDto> playlists,
        IReadOnlyDictionary<string, PresentationRefDto> discoveredPresentations)
    {
        foreach (var playlist in playlists)
            playlist.Items = NormalizePresentationRefs(playlist.Items, discoveredPresentations, preserveOrder: true, allowDuplicates: true);
    }

    private List<PresentationRefDto> NormalizePresentationRefs(
        IEnumerable<PresentationRefDto>? refs,
        IReadOnlyDictionary<string, PresentationRefDto> discoveredPresentations,
        bool preserveOrder,
        bool allowDuplicates)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<PresentationRefDto>();
        foreach (var presentation in refs ?? [])
        {
            if (string.IsNullOrWhiteSpace(presentation.Path))
                continue;

            var canonicalPath = GetCanonicalStoredPresentationPath(presentation.Path);
            var key = NormalizePath(canonicalPath);
            if (!discoveredPresentations.TryGetValue(key, out var discovered))
                continue;
            if (!allowDuplicates && !seen.Add(key))
                continue;

            normalized.Add(new PresentationRefDto
            {
                Path = discovered.Path,
                Title = discovered.Title,
                UpdatedAt = discovered.UpdatedAt,
                ArrangementId = presentation.ArrangementId,
                DestinationLayerId = presentation.DestinationLayerId,
                ThumbnailData = presentation.ThumbnailData,
            });
        }

        if (!preserveOrder)
        {
            normalized = normalized
                .OrderBy(static p => p.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static p => p.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return normalized;
    }

    // ── Content layout repair ─────────────────────────────────────────────────

    private async Task RepairContentLayoutAsync(MaintenanceRunState maintenance)
    {
        var contentRoot = _paths.GetDocumentsDataDirectory();
        var presentationsRoot = Path.GetFullPath(_paths.GetPresentationsRootDirectory());

        foreach (var file in EnumerateFilesSafe(contentRoot, "*.cpres", SearchOption.AllDirectories))
        {
            var fullPath = Path.GetFullPath(file);
            if (IsUnderDirectory(fullPath, presentationsRoot))
                continue;

            try
            {
                var destinationPath = GetManagedPresentationPath(fullPath);
                if (string.Equals(fullPath, destinationPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Move(fullPath, destinationPath);
                _logger.LogInformation("Moved orphan presentation bundle from {SourcePath} to {DestinationPath}.", fullPath, destinationPath);
                maintenance.Record(
                    "info",
                    "orphan-bundle-moved",
                    $"Moved orphan presentation bundle into the managed presentations area: {Path.GetFileName(destinationPath)}.",
                    destinationPath,
                    countsAsRepair: true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not relocate orphan presentation bundle at {Path}.", fullPath);
                maintenance.Record(
                    "warning",
                    "orphan-bundle-move-failed",
                    $"Could not relocate orphan presentation bundle: {Path.GetFileName(fullPath)}.",
                    fullPath);
            }
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private string GetManagedPresentationPath(string file)
    {
        var manifest = ReadManifestOrNull(file);
        var presentationId = manifest?.PresentationId;
        if (string.IsNullOrWhiteSpace(presentationId))
            presentationId = Path.GetFileNameWithoutExtension(file);

        var title = string.IsNullOrWhiteSpace(manifest?.Title)
            ? Path.GetFileNameWithoutExtension(file)
            : manifest!.Title!;

        var destination = LooksLikeSongBundle(file)
            ? _paths.GetSongPresentationPath(presentationId)
            : _paths.GeneratePresentationPath(title, presentationId);

        return EnsureUniqueFilePath(destination, file);
    }

    private PresentationManifestDto? ReadManifestOrNull(string file)
    {
        try
        {
            var parsed = _cpres.Open(file);
            return JsonSerializer.Deserialize<PresentationManifestDto>(parsed.ManifestJson, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read manifest for orphan bundle {Path}; falling back to file name.", file);
            return null;
        }
    }

    private bool LooksLikeSongBundle(string file)
    {
        var relative = NormalizePath(_paths.ToContentRelativePath(file));
        return relative.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(static segment => string.Equals(segment, "songs", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsUnderDirectory(string path, string directory)
    {
        var fullPath = Path.GetFullPath(path);
        var fullDirectory = Path.GetFullPath(directory);
        if (!fullDirectory.EndsWith(Path.DirectorySeparatorChar))
            fullDirectory += Path.DirectorySeparatorChar;

        return fullPath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureUniqueFilePath(string desiredPath, string sourcePath)
    {
        var fullDesiredPath = Path.GetFullPath(desiredPath);
        if (!File.Exists(fullDesiredPath) || string.Equals(Path.GetFullPath(sourcePath), fullDesiredPath, StringComparison.OrdinalIgnoreCase))
            return fullDesiredPath;

        var directory = Path.GetDirectoryName(fullDesiredPath)!;
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fullDesiredPath);
        var extension = Path.GetExtension(fullDesiredPath);
        var counter = 2;
        string candidate;
        do
        {
            candidate = Path.Combine(directory, $"{fileNameWithoutExtension}_{counter}{extension}");
            counter++;
        } while (File.Exists(candidate));

        return candidate;
    }

}