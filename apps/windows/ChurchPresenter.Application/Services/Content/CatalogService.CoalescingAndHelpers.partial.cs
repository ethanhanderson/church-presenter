using System.Text.Json;
using System.Text.Json.Serialization;


using Microsoft.Extensions.Logging;

namespace ChurchPresenter.Services.Content;

public sealed partial class CatalogService
{
    // ── Coalescing ────────────────────────────────────────────────────────────

    private List<LibraryDto> CoalesceLibraries(IEnumerable<LibraryDto> libraries, MaintenanceRunState maintenance)
    {
        var merged = new List<LibraryDto>();
        var duplicateCount = 0;
        foreach (var library in libraries)
        {
            if (string.IsNullOrWhiteSpace(library.Id))
                continue;

            var existing = merged.FirstOrDefault(item => string.Equals(item.Id, library.Id, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                merged.Add(CloneLibrary(library));
                continue;
            }

            duplicateCount++;
            existing.Name = FirstNonEmpty(existing.Name, library.Name) ?? existing.Id;
            existing.Description ??= library.Description;
            existing.CreatedAt ??= library.CreatedAt;
            existing.UpdatedAt = FirstNonEmpty(library.UpdatedAt, existing.UpdatedAt);
            existing.DefaultFolder ??= library.DefaultFolder;
            existing.Presentations.AddRange((library.Presentations ?? []).Select(ClonePresentationRef));
            existing.ExtensionData = MergeExtensionData(existing.ExtensionData, library.ExtensionData);
        }

        foreach (var library in merged)
        {
            library.Name = FirstNonEmpty(library.Name, library.Id) ?? library.Id;
            library.Presentations ??= new List<PresentationRefDto>();
        }

        if (duplicateCount > 0)
        {
            maintenance.Record(
                "info",
                "duplicate-libraries-merged",
                $"Merged {duplicateCount} duplicate library metadata entr{(duplicateCount == 1 ? "y" : "ies")} by id.",
                countsAsRepair: true);
        }

        return merged;
    }

    private List<PlaylistDto> CoalescePlaylists(IEnumerable<PlaylistDto> playlists, MaintenanceRunState maintenance)
    {
        var merged = new List<PlaylistDto>();
        var duplicateCount = 0;
        foreach (var playlist in playlists)
        {
            if (string.IsNullOrWhiteSpace(playlist.Id))
                continue;

            var existing = merged.FirstOrDefault(item => string.Equals(item.Id, playlist.Id, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                merged.Add(ClonePlaylist(playlist));
                continue;
            }

            duplicateCount++;
            existing.Name = FirstNonEmpty(existing.Name, playlist.Name) ?? existing.Id;
            existing.Description ??= playlist.Description;
            existing.CreatedAt ??= playlist.CreatedAt;
            existing.UpdatedAt = FirstNonEmpty(playlist.UpdatedAt, existing.UpdatedAt);
            existing.Items.AddRange((playlist.Items ?? []).Select(ClonePresentationRef));
            existing.ExtensionData = MergeExtensionData(existing.ExtensionData, playlist.ExtensionData);
        }

        foreach (var playlist in merged)
        {
            playlist.Name = FirstNonEmpty(playlist.Name, playlist.Id) ?? playlist.Id;
            playlist.Items ??= new List<PresentationRefDto>();
        }

        if (duplicateCount > 0)
        {
            maintenance.Record(
                "info",
                "duplicate-playlists-merged",
                $"Merged {duplicateCount} duplicate playlist metadata entr{(duplicateCount == 1 ? "y" : "ies")} by id.",
                countsAsRepair: true);
        }

        return merged;
    }

    private static void EnsureAutoLibraryForUnassignedPresentations(
        List<LibraryDto> libraries,
        IReadOnlyDictionary<string, PresentationRefDto> discoveredPresentations,
        MaintenanceRunState maintenance)
    {
        var assignedOutsideAuto = libraries
            .Where(static l => !string.Equals(l.Id, AutoLibraryId, StringComparison.OrdinalIgnoreCase))
            .SelectMany(static l => l.Presentations)
            .Select(static p => NormalizePath(p.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var autoLibrary = libraries.FirstOrDefault(static l => string.Equals(l.Id, AutoLibraryId, StringComparison.OrdinalIgnoreCase));
        var autoLibraryExisted = autoLibrary != null;
        var previousAutoPaths = autoLibrary?.Presentations
            .Select(static p => NormalizePath(p.Path))
            .ToList()
            ?? new List<string>();
        var autoItems = discoveredPresentations
            .Where(kvp => !assignedOutsideAuto.Contains(kvp.Key))
            .Select(static kvp => new PresentationRefDto
            {
                Path = kvp.Value.Path,
                Title = kvp.Value.Title,
                UpdatedAt = kvp.Value.UpdatedAt,
                ThumbnailData = kvp.Value.ThumbnailData,
            })
            .OrderBy(static p => p.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static p => p.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (autoItems.Count == 0)
        {
            if (autoLibrary != null)
                libraries.Remove(autoLibrary);
            return;
        }

        if (autoLibrary == null)
        {
            autoLibrary = new LibraryDto
            {
                Id = AutoLibraryId,
                Name = AutoLibraryName,
                CreatedAt = DateTime.UtcNow.ToString("O"),
            };
            libraries.Add(autoLibrary);
        }

        autoLibrary.Name = AutoLibraryName;
        autoLibrary.UpdatedAt = DateTime.UtcNow.ToString("O");
        autoLibrary.Presentations = autoItems;

        var currentAutoPaths = autoItems
            .Select(static p => NormalizePath(p.Path))
            .ToList();
        if (!autoLibraryExisted || !previousAutoPaths.SequenceEqual(currentAutoPaths, StringComparer.OrdinalIgnoreCase))
        {
            maintenance.Record(
                "info",
                "auto-library-updated",
                $"Assigned {autoItems.Count} untracked presentation{(autoItems.Count == 1 ? string.Empty : "s")} to the Library bucket.",
                countsAsRepair: true);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string GetCanonicalStoredPresentationPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        try
        {
            var resolved = _paths.ResolvePresentationPath(path);
            return _paths.ToContentRelativePath(resolved);
        }
        catch
        {
            return path;
        }
    }

    private static Dictionary<string, JsonElement>? MergeExtensionData(Dictionary<string, JsonElement>? target, Dictionary<string, JsonElement>? source)
    {
        if (source == null)
            return target;

        target ??= new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in source)
        {
            if (!target.ContainsKey(key))
                target[key] = value;
        }

        return target;
    }

    private static LibraryDto CloneLibrary(LibraryDto library) =>
        new()
        {
            Id = library.Id,
            Name = library.Name,
            Description = library.Description,
            CreatedAt = library.CreatedAt,
            UpdatedAt = library.UpdatedAt,
            DefaultFolder = library.DefaultFolder,
            Presentations = library.Presentations.Select(ClonePresentationRef).ToList(),
            ExtensionData = library.ExtensionData == null
                ? null
                : new Dictionary<string, JsonElement>(library.ExtensionData, StringComparer.OrdinalIgnoreCase),
        };

    private static PlaylistDto ClonePlaylist(PlaylistDto playlist) =>
        new()
        {
            Id = playlist.Id,
            Name = playlist.Name,
            Description = playlist.Description,
            CreatedAt = playlist.CreatedAt,
            UpdatedAt = playlist.UpdatedAt,
            ExternalSet = playlist.ExternalSet == null ? null : CloneExternalSet(playlist.ExternalSet),
            Sync = playlist.Sync == null ? null : CloneSyncMetadata(playlist.Sync),
            Items = playlist.Items.Select(ClonePresentationRef).ToList(),
            ExtensionData = playlist.ExtensionData == null
                ? null
                : new Dictionary<string, JsonElement>(playlist.ExtensionData, StringComparer.OrdinalIgnoreCase),
        };

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

    private static ExternalSetLinkDto CloneExternalSet(ExternalSetLinkDto src) =>
        new()
        {
            SetId = src.SetId,
            GroupId = src.GroupId,
            SyncedAt = src.SyncedAt,
            ServiceDate = src.ServiceDate,
            RemoteVersion = src.RemoteVersion,
            ExtensionData = src.ExtensionData == null
                ? null
                : new Dictionary<string, JsonElement>(src.ExtensionData, StringComparer.OrdinalIgnoreCase),
        };

    private static SyncMetadata CloneSyncMetadata(SyncMetadata src) =>
        new()
        {
            Status = src.Status,
            LastSyncAttempt = src.LastSyncAttempt,
            ConflictUrl = src.ConflictUrl,
            Error = src.Error,
            ExtensionData = src.ExtensionData == null
                ? null
                : new Dictionary<string, JsonElement>(src.ExtensionData, StringComparer.OrdinalIgnoreCase),
        };

    private static string NormalizePath(string path)
    {
        var normalized = path.Trim().Replace('\\', '/');
        while (normalized.Contains("//", StringComparison.Ordinal))
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);

        if (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];

        return normalized;
    }

    private async Task<T?> TryReadJsonAsync<T>(string path, MaintenanceRunState maintenance) where T : class
    {
        if (!File.Exists(path))
            return null;

        try
        {
            await using var fs = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(fs, JsonOptions).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(ex, "Could not read catalog file {Path}; it will be ignored.", path);
            maintenance.Record(
                "warning",
                "catalog-file-ignored",
                $"Ignored unreadable catalog file: {Path.GetFileName(path)}.",
                path,
                countsAsRepair: true);
            return null;
        }
    }

    private IEnumerable<string> EnumerateFilesSafe(string root, string searchPattern, SearchOption option)
    {
        try
        {
            if (!Directory.Exists(root))
                return [];

            return Directory.EnumerateFiles(root, searchPattern, option).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            _logger.LogWarning(ex, "Could not enumerate files under {Path}.", root);
            return [];
        }
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static v => !string.IsNullOrWhiteSpace(v));

    private async Task FlushMaintenanceRunAsync(MaintenanceRunState maintenance, int presentationCount)
    {
        if (!maintenance.Enabled)
            return;

        var summaryMessage = maintenance.RepairCount == 0
            ? $"Scan completed. No changes detected across {presentationCount} presentation{(presentationCount == 1 ? string.Empty : "s")}."
            : $"Scan completed with {maintenance.RepairCount} repair action{(maintenance.RepairCount == 1 ? string.Empty : "s")} across {presentationCount} presentation{(presentationCount == 1 ? string.Empty : "s")}.";

        maintenance.Record("info", "scan-complete", summaryMessage);
        await _maintenanceLog.AppendEntriesAsync(maintenance.Entries).ConfigureAwait(false);
    }

    private sealed class MaintenanceRunState(ContentMaintenanceTrigger trigger)
    {
        public ContentMaintenanceTrigger Trigger { get; } = trigger;

        public List<ContentMaintenanceLogEntry> Entries { get; } = new();

        public int RepairCount { get; private set; }

        public bool Enabled => Trigger != ContentMaintenanceTrigger.Default;

        public void Record(string severity, string eventType, string message, string? path = null, bool countsAsRepair = false)
        {
            if (!Enabled)
                return;

            Entries.Add(new ContentMaintenanceLogEntry
            {
                Timestamp = DateTimeOffset.UtcNow.ToString("O"),
                Trigger = Trigger.ToString(),
                Severity = severity,
                EventType = eventType,
                Message = message,
                Path = path,
            });

            if (countsAsRepair)
                RepairCount++;
        }
    }
}