
using Moq;

namespace ChurchPresenter.App.Tests.TestSupport;

/// <summary>
/// Builds a mock <see cref="IContentDirectoryService"/> whose path layout matches
/// <see cref="ContentDirectoryService"/> (TitleCase registries + legacy lowercase migration folders)
/// under a temporary root. Keeps tests aligned with production path resolution.
/// </summary>
internal static class TestContentPaths
{
    public static Mock<IContentDirectoryService> Create(string root, string? appDataRoot = null)
    {
        Directory.CreateDirectory(root);
        appDataRoot ??= Path.Combine(root, ".appdata");
        Directory.CreateDirectory(appDataRoot);

        var librariesRoot = Path.Combine(root, "Libraries");
        var playlistsRoot = Path.Combine(root, "Playlists");
        var presentationsRoot = Path.Combine(root, "Presentations");
        var configurationsDir = Path.Combine(root, "Configurations");
        var themesRoot = Path.Combine(root, "Themes");
        var mediaRoot = Path.Combine(root, "Media");
        var auditsDir = Path.Combine(root, "Audits");
        var machineStateDir = Path.Combine(appDataRoot, "MachineState");

        var legacyLibrariesRoot = Path.Combine(root, "libraries");
        var legacyPlaylistsRoot = Path.Combine(root, "playlists");
        var legacyThemesDir = Path.Combine(root, "themes");

        var mock = new Mock<IContentDirectoryService>();
        mock.Setup(p => p.GetDefaultDocumentsDataDirectory()).Returns(root);
        mock.Setup(p => p.GetDocumentsDataDirectory()).Returns(root);
        mock.Setup(p => p.GetAppDataDirectory()).Returns(appDataRoot);
        mock.Setup(p => p.GetMachineStateDirectory()).Returns(machineStateDir);
        mock.Setup(p => p.GetMachineStatePath(It.IsAny<string>()))
            .Returns<string>(name => Path.Combine(machineStateDir, $"{name}.json"));
        mock.Setup(p => p.GetMigrationLastRunPath()).Returns(Path.Combine(appDataRoot, "Migration", "LastRun.json"));
        mock.Setup(p => p.GetConfigurationsDirectory()).Returns(configurationsDir);
        mock.Setup(p => p.GetSharedConfigPath(It.IsAny<string>()))
            .Returns<string>(name => Path.Combine(configurationsDir, $"{name}.json"));
        mock.Setup(p => p.GetConfigurationsManifestPath()).Returns(Path.Combine(configurationsDir, "Manifest.json"));
        mock.Setup(p => p.GetContentRootManifestPath()).Returns(Path.Combine(root, "ChurchPresenter.Content.json"));
        mock.Setup(p => p.GetAuditsDirectory()).Returns(auditsDir);
        mock.Setup(p => p.GetContentAuditPath()).Returns(Path.Combine(auditsDir, "ContentAudit.json"));
        mock.Setup(p => p.GetMigrationHistoryPath()).Returns(Path.Combine(auditsDir, "MigrationHistory.json"));
        mock.Setup(p => p.SetDocumentsDataDirectoryOverrideAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // TitleCase registry layout (canonical)
        mock.Setup(p => p.GetLibrariesRootDirectory()).Returns(librariesRoot);
        mock.Setup(p => p.GetLibraryRootDirectory(It.IsAny<string>()))
            .Returns<string>(id => Path.Combine(librariesRoot, id));
        mock.Setup(p => p.GetLibrariesIndexPath()).Returns(Path.Combine(librariesRoot, "Index.json"));
        mock.Setup(p => p.GetLibraryManifestPath(It.IsAny<string>()))
            .Returns<string>(id => Path.Combine(librariesRoot, id, "Library.json"));

        mock.Setup(p => p.GetPlaylistsRootDirectory()).Returns(playlistsRoot);
        mock.Setup(p => p.GetPlaylistRootDirectory(It.IsAny<string>()))
            .Returns<string>(id => Path.Combine(playlistsRoot, id));
        mock.Setup(p => p.GetPlaylistsIndexPath()).Returns(Path.Combine(playlistsRoot, "Index.json"));
        mock.Setup(p => p.GetPlaylistManifestPath(It.IsAny<string>()))
            .Returns<string>(id => Path.Combine(playlistsRoot, id, "Playlist.json"));

        mock.Setup(p => p.GetPresentationsRootDirectory()).Returns(presentationsRoot);
        mock.Setup(p => p.GetPresentationRootDirectory(It.IsAny<string>()))
            .Returns<string>(id => Path.Combine(presentationsRoot, id));
        mock.Setup(p => p.GetPresentationsIndexPath()).Returns(Path.Combine(presentationsRoot, "Index.json"));
        mock.Setup(p => p.GetPresentationManifestPath(It.IsAny<string>()))
            .Returns<string>(id => Path.Combine(presentationsRoot, id, "Presentation.json"));

        mock.Setup(p => p.GetThemesRootDirectory()).Returns(themesRoot);
        mock.Setup(p => p.GetThemesIndexPath()).Returns(Path.Combine(themesRoot, "Index.json"));
        mock.Setup(p => p.GetThemeFilePath(It.IsAny<string>()))
            .Returns<string>(id => Path.Combine(themesRoot, $"{id}.json"));

        mock.Setup(p => p.GetMediaRootDirectory()).Returns(mediaRoot);
        mock.Setup(p => p.GetManagedMediaFilesDirectory()).Returns(Path.Combine(mediaRoot, "Files"));
        mock.Setup(p => p.GetMediaIndexPath()).Returns(Path.Combine(mediaRoot, "Index.json"));
        mock.Setup(p => p.GetMediaPlaylistsRootDirectory()).Returns(Path.Combine(mediaRoot, "Playlists"));
        mock.Setup(p => p.GetMediaPlaylistDirectory(It.IsAny<string>()))
            .Returns<string>(id => Path.Combine(mediaRoot, "Playlists", id));
        mock.Setup(p => p.GetMediaPlaylistManifestPath(It.IsAny<string>()))
            .Returns<string>(id => Path.Combine(mediaRoot, "Playlists", id, "Playlist.json"));

        // Legacy lowercase paths (migration reads)
        mock.Setup(p => p.GetLibrariesDirectory()).Returns(legacyLibrariesRoot);
        mock.Setup(p => p.GetPlaylistsDirectory()).Returns(legacyPlaylistsRoot);
        // Match production discovery: bundles live under Presentations/. Tests used to write only to
        // legacy "presentations/"; unify so discovery (GetPresentationsRootDirectory) sees them.
        mock.Setup(p => p.GetPresentationsDirectory()).Returns(presentationsRoot);
        mock.Setup(p => p.GetLibraryDirectory(It.IsAny<string>()))
            .Returns<string>(id => Path.Combine(legacyLibrariesRoot, id));
        mock.Setup(p => p.GetLibraryMetadataPath(It.IsAny<string>()))
            .Returns<string>(id => Path.Combine(legacyLibrariesRoot, id, "library.json"));
        mock.Setup(p => p.GetPlaylistMetadataPath(It.IsAny<string>()))
            .Returns<string>(id => Path.Combine(legacyPlaylistsRoot, $"{id}.json"));
        mock.Setup(p => p.GetLibrariesJsonPath()).Returns(Path.Combine(legacyLibrariesRoot, "libraries.json"));
        mock.Setup(p => p.GetPlaylistsJsonPath()).Returns(Path.Combine(legacyPlaylistsRoot, "playlists.json"));
        mock.Setup(p => p.GetThemesJsonPath()).Returns(Path.Combine(legacyThemesDir, "themes.json"));

        mock.Setup(p => p.GetSongPresentationPath(It.IsAny<string>()))
            .Returns<string>(songId => Path.Combine(presentationsRoot, "songs", $"{songId}.cpres"));
        mock.Setup(p => p.GeneratePresentationPath(It.IsAny<string>(), It.IsAny<string>()))
            .Returns<string, string>((title, id) =>
            {
                var safeTitle = string.IsNullOrWhiteSpace(title) ? "Untitled" : title.Replace(' ', '_');
                var suffix = id.Length >= 8 ? id[..8] : id;
                return Path.Combine(presentationsRoot, $"{safeTitle}_{suffix}.cpres");
            });
        mock.Setup(p => p.ResolvePresentationPath(It.IsAny<string>()))
            .Returns<string>(path => Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(root, path)));
        mock.Setup(p => p.ToContentRelativePath(It.IsAny<string>()))
            .Returns<string>(path =>
            {
                var full = Path.GetFullPath(path);
                var relative = Path.GetRelativePath(root, full);
                return relative.StartsWith("..", StringComparison.Ordinal)
                    ? full
                    : relative.Replace('\\', '/');
            });

        mock.Setup(p => p.EnsureDocumentsLayoutAsync())
            .Returns(() =>
            {
                foreach (var sub in new[]
                         {
                             librariesRoot, playlistsRoot, presentationsRoot,
                             Path.Combine(presentationsRoot, "songs"),
                             configurationsDir, themesRoot, mediaRoot,
                             Path.Combine(mediaRoot, "Files"),
                             Path.Combine(mediaRoot, "Playlists"),
                             auditsDir,
                         })
                {
                    Directory.CreateDirectory(sub);
                }

                foreach (var sub in new[]
                         {
                             legacyLibrariesRoot, legacyPlaylistsRoot,
                             Path.Combine(root, "presentations"),
                             Path.Combine(root, "presentations", "songs"),
                             Path.Combine(root, "media-library"),
                             legacyThemesDir,
                         })
                {
                    Directory.CreateDirectory(sub);
                }

                return Task.CompletedTask;
            });

        return mock;
    }
}