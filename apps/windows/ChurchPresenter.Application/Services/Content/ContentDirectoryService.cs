using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging;

namespace ChurchPresenter.Services.Content;

/// <summary>
/// Resolves app data and document content roots and ensures expected subfolders exist.
/// TitleCase domain folders are the canonical new layout; lowercase folders are kept for
/// backward-compatible migration reads only.
/// </summary>
public sealed class ContentDirectoryService(ILogger<ContentDirectoryService> logger) : IContentDirectoryService
{
    private readonly ILogger<ContentDirectoryService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    private const string AppDirName = "Church Presenter";
    private const string AppLocalDirName = "ChurchPresenter";

    private static readonly Regex WhitespaceRuns = new(@"\s+", RegexOptions.Compiled);

    // ── Root resolution ──────────────────────────────────────────────────────

    /// <inheritdoc />
    public string GetAppDataDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppLocalDirName);

    /// <inheritdoc />
    public string GetDefaultDocumentsDataDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), AppDirName);

    /// <inheritdoc />
    public string GetDocumentsDataDirectory()
    {
        var binding = ReadContentRootBinding();
        if (!string.IsNullOrWhiteSpace(binding?.ContentRootPath))
            return Path.GetFullPath(binding.ContentRootPath.Trim());

        // Fall back to legacy content_dir.json for a graceful upgrade path
        var legacyConfigured = ReadLegacyContentDirConfig();
        if (!string.IsNullOrWhiteSpace(legacyConfigured))
            return Path.GetFullPath(legacyConfigured.Trim());

        return GetDefaultDocumentsDataDirectory();
    }

    /// <inheritdoc />
    public async Task SetDocumentsDataDirectoryOverrideAsync(string? path, CancellationToken cancellationToken = default)
    {
        var machineStateDir = GetMachineStateDirectory();
        Directory.CreateDirectory(machineStateDir);

        var normalizedPath = string.IsNullOrWhiteSpace(path)
            ? null
            : Path.GetFullPath(path.Trim());

        var isDefault = string.IsNullOrWhiteSpace(normalizedPath)
                        || string.Equals(
                            normalizedPath,
                            Path.GetFullPath(GetDefaultDocumentsDataDirectory()),
                            StringComparison.OrdinalIgnoreCase);

        var binding = new { contentRootPath = isDefault ? (string?)null : normalizedPath, isDefault };
        var json = JsonSerializer.Serialize(binding, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true });
        await File.WriteAllTextAsync(GetMachineStatePath("ContentRootBinding"), json, cancellationToken).ConfigureAwait(false);

        // Migrate away from the old content_dir.json so it no longer shadows the new binding
        var legacyCfg = Path.Combine(GetAppDataDirectory(), "content_dir.json");
        if (File.Exists(legacyCfg))
        {
            try { File.Delete(legacyCfg); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug(ex, "Could not delete legacy content_dir.json.");
            }
        }
    }

    // ── Presentation path utilities ──────────────────────────────────────────

    /// <inheritdoc />
    public string ResolvePresentationPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (Path.IsPathRooted(path))
            return Path.GetFullPath(path);

        return Path.GetFullPath(Path.Combine(GetDocumentsDataDirectory(), path));
    }

    /// <inheritdoc />
    public string ToContentRelativePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        var root = Path.GetFullPath(GetDocumentsDataDirectory());
        var relative = Path.GetRelativePath(root, full);
        return relative.StartsWith("..", StringComparison.Ordinal)
            ? full
            : relative.Replace('\\', '/');
    }

    /// <inheritdoc />
    public string GeneratePresentationPath(string title, string presentationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presentationId);
        var prefix = presentationId.Length >= 8 ? presentationId[..8] : presentationId;
        var sanitized = SanitizePresentationTitle(title);
        return Path.Combine(GetPresentationsRootDirectory(), $"{sanitized}_{prefix}.cpres");
    }

    /// <inheritdoc />
    public string GetSongPresentationPath(string songId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(songId);
        return Path.Combine(GetPresentationsRootDirectory(), "songs", $"{songId}.cpres");
    }

    // ── Content root manifest ────────────────────────────────────────────────

    /// <inheritdoc />
    public string GetContentRootManifestPath() =>
        Path.Combine(GetDocumentsDataDirectory(), "ChurchPresenter.Content.json");

    // ── TitleCase domain directories ─────────────────────────────────────────

    /// <inheritdoc />
    public string GetLibrariesRootDirectory() =>
        Path.Combine(GetDocumentsDataDirectory(), "Libraries");

    /// <inheritdoc />
    public string GetLibraryRootDirectory(string libraryId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryId);
        return Path.Combine(GetLibrariesRootDirectory(), libraryId);
    }

    /// <inheritdoc />
    public string GetLibrariesIndexPath() =>
        Path.Combine(GetLibrariesRootDirectory(), "Index.json");

    /// <inheritdoc />
    public string GetLibraryManifestPath(string libraryId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryId);
        return Path.Combine(GetLibraryRootDirectory(libraryId), "Library.json");
    }

    /// <inheritdoc />
    public string GetPlaylistsRootDirectory() =>
        Path.Combine(GetDocumentsDataDirectory(), "Playlists");

    /// <inheritdoc />
    public string GetPlaylistRootDirectory(string playlistId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playlistId);
        return Path.Combine(GetPlaylistsRootDirectory(), playlistId);
    }

    /// <inheritdoc />
    public string GetPlaylistsIndexPath() =>
        Path.Combine(GetPlaylistsRootDirectory(), "Index.json");

    /// <inheritdoc />
    public string GetPlaylistManifestPath(string playlistId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playlistId);
        return Path.Combine(GetPlaylistRootDirectory(playlistId), "Playlist.json");
    }

    /// <inheritdoc />
    public string GetPresentationsRootDirectory() =>
        Path.Combine(GetDocumentsDataDirectory(), "Presentations");

    /// <inheritdoc />
    public string GetPresentationRootDirectory(string presentationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presentationId);
        return Path.Combine(GetPresentationsRootDirectory(), presentationId);
    }

    /// <inheritdoc />
    public string GetPresentationsIndexPath() =>
        Path.Combine(GetPresentationsRootDirectory(), "Index.json");

    /// <inheritdoc />
    public string GetPresentationManifestPath(string presentationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presentationId);
        return Path.Combine(GetPresentationRootDirectory(presentationId), "Presentation.json");
    }

    /// <inheritdoc />
    public string GetConfigurationsDirectory() =>
        Path.Combine(GetDocumentsDataDirectory(), "Configurations");

    /// <inheritdoc />
    public string GetConfigurationsManifestPath() =>
        Path.Combine(GetConfigurationsDirectory(), "Manifest.json");

    /// <inheritdoc />
    public string GetSharedConfigPath(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Path.Combine(GetConfigurationsDirectory(), $"{name}.json");
    }

    /// <inheritdoc />
    public string GetThemesRootDirectory() =>
        Path.Combine(GetDocumentsDataDirectory(), "Themes");

    /// <inheritdoc />
    public string GetThemesIndexPath() =>
        Path.Combine(GetThemesRootDirectory(), "Index.json");

    /// <inheritdoc />
    public string GetThemeFilePath(string themeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeId);
        return Path.Combine(GetThemesRootDirectory(), $"{themeId}.json");
    }

    /// <inheritdoc />
    public string GetMediaRootDirectory() =>
        Path.Combine(GetDocumentsDataDirectory(), "Media");

    /// <inheritdoc />
    public string GetMediaIndexPath() =>
        Path.Combine(GetMediaRootDirectory(), "Index.json");

    /// <inheritdoc />
    public string GetManagedMediaFilesDirectory() =>
        Path.Combine(GetMediaRootDirectory(), "Files");

    /// <inheritdoc />
    public string GetMediaPlaylistsRootDirectory() =>
        Path.Combine(GetMediaRootDirectory(), "Playlists");

    /// <inheritdoc />
    public string GetMediaPlaylistDirectory(string playlistId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playlistId);
        return Path.Combine(GetMediaPlaylistsRootDirectory(), playlistId);
    }

    /// <inheritdoc />
    public string GetMediaPlaylistManifestPath(string playlistId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playlistId);
        return Path.Combine(GetMediaPlaylistDirectory(playlistId), "Playlist.json");
    }

    /// <inheritdoc />
    public string GetAuditsDirectory() =>
        Path.Combine(GetDocumentsDataDirectory(), "Audits");

    /// <inheritdoc />
    public string GetContentAuditPath() =>
        Path.Combine(GetAuditsDirectory(), "ContentAudit.json");

    /// <inheritdoc />
    public string GetMigrationHistoryPath() =>
        Path.Combine(GetAuditsDirectory(), "MigrationHistory.json");

    // ── Machine-local state ──────────────────────────────────────────────────

    /// <inheritdoc />
    public string GetMachineStateDirectory() =>
        Path.Combine(GetAppDataDirectory(), "MachineState");

    /// <inheritdoc />
    public string GetMachineStatePath(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Path.Combine(GetMachineStateDirectory(), $"{name}.json");
    }

    /// <inheritdoc />
    public string GetMigrationLastRunPath() =>
        Path.Combine(GetAppDataDirectory(), "Migration", "LastRun.json");

    // ── Legacy paths (kept for migration reads) ───────────────────────────────

    /// <inheritdoc />
    public string GetLibrariesDirectory() =>
        Path.Combine(GetDocumentsDataDirectory(), "libraries");

    /// <inheritdoc />
    public string GetPlaylistsDirectory() =>
        Path.Combine(GetDocumentsDataDirectory(), "playlists");

    /// <inheritdoc />
    public string GetPresentationsDirectory() =>
        Path.Combine(GetDocumentsDataDirectory(), "presentations");

    /// <inheritdoc />
    public string GetLibraryDirectory(string libraryId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryId);
        return Path.Combine(GetLibrariesDirectory(), libraryId);
    }

    /// <inheritdoc />
    public string GetLibraryMetadataPath(string libraryId) =>
        Path.Combine(GetLibraryDirectory(libraryId), "library.json");

    /// <inheritdoc />
    public string GetPlaylistMetadataPath(string playlistId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playlistId);
        return Path.Combine(GetPlaylistsDirectory(), $"{playlistId}.json");
    }

    /// <inheritdoc />
    public string GetLibrariesJsonPath() =>
        Path.Combine(GetLibrariesDirectory(), "libraries.json");

    /// <inheritdoc />
    public string GetPlaylistsJsonPath() =>
        Path.Combine(GetPlaylistsDirectory(), "playlists.json");

    /// <inheritdoc />
    public string GetThemesJsonPath() =>
        Path.Combine(GetDocumentsDataDirectory(), "themes", "themes.json");

    // ── Layout management ────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task EnsureDocumentsLayoutAsync()
    {
        var root = GetDocumentsDataDirectory();
        Directory.CreateDirectory(root);

        // New TitleCase layout
        foreach (var sub in new[]
                 {
                     "Libraries", "Playlists", "Presentations",
                     Path.Combine("Presentations", "songs"),
                     "Configurations", "Themes", "Media",
                     Path.Combine("Media", "Files"),
                     "Audits",
                 })
        {
            Directory.CreateDirectory(Path.Combine(root, sub));
        }

        // Legacy lowercase layout kept for migration compatibility
        foreach (var sub in new[]
                 {
                     "libraries", "playlists", "presentations",
                     Path.Combine("presentations", "songs"),
                     "media-library", "themes",
                 })
        {
            Directory.CreateDirectory(Path.Combine(root, sub));
        }

        _logger.LogTrace("Ensured documents layout under {Root}.", root);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private ContentRootBindingFile? ReadContentRootBinding()
    {
        try
        {
            var path = GetMachineStatePath("ContentRootBinding");
            if (!File.Exists(path))
                return null;

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ContentRootBindingFile>(json,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read ContentRootBinding.json from machine state.");
            return null;
        }
    }

    private string? ReadLegacyContentDirConfig()
    {
        try
        {
            var cfg = Path.Combine(GetAppDataDirectory(), "content_dir.json");
            if (!File.Exists(cfg))
                return null;
            var json = File.ReadAllText(cfg);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("path", out var p))
                return p.GetString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read legacy content directory override.");
        }

        return null;
    }

    private static string SanitizePresentationTitle(string title)
    {
        if (string.IsNullOrEmpty(title))
            return "Untitled";

        Span<char> buffer = stackalloc char[title.Length];
        var len = 0;
        foreach (var c in title)
        {
            if (c is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*')
                continue;
            buffer[len++] = c;
        }

        var slice = buffer[..len];
        var s = slice.ToString();
        s = WhitespaceRuns.Replace(s, "_").Trim();
        if (s.Length == 0)
            s = "Untitled";
        return s.Length <= 50 ? s : s[..50];
    }

    private sealed class ContentRootBindingFile
    {
        public string? ContentRootPath { get; init; }
        public bool IsDefault { get; init; } = true;
    }
}