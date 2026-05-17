using System.Text.Json;

using ChurchPresenter.App.Tests.TestSupport;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace ChurchPresenter.App.Tests.Services.Content;

public sealed class ContentDiagnosticsQueryServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    [Fact]
    public async Task GetSnapshotAsync_projects_pruned_presentation_cache_entries()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();

        var catalog = new Mock<ICatalogService>();
        catalog.SetupGet(service => service.Catalog).Returns(new CatalogDto
        {
            Libraries = [new LibraryDto { Id = "library", Name = "Library" }],
        });
        var sessionCache = new Mock<IShowSessionCache>();
        sessionCache.Setup(cache => cache.PruneMissingFiles()).Returns(["missing.cpres"]);

        var service = new ContentDiagnosticsQueryService(
            paths.Object,
            new ContentStore(NullLogger<ContentStore>.Instance),
            catalog.Object,
            new MediaLibraryService(paths.Object, NullLogger<MediaLibraryService>.Instance),
            sessionCache.Object);

        var snapshot = await service.GetSnapshotAsync();

        snapshot.Diagnostics.Should().ContainSingle(item =>
            item.Id == "presentation-cache-pruned:missing.cpres" &&
            item.FailureKind == ContentAccessFailureKind.Outdated);
        snapshot.RecoveryActions.Should().ContainSingle(action =>
            action.Id == "clear-affected-caches" &&
            action.ActionType == "clear-affected-caches");
    }

    [Fact]
    public async Task GetSnapshotAsync_projects_missing_media_and_relink_action()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();
        await File.WriteAllTextAsync(
            paths.Object.GetMediaIndexPath(),
            JsonSerializer.Serialize(
                new MediaLibraryIndex
                {
                    Items =
                    [
                        new MediaLibraryItem
                        {
                            Id = "missing-asset",
                            Name = "Missing Asset",
                            Path = "Media/Files/missing.mp4",
                            Type = "video",
                        },
                    ],
                },
                JsonOptions));

        var catalog = new Mock<ICatalogService>();
        catalog.SetupGet(service => service.Catalog).Returns(new CatalogDto
        {
            Libraries = [new LibraryDto { Id = "library", Name = "Library" }],
        });
        var sessionCache = new Mock<IShowSessionCache>();
        sessionCache.Setup(cache => cache.PruneMissingFiles()).Returns(Array.Empty<string>());

        var service = new ContentDiagnosticsQueryService(
            paths.Object,
            new ContentStore(NullLogger<ContentStore>.Instance),
            catalog.Object,
            new MediaLibraryService(paths.Object, NullLogger<MediaLibraryService>.Instance),
            sessionCache.Object);

        var snapshot = await service.GetSnapshotAsync();

        snapshot.Diagnostics.Should().ContainSingle(item =>
            item.Id == "media-missing:missing-asset" &&
            item.FailureKind == ContentAccessFailureKind.Missing);
        snapshot.RecoveryActions.Should().ContainSingle(action =>
            action.Id == "relink-media:missing-asset" &&
            action.ActionType == "relink-media");
    }
}
