using System.Text.Json;

using ChurchPresenter.App.Tests.TestSupport;
using ChurchPresenter.Core.Cpres;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

namespace ChurchPresenter.App.Tests.Services.Content;

/// <summary>
/// Catalog load/save: structured registries under <c>Libraries/</c> and <c>Playlists/</c> are canonical;
/// legacy aggregate JSON is migration-only and is not written by <see cref="CatalogService.SaveAsync"/>.
/// </summary>
public sealed class CatalogServiceTests
{
    [Fact]
    public async Task LoadAsync_discovers_presentations_and_assigns_untracked_files_to_auto_library()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        TestPresentationBundles.WriteBundle(paths.Object.GetPresentationsDirectory(), "fixture.cpres", "Fixture");
        var svc = new CatalogService(
            paths.Object,
            new LibraryRegistryService(paths.Object, NullLogger<LibraryRegistryService>.Instance),
            new PlaylistRegistryService(paths.Object, NullLogger<PlaylistRegistryService>.Instance),
            new CpresDocumentService(NullLogger<CpresDocumentService>.Instance),
            new ContentMaintenanceLogService(paths.Object, NullLogger<ContentMaintenanceLogService>.Instance),
            NullLogger<CatalogService>.Instance);

        await svc.LoadAsync();

        svc.Catalog.Libraries.Should().ContainSingle();
        svc.Catalog.Libraries[0].Id.Should().Be("local-library");
        svc.Catalog.Libraries[0].Presentations.Should().ContainSingle(p => p.Title == "Fixture");
        File.Exists(paths.Object.GetLibrariesIndexPath()).Should().BeTrue();
        File.Exists(paths.Object.GetLibraryManifestPath("local-library")).Should().BeTrue();
        File.Exists(paths.Object.GetLibrariesJsonPath()).Should().BeFalse("legacy aggregate is not written after registry migration");
        // On Windows, legacy "libraries/" and canonical "Libraries/" resolve to the same directory;
        // the registry may create subfolders like local-library/ there.
        Directory.Exists(paths.Object.GetLibraryRootDirectory("local-library")).Should().BeTrue();
    }

    [Fact]
    public async Task LoadAsync_preserves_library_directories_without_legacy_metadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var retainedDirectory = paths.Object.GetLibraryDirectory("cache-folder");
        Directory.CreateDirectory(retainedDirectory);
        await File.WriteAllTextAsync(Path.Combine(retainedDirectory, "marker.txt"), "keep");

        var svc = new CatalogService(
            paths.Object,
            new LibraryRegistryService(paths.Object, NullLogger<LibraryRegistryService>.Instance),
            new PlaylistRegistryService(paths.Object, NullLogger<PlaylistRegistryService>.Instance),
            new CpresDocumentService(NullLogger<CpresDocumentService>.Instance),
            new ContentMaintenanceLogService(paths.Object, NullLogger<ContentMaintenanceLogService>.Instance),
            NullLogger<CatalogService>.Instance);

        await svc.LoadAsync();

        Directory.Exists(retainedDirectory).Should().BeTrue();
        File.Exists(Path.Combine(retainedDirectory, "marker.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task LoadAsync_reads_canonical_registry_manifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var libraryRegistry = new LibraryRegistryService(paths.Object, NullLogger<LibraryRegistryService>.Instance);
        await libraryRegistry.SaveAsync(new LibraryManifest
        {
            Id = "library-1",
            Name = "Songs",
            CreatedAt = "2026-01-01T00:00:00.000Z",
            Presentations = [],
        });

        var svc = new CatalogService(
            paths.Object,
            new LibraryRegistryService(paths.Object, NullLogger<LibraryRegistryService>.Instance),
            new PlaylistRegistryService(paths.Object, NullLogger<PlaylistRegistryService>.Instance),
            new CpresDocumentService(NullLogger<CpresDocumentService>.Instance),
            new ContentMaintenanceLogService(paths.Object, NullLogger<ContentMaintenanceLogService>.Instance),
            NullLogger<CatalogService>.Instance);

        await svc.LoadAsync();

        svc.Catalog.Libraries.Should().ContainSingle(library => library.Id == "library-1");
        File.Exists(paths.Object.GetLibrariesIndexPath()).Should().BeTrue();
        File.Exists(paths.Object.GetLibraryManifestPath("library-1")).Should().BeTrue();
    }

    [Fact]
    public async Task LoadAsync_recreates_missing_layout_and_repairs_invalid_catalog_files()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);

        Directory.CreateDirectory(root);
        Directory.CreateDirectory(paths.Object.GetLibrariesDirectory());
        Directory.CreateDirectory(paths.Object.GetPlaylistsDirectory());
        await File.WriteAllTextAsync(paths.Object.GetLibrariesJsonPath(), "{not-json");
        await File.WriteAllTextAsync(paths.Object.GetPlaylistsJsonPath(), "{still-not-json");

        var svc = new CatalogService(
            paths.Object,
            new LibraryRegistryService(paths.Object, NullLogger<LibraryRegistryService>.Instance),
            new PlaylistRegistryService(paths.Object, NullLogger<PlaylistRegistryService>.Instance),
            new CpresDocumentService(NullLogger<CpresDocumentService>.Instance),
            new ContentMaintenanceLogService(paths.Object, NullLogger<ContentMaintenanceLogService>.Instance),
            NullLogger<CatalogService>.Instance);

        await svc.LoadAsync();

        Directory.Exists(paths.Object.GetPresentationsRootDirectory()).Should().BeTrue();
        Directory.Exists(Path.Combine(paths.Object.GetPresentationsRootDirectory(), "songs")).Should().BeTrue();
        // Invalid legacy aggregate files are ignored; runtime now reads only canonical registries.
        File.ReadAllText(paths.Object.GetLibrariesJsonPath()).Should().Be("{not-json");
        File.ReadAllText(paths.Object.GetPlaylistsJsonPath()).Should().Be("{still-not-json");
        svc.Catalog.Libraries.Should().BeEmpty();
        svc.Catalog.Playlists.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAsync_creates_missing_content_directory_and_bootstraps_empty_catalog()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        Directory.Delete(root, recursive: true);

        var svc = new CatalogService(
            paths.Object,
            new LibraryRegistryService(paths.Object, NullLogger<LibraryRegistryService>.Instance),
            new PlaylistRegistryService(paths.Object, NullLogger<PlaylistRegistryService>.Instance),
            new CpresDocumentService(NullLogger<CpresDocumentService>.Instance),
            new ContentMaintenanceLogService(paths.Object, NullLogger<ContentMaintenanceLogService>.Instance),
            NullLogger<CatalogService>.Instance);

        await svc.LoadAsync();

        Directory.Exists(paths.Object.GetLibrariesRootDirectory()).Should().BeTrue();
        Directory.Exists(paths.Object.GetPlaylistsRootDirectory()).Should().BeTrue();
        Directory.Exists(paths.Object.GetPresentationsRootDirectory()).Should().BeTrue();
        File.Exists(paths.Object.GetLibrariesJsonPath()).Should().BeFalse();
        File.Exists(paths.Object.GetPlaylistsJsonPath()).Should().BeFalse();
        svc.Catalog.Libraries.Should().BeEmpty();
        svc.Catalog.Playlists.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAsync_moves_orphan_bundles_into_managed_presentations_directory()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var orphanPath = TestPresentationBundles.WriteBundle(root, "orphan.cpres", "Recovered Song", "song-001");

        var svc = new CatalogService(
            paths.Object,
            new LibraryRegistryService(paths.Object, NullLogger<LibraryRegistryService>.Instance),
            new PlaylistRegistryService(paths.Object, NullLogger<PlaylistRegistryService>.Instance),
            new CpresDocumentService(NullLogger<CpresDocumentService>.Instance),
            new ContentMaintenanceLogService(paths.Object, NullLogger<ContentMaintenanceLogService>.Instance),
            NullLogger<CatalogService>.Instance);

        await svc.LoadAsync();

        File.Exists(orphanPath).Should().BeFalse();
        var managedBundles = Directory.EnumerateFiles(paths.Object.GetPresentationsRootDirectory(), "*.cpres", SearchOption.AllDirectories).ToList();
        managedBundles.Should().ContainSingle();
        svc.Catalog.Libraries.Should().ContainSingle(library => library.Id == "local-library");
        svc.Catalog.Libraries[0].Presentations.Should().ContainSingle();
        svc.Catalog.Libraries[0].Presentations[0].Path.Should().Be(paths.Object.ToContentRelativePath(managedBundles[0]));
    }

    [Fact]
    public async Task LoadAsync_with_manual_scan_trigger_writes_maintenance_log_entries()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        TestPresentationBundles.WriteBundle(root, "orphan.cpres", "Recovered Song", "song-002");
        var logService = new ContentMaintenanceLogService(paths.Object, NullLogger<ContentMaintenanceLogService>.Instance);
        var svc = new CatalogService(
            paths.Object,
            new LibraryRegistryService(paths.Object, NullLogger<LibraryRegistryService>.Instance),
            new PlaylistRegistryService(paths.Object, NullLogger<PlaylistRegistryService>.Instance),
            new CpresDocumentService(NullLogger<CpresDocumentService>.Instance),
            logService,
            NullLogger<CatalogService>.Instance);

        await svc.LoadAsync(ContentMaintenanceTrigger.ManualScan);

        var entries = await logService.ReadRecentEntriesAsync();
        entries.Should().Contain(entry => entry.Trigger == ContentMaintenanceTrigger.ManualScan.ToString() && entry.EventType == "orphan-bundle-moved");
        entries.Should().Contain(entry => entry.Trigger == ContentMaintenanceTrigger.ManualScan.ToString() && entry.EventType == "scan-complete");
    }

    [Fact]
    public async Task LoadAsync_merges_duplicate_library_and_playlist_ids()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        TestPresentationBundles.WriteBundle(paths.Object.GetPresentationsDirectory(), "alpha.cpres", "Alpha", "alpha-id");

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };

        await File.WriteAllTextAsync(
            paths.Object.GetLibrariesIndexPath(),
            JsonSerializer.Serialize(
                new DomainIndex
                {
                    Entries =
                    [
                        new DomainIndexEntry { Id = "songs", Name = "Songs", CreatedAt = "2026-01-01T00:00:00.000Z" },
                        new DomainIndexEntry { Id = "songs", Name = "Songs", Description = "merged", CreatedAt = "2026-01-01T00:00:00.000Z" },
                    ],
                },
                jsonOptions));
        Directory.CreateDirectory(paths.Object.GetLibraryRootDirectory("songs"));
        await File.WriteAllTextAsync(
            paths.Object.GetLibraryManifestPath("songs"),
            JsonSerializer.Serialize(
                new LibraryManifest
                {
                    Id = "songs",
                    Name = "Songs",
                    Description = "merged",
                    CreatedAt = "2026-01-01T00:00:00.000Z",
                    Presentations =
                    [
                        new PresentationRefDto { Path = "Presentations/alpha.cpres", Title = "Alpha", UpdatedAt = "2026-01-01T00:00:00.000Z" },
                    ],
                },
                jsonOptions));

        await File.WriteAllTextAsync(
            paths.Object.GetPlaylistsIndexPath(),
            JsonSerializer.Serialize(
                new DomainIndex
                {
                    Entries =
                    [
                        new DomainIndexEntry { Id = "set-1", Name = "Set 1", CreatedAt = "2026-01-01T00:00:00.000Z" },
                        new DomainIndexEntry { Id = "set-1", Name = "Set 1", CreatedAt = "2026-01-01T00:00:00.000Z" },
                    ],
                },
                jsonOptions));
        Directory.CreateDirectory(paths.Object.GetPlaylistRootDirectory("set-1"));
        await File.WriteAllTextAsync(
            paths.Object.GetPlaylistManifestPath("set-1"),
            JsonSerializer.Serialize(
                new PlaylistManifest
                {
                    Id = "set-1",
                    Name = "Set 1",
                    CreatedAt = "2026-01-01T00:00:00.000Z",
                    Items =
                    [
                        new PresentationRefDto { Path = "Presentations/alpha.cpres", Title = "Alpha", UpdatedAt = "2026-01-01T00:00:00.000Z" },
                    ],
                },
                jsonOptions));

        var svc = new CatalogService(
            paths.Object,
            new LibraryRegistryService(paths.Object, NullLogger<LibraryRegistryService>.Instance),
            new PlaylistRegistryService(paths.Object, NullLogger<PlaylistRegistryService>.Instance),
            new CpresDocumentService(NullLogger<CpresDocumentService>.Instance),
            new ContentMaintenanceLogService(paths.Object, NullLogger<ContentMaintenanceLogService>.Instance),
            NullLogger<CatalogService>.Instance);

        await svc.LoadAsync();

        svc.Catalog.Libraries.Should().ContainSingle(library => library.Id == "songs");
        svc.Catalog.Libraries[0].Description.Should().Be("merged");
        svc.Catalog.Libraries[0].Presentations.Should().ContainSingle();
        svc.Catalog.Playlists.Should().ContainSingle(playlist => playlist.Id == "set-1");
        svc.Catalog.Playlists[0].Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task SaveAsync_persists_library_and_playlist_registry_manifests()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var svc = new CatalogService(
            paths.Object,
            new LibraryRegistryService(paths.Object, NullLogger<LibraryRegistryService>.Instance),
            new PlaylistRegistryService(paths.Object, NullLogger<PlaylistRegistryService>.Instance),
            new CpresDocumentService(NullLogger<CpresDocumentService>.Instance),
            new ContentMaintenanceLogService(paths.Object, NullLogger<ContentMaintenanceLogService>.Instance),
            NullLogger<CatalogService>.Instance);
        await svc.LoadAsync();

        svc.Catalog.Libraries.Clear();
        svc.Catalog.Playlists.Clear();
        svc.Catalog.Libraries.Add(new LibraryDto
        {
            Id = "main",
            Name = "Main",
            Presentations = new List<PresentationRefDto>
            {
                new() { Path = "Presentations/test.cpres", Title = "Test", UpdatedAt = "2025-01-01T00:00:00.000Z" },
            },
        });
        svc.Catalog.Playlists.Add(new PlaylistDto
        {
            Id = "sunday",
            Name = "Sunday",
            Items = new List<PresentationRefDto>
            {
                new() { Path = "Presentations/test.cpres", Title = "Test", UpdatedAt = "2025-01-01T00:00:00.000Z" },
            },
        });

        await svc.SaveAsync();

        File.Exists(paths.Object.GetLibrariesIndexPath()).Should().BeTrue();
        File.Exists(paths.Object.GetPlaylistsIndexPath()).Should().BeTrue();
        File.ReadAllText(paths.Object.GetLibraryManifestPath("main")).Should().Contain("\"name\": \"Main\"");
        File.ReadAllText(paths.Object.GetPlaylistManifestPath("sunday")).Should().Contain("\"name\": \"Sunday\"");
    }
}