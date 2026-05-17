namespace ChurchPresenter.Services.Content;

/// <summary>
/// Resolves managed content roots and all well-known path locations within them.
/// The content root stores portable, shared content (libraries, playlists, presentations, configurations, themes, media, audits).
/// Machine-local state (bindings, cache, workspace) lives under local app data.
/// </summary>
public interface IContentDirectoryService
{
    // ── Root resolution ──────────────────────────────────────────────────────

    /// <summary>Gets the absolute path to the user's local app data folder for ChurchPresenter.</summary>
    string GetAppDataDirectory();

    /// <summary>Gets the default content root under the user's Documents folder.</summary>
    string GetDefaultDocumentsDataDirectory();

    /// <summary>Gets the active managed content root (custom override or default Documents path).</summary>
    string GetDocumentsDataDirectory();

    /// <summary>Persists or clears the content root override.</summary>
    Task SetDocumentsDataDirectoryOverrideAsync(string? path, CancellationToken cancellationToken = default);

    // ── Presentation path utilities ──────────────────────────────────────────

    /// <summary>Resolves a stored presentation path against the content root when it is relative.</summary>
    string ResolvePresentationPath(string path);

    /// <summary>Converts an absolute path under the content root to a root-relative path when possible.</summary>
    string ToContentRelativePath(string path);

    /// <summary>Builds a presentation path like <c>Presentations/{sanitizedTitle}_{idPrefix8}.cpres</c>.</summary>
    string GeneratePresentationPath(string title, string presentationId);

    /// <summary>Path to <c>Presentations/songs/{songId}.cpres</c>.</summary>
    string GetSongPresentationPath(string songId);

    // ── Content root manifest ────────────────────────────────────────────────

    /// <summary>Absolute path to <c>ChurchPresenter.Content.json</c> at the managed content root.</summary>
    string GetContentRootManifestPath();

    // ── TitleCase domain directories (new structured layout) ─────────────────

    /// <summary>Absolute path to the <c>Libraries/</c> directory under the content root.</summary>
    string GetLibrariesRootDirectory();

    /// <summary>Absolute path to a single library folder: <c>Libraries/&lt;libraryId&gt;/</c>.</summary>
    string GetLibraryRootDirectory(string libraryId);

    /// <summary>Absolute path to <c>Libraries/Index.json</c>.</summary>
    string GetLibrariesIndexPath();

    /// <summary>Absolute path to <c>Libraries/&lt;libraryId&gt;/Library.json</c>.</summary>
    string GetLibraryManifestPath(string libraryId);

    /// <summary>Absolute path to the <c>Playlists/</c> directory under the content root.</summary>
    string GetPlaylistsRootDirectory();

    /// <summary>Absolute path to a single playlist folder: <c>Playlists/&lt;playlistId&gt;/</c>.</summary>
    string GetPlaylistRootDirectory(string playlistId);

    /// <summary>Absolute path to <c>Playlists/Index.json</c>.</summary>
    string GetPlaylistsIndexPath();

    /// <summary>Absolute path to <c>Playlists/&lt;playlistId&gt;/Playlist.json</c>.</summary>
    string GetPlaylistManifestPath(string playlistId);

    /// <summary>Absolute path to the <c>Presentations/</c> directory under the content root.</summary>
    string GetPresentationsRootDirectory();

    /// <summary>Absolute path to a single presentation folder: <c>Presentations/&lt;presentationId&gt;/</c>.</summary>
    string GetPresentationRootDirectory(string presentationId);

    /// <summary>Absolute path to <c>Presentations/Index.json</c>.</summary>
    string GetPresentationsIndexPath();

    /// <summary>Absolute path to <c>Presentations/&lt;presentationId&gt;/Presentation.json</c>.</summary>
    string GetPresentationManifestPath(string presentationId);

    /// <summary>Absolute path to the <c>Configurations/</c> directory under the content root.</summary>
    string GetConfigurationsDirectory();

    /// <summary>Absolute path to <c>Configurations/Manifest.json</c>.</summary>
    string GetConfigurationsManifestPath();

