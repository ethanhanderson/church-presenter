using System.Text.Json;

using ChurchPresenter.App.Tests.TestSupport;
using ChurchPresenter.Backend.Media;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

namespace ChurchPresenter.App.Tests.Services.Media;

public sealed class MediaLibraryServiceResilienceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    [Fact]
    public async Task GetAssetsAsync_classifies_missing_media_with_failure_kind()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();
        Directory.CreateDirectory(paths.Object.GetMediaRootDirectory());
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

        var service = new MediaLibraryService(paths.Object, NullLogger<MediaLibraryService>.Instance);

        var asset = (await service.GetAssetsAsync()).Should().ContainSingle().Subject;

        asset.Availability.Status.Should().Be(MediaAvailabilityStatus.Missing);
        asset.Availability.FailureKind.Should().Be(ContentAccessFailureKind.Missing);
        asset.ResolvedPath.Should().BeNull();
    }

    [Fact]
    public async Task AddRootItemAsync_publishes_media_added_event()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        var paths = TestContentPaths.Create(root);
        await paths.Object.EnsureDocumentsLayoutAsync();
        var source = Path.Combine(root, "source.mp4");
        await File.WriteAllTextAsync(source, "media");

        var bus = new ContentChangeBus(NullLogger<ContentChangeBus>.Instance);
        var changes = new List<ContentChangeEvent>();
        bus.Changed += (_, change) => changes.Add(change);
        var service = new MediaLibraryService(paths.Object, NullLogger<MediaLibraryService>.Instance, bus);

        var item = await service.AddRootItemAsync(source);

        changes.Should().ContainSingle(change =>
            change.Kind == ContentChangeKind.MediaAssetAdded &&
            change.SubjectId == item.Id);
    }
}
