using System.IO.Compression;

using ChurchPresenter.App.Tests.TestSupport;
using ChurchPresenter.Core.Cpres;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace ChurchPresenter.App.Tests.Services.Content;

public sealed class CollectionPackageServiceTests
{
    [Fact]
    public async Task ExportLibraryAsync_and_ImportLibraryAsync_roundtrip_presentations()
    {
        var sourceRoot = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var packagePath = Path.Combine(Path.GetTempPath(), "church-presenter-tests", $"{Guid.NewGuid():N}.cplibrary");
        var sourcePaths = TestContentPaths.Create(sourceRoot);
        await sourcePaths.Object.EnsureDocumentsLayoutAsync();

        var alphaPath = TestPresentationBundles.WriteBundle(sourcePaths.Object.GetPresentationsDirectory(), "alpha.cpres", "Alpha");
        var betaPath = TestPresentationBundles.WriteBundle(sourcePaths.Object.GetPresentationsDirectory(), "beta.cpres", "Beta");
        var sourceCatalog = CreateCatalog(sourcePaths);
        await sourceCatalog.LoadAsync();
        sourceCatalog.Catalog.Libraries.Add(new LibraryDto
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Seasonal",
            Presentations = new List<PresentationRefDto>
            {
                new() { Path = sourcePaths.Object.ToContentRelativePath(alphaPath), Title = "Alpha", UpdatedAt = "2026-01-01T00:00:00.000Z" },
                new() { Path = sourcePaths.Object.ToContentRelativePath(betaPath), Title = "Beta", UpdatedAt = "2026-01-01T00:00:00.000Z" },
            },
        });
        await sourceCatalog.SaveAsync();

        var exporter = CreatePackageService(sourcePaths, sourceCatalog);
        await exporter.ExportLibraryAsync(sourceCatalog.Catalog.Libraries.Single(l => l.Name == "Seasonal").Id, packagePath);

        using (var archive = ZipFile.OpenRead(packagePath))
        {
            archive.GetEntry("manifest.json").Should().NotBeNull();
            archive.GetEntry("library.json").Should().NotBeNull();
            archive.Entries.Should().Contain(entry => entry.FullName == "Presentations/alpha.cpres");
            archive.Entries.Should().Contain(entry => entry.FullName == "Presentations/beta.cpres");
        }

        var importRoot = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var importPaths = TestContentPaths.Create(importRoot);
        await importPaths.Object.EnsureDocumentsLayoutAsync();
        var importCatalog = CreateCatalog(importPaths);
        var importer = CreatePackageService(importPaths, importCatalog);

        var imported = await importer.ImportLibraryAsync(packagePath);
        await importCatalog.LoadAsync();

