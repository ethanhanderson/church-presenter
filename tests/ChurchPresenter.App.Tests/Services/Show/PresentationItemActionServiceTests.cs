using ChurchPresenter.App.Tests.TestSupport;
using ChurchPresenter.Core.Cpres;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

namespace ChurchPresenter.App.Tests.Services.Show;

public sealed class PresentationItemActionServiceTests
{
    [Fact]
    public async Task AddPresentationToLibraryAsync_upserts_ref_without_creating_duplicate_entries()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var sourceAbsolutePath = TestPresentationBundles.WriteBundle(paths.Object.GetPresentationsDirectory(), "alpha.cpres", "Alpha", "alpha-id");
        var sourceRelativePath = paths.Object.ToContentRelativePath(sourceAbsolutePath);

        var catalog = CreateCatalog(paths);
        await catalog.LoadAsync();

        var sourceLibraryId = Guid.NewGuid().ToString();
        var targetLibraryId = Guid.NewGuid().ToString();
        catalog.Catalog.Libraries.Add(new LibraryDto
        {
            Id = sourceLibraryId,
            Name = "Songs",
            Presentations = new List<PresentationRefDto>
            {
                new() { Path = sourceRelativePath, Title = "Alpha", UpdatedAt = "2026-01-01T00:00:00.000Z", ArrangementId = "chorus-first" },
            },
        });
        catalog.Catalog.Libraries.Add(new LibraryDto
        {
            Id = targetLibraryId,
            Name = "Seasonal",
            Presentations = new List<PresentationRefDto>(),
        });
        await catalog.SaveAsync();

        var service = CreateService(paths, catalog);

        await service.AddPresentationToLibraryAsync(targetLibraryId, sourceRelativePath);
        await catalog.LoadAsync();

        var source = catalog.Catalog.Libraries.Single(l => l.Id == sourceLibraryId);
        var target = catalog.Catalog.Libraries.Single(l => l.Id == targetLibraryId);

        source.Presentations.Should().ContainSingle(p => p.Path == sourceRelativePath);
        target.Presentations.Should().ContainSingle(p => p.Path == sourceRelativePath);

        await service.AddPresentationToLibraryAsync(targetLibraryId, sourceRelativePath);
        await catalog.LoadAsync();

