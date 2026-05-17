using ChurchPresenter.App.Tests.TestSupport;
using ChurchPresenter.Core.Cpres;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

namespace ChurchPresenter.App.Tests.Services.Content;

public sealed class LocalCollectionServiceTests
{
    [Fact]
    public async Task CreatePresentationAsync_saves_blank_bundle_and_assigns_library()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var cpres = new CpresDocumentService(NullLogger<CpresDocumentService>.Instance);
        var libraryRegistry = new LibraryRegistryService(paths.Object, NullLogger<LibraryRegistryService>.Instance);
        var playlistRegistry = new PlaylistRegistryService(paths.Object, NullLogger<PlaylistRegistryService>.Instance);
        var catalog = new CatalogService(paths.Object, libraryRegistry, playlistRegistry, cpres, new ContentMaintenanceLogService(paths.Object, NullLogger<ContentMaintenanceLogService>.Instance), NullLogger<CatalogService>.Instance);
        var projects = new PresentationProjectService(paths.Object, cpres, NullLogger<PresentationProjectService>.Instance);
        var presentations = new PresentationDocumentService(paths.Object, cpres, projects, NullLogger<PresentationDocumentService>.Instance);
        var service = new LocalCollectionService(paths.Object, catalog, libraryRegistry, playlistRegistry, presentations, projects, NullLogger<LocalCollectionService>.Instance);

        var result = await service.CreatePresentationAsync(
            "Sunday Worship",
            null,
            null,
            "Songs",
            null,
            aspectRatio: "4:3",
            slideSize: new SlideSizeDto { Width = 1440, Height = 1080 });
        await catalog.LoadAsync();

        var absolutePath = paths.Object.ResolvePresentationPath(result.LocalPath);
        File.Exists(absolutePath).Should().BeTrue();
        result.Title.Should().Be("Sunday Worship");
        catalog.Catalog.Libraries.Should().ContainSingle(l => l.Id == result.LibraryId && l.Presentations.Any(p => p.Path == result.LocalPath));