        var library = importCatalog.Catalog.Libraries.Should().ContainSingle(item => item.Id == imported.LibraryId).Subject;
        library.Presentations.Should().HaveCount(2);
        imported.ImportedPresentationPaths.Should().OnlyContain(path => File.Exists(importPaths.Object.ResolvePresentationPath(path)));
    }

    [Fact]
    public async Task ExportPlaylistAsync_and_ImportPlaylistAsync_preserve_external_metadata_and_order()
    {
        var sourceRoot = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var packagePath = Path.Combine(Path.GetTempPath(), "church-presenter-tests", $"{Guid.NewGuid():N}.cpplaylist");
        var sourcePaths = TestContentPaths.Create(sourceRoot);
        await sourcePaths.Object.EnsureDocumentsLayoutAsync();

        var alphaPath = TestPresentationBundles.WriteBundle(sourcePaths.Object.GetPresentationsDirectory(), "alpha.cpres", "Alpha");
        var sourceCatalog = CreateCatalog(sourcePaths);
        await sourceCatalog.LoadAsync();
        sourceCatalog.Catalog.Playlists.Add(new PlaylistDto
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Sunday Set",
            ExternalSet = new ExternalSetLinkDto
            {
                SetId = "set-1",
                GroupId = "group-1",
                ServiceDate = "2026-04-05",
                SyncedAt = "2026-04-01T00:00:00.000Z",
            },
            Sync = new SyncMetadata
            {
                Status = "synced",
                LastSyncAttempt = "2026-04-01T00:00:00.000Z",
            },
            Items = new List<PresentationRefDto>
            {
                new() { Path = sourcePaths.Object.ToContentRelativePath(alphaPath), Title = "Alpha", UpdatedAt = "2026-01-01T00:00:00.000Z" },
                new() { Path = sourcePaths.Object.ToContentRelativePath(alphaPath), Title = "Alpha", UpdatedAt = "2026-01-01T00:00:00.000Z" },
            },
        });
        await sourceCatalog.SaveAsync();

        var exporter = CreatePackageService(sourcePaths, sourceCatalog);
        await exporter.ExportPlaylistAsync(sourceCatalog.Catalog.Playlists.Single().Id, packagePath);

        var importRoot = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var importPaths = TestContentPaths.Create(importRoot);
        await importPaths.Object.EnsureDocumentsLayoutAsync();
        var importCatalog = CreateCatalog(importPaths);
        var importer = CreatePackageService(importPaths, importCatalog);

        var imported = await importer.ImportPlaylistAsync(packagePath);
        await importCatalog.LoadAsync();

        var playlist = importCatalog.Catalog.Playlists.Should().ContainSingle(item => item.Id == imported.PlaylistId).Subject;
        playlist.ExternalSet.Should().NotBeNull();
        playlist.ExternalSet!.GroupId.Should().Be("group-1");
        playlist.ExternalSet.ServiceDate.Should().Be("2026-04-05");
        playlist.Sync.Should().NotBeNull();
        playlist.Sync!.Status.Should().Be("synced");
        playlist.Items.Should().HaveCount(2);
        playlist.Items[0].Path.Should().Be(playlist.Items[1].Path);
    }

    [Fact]
    public async Task PreviewLibraryImportAsync_reports_media_copy_requirements_and_conflicts_before_import()
    {
        var sourceRoot = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var packagePath = Path.Combine(Path.GetTempPath(), "church-presenter-tests", $"{Guid.NewGuid():N}.cplibrary");
        var sourcePaths = TestContentPaths.Create(sourceRoot);
        await sourcePaths.Object.EnsureDocumentsLayoutAsync();

        var alphaPath = TestPresentationBundles.WriteBundle(
            sourcePaths.Object.GetPresentationsDirectory(),
            "alpha.cpres",
            "Alpha",
            presentationId: "alpha-id",
            includeMedia: true);
        var sourceCatalog = CreateCatalog(sourcePaths);
        await sourceCatalog.LoadAsync();
        sourceCatalog.Catalog.Libraries.Add(new LibraryDto
        {
            Id = "library-1",
            Name = "Seasonal",
            Presentations =
            [
                new() { Path = sourcePaths.Object.ToContentRelativePath(alphaPath), Title = "Alpha" },
            ],
        });
        await sourceCatalog.SaveAsync();
        await CreatePackageService(sourcePaths, sourceCatalog).ExportLibraryAsync("library-1", packagePath);

        var importRoot = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var importPaths = TestContentPaths.Create(importRoot);
        await importPaths.Object.EnsureDocumentsLayoutAsync();
        TestPresentationBundles.WriteBundle(
            importPaths.Object.GetPresentationsDirectory(),
            "alpha.cpres",
            "Existing Alpha",
            presentationId: "different-id");
        var importCatalog = CreateCatalog(importPaths);
        var importer = CreatePackageService(importPaths, importCatalog);

        CollectionPackagePreview preview = await importer.PreviewLibraryImportAsync(packagePath);

        preview.Changes.Should().Contain(change =>
            change.Kind == SupportPackageChangeKind.Conflict &&
            change.Path == "Presentations/alpha.cpres" &&
            change.RequiresConfirmation &&
            !change.IsDestructive);
        preview.CopyRequirements.Should().Contain(requirement =>
            requirement.Kind == PackageCopyRequirementKind.CopyPresentationBundle &&
            requirement.DestinationPath == "Presentations/alpha.cpres");
        preview.CopyRequirements.Should().Contain(requirement =>
            requirement.Kind == PackageCopyRequirementKind.CopyEmbeddedMediaPayload &&
            requirement.SourcePath == "media/background.mp4");
    }

    private static CatalogService CreateCatalog(Mock<IContentDirectoryService> paths)
    {
        var cpres = new CpresDocumentService(NullLogger<CpresDocumentService>.Instance);
        return new CatalogService(paths.Object, new LibraryRegistryService(paths.Object, NullLogger<LibraryRegistryService>.Instance), new PlaylistRegistryService(paths.Object, NullLogger<PlaylistRegistryService>.Instance), cpres, new ContentMaintenanceLogService(paths.Object, NullLogger<ContentMaintenanceLogService>.Instance), NullLogger<CatalogService>.Instance);
    }

    private static CollectionPackageService CreatePackageService(Mock<IContentDirectoryService> paths, CatalogService catalog)
    {
        var cpres = new CpresDocumentService(NullLogger<CpresDocumentService>.Instance);
        var projects = new PresentationProjectService(paths.Object, cpres, NullLogger<PresentationProjectService>.Instance);
        var presentations = new PresentationDocumentService(paths.Object, cpres, projects, NullLogger<PresentationDocumentService>.Instance);
        return new CollectionPackageService(paths.Object, catalog, presentations, NullLogger<CollectionPackageService>.Instance);
    }
}