    /// <summary>Absolute path to a named portable configuration file: <c>Configurations/&lt;name&gt;.json</c>.</summary>
    string GetSharedConfigPath(string name);

    /// <summary>Absolute path to the <c>Themes/</c> directory under the content root.</summary>
    string GetThemesRootDirectory();

    /// <summary>Absolute path to <c>Themes/Index.json</c>.</summary>
    string GetThemesIndexPath();

    /// <summary>Absolute path to a named theme file: <c>Themes/&lt;themeId&gt;.json</c>.</summary>
    string GetThemeFilePath(string themeId);

    /// <summary>Absolute path to the <c>Media/</c> directory under the content root.</summary>
    string GetMediaRootDirectory();

    /// <summary>Absolute path to <c>Media/Index.json</c>.</summary>
    string GetMediaIndexPath();

    /// <summary>Absolute path to <c>Media/Files/</c> (managed copies of imported media).</summary>
    string GetManagedMediaFilesDirectory();

    /// <summary>Absolute path to the <c>Media/Playlists/</c> directory.</summary>
    string GetMediaPlaylistsRootDirectory();

    /// <summary>Absolute path to a single media-playlist folder: <c>Media/Playlists/&lt;playlistId&gt;/</c>.</summary>
    string GetMediaPlaylistDirectory(string playlistId);

    /// <summary>Absolute path to <c>Media/Playlists/&lt;playlistId&gt;/Playlist.json</c>.</summary>
    string GetMediaPlaylistManifestPath(string playlistId);

    /// <summary>Absolute path to the <c>Audits/</c> directory under the content root.</summary>
    string GetAuditsDirectory();

    /// <summary>Absolute path to <c>Audits/ContentAudit.json</c>.</summary>
    string GetContentAuditPath();

    /// <summary>Absolute path to <c>Audits/MigrationHistory.json</c>.</summary>
    string GetMigrationHistoryPath();

    // ── Machine-local state (under %LocalAppData%/ChurchPresenter/) ──────────

    /// <summary>Absolute path to <c>%LocalAppData%/ChurchPresenter/MachineState/</c>.</summary>
    string GetMachineStateDirectory();

    /// <summary>Absolute path to a named machine-state file: <c>MachineState/&lt;name&gt;.json</c>.</summary>
    string GetMachineStatePath(string name);

    /// <summary>Absolute path to <c>%LocalAppData%/ChurchPresenter/Migration/LastRun.json</c>.</summary>
    string GetMigrationLastRunPath();

    // ── Legacy paths (kept during migration; prefer TitleCase equivalents above) ──

    /// <summary>Absolute path to the legacy lowercase <c>libraries/</c> directory (read-only during migration).</summary>
    string GetLibrariesDirectory();

    /// <summary>Absolute path to the legacy lowercase <c>playlists/</c> directory (read-only during migration).</summary>
    string GetPlaylistsDirectory();

    /// <summary>Absolute path to the legacy lowercase <c>presentations/</c> directory.</summary>
    string GetPresentationsDirectory();

    /// <summary>Absolute path to a legacy per-library folder.</summary>
    string GetLibraryDirectory(string libraryId);

    /// <summary>Absolute path to a legacy per-library metadata file (<c>library.json</c>).</summary>
    string GetLibraryMetadataPath(string libraryId);

    /// <summary>Absolute path to a legacy per-playlist metadata file.</summary>
    string GetPlaylistMetadataPath(string playlistId);

    /// <summary>Absolute path to the legacy <c>libraries/libraries.json</c> aggregate file.</summary>
    string GetLibrariesJsonPath();

    /// <summary>Absolute path to the legacy <c>playlists/playlists.json</c> aggregate file.</summary>
    string GetPlaylistsJsonPath();

    /// <summary>Absolute path to the legacy <c>themes/themes.json</c> file.</summary>
    string GetThemesJsonPath();

    // ── Layout management ────────────────────────────────────────────────────

    /// <summary>Creates the full managed content layout (TitleCase folders + legacy folders) under the content root.</summary>
    Task EnsureDocumentsLayoutAsync();
}