        var project = projects.Open(result.LocalPath);
        project.Manifest.Title.Should().Be("Sunday Worship");
        project.Manifest.AspectRatio.Should().Be("4:3");
        project.Manifest.SlideSize.Should().BeEquivalentTo(new SlideSizeDto { Width = 1440, Height = 1080 });
        project.Slides.Should().ContainSingle(s => s.Type == "blank");
        project.Arrangement.Order.Should().ContainSingle(project.Slides[0].Id);
    }

    [Fact]
    public async Task ImportPresentationAsync_copies_bundle_into_local_collection_and_assigns_library_and_playlist()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var importRoot = Path.Combine(Path.GetTempPath(), "church-presenter-imports", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var sourcePath = TestPresentationBundles.WriteBundle(importRoot, "sermon.cpres", "Sunday Sermon");
        var cpres = new CpresDocumentService(NullLogger<CpresDocumentService>.Instance);
        var libraryRegistry = new LibraryRegistryService(paths.Object, NullLogger<LibraryRegistryService>.Instance);
        var playlistRegistry = new PlaylistRegistryService(paths.Object, NullLogger<PlaylistRegistryService>.Instance);
        var catalog = new CatalogService(paths.Object, libraryRegistry, playlistRegistry, cpres, new ContentMaintenanceLogService(paths.Object, NullLogger<ContentMaintenanceLogService>.Instance), NullLogger<CatalogService>.Instance);
        var projects = new PresentationProjectService(paths.Object, cpres, NullLogger<PresentationProjectService>.Instance);
        var presentations = new PresentationDocumentService(paths.Object, cpres, projects, NullLogger<PresentationDocumentService>.Instance);
        var service = new LocalCollectionService(paths.Object, catalog, libraryRegistry, playlistRegistry, presentations, projects, NullLogger<LocalCollectionService>.Instance);

        var result = await service.ImportPresentationAsync(sourcePath, null, null, "Songs", "Sunday Set");
        await catalog.LoadAsync();

        File.Exists(paths.Object.ResolvePresentationPath(result.LocalPath)).Should().BeTrue();
        Guid.TryParse(result.LibraryId, out _).Should().BeTrue();
        Guid.TryParse(result.PlaylistId, out _).Should().BeTrue();
        catalog.Catalog.Libraries.Should().ContainSingle(l => l.Id == result.LibraryId && l.Presentations.Any(p => p.Path == result.LocalPath));
        catalog.Catalog.Playlists.Should().ContainSingle(p => p.Id == result.PlaylistId && p.Items.Any(i => i.Path == result.LocalPath));
    }

    [Fact]
    public async Task ImportLibraryAsync_imports_all_presentations_into_named_library()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var importRoot = Path.Combine(Path.GetTempPath(), "church-presenter-imports", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        TestPresentationBundles.WriteBundle(importRoot, "alpha.cpres", "Alpha");
        TestPresentationBundles.WriteBundle(importRoot, "beta.cpres", "Beta");

        var cpres = new CpresDocumentService(NullLogger<CpresDocumentService>.Instance);
        var libraryRegistry = new LibraryRegistryService(paths.Object, NullLogger<LibraryRegistryService>.Instance);
        var playlistRegistry = new PlaylistRegistryService(paths.Object, NullLogger<PlaylistRegistryService>.Instance);
        var catalog = new CatalogService(paths.Object, libraryRegistry, playlistRegistry, cpres, new ContentMaintenanceLogService(paths.Object, NullLogger<ContentMaintenanceLogService>.Instance), NullLogger<CatalogService>.Instance);
        var projects = new PresentationProjectService(paths.Object, cpres, NullLogger<PresentationProjectService>.Instance);
        var presentations = new PresentationDocumentService(paths.Object, cpres, projects, NullLogger<PresentationDocumentService>.Instance);
        var service = new LocalCollectionService(paths.Object, catalog, libraryRegistry, playlistRegistry, presentations, projects, NullLogger<LocalCollectionService>.Instance);

        var result = await service.ImportLibraryAsync(importRoot, "Seasonal");
        await catalog.LoadAsync();

        Guid.TryParse(result.LibraryId, out _).Should().BeTrue();
        result.ImportedPresentationPaths.Should().HaveCount(2);
        catalog.Catalog.Libraries.Should().ContainSingle(l => l.Id == result.LibraryId && l.Presentations.Count == 2);
        result.ImportedPresentationPaths.Should().OnlyContain(path => File.Exists(paths.Object.ResolvePresentationPath(path)));
    }

    [Fact]
    public async Task Playlist_management_operations_rename_reorder_and_remove_presentations()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var importRoot = Path.Combine(Path.GetTempPath(), "church-presenter-imports", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var firstSource = TestPresentationBundles.WriteBundle(importRoot, "first.cpres", "First");
        var secondSource = TestPresentationBundles.WriteBundle(importRoot, "second.cpres", "Second");
        var cpres = new CpresDocumentService(NullLogger<CpresDocumentService>.Instance);
        var libraryRegistry = new LibraryRegistryService(paths.Object, NullLogger<LibraryRegistryService>.Instance);
        var playlistRegistry = new PlaylistRegistryService(paths.Object, NullLogger<PlaylistRegistryService>.Instance);
        var catalog = new CatalogService(paths.Object, libraryRegistry, playlistRegistry, cpres, new ContentMaintenanceLogService(paths.Object, NullLogger<ContentMaintenanceLogService>.Instance), NullLogger<CatalogService>.Instance);
        var projects = new PresentationProjectService(paths.Object, cpres, NullLogger<PresentationProjectService>.Instance);
        var presentations = new PresentationDocumentService(paths.Object, cpres, projects, NullLogger<PresentationDocumentService>.Instance);
        var service = new LocalCollectionService(paths.Object, catalog, libraryRegistry, playlistRegistry, presentations, projects, NullLogger<LocalCollectionService>.Instance);

        var first = await service.ImportPresentationAsync(firstSource, null, null, "Songs", "Set");
        var second = await service.ImportPresentationAsync(secondSource, first.LibraryId, first.PlaylistId, null, null);
        await service.RenamePlaylistAsync(first.PlaylistId!, "Sunday Set");
        await service.MovePlaylistItemAsync(first.PlaylistId!, second.LocalPath, -1);
        await service.RemovePresentationFromPlaylistAsync(first.PlaylistId!, first.LocalPath);
        await catalog.LoadAsync();

        var playlist = catalog.Catalog.Playlists.Should().ContainSingle().Subject;
        playlist.Name.Should().Be("Sunday Set");
        var playlistManifest = await playlistRegistry.LoadAsync(first.PlaylistId!);
        playlistManifest.Should().NotBeNull();
        playlistManifest!.Name.Should().Be("Sunday Set");
        playlist.Items.Select(item => item.Path).Should().ContainInOrder(second.LocalPath);

        var library = catalog.Catalog.Libraries.Should().ContainSingle(item => item.Id == first.LibraryId).Subject;
        library.Presentations.Select(item => item.Path).Should().Contain(first.LocalPath);
    }

    [Fact]
    public async Task AddPresentationToPlaylistAsync_preserves_duplicate_entries()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var importRoot = Path.Combine(Path.GetTempPath(), "church-presenter-imports", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var sourcePath = TestPresentationBundles.WriteBundle(importRoot, "chorus.cpres", "Chorus");
        var cpres = new CpresDocumentService(NullLogger<CpresDocumentService>.Instance);
        var libraryRegistry = new LibraryRegistryService(paths.Object, NullLogger<LibraryRegistryService>.Instance);
        var playlistRegistry = new PlaylistRegistryService(paths.Object, NullLogger<PlaylistRegistryService>.Instance);
        var catalog = new CatalogService(paths.Object, libraryRegistry, playlistRegistry, cpres, new ContentMaintenanceLogService(paths.Object, NullLogger<ContentMaintenanceLogService>.Instance), NullLogger<CatalogService>.Instance);
        var projects = new PresentationProjectService(paths.Object, cpres, NullLogger<PresentationProjectService>.Instance);
        var presentations = new PresentationDocumentService(paths.Object, cpres, projects, NullLogger<PresentationDocumentService>.Instance);
        var service = new LocalCollectionService(paths.Object, catalog, libraryRegistry, playlistRegistry, presentations, projects, NullLogger<LocalCollectionService>.Instance);

        var imported = await service.ImportPresentationAsync(sourcePath, null, null, "Songs", "Set");
        await service.AddPresentationToPlaylistAsync(imported.PlaylistId!, imported.LocalPath);
        await catalog.LoadAsync();

        var playlist = catalog.Catalog.Playlists.Should().ContainSingle().Subject;
        playlist.Items.Select(item => item.Path).Should().ContainInOrder(imported.LocalPath, imported.LocalPath);
    }

    [Fact]
    public async Task RemovePresentationFromPlaylistAsync_removes_one_duplicate_by_index()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var importRoot = Path.Combine(Path.GetTempPath(), "church-presenter-imports", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var sourcePath = TestPresentationBundles.WriteBundle(importRoot, "chorus.cpres", "Chorus");
        var cpres = new CpresDocumentService(NullLogger<CpresDocumentService>.Instance);
        var libraryRegistry = new LibraryRegistryService(paths.Object, NullLogger<LibraryRegistryService>.Instance);
        var playlistRegistry = new PlaylistRegistryService(paths.Object, NullLogger<PlaylistRegistryService>.Instance);
        var catalog = new CatalogService(paths.Object, libraryRegistry, playlistRegistry, cpres, new ContentMaintenanceLogService(paths.Object, NullLogger<ContentMaintenanceLogService>.Instance), NullLogger<CatalogService>.Instance);
        var projects = new PresentationProjectService(paths.Object, cpres, NullLogger<PresentationProjectService>.Instance);
        var presentations = new PresentationDocumentService(paths.Object, cpres, projects, NullLogger<PresentationDocumentService>.Instance);
        var service = new LocalCollectionService(paths.Object, catalog, libraryRegistry, playlistRegistry, presentations, projects, NullLogger<LocalCollectionService>.Instance);

        var imported = await service.ImportPresentationAsync(sourcePath, null, null, "Songs", "Set");
        await service.AddPresentationToPlaylistAsync(imported.PlaylistId!, imported.LocalPath);

        await service.RemovePresentationFromPlaylistAsync(imported.PlaylistId!, imported.LocalPath, playlistIndex: 1);
        await catalog.LoadAsync();

        var playlist = catalog.Catalog.Playlists.Should().ContainSingle().Subject;
        playlist.Items.Select(item => item.Path).Should().ContainSingle().Which.Should().Be(imported.LocalPath);
    }

    [Fact]
    public async Task RemovePresentationFromPlaylistAsync_can_remove_all_duplicates()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var importRoot = Path.Combine(Path.GetTempPath(), "church-presenter-imports", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var sourcePath = TestPresentationBundles.WriteBundle(importRoot, "chorus.cpres", "Chorus");
        var cpres = new CpresDocumentService(NullLogger<CpresDocumentService>.Instance);
        var libraryRegistry = new LibraryRegistryService(paths.Object, NullLogger<LibraryRegistryService>.Instance);
        var playlistRegistry = new PlaylistRegistryService(paths.Object, NullLogger<PlaylistRegistryService>.Instance);
        var catalog = new CatalogService(paths.Object, libraryRegistry, playlistRegistry, cpres, new ContentMaintenanceLogService(paths.Object, NullLogger<ContentMaintenanceLogService>.Instance), NullLogger<CatalogService>.Instance);
        var projects = new PresentationProjectService(paths.Object, cpres, NullLogger<PresentationProjectService>.Instance);
        var presentations = new PresentationDocumentService(paths.Object, cpres, projects, NullLogger<PresentationDocumentService>.Instance);
        var service = new LocalCollectionService(paths.Object, catalog, libraryRegistry, playlistRegistry, presentations, projects, NullLogger<LocalCollectionService>.Instance);

        var imported = await service.ImportPresentationAsync(sourcePath, null, null, "Songs", "Set");
        await service.AddPresentationToPlaylistAsync(imported.PlaylistId!, imported.LocalPath);

        await service.RemovePresentationFromPlaylistAsync(imported.PlaylistId!, imported.LocalPath, removeAllInstances: true);
        await catalog.LoadAsync();

        catalog.Catalog.Playlists.Should().ContainSingle().Subject.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task RemovePresentationFromPlaylistAsync_preserves_library_source_and_bundle()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var importRoot = Path.Combine(Path.GetTempPath(), "church-presenter-imports", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var sourcePath = TestPresentationBundles.WriteBundle(importRoot, "sermon.cpres", "Sermon");
        var cpres = new CpresDocumentService(NullLogger<CpresDocumentService>.Instance);
        var libraryRegistry = new LibraryRegistryService(paths.Object, NullLogger<LibraryRegistryService>.Instance);
        var playlistRegistry = new PlaylistRegistryService(paths.Object, NullLogger<PlaylistRegistryService>.Instance);
        var catalog = new CatalogService(paths.Object, libraryRegistry, playlistRegistry, cpres, new ContentMaintenanceLogService(paths.Object, NullLogger<ContentMaintenanceLogService>.Instance), NullLogger<CatalogService>.Instance);
        var projects = new PresentationProjectService(paths.Object, cpres, NullLogger<PresentationProjectService>.Instance);
        var presentations = new PresentationDocumentService(paths.Object, cpres, projects, NullLogger<PresentationDocumentService>.Instance);
        var service = new LocalCollectionService(paths.Object, catalog, libraryRegistry, playlistRegistry, presentations, projects, NullLogger<LocalCollectionService>.Instance);

        var imported = await service.ImportPresentationAsync(sourcePath, null, null, "Messages", "Sunday Set");

        await service.RemovePresentationFromPlaylistAsync(imported.PlaylistId!, imported.LocalPath);
        await catalog.LoadAsync();

        File.Exists(paths.Object.ResolvePresentationPath(imported.LocalPath)).Should().BeTrue();
        catalog.Catalog.Libraries.Should().ContainSingle(item => item.Id == imported.LibraryId && item.Presentations.Any(p => p.Path == imported.LocalPath));
        catalog.Catalog.Playlists.Should().ContainSingle(item => item.Id == imported.PlaylistId && item.Items.All(p => p.Path != imported.LocalPath));

    }

    [Fact]
    public async Task DuplicatePlaylistAsync_copies_items_with_independent_name_and_id()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var importRoot = Path.Combine(Path.GetTempPath(), "church-presenter-imports", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var sourcePath = TestPresentationBundles.WriteBundle(importRoot, "song.cpres", "Song");
        var cpres = new CpresDocumentService(NullLogger<CpresDocumentService>.Instance);
        var libraryRegistry = new LibraryRegistryService(paths.Object, NullLogger<LibraryRegistryService>.Instance);
        var playlistRegistry = new PlaylistRegistryService(paths.Object, NullLogger<PlaylistRegistryService>.Instance);
        var catalog = new CatalogService(paths.Object, libraryRegistry, playlistRegistry, cpres, new ContentMaintenanceLogService(paths.Object, NullLogger<ContentMaintenanceLogService>.Instance), NullLogger<CatalogService>.Instance);
        var projects = new PresentationProjectService(paths.Object, cpres, NullLogger<PresentationProjectService>.Instance);
        var presentations = new PresentationDocumentService(paths.Object, cpres, projects, NullLogger<PresentationDocumentService>.Instance);
        var service = new LocalCollectionService(paths.Object, catalog, libraryRegistry, playlistRegistry, presentations, projects, NullLogger<LocalCollectionService>.Instance);

        var imported = await service.ImportPresentationAsync(sourcePath, null, null, "Songs", "Set");
        var duplicate = await service.DuplicatePlaylistAsync(imported.PlaylistId!);
        await catalog.LoadAsync();

        duplicate.Id.Should().NotBe(imported.PlaylistId);
        duplicate.Name.Should().Be("Set Copy");
        duplicate.Items.Should().ContainSingle(item => item.Path == imported.LocalPath);
        var duplicateManifest = await playlistRegistry.LoadAsync(duplicate.Id);
        duplicateManifest.Should().NotBeNull();
        duplicateManifest!.Items.Should().ContainSingle(item => item.Path == imported.LocalPath);
        catalog.Catalog.Playlists.Should().HaveCount(2);
    }

    [Fact]
    public async Task Move_source_operations_persist_registry_index_order()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var cpres = new CpresDocumentService(NullLogger<CpresDocumentService>.Instance);
        var libraryRegistry = new LibraryRegistryService(paths.Object, NullLogger<LibraryRegistryService>.Instance);
        var playlistRegistry = new PlaylistRegistryService(paths.Object, NullLogger<PlaylistRegistryService>.Instance);
        var catalog = new CatalogService(paths.Object, libraryRegistry, playlistRegistry, cpres, new ContentMaintenanceLogService(paths.Object, NullLogger<ContentMaintenanceLogService>.Instance), NullLogger<CatalogService>.Instance);
        var projects = new PresentationProjectService(paths.Object, cpres, NullLogger<PresentationProjectService>.Instance);
        var presentations = new PresentationDocumentService(paths.Object, cpres, projects, NullLogger<PresentationDocumentService>.Instance);
        var service = new LocalCollectionService(paths.Object, catalog, libraryRegistry, playlistRegistry, presentations, projects, NullLogger<LocalCollectionService>.Instance);

        var firstLibrary = await service.EnsureLibraryAsync("First Library");
        var secondLibrary = await service.EnsureLibraryAsync("Second Library");
        var thirdLibrary = await service.EnsureLibraryAsync("Third Library");
        var firstPlaylist = await service.EnsurePlaylistAsync("First Playlist");
        var secondPlaylist = await service.EnsurePlaylistAsync("Second Playlist");
        var thirdPlaylist = await service.EnsurePlaylistAsync("Third Playlist");

        await service.MoveLibraryAsync(thirdLibrary.Id, 0);
        await service.MovePlaylistAsync(thirdPlaylist.Id, 0);
        await catalog.LoadAsync();

        catalog.Catalog.Libraries.Select(item => item.Id).Should().ContainInOrder(thirdLibrary.Id, firstLibrary.Id, secondLibrary.Id);
        catalog.Catalog.Playlists.Select(item => item.Id).Should().ContainInOrder(thirdPlaylist.Id, firstPlaylist.Id, secondPlaylist.Id);
        (await libraryRegistry.LoadIndexAsync()).Entries.Select(item => item.Id).Should().ContainInOrder(thirdLibrary.Id, firstLibrary.Id, secondLibrary.Id);
        (await playlistRegistry.LoadIndexAsync()).Entries.Select(item => item.Id).Should().ContainInOrder(thirdPlaylist.Id, firstPlaylist.Id, secondPlaylist.Id);
    }

    [Fact]
    public async Task Delete_source_operations_remove_registry_manifests_and_indexes()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var cpres = new CpresDocumentService(NullLogger<CpresDocumentService>.Instance);
        var libraryRegistry = new LibraryRegistryService(paths.Object, NullLogger<LibraryRegistryService>.Instance);
        var playlistRegistry = new PlaylistRegistryService(paths.Object, NullLogger<PlaylistRegistryService>.Instance);
        var catalog = new CatalogService(paths.Object, libraryRegistry, playlistRegistry, cpres, new ContentMaintenanceLogService(paths.Object, NullLogger<ContentMaintenanceLogService>.Instance), NullLogger<CatalogService>.Instance);
        var projects = new PresentationProjectService(paths.Object, cpres, NullLogger<PresentationProjectService>.Instance);
        var presentations = new PresentationDocumentService(paths.Object, cpres, projects, NullLogger<PresentationDocumentService>.Instance);
        var service = new LocalCollectionService(paths.Object, catalog, libraryRegistry, playlistRegistry, presentations, projects, NullLogger<LocalCollectionService>.Instance);

        var library = await service.EnsureLibraryAsync("Temporary Library");
        var playlist = await service.EnsurePlaylistAsync("Temporary Playlist");

        await service.DeleteLibraryAsync(library.Id);
        await service.DeletePlaylistAsync(playlist.Id);

        (await libraryRegistry.LoadAsync(library.Id)).Should().BeNull();
        (await playlistRegistry.LoadAsync(playlist.Id)).Should().BeNull();
        (await libraryRegistry.LoadIndexAsync()).Entries.Should().NotContain(entry => entry.Id == library.Id);
        (await playlistRegistry.LoadIndexAsync()).Entries.Should().NotContain(entry => entry.Id == playlist.Id);
    }

    [Fact]
    public async Task DeleteLibraryAsync_deletes_owned_presentations_and_downstream_references()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var importRoot = Path.Combine(Path.GetTempPath(), "church-presenter-imports", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var firstSource = TestPresentationBundles.WriteBundle(importRoot, "first.cpres", "First");
        var secondSource = TestPresentationBundles.WriteBundle(importRoot, "second.cpres", "Second");
        var cpres = new CpresDocumentService(NullLogger<CpresDocumentService>.Instance);
        var libraryRegistry = new LibraryRegistryService(paths.Object, NullLogger<LibraryRegistryService>.Instance);
        var playlistRegistry = new PlaylistRegistryService(paths.Object, NullLogger<PlaylistRegistryService>.Instance);
        var catalog = new CatalogService(paths.Object, libraryRegistry, playlistRegistry, cpres, new ContentMaintenanceLogService(paths.Object, NullLogger<ContentMaintenanceLogService>.Instance), NullLogger<CatalogService>.Instance);
        var projects = new PresentationProjectService(paths.Object, cpres, NullLogger<PresentationProjectService>.Instance);
        var presentations = new PresentationDocumentService(paths.Object, cpres, projects, NullLogger<PresentationDocumentService>.Instance);
        var workspace = new WorkspaceService(paths.Object, NullLogger<WorkspaceService>.Instance);
        var settings = new SettingsService(paths.Object, new SharedConfigService(paths.Object, NullLogger<SharedConfigService>.Instance), new MachineStateService(paths.Object, NullLogger<MachineStateService>.Instance), NullLogger<SettingsService>.Instance);
        var presentationActions = new PresentationItemActionService(
            paths.Object,
            catalog,
            workspace,
            settings,
            projects,
            presentations,
            NullLogger<PresentationItemActionService>.Instance);
        var service = new LocalCollectionService(paths.Object, catalog, libraryRegistry, playlistRegistry, presentations, projects, NullLogger<LocalCollectionService>.Instance, presentationActions);

        var first = await service.ImportPresentationAsync(firstSource, null, null, "Messages", "Sunday Set");
        var second = await service.ImportPresentationAsync(secondSource, first.LibraryId, first.PlaylistId, null, null);
        workspace.Update(item => item.SelectedPresentationPath = first.LocalPath);
        settings.Update(item =>
        {
            item.RecentFiles.Add(new PresentationRefDto { Path = first.LocalPath, Title = first.Title, UpdatedAt = "" });
            item.RecentFiles.Add(new PresentationRefDto { Path = second.LocalPath, Title = second.Title, UpdatedAt = "" });
        });
        await settings.SaveAsync();

        await service.DeleteLibraryAsync(first.LibraryId);
        await catalog.LoadAsync();
        await workspace.LoadAsync();
        await settings.LoadAsync();

        (await libraryRegistry.LoadAsync(first.LibraryId)).Should().BeNull();
        catalog.Catalog.Libraries.Should().NotContain(item => item.Id == first.LibraryId);
        catalog.Catalog.Playlists.Should().ContainSingle(item =>
            item.Id == first.PlaylistId
            && item.Items.All(p => p.Path != first.LocalPath && p.Path != second.LocalPath));
        File.Exists(paths.Object.ResolvePresentationPath(first.LocalPath)).Should().BeFalse();
        File.Exists(paths.Object.ResolvePresentationPath(second.LocalPath)).Should().BeFalse();
        workspace.Workspace.SelectedPresentationPath.Should().BeNull();
        settings.Settings.RecentFiles.Should().NotContain(item => item.Path == first.LocalPath || item.Path == second.LocalPath);
    }
}