        target = catalog.Catalog.Libraries.Single(l => l.Id == targetLibraryId);
        target.Presentations.Should().ContainSingle(p => p.Path == sourceRelativePath, "upsert must not create duplicate entries");
    }

    [Fact]
    public async Task AddPresentationToPlaylistAsync_appends_ref_to_playlist_preserving_library_membership()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var sourceAbsolutePath = TestPresentationBundles.WriteBundle(paths.Object.GetPresentationsDirectory(), "alpha.cpres", "Alpha", "alpha-id");
        var sourceRelativePath = paths.Object.ToContentRelativePath(sourceAbsolutePath);

        var catalog = CreateCatalog(paths);
        await catalog.LoadAsync();

        var libraryId = Guid.NewGuid().ToString();
        var playlistId = Guid.NewGuid().ToString();
        catalog.Catalog.Libraries.Add(new LibraryDto
        {
            Id = libraryId,
            Name = "Songs",
            Presentations = new List<PresentationRefDto>
            {
                new() { Path = sourceRelativePath, Title = "Alpha", UpdatedAt = "2026-01-01T00:00:00.000Z", ArrangementId = "chorus-first" },
            },
        });
        catalog.Catalog.Playlists.Add(new PlaylistDto
        {
            Id = playlistId,
            Name = "Sunday Set",
            Items = new List<PresentationRefDto>(),
        });
        await catalog.SaveAsync();

        var service = CreateService(paths, catalog);

        await service.AddPresentationToPlaylistAsync(playlistId, sourceRelativePath);
        await catalog.LoadAsync();

        var library = catalog.Catalog.Libraries.Single(l => l.Id == libraryId);
        var playlist = catalog.Catalog.Playlists.Single(p => p.Id == playlistId);

        library.Presentations.Should().ContainSingle(p => p.Path == sourceRelativePath, "library membership must be preserved");
        playlist.Items.Should().ContainSingle(p => p.Path == sourceRelativePath);
    }

    [Fact]
    public async Task DuplicatePresentationAsync_creates_new_bundle_and_attaches_to_targets()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var sourceAbsolutePath = TestPresentationBundles.WriteBundle(paths.Object.GetPresentationsDirectory(), "alpha.cpres", "Alpha", "alpha-id");
        var sourceRelativePath = paths.Object.ToContentRelativePath(sourceAbsolutePath);

        var catalog = CreateCatalog(paths);
        await catalog.LoadAsync();

        var libraryId = Guid.NewGuid().ToString();
        var playlistId = Guid.NewGuid().ToString();
        catalog.Catalog.Libraries.Add(new LibraryDto
        {
            Id = libraryId,
            Name = "Songs",
            Presentations = new List<PresentationRefDto>
            {
                new() { Path = sourceRelativePath, Title = "Alpha", UpdatedAt = "2026-01-01T00:00:00.000Z" },
            },
        });
        catalog.Catalog.Playlists.Add(new PlaylistDto
        {
            Id = playlistId,
            Name = "Sunday Set",
            Items = new List<PresentationRefDto>
            {
                new() { Path = sourceRelativePath, Title = "Alpha", UpdatedAt = "2026-01-01T00:00:00.000Z" },
            },
        });
        await catalog.SaveAsync();

        var service = CreateService(paths, catalog);

        var duplicated = await service.DuplicatePresentationAsync(sourceRelativePath, libraryId, playlistId);
        await catalog.LoadAsync();

        duplicated.PresentationPath.Should().NotBe(sourceRelativePath);
        duplicated.Title.Should().Be("Alpha Copy");
        File.Exists(paths.Object.ResolvePresentationPath(duplicated.PresentationPath)).Should().BeTrue();

        var library = catalog.Catalog.Libraries.Should().ContainSingle(item => item.Id == libraryId).Subject;
        library.Presentations.Select(item => item.Path).Should().Contain(sourceRelativePath);
        library.Presentations.Select(item => item.Path).Should().Contain(duplicated.PresentationPath);

        var playlist = catalog.Catalog.Playlists.Should().ContainSingle(item => item.Id == playlistId).Subject;
        playlist.Items.Select(item => item.Path).Should().Contain(sourceRelativePath);
        playlist.Items.Select(item => item.Path).Should().Contain(duplicated.PresentationPath);
    }

    [Fact]
    public async Task RenamePresentationAsync_remaps_catalog_and_workspace_paths()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var sourceAbsolutePath = TestPresentationBundles.WriteBundle(paths.Object.GetPresentationsDirectory(), "alpha.cpres", "Alpha", "alpha-id");
        var sourceRelativePath = paths.Object.ToContentRelativePath(sourceAbsolutePath);

        var catalog = CreateCatalog(paths);
        await catalog.LoadAsync();

        var libraryId = Guid.NewGuid().ToString();
        var playlistId = Guid.NewGuid().ToString();
        catalog.Catalog.Libraries.Add(new LibraryDto
        {
            Id = libraryId,
            Name = "Songs",
            Presentations = new List<PresentationRefDto>
            {
                new() { Path = sourceRelativePath, Title = "Alpha", UpdatedAt = "2026-01-01T00:00:00.000Z" },
            },
        });
        catalog.Catalog.Playlists.Add(new PlaylistDto
        {
            Id = playlistId,
            Name = "Sunday Set",
            Items = new List<PresentationRefDto>
            {
                new() { Path = sourceRelativePath, Title = "Alpha", UpdatedAt = "2026-01-01T00:00:00.000Z" },
            },
        });
        await catalog.SaveAsync();

        var workspace = new WorkspaceService(paths.Object, NullLogger<WorkspaceService>.Instance);
        workspace.Update(item => item.SelectedPresentationPath = sourceRelativePath);

        var service = CreateService(paths, catalog, workspace);

        var renamed = await service.RenamePresentationAsync(sourceRelativePath, "Renamed Song");
        await catalog.LoadAsync();

        renamed.NewPresentationPath.Should().NotBe(sourceRelativePath);
        File.Exists(paths.Object.ResolvePresentationPath(renamed.NewPresentationPath)).Should().BeTrue();
        File.Exists(sourceAbsolutePath).Should().BeFalse();

        var library = catalog.Catalog.Libraries.Should().ContainSingle(item => item.Id == libraryId).Subject;
        library.Presentations.Should().ContainSingle(item => item.Path == renamed.NewPresentationPath && item.Title == "Renamed Song");

        var playlist = catalog.Catalog.Playlists.Should().ContainSingle(item => item.Id == playlistId).Subject;
        playlist.Items.Should().ContainSingle(item => item.Path == renamed.NewPresentationPath && item.Title == "Renamed Song");

        workspace.Workspace.SelectedPresentationPath.Should().Be(renamed.NewPresentationPath);
    }

    [Fact]
    public async Task MovePresentationToLibraryAsync_moves_membership_without_deleting_bundle()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var sourceAbsolutePath = TestPresentationBundles.WriteBundle(paths.Object.GetPresentationsDirectory(), "alpha.cpres", "Alpha");
        var sourceRelativePath = paths.Object.ToContentRelativePath(sourceAbsolutePath);

        var catalog = CreateCatalog(paths);
        await catalog.LoadAsync();

        var sourceLibraryId = Guid.NewGuid().ToString();
        var targetLibraryId = Guid.NewGuid().ToString();
        catalog.Catalog.Libraries.Add(new LibraryDto
        {
            Id = sourceLibraryId,
            Name = "Songs",
            Presentations = new List<PresentationRefDto>
            {
                new() { Path = sourceRelativePath, Title = "Alpha", UpdatedAt = "2026-01-01T00:00:00.000Z", ArrangementId = "chorus-first" },
            },
        });
        catalog.Catalog.Libraries.Add(new LibraryDto
        {
            Id = targetLibraryId,
            Name = "Seasonal",
            Presentations = new List<PresentationRefDto>(),
        });
        await catalog.SaveAsync();

        var service = CreateService(paths, catalog);

        await service.MovePresentationToLibraryAsync(sourceLibraryId, targetLibraryId, sourceRelativePath);
        await catalog.LoadAsync();

        catalog.Catalog.Libraries.Single(item => item.Id == sourceLibraryId).Presentations.Should().BeEmpty();
        catalog.Catalog.Libraries.Single(item => item.Id == targetLibraryId).Presentations.Should().ContainSingle(item =>
            item.Path == sourceRelativePath
            && item.ArrangementId == "chorus-first");
        File.Exists(sourceAbsolutePath).Should().BeTrue();
    }

    [Fact]
    public async Task Presentation_reference_metadata_is_persisted_for_scoped_reference()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var sourceAbsolutePath = TestPresentationBundles.WriteBundle(paths.Object.GetPresentationsDirectory(), "alpha.cpres", "Alpha");
        var sourceRelativePath = paths.Object.ToContentRelativePath(sourceAbsolutePath);

        var catalog = CreateCatalog(paths);
        await catalog.LoadAsync();

        var libraryId = Guid.NewGuid().ToString();
        var playlistId = Guid.NewGuid().ToString();
        catalog.Catalog.Libraries.Add(new LibraryDto
        {
            Id = libraryId,
            Name = "Songs",
            Presentations =
            [
                new() { Path = sourceRelativePath, Title = "Alpha", UpdatedAt = "2026-01-01T00:00:00.000Z" },
            ],
        });
        catalog.Catalog.Playlists.Add(new PlaylistDto
        {
            Id = playlistId,
            Name = "Sunday Set",
            Items =
            [
                new() { Path = sourceRelativePath, Title = "Alpha", UpdatedAt = "2026-01-01T00:00:00.000Z" },
            ],
        });
        await catalog.SaveAsync();

        var service = CreateService(paths, catalog);

        await service.SetPresentationReferenceArrangementAsync(libraryId, null, sourceRelativePath, "chorus-first");
        await service.SetPresentationReferenceDestinationAsync(null, playlistId, sourceRelativePath, "announcements");
        await catalog.LoadAsync();

        catalog.Catalog.Libraries.Single(item => item.Id == libraryId)
            .Presentations.Single()
            .ArrangementId.Should().Be("chorus-first");
        catalog.Catalog.Playlists.Single(item => item.Id == playlistId)
            .Items.Single()
            .DestinationLayerId.Should().Be("announcements");
    }

    [Fact]
    public async Task ResizePresentationAsync_updates_manifest_size_and_catalog_timestamp()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var sourceAbsolutePath = TestPresentationBundles.WriteBundle(paths.Object.GetPresentationsDirectory(), "alpha.cpres", "Alpha");
        var sourceRelativePath = paths.Object.ToContentRelativePath(sourceAbsolutePath);

        var catalog = CreateCatalog(paths);
        await catalog.LoadAsync();

        var libraryId = Guid.NewGuid().ToString();
        catalog.Catalog.Libraries.Add(new LibraryDto
        {
            Id = libraryId,
            Name = "Songs",
            Presentations =
            [
                new() { Path = sourceRelativePath, Title = "Alpha", UpdatedAt = "2026-01-01T00:00:00.000Z" },
            ],
        });
        await catalog.SaveAsync();

        var service = CreateService(paths, catalog);

        await service.ResizePresentationAsync(sourceRelativePath, new SlideSizeDto { Width = 1400, Height = 1050 });
        await catalog.LoadAsync();

        var cpres = new CpresDocumentService(NullLogger<CpresDocumentService>.Instance);
        var projects = new PresentationProjectService(paths.Object, cpres, NullLogger<PresentationProjectService>.Instance);
        var project = projects.Open(sourceRelativePath);

        project.Manifest.SlideSize.Should().BeEquivalentTo(new SlideSizeDto { Width = 1400, Height = 1050 });
        project.Manifest.AspectRatio.Should().Be("4:3");
        catalog.Catalog.Libraries.Single(item => item.Id == libraryId)
            .Presentations.Single()
            .UpdatedAt.Should().Be(project.Manifest.UpdatedAt);
    }

    [Fact]
    public async Task MovePresentationToPlaylistAsync_moves_membership_without_removing_library_reference()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var sourceAbsolutePath = TestPresentationBundles.WriteBundle(paths.Object.GetPresentationsDirectory(), "alpha.cpres", "Alpha");
        var sourceRelativePath = paths.Object.ToContentRelativePath(sourceAbsolutePath);

        var catalog = CreateCatalog(paths);
        await catalog.LoadAsync();

        var libraryId = Guid.NewGuid().ToString();
        var sourcePlaylistId = Guid.NewGuid().ToString();
        var targetPlaylistId = Guid.NewGuid().ToString();
        catalog.Catalog.Libraries.Add(new LibraryDto
        {
            Id = libraryId,
            Name = "Songs",
            Presentations = new List<PresentationRefDto>
            {
                new() { Path = sourceRelativePath, Title = "Alpha", UpdatedAt = "2026-01-01T00:00:00.000Z" },
            },
        });
        catalog.Catalog.Playlists.Add(new PlaylistDto
        {
            Id = sourcePlaylistId,
            Name = "Set A",
            Items = new List<PresentationRefDto>
            {
                new() { Path = sourceRelativePath, Title = "Alpha", UpdatedAt = "2026-01-01T00:00:00.000Z" },
            },
        });
        catalog.Catalog.Playlists.Add(new PlaylistDto
        {
            Id = targetPlaylistId,
            Name = "Set B",
            Items = new List<PresentationRefDto>(),
        });
        await catalog.SaveAsync();

        var service = CreateService(paths, catalog);

        await service.MovePresentationToPlaylistAsync(sourcePlaylistId, targetPlaylistId, sourceRelativePath);
        await catalog.LoadAsync();

        catalog.Catalog.Playlists.Single(item => item.Id == sourcePlaylistId).Items.Should().BeEmpty();
        catalog.Catalog.Playlists.Single(item => item.Id == targetPlaylistId).Items.Should().ContainSingle(item => item.Path == sourceRelativePath);
        catalog.Catalog.Libraries.Single(item => item.Id == libraryId).Presentations.Should().ContainSingle(item => item.Path == sourceRelativePath);
    }

    [Fact]
    public async Task AddPresentationToPlaylistAsync_inserts_at_requested_index()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var firstAbsolutePath = TestPresentationBundles.WriteBundle(paths.Object.GetPresentationsDirectory(), "first.cpres", "First");
        var secondAbsolutePath = TestPresentationBundles.WriteBundle(paths.Object.GetPresentationsDirectory(), "second.cpres", "Second");
        var thirdAbsolutePath = TestPresentationBundles.WriteBundle(paths.Object.GetPresentationsDirectory(), "third.cpres", "Third");
        var firstRelativePath = paths.Object.ToContentRelativePath(firstAbsolutePath);
        var secondRelativePath = paths.Object.ToContentRelativePath(secondAbsolutePath);
        var thirdRelativePath = paths.Object.ToContentRelativePath(thirdAbsolutePath);

        var catalog = CreateCatalog(paths);
        await catalog.LoadAsync();

        var libraryId = Guid.NewGuid().ToString();
        var playlistId = Guid.NewGuid().ToString();
        catalog.Catalog.Libraries.Add(new LibraryDto
        {
            Id = libraryId,
            Name = "Songs",
            Presentations =
            [
                new() { Path = firstRelativePath, Title = "First", UpdatedAt = "2026-01-01T00:00:00.000Z" },
                new() { Path = secondRelativePath, Title = "Second", UpdatedAt = "2026-01-01T00:00:00.000Z" },
                new() { Path = thirdRelativePath, Title = "Third", UpdatedAt = "2026-01-01T00:00:00.000Z" },
            ],
        });
        catalog.Catalog.Playlists.Add(new PlaylistDto
        {
            Id = playlistId,
            Name = "Sunday Set",
            Items =
            [
                new() { Path = firstRelativePath, Title = "First", UpdatedAt = "2026-01-01T00:00:00.000Z" },
                new() { Path = thirdRelativePath, Title = "Third", UpdatedAt = "2026-01-01T00:00:00.000Z" },
            ],
        });
        await catalog.SaveAsync();

        var service = CreateService(paths, catalog);

        await service.AddPresentationToPlaylistAsync(playlistId, secondRelativePath, insertIndex: 1);
        await catalog.LoadAsync();

        catalog.Catalog.Playlists.Single(item => item.Id == playlistId)
            .Items.Select(item => item.Path)
            .Should().ContainInOrder(firstRelativePath, secondRelativePath, thirdRelativePath);
    }

    [Fact]
    public async Task MovePresentationToPlaylistAsync_inserts_at_requested_index()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var firstAbsolutePath = TestPresentationBundles.WriteBundle(paths.Object.GetPresentationsDirectory(), "first.cpres", "First");
        var secondAbsolutePath = TestPresentationBundles.WriteBundle(paths.Object.GetPresentationsDirectory(), "second.cpres", "Second");
        var thirdAbsolutePath = TestPresentationBundles.WriteBundle(paths.Object.GetPresentationsDirectory(), "third.cpres", "Third");
        var firstRelativePath = paths.Object.ToContentRelativePath(firstAbsolutePath);
        var secondRelativePath = paths.Object.ToContentRelativePath(secondAbsolutePath);
        var thirdRelativePath = paths.Object.ToContentRelativePath(thirdAbsolutePath);

        var catalog = CreateCatalog(paths);
        await catalog.LoadAsync();

        var sourcePlaylistId = Guid.NewGuid().ToString();
        var targetPlaylistId = Guid.NewGuid().ToString();
        catalog.Catalog.Libraries.Add(new LibraryDto
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Songs",
            Presentations =
            [
                new() { Path = firstRelativePath, Title = "First", UpdatedAt = "2026-01-01T00:00:00.000Z" },
                new() { Path = secondRelativePath, Title = "Second", UpdatedAt = "2026-01-01T00:00:00.000Z" },
                new() { Path = thirdRelativePath, Title = "Third", UpdatedAt = "2026-01-01T00:00:00.000Z" },
            ],
        });
        catalog.Catalog.Playlists.Add(new PlaylistDto
        {
            Id = sourcePlaylistId,
            Name = "Source",
            Items = [new() { Path = secondRelativePath, Title = "Second", UpdatedAt = "2026-01-01T00:00:00.000Z" }],
        });
        catalog.Catalog.Playlists.Add(new PlaylistDto
        {
            Id = targetPlaylistId,
            Name = "Target",
            Items =
            [
                new() { Path = firstRelativePath, Title = "First", UpdatedAt = "2026-01-01T00:00:00.000Z" },
                new() { Path = thirdRelativePath, Title = "Third", UpdatedAt = "2026-01-01T00:00:00.000Z" },
            ],
        });
        await catalog.SaveAsync();

        var service = CreateService(paths, catalog);

        await service.MovePresentationToPlaylistAsync(sourcePlaylistId, targetPlaylistId, secondRelativePath, insertIndex: 1);
        await catalog.LoadAsync();

        catalog.Catalog.Playlists.Single(item => item.Id == sourcePlaylistId).Items.Should().BeEmpty();
        catalog.Catalog.Playlists.Single(item => item.Id == targetPlaylistId)
            .Items.Select(item => item.Path)
            .Should().ContainInOrder(firstRelativePath, secondRelativePath, thirdRelativePath);
    }

    [Fact]
    public async Task ExportPresentationBundleAsync_copies_bundle_to_destination()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var exportPath = Path.Combine(Path.GetTempPath(), "church-presenter-tests", $"{Guid.NewGuid():N}.cpres");
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var sourceAbsolutePath = TestPresentationBundles.WriteBundle(paths.Object.GetPresentationsDirectory(), "alpha.cpres", "Alpha");
        var sourceRelativePath = paths.Object.ToContentRelativePath(sourceAbsolutePath);

        var catalog = CreateCatalog(paths);
        var service = CreateService(paths, catalog);

        await service.ExportPresentationBundleAsync(sourceRelativePath, exportPath);

        File.Exists(exportPath).Should().BeTrue();
        new FileInfo(exportPath).Length.Should().BeGreaterThan(0);

        var cpres = new CpresDocumentService(NullLogger<CpresDocumentService>.Instance);
        var parsed = cpres.Open(exportPath);
        parsed.ManifestJson.Should().Contain("Alpha");
    }

    [Fact]
    public async Task DeletePresentationAsync_removes_catalog_workspace_recent_file_and_bundle()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var sourceAbsolutePath = TestPresentationBundles.WriteBundle(paths.Object.GetPresentationsDirectory(), "alpha.cpres", "Alpha", "alpha-id");
        var sourceRelativePath = paths.Object.ToContentRelativePath(sourceAbsolutePath);

        var catalog = CreateCatalog(paths);
        await catalog.LoadAsync();

        var libraryId = Guid.NewGuid().ToString();
        var playlistId = Guid.NewGuid().ToString();
        catalog.Catalog.Libraries.Add(new LibraryDto
        {
            Id = libraryId,
            Name = "Songs",
            Presentations = new List<PresentationRefDto>
            {
                new() { Path = sourceRelativePath, Title = "Alpha", UpdatedAt = "2026-01-01T00:00:00.000Z" },
            },
        });
        catalog.Catalog.Playlists.Add(new PlaylistDto
        {
            Id = playlistId,
            Name = "Sunday Set",
            Items = new List<PresentationRefDto>
            {
                new() { Path = sourceRelativePath, Title = "Alpha", UpdatedAt = "2026-01-01T00:00:00.000Z" },
            },
        });
        await catalog.SaveAsync();

        var workspace = new WorkspaceService(paths.Object, NullLogger<WorkspaceService>.Instance);
        workspace.Update(item => item.SelectedPresentationPath = sourceRelativePath);
        var settings = new SettingsService(paths.Object, new SharedConfigService(paths.Object, NullLogger<SharedConfigService>.Instance), new MachineStateService(paths.Object, NullLogger<MachineStateService>.Instance), NullLogger<SettingsService>.Instance);
        settings.Update(item =>
        {
            item.RecentFiles.Add(new PresentationRefDto
            {
                Path = sourceRelativePath,
                Title = "Alpha",
                UpdatedAt = "2026-01-01T00:00:00.000Z",
            });
        });
        await settings.SaveAsync();

        var service = CreateService(paths, catalog, workspace, settings);

        var deleted = await service.DeletePresentationAsync(sourceRelativePath);
        await catalog.LoadAsync();
        await workspace.LoadAsync();
        await settings.LoadAsync();

        deleted.PresentationPath.Should().Be(sourceRelativePath);
        deleted.DeletedBundleFile.Should().BeTrue();
        catalog.Catalog.Libraries.Single(item => item.Id == libraryId).Presentations.Should().BeEmpty();
        catalog.Catalog.Playlists.Single(item => item.Id == playlistId).Items.Should().BeEmpty();
        workspace.Workspace.SelectedPresentationPath.Should().BeNull();
        settings.Settings.RecentFiles.Should().NotContain(item => item.Path == sourceRelativePath);
        File.Exists(sourceAbsolutePath).Should().BeFalse();
    }

    private static CatalogService CreateCatalog(Moq.Mock<IContentDirectoryService> paths)
    {
        var cpres = new CpresDocumentService(NullLogger<CpresDocumentService>.Instance);
        return new CatalogService(paths.Object, new LibraryRegistryService(paths.Object, NullLogger<LibraryRegistryService>.Instance), new PlaylistRegistryService(paths.Object, NullLogger<PlaylistRegistryService>.Instance), cpres, new ContentMaintenanceLogService(paths.Object, NullLogger<ContentMaintenanceLogService>.Instance), NullLogger<CatalogService>.Instance);
    }

    private static PresentationItemActionService CreateService(
        Moq.Mock<IContentDirectoryService> paths,
        CatalogService catalog,
        WorkspaceService? workspace = null,
        SettingsService? settings = null)
    {
        workspace ??= new WorkspaceService(paths.Object, NullLogger<WorkspaceService>.Instance);
        settings ??= new SettingsService(paths.Object, new SharedConfigService(paths.Object, NullLogger<SharedConfigService>.Instance), new MachineStateService(paths.Object, NullLogger<MachineStateService>.Instance), NullLogger<SettingsService>.Instance);
        var cpres = new CpresDocumentService(NullLogger<CpresDocumentService>.Instance);
        var projects = new PresentationProjectService(paths.Object, cpres, NullLogger<PresentationProjectService>.Instance);
        var documents = new PresentationDocumentService(paths.Object, cpres, projects, NullLogger<PresentationDocumentService>.Instance);
        return new PresentationItemActionService(
            paths.Object,
            catalog,
            workspace,
            settings,
            projects,
            documents,
            NullLogger<PresentationItemActionService>.Instance);
    }
}